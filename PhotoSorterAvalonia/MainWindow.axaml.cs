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
        
        /// <summary>Outer photo chrome; receives wheel/pan so letterboxing outside the image still works.</summary>
        private Border _photoStageBorder = null!;
        
        private bool _isPanning;
        private Point _lastPanPoint;
        
        // Persistent statistics
        private StatisticsManager.StatisticsData _persistentStats = null!;
        
        // Full-resolution cache (small LRU); preview cache (larger LRU) for fast navigation
        private readonly Dictionary<string, Bitmap> _imageCache = new();
        private readonly LinkedList<string> _lruList = new(); // Most recent at front, least recent at back
        private readonly HashSet<string> _loadingImages = new();
        
        private readonly Dictionary<string, Bitmap> _previewCache = new();
        private readonly LinkedList<string> _previewLruList = new();
        private readonly HashSet<string> _loadingPreviewPaths = new();
        
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
            _photoStageBorder = this.FindControl<Border>("PhotoStageBorder")!;
            
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
            Directory.CreateDirectory(_goodFolder);
            Directory.CreateDirectory(_veryGoodFolder);
            Directory.CreateDirectory(_sortedOutFolder);
        }
        
        /// <summary>
        /// Sets up all event handlers for the window.
        /// </summary>
        private void SetupEventHandlers()
        {
            // Keyboard handlers
            KeyDown += OnKeyDown;
            
            _photoStageBorder.PointerWheelChanged += OnPhotoStagePointerWheelChanged;
            _photoStageBorder.PointerPressed += OnPhotoStagePointerPressed;
            _photoStageBorder.PointerMoved += OnPhotoStagePointerMoved;
            _photoStageBorder.PointerReleased += OnPhotoStagePointerReleased;
            _photoStageBorder.PointerCaptureLost += OnPhotoStagePointerCaptureLost;
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
                var folderStats = StatisticsManager.ScanFolderStatistics();
                _goodCount = folderStats.GoodCount;
                _veryGoodCount = folderStats.VeryGoodCount;
                _sortedOutCount = folderStats.SortedOutCount;
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
                _photos.AddRange(Directory.GetFiles(AppConfig.SourceFolder, AppConfig.FileExtension)
                    .OrderBy(f => f));
                
                _totalPhotos = _photos.Count;
                ImageDecoder.LogDiagnostic($"LoadPhotos complete. SourceFolder='{AppConfig.SourceFolder}', Filter='{AppConfig.FileExtension}', Total={_totalPhotos}");
                UpdateStatistics();
            }
            catch (Exception ex)
            {
                ShowError($"Error loading photos: {ex.Message}");
            }
        }
        
        #endregion
        
        #region Pointer pan and trackpad zoom
        
        private double GetImageContainerUniformScale()
        {
            if (_imageContainer.Child is not Control child)
                return 1;
            
            double cw = child.Bounds.Width;
            double ch = child.Bounds.Height;
            if (cw <= 0 || ch <= 0)
                return 1;
            
            double vw = _imageContainer.Bounds.Width;
            double vh = _imageContainer.Bounds.Height;
            if (vw <= 0 || vh <= 0)
                return 1;
            
            return Math.Min(vw / cw, vh / ch);
        }
        
        private bool EventSourceIsUnderInstructionsOverlay(object? source)
        {
            if (!InstructionsOverlay.IsVisible)
                return false;
            for (Visual? v = source as Visual; v != null; v = v.GetVisualParent())
            {
                if (ReferenceEquals(v, InstructionsOverlay))
                    return true;
            }
            return false;
        }
        
        private void OnPhotoStagePointerWheelChanged(object? sender, PointerWheelEventArgs e)
        {
            if (EventSourceIsUnderInstructionsOverlay(e.Source))
                return;
            
            if (CurrentImage.Source is not Bitmap)
                return;
            
            double deltaY = e.Delta.Y;
            if (Math.Abs(deltaY) < double.Epsilon)
                return;
            
            double oldScale = _currentScale;
            double exponent = deltaY / AppConfig.WheelScrollZoomNotchDivisor;
            double factor = Math.Pow(AppConfig.ZoomFactor, exponent);
            double newScale = Math.Clamp(oldScale * factor, AppConfig.MinZoomScale, AppConfig.MaxZoomScale);
            if (Math.Abs(newScale - oldScale) < 1e-9)
                return;
            
            // Keep the point under the cursor stable while zooming (Scale about center, then Translate, then Rotate).
            // Correct delta: T += (p - c) * (s0 - s1). Using (1 - s1/s0) without *s0 is wrong and shifts the image on each wheel step.
            if (CurrentImage.RenderTransform is TransformGroup transformGroup &&
                transformGroup.Children[1] is TranslateTransform translateTransform &&
                IsRotationUprightForWheelZoom())
            {
                Size img = GetBitmapLogicalSize((Bitmap)CurrentImage.Source);
                double w = img.Width > 1e-6 ? img.Width : CurrentImage.Bounds.Width;
                double h = img.Height > 1e-6 ? img.Height : CurrentImage.Bounds.Height;
                if (w > 0 && h > 0)
                {
                    Point pos = e.GetPosition(CurrentImage);
                    double cx = w * 0.5;
                    double cy = h * 0.5;
                    bool inside = pos.X >= 0 && pos.X <= w && pos.Y >= 0 && pos.Y <= h;
                    double px = inside ? pos.X : cx;
                    double py = inside ? pos.Y : cy;
                    double dScale = oldScale - newScale;
                    translateTransform.X += (px - cx) * dScale;
                    translateTransform.Y += (py - cy) * dScale;
                }
            }
            
            _currentScale = newScale;
            ApplyZoom();
            e.Handled = true;
        }
        
        private bool IsRotationUprightForWheelZoom()
        {
            double a = NormalizeAngle360(_currentRotation);
            return a < 0.01 || a > 359.99;
        }
        
        private static double NormalizeAngle360(double deg)
        {
            deg %= 360;
            if (deg < 0)
                deg += 360;
            return deg;
        }
        
        private static Size GetBitmapLogicalSize(Bitmap bmp)
        {
            var dpi = bmp.Dpi;
            double dx = dpi.X >= 1 ? dpi.X : 96.0;
            double dy = dpi.Y >= 1 ? dpi.Y : 96.0;
            return bmp.PixelSize.ToSizeWithDpi(new Vector(dx, dy));
        }
        
        private void OnPhotoStagePointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (EventSourceIsUnderInstructionsOverlay(e.Source))
                return;
            
            if (CurrentImage.Source is not Bitmap)
                return;
            
            if (!e.GetCurrentPoint(_photoStageBorder).Properties.IsLeftButtonPressed)
                return;
            
            _isPanning = true;
            _lastPanPoint = e.GetPosition(_imageContainer);
            e.Pointer.Capture(_photoStageBorder);
        }
        
        private void OnPhotoStagePointerMoved(object? sender, PointerEventArgs e)
        {
            if (!_isPanning || e.Pointer.Captured != _photoStageBorder)
                return;
            
            var pos = e.GetPosition(_imageContainer);
            double sdx = pos.X - _lastPanPoint.X;
            double sdy = pos.Y - _lastPanPoint.Y;
            _lastPanPoint = pos;
            
            if (Math.Abs(sdx) < double.Epsilon && Math.Abs(sdy) < double.Epsilon)
                return;
            
            double u = GetImageContainerUniformScale();
            if (u <= 0)
                return;
            
            double rad = -_currentRotation * (Math.PI / 180.0);
            double cos = Math.Cos(rad);
            double sin = Math.Sin(rad);
            // Map viewbox pointer delta to translate units. Do not divide by zoom scale here: zoom is
            // already in RenderTransform; an extra /_currentScale roughly halves pan range at 2× zoom.
            double innerDx = (cos * sdx + sin * sdy) / u * AppConfig.PointerPanSpeedMultiplier;
            double innerDy = (-sin * sdx + cos * sdy) / u * AppConfig.PointerPanSpeedMultiplier;
            
            ApplyTranslationWithBounds(innerDx, innerDy);
        }
        
        private void OnPhotoStagePointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (e.Pointer.Captured == _photoStageBorder)
                e.Pointer.Capture(null);
            
            _isPanning = false;
        }
        
        private void OnPhotoStagePointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
        {
            _isPanning = false;
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
                        var decodeTimer = Stopwatch.StartNew();
                        try
                        {
                            int exifOrientation = await Task.Run(() => ImageDecoder.GetExifOrientation(imagePath)).ConfigureAwait(false);
                            var bitmap = await Task.Run(() => ImageDecoder.LoadBitmapWithFallback(imagePath)).ConfigureAwait(false);
                            ImageDecoder.LogDiagnostic($"Full decode after preview hit. Path='{imagePath}', ElapsedMs={decodeTimer.ElapsedMilliseconds}");
                            await Dispatcher.UIThread.InvokeAsync(() =>
                            {
                                try
                                {
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
                        
                        if (previewBitmap != null)
                        {
                            await Dispatcher.UIThread.InvokeAsync(() =>
                            {
                                try
                                {
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
                                if (IsStillCurrentImagePath(imagePath))
                                    ShowImagePlaceholder(imagePath);
                            });
                        }
                        
                        var bitmap = ImageDecoder.LoadBitmapWithFallback(imagePath);
                        ImageDecoder.LogDiagnostic($"Decode success. Path='{imagePath}', ExifOrientation={exifOrientation}, ElapsedMs={decodeTimer.ElapsedMilliseconds}");
                        
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            try
                            {
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
            Task.Run(() =>
            {
                for (int offset = -AppConfig.PreviewPreloadRange; offset <= AppConfig.PreviewPreloadRange; offset++)
                {
                    if (offset == 0)
                        continue;
                    
                    int targetIndex = _currentIndex + offset;
                    if (targetIndex < 0 || targetIndex >= _photos.Count)
                        continue;
                    
                    string imagePath = _photos[targetIndex];
                    
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
                        
                        int exifOrientation = ImageDecoder.GetExifOrientation(imagePath);
                        
                        Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            try
                            {
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
            if (CurrentImage.Source is not Bitmap imageSource)
                return;
            
            if (CurrentImage.Bounds.Width < 1 || CurrentImage.Bounds.Height < 1)
                return;
            
            // Logical size from pixels + DPI so clamp matches layout (Size alone can disagree with Bounds).
            Size logical = GetBitmapLogicalSize(imageSource);
            double bw = logical.Width;
            double bh = logical.Height;
            if (bw < 1e-6 || bh < 1e-6)
                return;
            
            double rot = NormalizeAngle360(_currentRotation);
            bool swapAxes = Math.Abs(rot - 90) < 1.0 || Math.Abs(rot - 270) < 1.0;
            
            double spanX = (swapAxes ? bh : bw) * _currentScale;
            double spanY = (swapAxes ? bw : bh) * _currentScale;
            
            double viewW = Math.Min(bw, CurrentImage.Bounds.Width);
            double viewH = Math.Min(bh, CurrentImage.Bounds.Height);
            
            if (spanX <= viewW)
                x = 0;
            else
            {
                double maxX = (spanX - viewW) / 2;
                x = Math.Clamp(x, -maxX, maxX);
            }
            
            if (spanY <= viewH)
                y = 0;
            else
            {
                double maxY = (spanY - viewH) / 2;
                y = Math.Clamp(y, -maxY, maxY);
            }
        }
        
        /// <summary>
        /// Re-clamps pan after a new bitmap so bounds match the new image size (layout may not be ready yet).
        /// </summary>
        private void ScheduleClampTranslationAfterLayout()
        {
            Dispatcher.UIThread.Post(() =>
            {
                ClampCurrentTranslation();
                UpdateViewboxDisplay();
            }, DispatcherPriority.Loaded);
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
                ReplaceCurrentImageSource(null);
                
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
                
                lock (_loadingPreviewPaths)
                {
                    _loadingPreviewPaths.Remove(currentPhoto);
                }
                
                RemovePreviewForPath(currentPhoto);
                
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
        private void Quit_Click(object? sender, RoutedEventArgs e)
        {
            ShowFinalStatistics();
            Close();
        }
        
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
        
        private void ShowTextDialog(string title, string text, double width, double height, bool scroll)
        {
            var textBlock = new TextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
            };
            if (scroll)
            {
                textBlock.HorizontalAlignment = HorizontalAlignment.Left;
                textBlock.VerticalAlignment = VerticalAlignment.Top;
                textBlock.Margin = new Thickness(20);
            }
            else
            {
                textBlock.HorizontalAlignment = HorizontalAlignment.Center;
                textBlock.VerticalAlignment = VerticalAlignment.Center;
            }

            object content = scroll ? new ScrollViewer { Content = textBlock } : textBlock;
            var dialog = new Window
            {
                Title = title,
                Width = width,
                Height = height,
                Content = content,
            };
            dialog.ShowDialog(this);
        }
        
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
            
            ShowTextDialog("Photo Sorter - Complete", stats, 600, 450, scroll: true);
        }
        
        /// <summary>
        /// Shows an error message in a dialog window.
        /// </summary>
        /// <param name="message">The error message to display.</param>
        private void ShowError(string message) =>
            ShowTextDialog("Error", message, 400, 200, scroll: false);
        
        #endregion
    }
}
