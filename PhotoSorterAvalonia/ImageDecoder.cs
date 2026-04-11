using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;

namespace PhotoSorterAvalonia;

/// <summary>
/// Loads bitmaps from disk with ExifTool/sips fallbacks and reads EXIF orientation off the UI thread.
/// </summary>
internal static class ImageDecoder
{
    internal static void LogDiagnostic(string message) =>
        Console.WriteLine($"[ImageDiag {DateTime.Now:HH:mm:ss.fff}] {message}");

    internal static int GetExifOrientation(string imagePath)
    {
        var exifTimer = Stopwatch.StartNew();
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "exiftool",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("-Orientation");
            startInfo.ArgumentList.Add("-n");
            startInfo.ArgumentList.Add("-s3");
            startInfo.ArgumentList.Add(imagePath);

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                LogDiagnostic($"ExifTool failed to start. Path='{imagePath}'");
                return 1;
            }

            bool exited = process.WaitForExit(AppConfig.ExifToolTimeoutMs);
            if (!exited)
            {
                KillProcessAfterWaitTimeout(process);
                LogDiagnostic($"ExifTool timeout. Path='{imagePath}', TimeoutMs={AppConfig.ExifToolTimeoutMs}");
                return 1;
            }

            if (process.ExitCode == 0)
            {
                string output = process.StandardOutput.ReadToEnd().Trim();
                if (int.TryParse(output, out int orientation) && orientation >= 1 && orientation <= 8)
                {
                    LogDiagnostic($"Exif orientation read. Path='{imagePath}', Orientation={orientation}, ElapsedMs={exifTimer.ElapsedMilliseconds}");
                    return orientation;
                }
            }

            string error = process.StandardError.ReadToEnd().Trim();
            LogDiagnostic($"ExifTool returned no valid orientation. Path='{imagePath}', ExitCode={process.ExitCode}, StdErr='{error}'");
        }
        catch (Exception ex)
        {
            LogDiagnostic($"Exif orientation read failed. Path='{imagePath}', Error='{ex.GetType().Name}: {ex.Message}'");
        }

        return 1;
    }

    /// <summary>
    /// When <see cref="Process.WaitForExit(int)"/> times out, the child process keeps running;
    /// disposing <see cref="Process"/> does not terminate it. Kill the tree and reap the handle.
    /// </summary>
    private static void KillProcessAfterWaitTimeout(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (Exception ex)
        {
            LogDiagnostic($"Failed to terminate child process after timeout. Pid={process.Id}, Error='{ex.GetType().Name}: {ex.Message}'");
            return;
        }

        try
        {
            process.WaitForExit();
        }
        catch
        {
            // Best-effort reap after Kill.
        }
    }

    /// <summary>
    /// Decodes a bitmap from an in-memory buffer. The stream is not retained after construction.
    /// </summary>
    private static Bitmap DecodeBitmapFromBuffer(byte[] buffer, int? maxDecodeWidth = null)
    {
        using var ms = new MemoryStream(buffer);
        if (maxDecodeWidth.HasValue)
            return Bitmap.DecodeToWidth(ms, maxDecodeWidth.Value);
        return new Bitmap(ms);
    }

    private static void TryDeleteFileIfExists(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Best-effort cleanup.
        }
    }

    /// <summary>
    /// Loads bitmap using direct decode first, then falls back to ExifTool preview extraction.
    /// </summary>
    internal static Bitmap LoadBitmapWithFallback(string imagePath, int? maxDecodeWidth = null)
    {
        try
        {
            using var fs = File.OpenRead(imagePath);
            if (maxDecodeWidth.HasValue)
                return Bitmap.DecodeToWidth(fs, maxDecodeWidth.Value);
            return new Bitmap(fs);
        }
        catch (Exception ex)
        {
            LogDiagnostic($"Direct decode failed, trying fallback preview extraction. Path='{imagePath}', Error='{ex.GetType().Name}: {ex.Message}'");
        }

        if (TryLoadBitmapViaSipsConversion(imagePath, maxDecodeWidth, out var sipsBitmap))
            return sipsBitmap!;

        if (TryLoadBitmapViaExifTool(imagePath, "-PreviewImage", maxDecodeWidth, out var previewBitmap))
            return previewBitmap!;

        if (TryLoadBitmapViaExifTool(imagePath, "-JpgFromRaw", maxDecodeWidth, out var rawJpegBitmap))
            return rawJpegBitmap!;

        throw new ArgumentException("Unable to load bitmap from provided data");
    }

    private static bool TryLoadBitmapViaSipsConversion(string imagePath, int? maxDecodeWidth, out Bitmap? bitmap)
    {
        bitmap = null;
        var timer = Stopwatch.StartNew();
        string tempJpegPath = Path.Combine(Path.GetTempPath(), $"photosorter-{Guid.NewGuid():N}.jpg");

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "/usr/bin/sips",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("-s");
            startInfo.ArgumentList.Add("format");
            startInfo.ArgumentList.Add("jpeg");
            startInfo.ArgumentList.Add(imagePath);
            startInfo.ArgumentList.Add("--out");
            startInfo.ArgumentList.Add(tempJpegPath);

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                LogDiagnostic($"sips conversion failed to start. Path='{imagePath}'");
                return false;
            }

            var stderrTask = Task.Run(() => process.StandardError.ReadToEnd());
            var stdoutTask = Task.Run(() => process.StandardOutput.ReadToEnd());
            bool exited = process.WaitForExit(AppConfig.ExifToolTimeoutMs);
            if (!exited)
            {
                KillProcessAfterWaitTimeout(process);
                try
                {
                    Task.WaitAll(new Task[] { stderrTask, stdoutTask }, millisecondsTimeout: 5000);
                }
                catch
                {
                    // Best-effort wait for readers after kill.
                }

                TryDeleteFileIfExists(tempJpegPath);
                LogDiagnostic($"sips conversion timeout. Path='{imagePath}', TimeoutMs={AppConfig.ExifToolTimeoutMs}");
                return false;
            }

            string stdErr;
            string stdOut;
            try
            {
                stdErr = stderrTask.Result.Trim();
                stdOut = stdoutTask.Result.Trim();
            }
            catch (Exception readEx)
            {
                LogDiagnostic($"sips conversion output read failed. Path='{imagePath}', Error='{readEx.GetType().Name}: {readEx.Message}'");
                return false;
            }

            if (process.ExitCode != 0 || !File.Exists(tempJpegPath))
            {
                LogDiagnostic($"sips conversion unavailable. Path='{imagePath}', ExitCode={process.ExitCode}, StdOut='{stdOut}', StdErr='{stdErr}'");
                return false;
            }

            byte[] bytes = File.ReadAllBytes(tempJpegPath);
            bitmap = DecodeBitmapFromBuffer(bytes, maxDecodeWidth);
            LogDiagnostic($"sips conversion success. Path='{imagePath}', OutputBytes={bytes.Length}, ElapsedMs={timer.ElapsedMilliseconds}");
            return true;
        }
        catch (Exception ex)
        {
            LogDiagnostic($"sips conversion failed. Path='{imagePath}', Error='{ex.GetType().Name}: {ex.Message}'");
            return false;
        }
        finally
        {
            TryDeleteFileIfExists(tempJpegPath);
        }
    }

    private static bool TryLoadBitmapViaExifTool(string imagePath, string exifArgument, int? maxDecodeWidth, out Bitmap? bitmap)
    {
        bitmap = null;
        var timer = Stopwatch.StartNew();

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "exiftool",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("-b");
            startInfo.ArgumentList.Add(exifArgument);
            startInfo.ArgumentList.Add(imagePath);

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                LogDiagnostic($"Fallback decode failed to start exiftool. Path='{imagePath}', Mode='{exifArgument}'");
                return false;
            }

            var memoryStream = new MemoryStream();
            try
            {
                var stdoutTask = Task.Run(() =>
                {
                    try
                    {
                        process.StandardOutput.BaseStream.CopyTo(memoryStream);
                    }
                    catch (IOException)
                    {
                        // Pipe closed after kill or process exit.
                    }
                    catch (ObjectDisposedException)
                    {
                    }
                });
                var stderrTask = Task.Run(() =>
                {
                    try
                    {
                        return process.StandardError.ReadToEnd();
                    }
                    catch
                    {
                        return string.Empty;
                    }
                });

                bool exited = process.WaitForExit(AppConfig.ExifToolTimeoutMs);
                if (!exited)
                {
                    KillProcessAfterWaitTimeout(process);
                    try
                    {
                        stdoutTask.Wait(TimeSpan.FromSeconds(30));
                    }
                    catch
                    {
                    }

                    try
                    {
                        stderrTask.Wait(TimeSpan.FromSeconds(5));
                    }
                    catch
                    {
                    }

                    LogDiagnostic($"Fallback decode timeout. Path='{imagePath}', Mode='{exifArgument}', TimeoutMs={AppConfig.ExifToolTimeoutMs}");
                    return false;
                }

                try
                {
                    stdoutTask.Wait();
                }
                catch
                {
                }

                string stdErr = string.Empty;
                try
                {
                    stderrTask.Wait();
                    stdErr = stderrTask.Result.Trim();
                }
                catch
                {
                }

                if (process.ExitCode != 0 || memoryStream.Length == 0)
                {
                    LogDiagnostic($"Fallback decode unavailable. Path='{imagePath}', Mode='{exifArgument}', ExitCode={process.ExitCode}, Bytes={memoryStream.Length}, StdErr='{stdErr}'");
                    return false;
                }

                byte[] buffer = memoryStream.ToArray();
                bitmap = DecodeBitmapFromBuffer(buffer, maxDecodeWidth);
                LogDiagnostic($"Fallback decode success. Path='{imagePath}', Mode='{exifArgument}', Bytes={buffer.Length}, ElapsedMs={timer.ElapsedMilliseconds}");
                return true;
            }
            finally
            {
                memoryStream.Dispose();
            }
        }
        catch (Exception ex)
        {
            LogDiagnostic($"Fallback decode failed. Path='{imagePath}', Mode='{exifArgument}', Error='{ex.GetType().Name}: {ex.Message}'");
            return false;
        }
    }
}
