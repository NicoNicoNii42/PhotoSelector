using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PhotoSorterAvalonia
{
    /// <summary>
    /// Main window for the Photo Sorter application.
    /// Allows sorting photos into three categories using keyboard shortcuts and mouse gestures.
    /// </summary>
    public partial class MainWindow : Window
    {
        #region Constants and Configuration
        
        private const string SourceFolder = "/Users/niconiconii/Pictures/DCIM/100LEICA";
        private const string FileExtension = "*.DNG";
        private const double MinZoomScale = 0.2;
        private const double MaxZoomScale = 5.0;
        private const double ZoomFactor = 1.1;
        private const double RotationStep = 90.0;
        
        #endregion
        
        #region Private Fields
        
        private readonly string _goodFolder;
        private readonly string _veryGoodFolder;
        private readonly string _sortedOutFolder;
        
        private readonly List<string> _photos = new();
        private int _currentIndex;
        private int _totalPhotos;
        private int _goodCount;
        private int _veryGoodCount;
        private int _sortedOutCount;
        
        private double _currentScale = 1.0;
        private double _currentRotation;
        private Point _lastMousePosition;
        private bool _isDragging;
        
        #endregion
        
        #region Constructor
        
        /// <summary>
        /// Initializes a new instance of the MainWindow class.
        /// </summary>
        public MainWindow()
        {
            InitializeComponent();
            
            // Initialize folder paths
            _goodFolder = Path.Combine(SourceFolder, "good");
            _veryGoodFolder = Path.Combine(SourceFolder, "verygood");
            _sortedOutFolder = Path.Combine(SourceFolder, "sortedout");
            
            CreateDestinationFolders();
            LoadPhotos();
            SetupEventHandlers();
            UpdateDisplay();
            
            // Hide instructions on startup
            InstructionsOverlay.IsVisible = false;
            
            // Focus the window for keyboard input
            Activated += (_, _) => Focus();
        }
        
        #endregion
        
        #region Initialization Methods
        
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
            
            // Mouse wheel for zoom
            CurrentImage.PointerWheelChanged += OnMouseWheel;
            
            // Mouse drag for pan
            CurrentImage.PointerPressed += OnMousePressed;
            CurrentImage.PointerMoved += OnMouseMoved;
            CurrentImage.PointerReleased += OnMouseReleased;
            
            // Double click to reset zoom
            CurrentImage.DoubleTapped += OnDoubleTapped;
        }
        
        /// <summary>
        /// Loads all photos from the source folder.
        /// </summary>
        private void LoadPhotos()
        {
            try
            {
                _photos.Clear();
                _photos.AddRange(Directory.GetFiles(SourceFolder, FileExtension)
                    .OrderBy(f => f));
                
                _totalPhotos = _photos.Count;
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
                FileText.Text = "All photos sorted!";
                CurrentImage.Source = null;
                return;
            }
            
            string currentPhoto = _photos[_currentIndex];
            string fileName = Path.GetFileName(currentPhoto);
            
            FileText.Text = fileName;
            LoadImage(currentPhoto);
            UpdateStatistics();
            ResetZoom();
        }
        
        /// <summary>
        /// Attempts to load an image from the specified path.
        /// </summary>
        /// <param name="imagePath">The path to the image file.</param>
        private void LoadImage(string imagePath)
        {
            try
            {
                CurrentImage.Source = new Bitmap(imagePath);
            }
            catch
            {
                // If we can't load the DNG, show a placeholder message
                FileText.Text = $"{Path.GetFileName(imagePath)} (Preview not available)";
            }
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
            ProgressBar.Value = _totalPhotos > 0 ? (double)(_currentIndex + 1) / _totalPhotos * 100 : 0;
        }
        
        #endregion
        
        #region Zoom and Pan Methods
        
        /// <summary>
        /// Handles mouse wheel events for zooming.
        /// </summary>
        private void OnMouseWheel(object? sender, PointerWheelEventArgs e)
        {
            if (e.Delta.Y > 0)
            {
                ZoomIn();
            }
            else
            {
                ZoomOut();
            }
            e.Handled = true;
        }
        
        /// <summary>
        /// Handles mouse press events for starting drag operations.
        /// </summary>
        private void OnMousePressed(object? sender, PointerPressedEventArgs e)
        {
            if (_currentScale > 1.0 && e.GetCurrentPoint(CurrentImage).Properties.IsLeftButtonPressed)
            {
                _isDragging = true;
                _lastMousePosition = e.GetPosition(CurrentImage);
                e.Handled = true;
            }
        }
        
        /// <summary>
        /// Handles mouse move events for dragging/panning.
        /// </summary>
        private void OnMouseMoved(object? sender, PointerEventArgs e)
        {
            if (!_isDragging) return;
            
            var currentPosition = e.GetPosition(CurrentImage);
            var delta = currentPosition - _lastMousePosition;
            
            ApplyTranslationWithBounds(delta.X, delta.Y);
            _lastMousePosition = currentPosition;
            e.Handled = true;
        }
        
        /// <summary>
        /// Handles mouse release events for ending drag operations.
        /// </summary>
        private void OnMouseReleased(object? sender, PointerReleasedEventArgs e)
        {
            _isDragging = false;
        }
        
        /// <summary>
        /// Handles double-tap events to reset zoom.
        /// </summary>
        private void OnDoubleTapped(object? sender, RoutedEventArgs e)
        {
            ResetZoom();
        }
        
        /// <summary>
        /// Zooms in on the current image.
        /// </summary>
        private void ZoomIn()
        {
            _currentScale = Math.Min(_currentScale * ZoomFactor, MaxZoomScale);
            ApplyZoom();
        }
        
        /// <summary>
        /// Zooms out from the current image.
        /// </summary>
        private void ZoomOut()
        {
            _currentScale = Math.Max(_currentScale / ZoomFactor, MinZoomScale);
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
            
            // After zooming, clamp translation to keep image in bounds
            if (transformGroup.Children[1] is TranslateTransform translateTransform)
            {
                double x = translateTransform.X;
                double y = translateTransform.Y;
                ClampTranslation(ref x, ref y);
                translateTransform.X = x;
                translateTransform.Y = y;
            }
        }
        
        /// <summary>
        /// Resets zoom and rotation to their default values.
        /// </summary>
        private void ResetZoom()
        {
            _currentScale = 1.0;
            _currentRotation = 0.0;
            ApplyZoom();
            ApplyRotation();
            
            // Reset translation
            if (CurrentImage.RenderTransform is TransformGroup transformGroup &&
                transformGroup.Children[1] is TranslateTransform translateTransform)
            {
                translateTransform.X = 0;
                translateTransform.Y = 0;
            }
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
            _currentRotation = (_currentRotation + RotationStep) % 360;
            ApplyRotation();
        }
        
        /// <summary>
        /// Rotates the image counter-clockwise by 90 degrees.
        /// </summary>
        private void RotateCounterClockwise()
        {
            _currentRotation = (_currentRotation - RotationStep) % 360;
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
        /// Handles keyboard input for navigation and sorting.
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
                    ShowFinalStatistics();
                    Close();
                    e.Handled = true;
                    break;
                    
                case Key.H:
                    InstructionsOverlay.IsVisible = !InstructionsOverlay.IsVisible;
                    e.Handled = true;
                    break;
                    
                case Key.Add:
                case Key.OemPlus:
                    ZoomIn();
                    e.Handled = true;
                    break;
                    
                case Key.Subtract:
                case Key.OemMinus:
                    ZoomOut();
                    e.Handled = true;
                    break;
                    
                case Key.D0:
                case Key.NumPad0:
                    ResetZoom();
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
        private void ZoomIn_Click(object? sender, RoutedEventArgs e) => ZoomIn();
        private void ZoomOut_Click(object? sender, RoutedEventArgs e) => ZoomOut();
        private void ResetZoom_Click(object? sender, RoutedEventArgs e) => ResetZoom();
        private void RotateClockwise_Click(object? sender, RoutedEventArgs e) => RotateClockwise();
        private void RotateCounterClockwise_Click(object? sender, RoutedEventArgs e) => RotateCounterClockwise();
        
        private void StartSorting_Click(object? sender, RoutedEventArgs e)
        {
            InstructionsOverlay.IsVisible = false;
            
            if (_photos.Count == 0)
            {
                ShowError("No .DNG files found in the source folder.");
            }
        }
        
        private void ToggleInstructions_Click(object? sender, RoutedEventArgs e)
        {
            InstructionsOverlay.IsVisible = !InstructionsOverlay.IsVisible;
        }
        
        #endregion
        
        #region Utility Methods
        
        /// <summary>
        /// Shows final statistics in a dialog window.
        /// </summary>
        private void ShowFinalStatistics()
        {
            string stats = $"Final Statistics:\n\n" +
                          $"Total photos: {_totalPhotos}\n" +
                          $"Sorted out: {_sortedOutCount}\n" +
                          $"Good: {_goodCount}\n" +
                          $"Very Good: {_veryGoodCount}\n\n" +
                          $"Photos moved to 'sorted out' folder: {_sortedOutFolder}\n" +
                          $"Photos moved to 'good' folder: {_goodFolder}\n" +
                          $"Photos moved to 'very good' folder: {_veryGoodFolder}";
            
            var dialog = new Window()
            {
                Title = "Photo Sorter - Complete",
                Width = 500,
                Height = 350,
                Content = new TextBlock 
                { 
                    Text = stats,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap
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
