using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PhotoSorterAvalonia
{
    public partial class MainWindow : Window
    {

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
            
            // Keep zoom and pan across photos (same as scale); clamp after bitmap layout for each image.
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
            ReplaceCurrentImageSource(null);
        }
        
        /// <summary>
        /// Swaps the image control source and disposes the previous bitmap only when it is not held in a cache.
        /// </summary>
        private void ReplaceCurrentImageSource(IImage? newSource)
        {
            var old = CurrentImage.Source;
            CurrentImage.Source = newSource;
            if (old is Bitmap oldBmp && !IsBitmapHeldByAnyCache(oldBmp))
                oldBmp.Dispose();
        }
        
        private bool IsBitmapHeldByAnyCache(Bitmap bmp)
        {
            lock (_imageCache)
            {
                foreach (var kv in _imageCache)
                {
                    if (ReferenceEquals(kv.Value, bmp))
                        return true;
                }
            }
            lock (_previewCache)
            {
                foreach (var kv in _previewCache)
                {
                    if (ReferenceEquals(kv.Value, bmp))
                        return true;
                }
            }
            return false;
        }
        
        /// <summary>Clears decoded image caches (e.g. after switching the working folder).</summary>
        private void ClearAllImageCaches()
        {
            ReplaceCurrentImageSource(null);
            
            lock (_imageCache)
            {
                foreach (var kv in _imageCache)
                    kv.Value.Dispose();
                _imageCache.Clear();
            }
            lock (_lruList)
                _lruList.Clear();
            lock (_loadingImages)
                _loadingImages.Clear();
            
            lock (_previewCache)
            {
                foreach (var kv in _previewCache)
                    kv.Value.Dispose();
                _previewCache.Clear();
            }
            lock (_previewLruList)
                _previewLruList.Clear();
            lock (_loadingPreviewPaths)
                _loadingPreviewPaths.Clear();
        }
        
        private bool IsStillCurrentImagePath(string imagePath)
        {
            string? expected = _currentIndex >= 0 && _currentIndex < _photos.Count
                ? _photos[_currentIndex]
                : null;
            return string.Equals(expected, imagePath, StringComparison.Ordinal);
        }
        
        /// <summary>
        /// Stores an EXIF-normalized preview bitmap (same instance may be shown on <see cref="CurrentImage"/>).
        /// </summary>
        private void AddOrReplacePreviewInCache(string imagePath, Bitmap normalizedBitmap)
        {
            lock (_previewCache)
            {
                if (_previewCache.TryGetValue(imagePath, out var previous) && !ReferenceEquals(previous, normalizedBitmap))
                {
                    if (!ReferenceEquals(CurrentImage.Source, previous))
                        previous.Dispose();
                }
                
                _previewCache[imagePath] = normalizedBitmap;
                lock (_previewLruList)
                {
                    _previewLruList.Remove(imagePath);
                    _previewLruList.AddFirst(imagePath);
                }
                
                EnforcePreviewCacheSizeLimit();
            }
        }
        
        private void EnforcePreviewCacheSizeLimit()
        {
            lock (_previewCache)
            {
                lock (_previewLruList)
                {
                    while (_previewCache.Count > AppConfig.MaxPreviewCacheSize && _previewLruList.Last != null)
                    {
                        string victim = _previewLruList.Last.Value;
                        if (!_previewCache.TryGetValue(victim, out var bmp))
                        {
                            _previewLruList.RemoveLast();
                            continue;
                        }
                        
                        if (ReferenceEquals(CurrentImage.Source, bmp))
                        {
                            _previewLruList.RemoveLast();
                            _previewLruList.AddFirst(victim);
                            if (_previewLruList.Last != null &&
                                string.Equals(_previewLruList.Last.Value, victim, StringComparison.Ordinal))
                                return;
                            continue;
                        }
                        
                        bmp.Dispose();
                        _previewCache.Remove(victim);
                        _previewLruList.RemoveLast();
                    }
                }
            }
        }
        
        /// <summary>
        /// Drops a preview entry after the full-resolution image is cached or the file is gone.
        /// </summary>
        private void RemovePreviewForPath(string imagePath)
        {
            lock (_previewCache)
            {
                if (!_previewCache.TryGetValue(imagePath, out var bmp))
                    return;
                if (!ReferenceEquals(CurrentImage.Source, bmp))
                    bmp.Dispose();
                _previewCache.Remove(imagePath);
                lock (_previewLruList)
                    _previewLruList.Remove(imagePath);
            }
        }
        
        /// <summary>
        /// Shows a downscaled, EXIF-corrected preview before the full decode finishes and registers it in <see cref="_previewCache"/>.
        /// </summary>
        private void ApplyTransientPreviewOnUiThread(string imagePath, Bitmap decodedPreview, int exifOrientation)
        {
            if (!IsStillCurrentImagePath(imagePath))
            {
                decodedPreview.Dispose();
                return;
            }
            
            Bitmap normalized = BitmapOrientationHelper.NormalizeBitmapExifOnUiThread(decodedPreview, exifOrientation);
            if (!IsStillCurrentImagePath(imagePath))
            {
                normalized.Dispose();
                return;
            }
            
            ReplaceCurrentImageSource(normalized);
            AddOrReplacePreviewInCache(imagePath, normalized);
            ImageDecoder.LogDiagnostic($"Preview bitmap shown. Path='{imagePath}'");
            ResetRotationForUprightPixels();
            ScheduleClampTranslationAfterLayout();
        }
        
        /// <summary>
        /// Shows a placeholder when no fast preview decode is available.
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
                ReplaceCurrentImageSource(drawingImage);
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
                ImageDecoder.LogDiagnostic($"Load requested. Index={_currentIndex}, Path='{imagePath}', Exists={fileExists}, SizeBytes={fileSize}");

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
                        ReplaceCurrentImageSource(cachedBitmap);
                        ImageDecoder.LogDiagnostic($"Cache hit. Path='{imagePath}', ElapsedMs={loadTimer.ElapsedMilliseconds}");
                        
                        ResetRotationForUprightPixels();
                        ScheduleClampTranslationAfterLayout();
                        
                        return;
                    }
                }
                
                Bitmap? previewFromCache = null;
                lock (_previewCache)
                {
                    if (_previewCache.TryGetValue(imagePath, out var cachedPreview))
                    {
                        previewFromCache = cachedPreview;
                        lock (_previewLruList)
                        {
                            _previewLruList.Remove(imagePath);
                            _previewLruList.AddFirst(imagePath);
                        }
                    }
                }
                
                if (previewFromCache != null)
                {
                    ReplaceCurrentImageSource(previewFromCache);
                    ImageDecoder.LogDiagnostic($"Preview cache hit. Path='{imagePath}', ElapsedMs={loadTimer.ElapsedMilliseconds}");
                    ResetRotationForUprightPixels();
                    ScheduleClampTranslationAfterLayout();
                    
                    Task.Run(async () =>
                    {
                        int loadSession = Volatile.Read(ref _folderSession);
                        var decodeTimer = Stopwatch.StartNew();
                        try
                        {
                            int exifOrientation = await Task.Run(() => ImageDecoder.GetExifOrientation(imagePath)).ConfigureAwait(false);
                            var bitmap = await Task.Run(() => ImageDecoder.LoadBitmapWithFallback(imagePath)).ConfigureAwait(false);
                            ImageDecoder.LogDiagnostic($"Full decode after preview hit. Path='{imagePath}', ElapsedMs={decodeTimer.ElapsedMilliseconds}");
                            if (Volatile.Read(ref _folderSession) != loadSession)
                            {
                                bitmap.Dispose();
                                return;
                            }
                            
                            await Dispatcher.UIThread.InvokeAsync(() =>
                            {
                                try
                                {
                                    if (Volatile.Read(ref _folderSession) != loadSession)
                                    {
                                        bitmap.Dispose();
                                        return;
                                    }
                                    
                                    FinishImageDecodeOnUiThread(imagePath, bitmap, exifOrientation, loadTimer);
                                }
                                catch (Exception ex)
                                {
                                    ImageDecoder.LogDiagnostic($"UI finalize decode failed. Path='{imagePath}', Error='{ex.GetType().Name}: {ex.Message}'");
                                    bitmap.Dispose();
                                }
                            });
                        }
                        catch (Exception ex)
                        {
                            ImageDecoder.LogDiagnostic($"Decode failed. Path='{imagePath}', Error='{ex.GetType().Name}: {ex.Message}'");
                        }
                    });
                    return;
                }
                
                // Clear previous transient preview; full decode + optional fast preview run on a worker.
                ImageDecoder.LogDiagnostic($"Cache miss. Starting preview/full decode. Path='{imagePath}'");
                ReplaceCurrentImageSource(null);
                
                Task.Run(async () =>
                {
                    int loadSession = Volatile.Read(ref _folderSession);
                    var decodeTimer = Stopwatch.StartNew();
                    try
                    {
                        var previewTask = Task.Run(() =>
                        {
                            try
                            {
                                return ImageDecoder.LoadBitmapWithFallback(imagePath, AppConfig.PreviewDecodeMaxWidth);
                            }
                            catch (Exception ex)
                            {
                                ImageDecoder.LogDiagnostic($"Preview decode failed (full-size load continues). Path='{imagePath}', Error='{ex.GetType().Name}: {ex.Message}'");
                                return null;
                            }
                        });
                        var orientationTask = Task.Run(() => ImageDecoder.GetExifOrientation(imagePath));
                        await Task.WhenAll(previewTask, orientationTask).ConfigureAwait(false);
                        Bitmap? previewBitmap = previewTask.Result;
                        int exifOrientation = orientationTask.Result;
                        
                        if (Volatile.Read(ref _folderSession) != loadSession)
                        {
                            previewBitmap?.Dispose();
                            return;
                        }
                        
                        if (previewBitmap != null)
                        {
                            await Dispatcher.UIThread.InvokeAsync(() =>
                            {
                                try
                                {
                                    if (Volatile.Read(ref _folderSession) != loadSession)
                                    {
                                        previewBitmap.Dispose();
                                        return;
                                    }
                                    
                                    ApplyTransientPreviewOnUiThread(imagePath, previewBitmap, exifOrientation);
                                }
                                catch (Exception ex)
                                {
                                    ImageDecoder.LogDiagnostic($"Preview UI apply failed. Path='{imagePath}', Error='{ex.GetType().Name}: {ex.Message}'");
                                    previewBitmap.Dispose();
                                }
                            });
                        }
                        else
                        {
                            await Dispatcher.UIThread.InvokeAsync(() =>
                            {
                                if (Volatile.Read(ref _folderSession) != loadSession)
                                    return;
                                if (IsStillCurrentImagePath(imagePath))
                                    ShowImagePlaceholder(imagePath);
                            });
                        }
                        
                        if (Volatile.Read(ref _folderSession) != loadSession)
                            return;
                        
                        var bitmap = ImageDecoder.LoadBitmapWithFallback(imagePath);
                        ImageDecoder.LogDiagnostic($"Decode success. Path='{imagePath}', ExifOrientation={exifOrientation}, ElapsedMs={decodeTimer.ElapsedMilliseconds}");
                        
                        if (Volatile.Read(ref _folderSession) != loadSession)
                        {
                            bitmap.Dispose();
                            return;
                        }
                        
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            try
                            {
                                if (Volatile.Read(ref _folderSession) != loadSession)
                                {
                                    bitmap.Dispose();
                                    return;
                                }
                                
                                FinishImageDecodeOnUiThread(imagePath, bitmap, exifOrientation, loadTimer);
                            }
                            catch (Exception ex)
                            {
                                ImageDecoder.LogDiagnostic($"UI finalize decode failed. Path='{imagePath}', Error='{ex.GetType().Name}: {ex.Message}'");
                                bitmap.Dispose();
                            }
                        });
                    }
                    catch (Exception ex)
                    {
                        ImageDecoder.LogDiagnostic($"Decode failed. Path='{imagePath}', Error='{ex.GetType().Name}: {ex.Message}'");
                    }
                });
            }
            catch (Exception ex)
            {
                ImageDecoder.LogDiagnostic($"LoadImageWithCache failed before decode. Path='{imagePath}', Error='{ex.GetType().Name}: {ex.Message}'");
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
                    while (_imageCache.Count > AppConfig.MaxFullImageCacheSize && _lruList.Count > 0)
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
        /// Preloads downscaled previews for a wide window around the current index (full-res loads happen on demand).
        /// </summary>
        private void PreloadAdjacentImages()
        {
            string[] photosSnapshot = _photos.ToArray();
            if (photosSnapshot.Length <= 1)
                return;
            int currentIndexSnapshot = Math.Clamp(_currentIndex, 0, photosSnapshot.Length - 1);
            
            int session = Volatile.Read(ref _folderSession);
            Task.Run(() =>
            {
                var scheduledIndices = new HashSet<int>();
                for (int offset = -AppConfig.PreviewPreloadRange; offset <= AppConfig.PreviewPreloadRange; offset++)
                {
                    if (Volatile.Read(ref _folderSession) != session)
                        return;
                    
                    if (offset == 0)
                        continue;
                    
                    int targetIndex = ((currentIndexSnapshot + offset) % photosSnapshot.Length + photosSnapshot.Length) % photosSnapshot.Length;
                    if (!scheduledIndices.Add(targetIndex))
                        continue;
                    
                    string imagePath = photosSnapshot[targetIndex];
                    
                    lock (_imageCache)
                    {
                        if (_imageCache.ContainsKey(imagePath))
                            continue;
                    }
                    
                    lock (_previewCache)
                    {
                        if (_previewCache.ContainsKey(imagePath))
                            continue;
                    }
                    
                    lock (_loadingPreviewPaths)
                    {
                        if (_loadingPreviewPaths.Contains(imagePath))
                            continue;
                        _loadingPreviewPaths.Add(imagePath);
                    }
                    
                    Task.Run(() =>
                    {
                        if (Volatile.Read(ref _folderSession) != session)
                            return;
                        
                        var preloadTimer = Stopwatch.StartNew();
                        Bitmap? previewRaw = null;
                        try
                        {
                            previewRaw = ImageDecoder.LoadBitmapWithFallback(imagePath, AppConfig.PreviewDecodeMaxWidth);
                        }
                        catch (Exception ex)
                        {
                            ImageDecoder.LogDiagnostic($"Preview preload decode failed. Path='{imagePath}', Error='{ex.GetType().Name}: {ex.Message}'");
                            lock (_loadingPreviewPaths)
                                _loadingPreviewPaths.Remove(imagePath);
                            return;
                        }
                        
                        if (Volatile.Read(ref _folderSession) != session)
                        {
                            previewRaw?.Dispose();
                            lock (_loadingPreviewPaths)
                                _loadingPreviewPaths.Remove(imagePath);
                            return;
                        }
                        
                        int exifOrientation = ImageDecoder.GetExifOrientation(imagePath);
                        
                        Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            try
                            {
                                if (Volatile.Read(ref _folderSession) != session)
                                {
                                    previewRaw?.Dispose();
                                    lock (_loadingPreviewPaths)
                                        _loadingPreviewPaths.Remove(imagePath);
                                    return;
                                }
                                
                                lock (_imageCache)
                                {
                                    if (_imageCache.ContainsKey(imagePath))
                                    {
                                        previewRaw!.Dispose();
                                        return;
                                    }
                                }
                                
                                lock (_previewCache)
                                {
                                    if (_previewCache.ContainsKey(imagePath))
                                    {
                                        previewRaw!.Dispose();
                                        return;
                                    }
                                }
                                
                                Bitmap normalized = BitmapOrientationHelper.NormalizeBitmapExifOnUiThread(previewRaw!, exifOrientation);
                                AddOrReplacePreviewInCache(imagePath, normalized);
                                ImageDecoder.LogDiagnostic($"Preview preload cached. Path='{imagePath}', ElapsedMs={preloadTimer.ElapsedMilliseconds}");
                            }
                            catch (Exception ex)
                            {
                                ImageDecoder.LogDiagnostic($"Preview preload UI failed. Path='{imagePath}', Error='{ex.GetType().Name}: {ex.Message}'");
                                try
                                {
                                    previewRaw?.Dispose();
                                }
                                catch (ObjectDisposedException)
                                {
                                }
                            }
                            finally
                            {
                                lock (_loadingPreviewPaths)
                                    _loadingPreviewPaths.Remove(imagePath);
                            }
                        });
                    });
                }
            });
        }
        
        /// <summary>
        /// EXIF orientation is baked into decoded bitmaps on the UI thread; reset transform rotation for the new image.
        /// </summary>
        private void ResetRotationForUprightPixels()
        {
            _currentRotation = AppConfig.DefaultRotation;
            ApplyRotation();
        }
        
        
        /// <summary>
        /// Caches the EXIF-normalized bitmap and assigns it to the view when it is still the current file.
        /// </summary>
        private void FinishImageDecodeOnUiThread(string imagePath, Bitmap decoded, int exifOrientation, Stopwatch? loadTimer)
        {
            Bitmap ready = BitmapOrientationHelper.NormalizeBitmapExifOnUiThread(decoded, exifOrientation);
            
            lock (_imageCache)
            {
                if (_imageCache.ContainsKey(imagePath))
                {
                    ready.Dispose();
                    return;
                }
                
                _imageCache[imagePath] = ready;
                lock (_lruList)
                {
                    _lruList.Remove(imagePath);
                    _lruList.AddFirst(imagePath);
                }
                
                EnforceCacheSizeLimit();
            }
            
            string? currentExpectedPath = _currentIndex >= 0 && _currentIndex < _photos.Count
                ? _photos[_currentIndex]
                : null;
            if (!string.Equals(currentExpectedPath, imagePath, StringComparison.Ordinal))
            {
                ImageDecoder.LogDiagnostic($"Stale UI update candidate. Requested='{imagePath}', CurrentExpected='{currentExpectedPath}'");
                RemovePreviewForPath(imagePath);
                return;
            }
            
            ReplaceCurrentImageSource(ready);
            RemovePreviewForPath(imagePath);
            if (loadTimer != null)
            {
                ImageDecoder.LogDiagnostic($"UI image source set. Path='{imagePath}', TotalElapsedMs={loadTimer.ElapsedMilliseconds}");
            }
            
            ResetRotationForUprightPixels();
            ScheduleClampTranslationAfterLayout();
        }
        
        #endregion
    }
}
