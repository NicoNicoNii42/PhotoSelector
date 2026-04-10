using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace PhotoSorterAvalonia
{
    /// <summary>
    /// Main window for the Photo Sorter application.
    /// Allows sorting photos into three categories using keyboard shortcuts and mouse gestures.
    /// </summary>
    public partial class MainWindow : Window
    {
        #region Private Fields
        
        private string _goodFolder = null!;
        private string _veryGoodFolder = null!;
        private string _sortedOutFolder = null!;
        
        private readonly List<string> _photos = new();
        private int _currentIndex;
        private int _totalPhotos;
        private int _goodCount;
        private int _veryGoodCount;
        private int _sortedOutCount;
        
        private double _currentScale = AppConfig.DefaultZoomScale;
        private double _currentRotation = AppConfig.DefaultRotation;
        
        // Viewbox for responsive sizing
        private Viewbox _imageContainer = null!;
        
        // Persistent statistics
        private StatisticsManager.StatisticsData _persistentStats = null!;
        
        // Image cache for preloading with LRU eviction
        private readonly Dictionary<string, Bitmap> _imageCache = new();
        private readonly LinkedList<string> _lruList = new(); // Most recent at front, least recent at back
        private readonly HashSet<string> _loadingImages = new();
        
        #endregion
        
        #region Constructor
        
        /// <summary>
        /// Initializes a new instance of the MainWindow class.
        /// </summary>
        public MainWindow()
        {
            InitializeComponent();
            
            // Initialize Viewbox reference
            _imageContainer = this.FindControl<Viewbox>("ImageContainer")!;
            
            // Initialize folder paths first
            InitializeFolderPaths();
            
            // Load persistent statistics and merge with folder scan
            _persistentStats = StatisticsManager.LoadStatistics();
            _persistentStats = StatisticsManager.MergeWithFolderScan(_persistentStats);
            
            // Initialize current session counts from folder scan
            InitializeCurrentCountsFromFolders();
            
            CreateDestinationFolders();
            LoadPhotos();
            SetupEventHandlers();
            UpdateDisplay();
            
            HideInstructionsOnStartup();
            SetupWindowFocus();
        }
        
        #endregion
        
        #region Initialization Methods
        
        /// <summary>
        /// Initializes the folder paths for sorting categories.
        /// </summary>
        private void InitializeFolderPaths()
        {
            _goodFolder = AppConfig.GetGoodFolderPath();
            _veryGoodFolder = AppConfig.GetVeryGoodFolderPath();
            _sortedOutFolder = AppConfig.GetSortedOutFolderPath();
        }
        
        /// <summary>
        /// Creates the destination folders if they don't exist.
        /// </summary>
        private void CreateDestinationFolders()
        {
            System.IO.Directory.CreateDirectory(_goodFolder);
            System.IO.Directory.CreateDirectory(_veryGoodFolder);
            System.IO.Directory.CreateDirectory(_sortedOutFolder);
        }
        
        /// <summary>
        /// Sets up all event handlers for the window.
        /// </summary>
        private void SetupEventHandlers()
        {
            // Keyboard handlers
            KeyDown += OnKeyDown;
            
            // Note: All mouse-based zoom and pan controls are disabled by default
            // They can be re-enabled by checking the "Show Zoom Controls" checkbox
            // This includes: mouse wheel zoom, double-click reset, and drag panning
        }
        
        /// <summary>
        /// Hides the instructions overlay on application startup.
        /// </summary>
        private void HideInstructionsOnStartup()
        {
            InstructionsOverlay.IsVisible = false;
        }
        
        /// <summary>
        /// Sets up window focus for keyboard input.
        /// </summary>
        private void SetupWindowFocus()
        {
            // Focus the window when it's activated
            Activated += (_, _) => Focus();
            
            // Also focus the image control to ensure keyboard events are captured
            Loaded += (_, _) =>
            {
                Focus();
                CurrentImage.Focus();
            };
        }
        
        /// <summary>
        /// Initializes current session counts by scanning destination folders.
        /// </summary>
        private void InitializeCurrentCountsFromFolders()
        {
            try
            {
                _goodCount = System.IO.Directory.Exists(_goodFolder) ? 
                    System.IO.Directory.GetFiles(_goodFolder, AppConfig.FileExtension).Length : 0;
                
                _veryGoodCount = System.IO.Directory.Exists(_veryGoodFolder) ? 
                    System.IO.Directory.GetFiles(_veryGoodFolder, AppConfig.FileExtension).Length : 0;
                
                _sortedOutCount = System.IO.Directory.Exists(_sortedOutFolder) ? 
                    System.IO.Directory.GetFiles(_sortedOutFolder, AppConfig.FileExtension).Length : 0;
                
                // Update display with folder-scanned counts
                UpdateStatistics();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error initializing counts from folders: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Loads all photos from the source folder.
        /// </summary>
        private void LoadPhotos()
        {
            try
            {
                _photos.Clear();
                _photos.AddRange(System.IO.Directory.GetFiles(AppConfig.SourceFolder, AppConfig.FileExtension)
                    .OrderBy(f => f));
                
                _totalPhotos = _photos.Count;
                LogImageDiagnostic($"LoadPhotos complete. SourceFolder='{AppConfig.SourceFolder}', Filter='{AppConfig.FileExtension}', Total={_totalPhotos}");
                UpdateStatistics();
            }
            catch (Exception ex)
            {
                ShowError($"Error loading photos: {ex.Message}");
            }
        }
        
        #endregion
        
        #region Display Methods
        
        /// <summary>
        /// Updates the display with the current photo and statistics.
        /// </summary>
        private void UpdateDisplay()
        {
            if (_currentIndex >= _photos.Count)
            {
                ShowCompletionMessage();
                return;
            }
            
            string currentPhoto = _photos[_currentIndex];
            string fileName = Path.GetFileName(currentPhoto);
            
            FileText.Text = fileName;
            
            // Keep current zoom scale but reset translation for new photo
            ResetTranslation();
            
            LoadImageWithCache(currentPhoto);
            UpdateStatistics();
            
            // Preload adjacent images in background
            PreloadAdjacentImages();
        }
        
        /// <summary>
        /// Shows the completion message when all photos are sorted.
        /// </summary>
        private void ShowCompletionMessage()
        {
            FileText.Text = "All photos sorted!";
            CurrentImage.Source = null;
        }
        
        /// <summary>
        /// Shows a placeholder or low-resolution preview of the image.
        /// </summary>
        /// <param name="imagePath">The path to the image file.</param>
        private void ShowImagePlaceholder(string imagePath)
        {
            try
            {
                // Create a simple colored placeholder with file name
                var drawing = new DrawingGroup();
                using (var context = drawing.Open())
                {
                    // Background
                    context.DrawRectangle(
                        new SolidColorBrush(Color.FromRgb(60, 60, 60)),
                        null,
                        new Rect(0, 0, 800, 600));
                    
                    // Text with file name
                    var text = new FormattedText(
                        Path.GetFileName(imagePath),
                        System.Globalization.CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight,
                        new Typeface("Arial"),
                        24,
                        Brushes.White);
                    
                    context.DrawText(text, new Point(20, 20));
                    
                    // Loading text
                    var loadingText = new FormattedText(
                        "Loading full image...",
                        System.Globalization.CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight,
                        new Typeface("Arial"),
                        16,
                        Brushes.LightGray);
                    
                    context.DrawText(loadingText, new Point(20, 60));
                }
                
                var drawingImage = new DrawingImage(drawing);
                CurrentImage.Source = drawingImage;
            }
            catch
            {
                // If placeholder fails, show simple error
                ShowImageLoadError(imagePath);
            }
        }
        
        /// <summary>
        /// Shows an error message when an image cannot be loaded.
        /// </summary>
        /// <param name="imagePath">The path to the image file that failed to load.</param>
        private void ShowImageLoadError(string imagePath)
        {
            FileText.Text = $"{Path.GetFileName(imagePath)} (Preview not available)";
        }
        
        /// <summary>
        /// Loads an image using cache-first approach with LRU eviction.
        /// </summary>
        /// <param name="imagePath">The path to the image file.</param>
        private void LoadImageWithCache(string imagePath)
        {
            var loadTimer = Stopwatch.StartNew();
            try
            {
                bool fileExists = File.Exists(imagePath);
                long fileSize = fileExists ? new FileInfo(imagePath).Length : -1;
                LogImageDiagnostic($"Load requested. Index={_currentIndex}, Path='{imagePath}', Exists={fileExists}, SizeBytes={fileSize}");

                // Check if image is already in cache
                lock (_imageCache)
                {
                    if (_imageCache.TryGetValue(imagePath, out var cachedBitmap))
                    {
                        // Update LRU - move to front (most recently used)
                        lock (_lruList)
                        {
                            _lruList.Remove(imagePath);
                            _lruList.AddFirst(imagePath);
                        }
                        
                        // Use cached image immediately
                        CurrentImage.Source = cachedBitmap;
                        LogImageDiagnostic($"Cache hit. Path='{imagePath}', ElapsedMs={loadTimer.ElapsedMilliseconds}");
                        
                        // Apply EXIF auto-rotation
                        ApplyExifOrientation(imagePath);
                        
                        return;
                    }
                }
                
                // Show placeholder while loading
                LogImageDiagnostic($"Cache miss. Showing placeholder. Path='{imagePath}'");
                ShowImagePlaceholder(imagePath);
                
                // Load image in background and cache it
                Task.Run(() =>
                {
                    var decodeTimer = Stopwatch.StartNew();
                    try
                    {
                        var bitmap = LoadBitmapWithFallback(imagePath);
                        LogImageDiagnostic($"Decode success. Path='{imagePath}', ElapsedMs={decodeTimer.ElapsedMilliseconds}");
                        
                        // Add to cache with LRU management
                        lock (_imageCache)
                        {
                            _imageCache[imagePath] = bitmap;
                            
                            // Update LRU
                            lock (_lruList)
                            {
                                _lruList.AddFirst(imagePath);
                            }
                            
                            // Enforce cache size limit
                            EnforceCacheSizeLimit();
                        }
                        
                        // Update UI on main thread
                        Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            string? currentExpectedPath = _currentIndex >= 0 && _currentIndex < _photos.Count
                                ? _photos[_currentIndex]
                                : null;
                            if (!string.Equals(currentExpectedPath, imagePath, StringComparison.Ordinal))
                            {
                                LogImageDiagnostic($"Stale UI update candidate. Requested='{imagePath}', CurrentExpected='{currentExpectedPath}'");
                                return;
                            }

                            CurrentImage.Source = bitmap;
                            LogImageDiagnostic($"UI image source set. Path='{imagePath}', TotalElapsedMs={loadTimer.ElapsedMilliseconds}");
                            
                            // Apply EXIF auto-rotation for non-cached images
                            ApplyExifOrientation(imagePath);
                        });
                    }
                    catch (Exception ex)
                    {
                        // If loading fails, keep placeholder
                        LogImageDiagnostic($"Decode failed. Path='{imagePath}', Error='{ex.GetType().Name}: {ex.Message}'");
                    }
                    finally
                    {
                        // Remove from loading set
                        lock (_loadingImages)
                        {
                            _loadingImages.Remove(imagePath);
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                LogImageDiagnostic($"LoadImageWithCache failed before decode. Path='{imagePath}', Error='{ex.GetType().Name}: {ex.Message}'");
                ShowImageLoadError(imagePath);
            }
        }
        
        /// <summary>
        /// Enforces the cache size limit by removing least recently used images.
        /// </summary>
        private void EnforceCacheSizeLimit()
        {
            lock (_imageCache)
            {
                lock (_lruList)
                {
                    while (_imageCache.Count > AppConfig.MaxCacheSize && _lruList.Count > 0)
                    {
                        // Remove least recently used item (from the back of the list)
                        string lruKey = _lruList.Last!.Value;
                        _lruList.RemoveLast();
                        
                        if (_imageCache.TryGetValue(lruKey, out var bitmap))
                        {
                            bitmap.Dispose();
                            _imageCache.Remove(lruKey);
                        }
                    }
                }
            }
        }
        
        /// <summary>
        /// Preloads adjacent images (next and previous 2) in the background.
        /// </summary>
        private void PreloadAdjacentImages()
        {
            Task.Run(() =>
            {
                for (int offset = -AppConfig.PreloadRange; offset <= AppConfig.PreloadRange; offset++)
                {
                    if (offset == 0) continue; // Skip current image
                    
                    int targetIndex = _currentIndex + offset;
                    if (targetIndex >= 0 && targetIndex < _photos.Count)
                    {
                        string imagePath = _photos[targetIndex];
                        
                        // Check if already cached or loading
                        lock (_imageCache)
                        {
                            if (_imageCache.ContainsKey(imagePath))
                                continue;
                        }
                        
                        lock (_loadingImages)
                        {
                            if (_loadingImages.Contains(imagePath))
                                continue;
                            
                            _loadingImages.Add(imagePath);
                        }
                        
                        // Load and cache in background
                        Task.Run(() =>
                        {
                            var preloadTimer = Stopwatch.StartNew();
                            try
                            {
                                var bitmap = LoadBitmapWithFallback(imagePath);
                                
                                lock (_imageCache)
                                {
                                    _imageCache[imagePath] = bitmap;
                                    
                                    // Update LRU for preloaded images
                                    lock (_lruList)
                                    {
                                        _lruList.AddFirst(imagePath);
                                    }
                                    
                                    // Enforce cache size limit
                                    EnforceCacheSizeLimit();
                                }
                                LogImageDiagnostic($"Preload success. Path='{imagePath}', ElapsedMs={preloadTimer.ElapsedMilliseconds}");
                            }
                            catch (Exception ex)
                            {
                                // Ignore loading errors for preloaded images
                                LogImageDiagnostic($"Preload failed. Path='{imagePath}', Error='{ex.GetType().Name}: {ex.Message}'");
                            }
                            finally
                            {
                                lock (_loadingImages)
                                {
                                    _loadingImages.Remove(imagePath);
                                }
                            }
                        });
                    }
                }
            });
        }
        
        /// <summary>
        /// Reads EXIF orientation metadata from an image file using ExifTool command-line.
        /// </summary>
        /// <param name="imagePath">The path to the image file.</param>
        /// <returns>The EXIF orientation value (1-8), or 1 if not found.</returns>
        private int GetExifOrientation(string imagePath)
        {
            var exifTimer = Stopwatch.StartNew();
            try
            {
                // Use ExifTool command-line to get numeric orientation (ArgumentList avoids shell injection via paths).
                var startInfo = new System.Diagnostics.ProcessStartInfo
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

                using var process = System.Diagnostics.Process.Start(startInfo);
                if (process == null)
                {
                    LogImageDiagnostic($"ExifTool failed to start. Path='{imagePath}'");
                    return 1;
                }
                
                bool exited = process.WaitForExit(AppConfig.ExifToolTimeoutMs); // Wait up to configured timeout
                if (!exited)
                {
                    KillProcessAfterWaitTimeout(process);
                    LogImageDiagnostic($"ExifTool timeout. Path='{imagePath}', TimeoutMs={AppConfig.ExifToolTimeoutMs}");
                    return 1;
                }
                
                if (process.ExitCode == 0)
                {
                    string output = process.StandardOutput.ReadToEnd().Trim();
                    if (int.TryParse(output, out int orientation) && orientation >= 1 && orientation <= 8)
                    {
                        LogImageDiagnostic($"Exif orientation read. Path='{imagePath}', Orientation={orientation}, ElapsedMs={exifTimer.ElapsedMilliseconds}");
                        return orientation;
                    }
                }

                string error = process.StandardError.ReadToEnd().Trim();
                LogImageDiagnostic($"ExifTool returned no valid orientation. Path='{imagePath}', ExitCode={process.ExitCode}, StdErr='{error}'");
            }
            catch (Exception ex)
            {
                // Silent fallback
                LogImageDiagnostic($"Exif orientation read failed. Path='{imagePath}', Error='{ex.GetType().Name}: {ex.Message}'");
            }
            
            return 1; // Default orientation (normal)
        }

        /// <summary>
        /// Writes temporary diagnostics for image loading issues.
        /// </summary>
        private static void LogImageDiagnostic(string message)
        {
            Console.WriteLine($"[ImageDiag {DateTime.Now:HH:mm:ss.fff}] {message}");
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
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (Exception ex)
            {
                LogImageDiagnostic($"Failed to terminate child process after timeout. Pid={process.Id}, Error='{ex.GetType().Name}: {ex.Message}'");
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
        private static Bitmap DecodeBitmapFromBuffer(byte[] buffer)
        {
            using var ms = new MemoryStream(buffer);
            return new Bitmap(ms);
        }

        private static void TryDeleteFileIfExists(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // Best-effort cleanup.
            }
        }

        /// <summary>
        /// Loads bitmap using direct decode first, then falls back to ExifTool preview extraction.
        /// </summary>
        private Bitmap LoadBitmapWithFallback(string imagePath)
        {
            try
            {
                return new Bitmap(imagePath);
            }
            catch (Exception ex)
            {
                LogImageDiagnostic($"Direct decode failed, trying fallback preview extraction. Path='{imagePath}', Error='{ex.GetType().Name}: {ex.Message}'");
            }

            if (TryLoadBitmapViaSipsConversion(imagePath, out var sipsBitmap))
            {
                return sipsBitmap!;
            }

            if (TryLoadBitmapViaExifTool(imagePath, "-PreviewImage", out var previewBitmap))
            {
                return previewBitmap!;
            }

            if (TryLoadBitmapViaExifTool(imagePath, "-JpgFromRaw", out var rawJpegBitmap))
            {
                return rawJpegBitmap!;
            }

            throw new ArgumentException("Unable to load bitmap from provided data");
        }

        /// <summary>
        /// Attempts high-quality conversion through macOS sips and decodes the resulting JPEG.
        /// </summary>
        private bool TryLoadBitmapViaSipsConversion(string imagePath, out Bitmap? bitmap)
        {
            bitmap = null;
            var timer = Stopwatch.StartNew();
            string tempJpegPath = Path.Combine(Path.GetTempPath(), $"photosorter-{Guid.NewGuid():N}.jpg");

            try
            {
                var startInfo = new System.Diagnostics.ProcessStartInfo
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

                using var process = System.Diagnostics.Process.Start(startInfo);
                if (process == null)
                {
                    LogImageDiagnostic($"sips conversion failed to start. Path='{imagePath}'");
                    return false;
                }

                // Read stdout/stderr concurrently so the child cannot deadlock on full pipe buffers while we wait.
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
                    LogImageDiagnostic($"sips conversion timeout. Path='{imagePath}', TimeoutMs={AppConfig.ExifToolTimeoutMs}");
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
                    LogImageDiagnostic($"sips conversion output read failed. Path='{imagePath}', Error='{readEx.GetType().Name}: {readEx.Message}'");
                    return false;
                }

                if (process.ExitCode != 0 || !File.Exists(tempJpegPath))
                {
                    LogImageDiagnostic($"sips conversion unavailable. Path='{imagePath}', ExitCode={process.ExitCode}, StdOut='{stdOut}', StdErr='{stdErr}'");
                    return false;
                }

                byte[] bytes = File.ReadAllBytes(tempJpegPath);
                bitmap = DecodeBitmapFromBuffer(bytes);
                LogImageDiagnostic($"sips conversion success. Path='{imagePath}', OutputBytes={bytes.Length}, ElapsedMs={timer.ElapsedMilliseconds}");
                return true;
            }
            catch (Exception ex)
            {
                LogImageDiagnostic($"sips conversion failed. Path='{imagePath}', Error='{ex.GetType().Name}: {ex.Message}'");
                return false;
            }
            finally
            {
                TryDeleteFileIfExists(tempJpegPath);
            }
        }

        /// <summary>
        /// Attempts to extract a JPEG preview using ExifTool and decode it as an Avalonia bitmap.
        /// </summary>
        private bool TryLoadBitmapViaExifTool(string imagePath, string exifArgument, out Bitmap? bitmap)
        {
            bitmap = null;
            var timer = Stopwatch.StartNew();

            try
            {
                var startInfo = new System.Diagnostics.ProcessStartInfo
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

                using var process = System.Diagnostics.Process.Start(startInfo);
                if (process == null)
                {
                    LogImageDiagnostic($"Fallback decode failed to start exiftool. Path='{imagePath}', Mode='{exifArgument}'");
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

                        LogImageDiagnostic($"Fallback decode timeout. Path='{imagePath}', Mode='{exifArgument}', TimeoutMs={AppConfig.ExifToolTimeoutMs}");
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
                        LogImageDiagnostic($"Fallback decode unavailable. Path='{imagePath}', Mode='{exifArgument}', ExitCode={process.ExitCode}, Bytes={memoryStream.Length}, StdErr='{stdErr}'");
                        return false;
                    }

                    byte[] buffer = memoryStream.ToArray();
                    bitmap = DecodeBitmapFromBuffer(buffer);
                    LogImageDiagnostic($"Fallback decode success. Path='{imagePath}', Mode='{exifArgument}', Bytes={buffer.Length}, ElapsedMs={timer.ElapsedMilliseconds}");
                    return true;
                }
                finally
                {
                    memoryStream.Dispose();
                }
            }
            catch (Exception ex)
            {
                LogImageDiagnostic($"Fallback decode failed. Path='{imagePath}', Mode='{exifArgument}', Error='{ex.GetType().Name}: {ex.Message}'");
                return false;
            }
        }
        
        /// <summary>
        /// Applies automatic rotation based on EXIF orientation metadata using ExifTool.
        /// </summary>
        /// <param name="imagePath">The path to the image file.</param>
        private void ApplyExifOrientation(string imagePath)
        {
            int exifOrientation = GetExifOrientation(imagePath);
            
            // Map EXIF orientation to rotation degrees
            double rotationDegrees = exifOrientation switch
            {
                1 => 0,    // Normal
                3 => 180,  // Rotated 180°
                6 => 90,   // Rotated 90° clockwise
                8 => 270,  // Rotated 270° clockwise (90° counter-clockwise)
                _ => 0     // Default
            };
            
            // Always apply rotation (even if 0°) to reset from previous photo
            _currentRotation = rotationDegrees;
            ApplyRotation();
        }
        
        /// <summary>
        /// Updates the statistics display.
        /// </summary>
        private void UpdateStatistics()
        {
            StatsText.Text = $"Total: {_totalPhotos}\n" +
                            $"Good: {_goodCount}\n" +
                            $"Very Good: {_veryGoodCount}\n" +
                            $"Sorted Out: {_sortedOutCount}";
            
            ProgressText.Text = $"{_currentIndex + 1}/{_totalPhotos}";
            ProgressBar.Value = CalculateProgressPercentage();
        }
        
        /// <summary>
        /// Calculates the progress percentage for the progress bar.
        /// </summary>
        /// <returns>The progress percentage (0-100).</returns>
        private double CalculateProgressPercentage()
        {
            return _totalPhotos > 0 ? (double)(_currentIndex + 1) / _totalPhotos * 100 : 0;
        }
        
        #endregion
        
        #region Zoom Methods
        
        /// <summary>
        /// Zooms in on the current image.
        /// </summary>
        private void ZoomIn()
        {
            _currentScale = Math.Min(_currentScale * AppConfig.ZoomFactor, AppConfig.MaxZoomScale);
            ApplyZoom();
        }
        
        /// <summary>
        /// Zooms out from the current image.
        /// </summary>
        private void ZoomOut()
        {
            _currentScale = Math.Max(_currentScale / AppConfig.ZoomFactor, AppConfig.MinZoomScale);
            ApplyZoom();
        }
        
        /// <summary>
        /// Applies the current zoom scale to the image transform.
        /// </summary>
        private void ApplyZoom()
        {
            if (CurrentImage.RenderTransform is not TransformGroup transformGroup) return;
            
            if (transformGroup.Children[0] is ScaleTransform scaleTransform)
            {
                scaleTransform.ScaleX = _currentScale;
                scaleTransform.ScaleY = _currentScale;
            }
            
            ClampCurrentTranslation();
            
            // Update the Viewbox display after zoom
            UpdateViewboxDisplay();
        }
        
        /// <summary>
        /// Applies the current zoom and rotation to the image.
        /// </summary>
        private void ApplyCurrentZoomAndRotation()
        {
            ApplyZoom();
            ApplyRotation();
        }
        
        /// <summary>
        /// Resets zoom and rotation to their default values.
        /// </summary>
        private void ResetZoom()
        {
            _currentScale = AppConfig.DefaultZoomScale;
            _currentRotation = AppConfig.DefaultRotation;
            ApplyCurrentZoomAndRotation();
            ResetTranslation();
        }
        
        /// <summary>
        /// Resets translation to center the image.
        /// </summary>
        private void ResetTranslation()
        {
            if (CurrentImage.RenderTransform is TransformGroup transformGroup &&
                transformGroup.Children[1] is TranslateTransform translateTransform)
            {
                translateTransform.X = 0;
                translateTransform.Y = 0;
            }
        }
        
        /// <summary>
        /// Clamps the current translation to keep the image within bounds.
        /// </summary>
        private void ClampCurrentTranslation()
        {
            if (CurrentImage.RenderTransform is not TransformGroup transformGroup) return;
            if (transformGroup.Children[1] is not TranslateTransform translateTransform) return;
            
            double x = translateTransform.X;
            double y = translateTransform.Y;
            ClampTranslation(ref x, ref y);
            translateTransform.X = x;
            translateTransform.Y = y;
        }
        
        /// <summary>
        /// Applies translation with bounds checking.
        /// </summary>
        /// <param name="deltaX">The X translation delta.</param>
        /// <param name="deltaY">The Y translation delta.</param>
        private void ApplyTranslationWithBounds(double deltaX, double deltaY)
        {
            if (CurrentImage.RenderTransform is not TransformGroup transformGroup) return;
            if (transformGroup.Children[1] is not TranslateTransform translateTransform) return;
            
            double newX = translateTransform.X + deltaX;
            double newY = translateTransform.Y + deltaY;
            
            ClampTranslation(ref newX, ref newY);
            
            translateTransform.X = newX;
            translateTransform.Y = newY;
        }
        
        /// <summary>
        /// Clamps translation values to keep the image within bounds.
        /// </summary>
        /// <param name="x">The X translation value to clamp.</param>
        /// <param name="y">The Y translation value to clamp.</param>
        private void ClampTranslation(ref double x, ref double y)
        {
            if (CurrentImage.Source is not Bitmap imageSource) return;
            
            double imageWidth = imageSource.Size.Width * _currentScale;
            double imageHeight = imageSource.Size.Height * _currentScale;
            
            double containerWidth = CurrentImage.Bounds.Width;
            double containerHeight = CurrentImage.Bounds.Height;
            
            // If image is smaller than container, center it
            if (imageWidth <= containerWidth)
            {
                x = 0;
            }
            else
            {
                // Calculate maximum allowed translation
                double maxX = (imageWidth - containerWidth) / 2;
                x = Math.Clamp(x, -maxX, maxX);
            }
            
            if (imageHeight <= containerHeight)
            {
                y = 0;
            }
            else
            {
                // Calculate maximum allowed translation
                double maxY = (imageHeight - containerHeight) / 2;
                y = Math.Clamp(y, -maxY, maxY);
            }
        }
        
        #endregion
        
        #region Rotation Methods
        
        /// <summary>
        /// Rotates the image clockwise by 90 degrees.
        /// </summary>
        private void RotateClockwise()
        {
            _currentRotation = (_currentRotation + AppConfig.RotationStep) % 360;
            ApplyRotation();
        }
        
        /// <summary>
        /// Rotates the image counter-clockwise by 90 degrees.
        /// </summary>
        private void RotateCounterClockwise()
        {
            _currentRotation = (_currentRotation - AppConfig.RotationStep) % 360;
            if (_currentRotation < 0) _currentRotation += 360;
            ApplyRotation();
        }
        
        /// <summary>
        /// Applies the current rotation to the image transform.
        /// </summary>
        private void ApplyRotation()
        {
            if (CurrentImage.RenderTransform is not TransformGroup transformGroup) return;
            if (transformGroup.Children.Count <= 2) return;
            if (transformGroup.Children[2] is not RotateTransform rotateTransform) return;
            
            rotateTransform.Angle = _currentRotation;
            
            // Update the Viewbox display after rotation
            UpdateViewboxDisplay();
        }
        
        /// <summary>
        /// Updates the Viewbox to ensure proper display after zoom/rotation changes.
        /// </summary>
        private void UpdateViewboxDisplay()
        {
            if (_imageContainer == null || CurrentImage.Source == null) return;
            
            // Wait for layout to update
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                try
                {
                    // Force the Viewbox to recalculate its layout
                    _imageContainer.InvalidateMeasure();
                    _imageContainer.InvalidateArrange();
                }
                catch
                {
                    // Silent fallback
                }
            }, DispatcherPriority.Background);
        }
        
        #endregion
        
        #region Navigation Methods
        
        /// <summary>
        /// Moves to the next photo in the list.
        /// </summary>
        private void MoveToNext()
        {
            if (_currentIndex < _photos.Count - 1)
            {
                _currentIndex++;
                UpdateDisplay();
                FileText.Text = "→ Next photo";
            }
            else if (_currentIndex == _photos.Count - 1)
            {
                FileText.Text = "Last photo";
            }
        }
        
        /// <summary>
        /// Moves to the previous photo in the list.
        /// </summary>
        private void MoveToPrevious()
        {
            if (_currentIndex > 0)
            {
                _currentIndex--;
                UpdateDisplay();
                FileText.Text = "← Previous photo";
            }
            else if (_currentIndex == 0)
            {
                FileText.Text = "First photo";
            }
        }
        
        #endregion
        
        #region Sorting Methods
        
        /// <summary>
        /// Sorts the current photo into the "sorted out" folder.
        /// </summary>
        private void SortOutCurrent()
        {
            MovePhotoToFolder(_sortedOutFolder, ref _sortedOutCount, "Sorted out");
        }
        
        /// <summary>
        /// Sorts the current photo into the "good" folder.
        /// </summary>
        private void MoveToGood()
        {
            MovePhotoToFolder(_goodFolder, ref _goodCount, "Good");
        }
        
        /// <summary>
        /// Sorts the current photo into the "very good" folder.
        /// </summary>
        private void MoveToVeryGood()
        {
            MovePhotoToFolder(_veryGoodFolder, ref _veryGoodCount, "Very Good");
        }
        
        /// <summary>
        /// Moves the current photo to the specified folder.
        /// </summary>
        /// <param name="destinationFolder">The destination folder path.</param>
        /// <param name="counter">The counter to increment.</param>
        /// <param name="actionName">The name of the action for feedback.</param>
        private void MovePhotoToFolder(string destinationFolder, ref int counter, string actionName)
        {
            if (_currentIndex >= _photos.Count) return;
            
            string currentPhoto = _photos[_currentIndex];
            string fileName = Path.GetFileName(currentPhoto);
            string destination = Path.Combine(destinationFolder, fileName);
            
            try
            {
                File.Move(currentPhoto, destination);
                counter++;
                
                // Clear current image source before disposing to prevent accessing disposed bitmap
                CurrentImage.Source = null;
                
                // Remove from cache and LRU list
                lock (_imageCache)
                {
                    if (_imageCache.TryGetValue(currentPhoto, out var bitmap))
                    {
                        bitmap.Dispose();
                        _imageCache.Remove(currentPhoto);
                        
                        // Also remove from LRU list
                        lock (_lruList)
                        {
                            _lruList.Remove(currentPhoto);
                        }
                    }
                }
                
                lock (_loadingImages)
                {
                    _loadingImages.Remove(currentPhoto);
                }
                
                // Remove the moved photo from the list
                _photos.RemoveAt(_currentIndex);
                
                // Update total count
                _totalPhotos = _photos.Count;
                
                // If we're at the end of the list after removal, stay at current position
                if (_currentIndex >= _photos.Count && _photos.Count > 0)
                {
                    _currentIndex = _photos.Count - 1;
                }
                
                UpdateDisplay();
                FileText.Text = $"{fileName} → {actionName}";
            }
            catch (Exception ex)
            {
                ShowError($"Error moving file: {ex.Message}");
            }
        }
        
        #endregion
        
        #region Keyboard Handlers
        
        /// <summary>
        /// Handles keyboard input for navigation, sorting, and image manipulation.
        /// 
        /// Navigation:
        ///   ← → ↑ ↓     - Navigate between photos
        ///   Shift+←     - Sort out (move to sortedout folder)
        ///   Shift+→     - Good (move to good folder)
        ///   Shift+↑     - Very Good (move to verygood folder)
        ///   
        /// Image Manipulation:
        ///   Q           - Rotate left (counter-clockwise)
        ///   E           - Rotate right (clockwise)
        ///   + / -       - Zoom in/out
        ///   0           - Reset zoom
        ///   
        /// Application:
        ///   H           - Toggle help overlay
        ///   Escape      - Quit with statistics
        /// </summary>
        private void OnKeyDown(object? sender, KeyEventArgs e)
        {
            bool shiftPressed = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
            
            switch (e.Key)
            {
                case Key.Left:
                    if (shiftPressed)
                    {
                        SortOutCurrent();
                    }
                    else
                    {
                        MoveToPrevious();
                    }
                    e.Handled = true;
                    break;
                    
                case Key.Right:
                    if (shiftPressed)
                    {
                        MoveToGood();
                    }
                    else
                    {
                        MoveToNext();
                    }
                    e.Handled = true;
                    break;
                    
                case Key.Up:
                    if (shiftPressed)
                    {
                        MoveToVeryGood();
                    }
                    else
                    {
                        MoveToNext();
                    }
                    e.Handled = true;
                    break;
                    
                case Key.Down:
                    if (!shiftPressed)
                    {
                        MoveToPrevious();
                        e.Handled = true;
                    }
                    break;
                    
                case Key.Q:
                    RotateCounterClockwise();
                    e.Handled = true;
                    break;
                    
                case Key.E:
                    RotateClockwise();
                    e.Handled = true;
                    break;
                    
                case Key.H:
                    InstructionsOverlay.IsVisible = !InstructionsOverlay.IsVisible;
                    e.Handled = true;
                    break;
                    
                case Key.Add:
                case Key.W:
                    ZoomIn();
                    e.Handled = true;
                    break;
                    
                case Key.Subtract:
                case Key.S:
                    ZoomOut();
                    e.Handled = true;
                    break;
                    
                case Key.D0:
                case Key.NumPad0:
                    ResetZoom();
                    e.Handled = true;
                    break;
                    
                case Key.Escape:
                    ShowFinalStatistics();
                    Close();
                    e.Handled = true;
                    break;
            }
        }
        
        #endregion
        
        #region UI Event Handlers
        
        private void SortOut_Click(object? sender, RoutedEventArgs e) => SortOutCurrent();
        private void Good_Click(object? sender, RoutedEventArgs e) => MoveToGood();
        private void VeryGood_Click(object? sender, RoutedEventArgs e) => MoveToVeryGood();
        private void Quit_Click(object? sender, RoutedEventArgs e) { ShowFinalStatistics(); Close(); }
        
        private void StartSorting_Click(object? sender, RoutedEventArgs e)
        {
            InstructionsOverlay.IsVisible = false;
            
            if (_photos.Count == 0)
            {
                ShowError("No .DNG files found in the source folder.");
            }
        }
        
        #endregion
        
        #region Utility Methods
        
        /// <summary>
        /// Shows final statistics in a dialog window, including persistent statistics.
        /// </summary>
        private void ShowFinalStatistics()
        {
            // Merge current session statistics with persistent statistics
            var mergedStats = StatisticsManager.MergeStatistics(
                _persistentStats,
                _goodCount,
                _veryGoodCount,
                _sortedOutCount,
                _totalPhotos);
            
            // Save the merged statistics
            StatisticsManager.SaveStatistics(mergedStats);
            
            string stats = $"📊 FINAL STATISTICS\n" +
                          $"==================\n\n" +
                          
                          $"📈 Current Session:\n" +
                          $"   • Total photos: {_totalPhotos}\n" +
                          $"   • Sorted out: {_sortedOutCount}\n" +
                          $"   • Good: {_goodCount}\n" +
                          $"   • Very Good: {_veryGoodCount}\n\n" +
                          
                          $"📊 Persistent Statistics (All Sessions):\n" +
                          $"   • Total photos processed: {mergedStats.TotalPhotosProcessed}\n" +
                          $"   • Total sorted out: {mergedStats.SortedOutCount}\n" +
                          $"   • Total good: {mergedStats.GoodCount}\n" +
                          $"   • Total very good: {mergedStats.VeryGoodCount}\n" +
                          $"   • Session count: {mergedStats.SessionCount}\n" +
                          $"   • Last session: {mergedStats.LastSessionDate:yyyy-MM-dd HH:mm}\n\n" +
                          
                          $"📁 Folder Locations:\n" +
                          $"   • Sorted out: {_sortedOutFolder}\n" +
                          $"   • Good: {_goodFolder}\n" +
                          $"   • Very Good: {_veryGoodFolder}\n\n" +
                          
                          $"💾 Statistics saved to:\n" +
                          $"   {StatisticsManager.GetStatisticsFilePath()}";
            
            var dialog = new Window()
            {
                Title = "Photo Sorter - Complete",
                Width = 600,
                Height = 450,
                Content = new ScrollViewer
                {
                    Content = new TextBlock 
                    { 
                        Text = stats,
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
                        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                        Margin = new Thickness(20)
                    }
                }
            };
            dialog.ShowDialog(this);
        }
        
        /// <summary>
        /// Shows an error message in a dialog window.
        /// </summary>
        /// <param name="message">The error message to display.</param>
        private void ShowError(string message)
        {
            var dialog = new Window()
            {
                Title = "Error",
                Width = 400,
                Height = 200,
                Content = new TextBlock 
                { 
                    Text = message,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap
                }
            };
            dialog.ShowDialog(this);
        }
        
        #endregion
    }
}
               