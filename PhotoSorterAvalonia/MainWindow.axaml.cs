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
        
        #region Statistics display

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
    }
}
