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
    /// <summary>
    /// Main window for the Photo Sorter application.
    /// Allows sorting photos into three categories using keyboard shortcuts and mouse gestures.
    /// </summary>
    public partial class MainWindow : Window
    {
        #region Private Fields
        
        private string _workingFolder = null!;
        private string _goodFolder = null!;
        private string _veryGoodFolder = null!;
        private string _sortedOutFolder = null!;
        
        /// <summary>Suppresses <see cref="WorkingFolderCombo"/> programmatic updates from applying a folder change.</summary>
        private bool _workingFolderComboUpdating;
        
        /// <summary>Incremented when the working folder changes so background preload/decode work can bail out safely.</summary>
        private int _folderSession;
        
        private readonly List<string> _photos = new();
        private int _currentIndex;
        private int _totalPhotos;
        private int _goodCount;
        private int _veryGoodCount;
        private int _sortedOutCount;
        
        /// <summary>Top-level photo count under <see cref="AppConfig.SourceFolder"/> (stats panel Total line).</summary>
        private int _statsPanelRootTotal;
        
        /// <summary>Photos moved into each bucket this session (for persistent stats merge).</summary>
        private int _sessionMovesToGood;
        private int _sessionMovesToVeryGood;
        private int _sessionMovesToSortedOut;
        
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
            
            var session = SessionSettings.Load();
            _workingFolder = ResolveInitialWorkingFolder(session);
            
            // Initialize folder paths first (destinations are under the working folder)
            InitializeFolderPaths();
            
            // Load persistent statistics and merge with folder scan (buckets under source root, not nested under working folder)
            _persistentStats = StatisticsManager.LoadStatistics();
            string statsRoot = GetStatisticsAnchorFolder();
            _persistentStats = StatisticsManager.MergeWithFolderScan(
                _persistentStats,
                AppConfig.GetGoodFolderPath(statsRoot),
                AppConfig.GetVeryGoodFolderPath(statsRoot),
                AppConfig.GetSortedOutFolderPath(statsRoot));
            
            // Initialize current session counts from folder scan
            InitializeCurrentCountsFromFolders();
            ResetSessionMoveCounts();
            
            CreateDestinationFolders();
            LoadPhotos();
            SetupEventHandlers();
            UpdateDisplay();
            
            HideInstructionsOnStartup();
            SetupWindowFocus();
            SetupWorkingFolderCombo();
        }
        
        #endregion
        
        #region Initialization Methods
        
        /// <summary>Fills the working-folder combo with root + sort destinations under <see cref="AppConfig.SourceFolder"/>.</summary>
        private void SetupWorkingFolderCombo()
        {
            _workingFolderComboUpdating = true;
            try
            {
                WorkingFolderCombo.Items.Clear();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var (label, path) in AppConfig.GetWorkingFolderPresetChoices())
                {
                    string full;
                    try
                    {
                        full = Path.GetFullPath(path);
                    }
                    catch
                    {
                        continue;
                    }
                    if (!seen.Add(full))
                        continue;
                    WorkingFolderCombo.Items.Add(new ComboBoxItem { Content = label, Tag = full });
                }
                
                string current;
                try
                {
                    current = Path.GetFullPath(_workingFolder);
                }
                catch
                {
                    current = _workingFolder;
                }
                
                ComboBoxItem? match = null;
                foreach (var o in WorkingFolderCombo.Items)
                {
                    if (o is ComboBoxItem item && item.Tag is string p &&
                        string.Equals(Path.GetFullPath(p), current, StringComparison.OrdinalIgnoreCase))
                    {
                        match = item;
                        break;
                    }
                }
                
                if (match is null)
                {
                    match = new ComboBoxItem { Content = "Other", Tag = current };
                    WorkingFolderCombo.Items.Add(match);
                }
                
                WorkingFolderCombo.SelectedItem = match;
            }
            finally
            {
                _workingFolderComboUpdating = false;
            }
        }
        
        /// <summary>Switches the working folder (photos loaded from here; destinations are under the same folder).</summary>
        private void SetWorkingFolder(string picked)
        {
            if (string.IsNullOrWhiteSpace(picked))
                return;
            
            string full;
            try
            {
                full = Path.GetFullPath(picked);
            }
            catch (Exception ex)
            {
                ShowError($"Invalid folder path: {ex.Message}");
                return;
            }
            
            try
            {
                if (string.Equals(full, Path.GetFullPath(_workingFolder), StringComparison.OrdinalIgnoreCase))
                    return;
            }
            catch
            {
                if (string.Equals(full, _workingFolder, StringComparison.OrdinalIgnoreCase))
                    return;
            }
            
            try
            {
                Directory.CreateDirectory(full);
            }
            catch (Exception ex)
            {
                ShowError($"Could not use folder: {ex.Message}");
                return;
            }
            
            Interlocked.Increment(ref _folderSession);
            
            _workingFolder = full;
            SessionSettings.Save(new SessionSettings.Data { WorkingFolder = _workingFolder });
            InitializeFolderPaths();
            CreateDestinationFolders();
            ClearAllImageCaches();
            _currentIndex = 0;
            LoadPhotos();
            
            ResetSessionMoveCounts();
            UpdateDisplay();
            // Never clear/rebuild the combo synchronously from SelectionChanged — defer to avoid Avalonia crashes.
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    SetupWorkingFolderCombo();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"SetupWorkingFolderCombo failed: {ex}");
                }
            }, DispatcherPriority.Background);
        }
        
        /// <summary>
        /// Initializes the folder paths for sorting categories.
        /// </summary>
        private void InitializeFolderPaths()
        {
            _goodFolder = AppConfig.GetGoodFolderPath(_workingFolder);
            _veryGoodFolder = AppConfig.GetVeryGoodFolderPath(_workingFolder);
            _sortedOutFolder = AppConfig.GetSortedOutFolderPath(_workingFolder);
        }
        
        /// <summary>Library root used for Good / Very good / Sorted out counts in the stats panel (preset buckets under <see cref="AppConfig.SourceFolder"/>).</summary>
        private static string GetStatisticsAnchorFolder()
        {
            try
            {
                return Path.GetFullPath(AppConfig.SourceFolder);
            }
            catch
            {
                return AppConfig.SourceFolder;
            }
        }
        
        /// <summary>Re-reads bucket file counts under the source root (same buckets for Root / Good / Very good / Sorted out views).</summary>
        private void RefreshStatisticsBucketCounts()
        {
            string anchor = GetStatisticsAnchorFolder();
            _statsPanelRootTotal = Directory.Exists(anchor)
                ? Directory.GetFiles(anchor, AppConfig.FileExtension).Length
                : 0;
            var folderStats = StatisticsManager.ScanFolderStatistics(
                AppConfig.GetGoodFolderPath(anchor),
                AppConfig.GetVeryGoodFolderPath(anchor),
                AppConfig.GetSortedOutFolderPath(anchor));
            _goodCount = folderStats.GoodCount;
            _veryGoodCount = folderStats.VeryGoodCount;
            _sortedOutCount = folderStats.SortedOutCount;
        }
        
        private void ResetSessionMoveCounts()
        {
            _sessionMovesToGood = 0;
            _sessionMovesToVeryGood = 0;
            _sessionMovesToSortedOut = 0;
        }
        
        private static string ResolveInitialWorkingFolder(SessionSettings.Data session)
        {
            if (!string.IsNullOrWhiteSpace(session.WorkingFolder))
            {
                try
                {
                    string full = Path.GetFullPath(session.WorkingFolder);
                    if (Directory.Exists(full))
                        return full;
                }
                catch
                {
                    // Fall back to configured source folder
                }
            }
            
            try
            {
                return Path.GetFullPath(AppConfig.SourceFolder);
            }
            catch
            {
                return AppConfig.SourceFolder;
            }
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
                RefreshStatisticsBucketCounts();
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
                _photos.AddRange(Directory.GetFiles(_workingFolder, AppConfig.FileExtension)
                    .OrderBy(f => f));
                
                _totalPhotos = _photos.Count;
                ImageDecoder.LogDiagnostic($"LoadPhotos complete. WorkingFolder='{_workingFolder}', Filter='{AppConfig.FileExtension}', Total={_totalPhotos}");
                RefreshStatisticsBucketCounts();
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
            WorkingFolderText.Text = _workingFolder;
            StatsText.Text = $"Total: {_statsPanelRootTotal}\n" +
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
