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
    public partial class MainWindow : Window
    {
        // Configuration
        private readonly string sourceFolder = "/Users/niconiconii/Pictures/DCIM/100LEICA";
        private readonly string goodFolder;
        private readonly string veryGoodFolder;
        private readonly string sortedOutFolder;
        
        // Photo management
        private List<string> photos = new();
        private int currentIndex = 0;
        private int totalPhotos = 0;
        private int goodCount = 0;
        private int veryGoodCount = 0;
        private int sortedOutCount = 0;
        
        // Zoom/pan/rotation state
        private double currentScale = 1.0;
        private double currentRotation = 0.0;
        private Point lastMousePosition;
        private bool isDragging = false;
        
        public MainWindow()
        {
            InitializeComponent();
            
            // Initialize folders
            goodFolder = Path.Combine(sourceFolder, "good");
            veryGoodFolder = Path.Combine(sourceFolder, "verygood");
            sortedOutFolder = Path.Combine(sourceFolder, "sortedout");
            
            // Create folders if they don't exist
            Directory.CreateDirectory(goodFolder);
            Directory.CreateDirectory(veryGoodFolder);
            Directory.CreateDirectory(sortedOutFolder);
            
            // Load photos
            LoadPhotos();
            
            // Set up event handlers
            SetupEventHandlers();
            
            // Update initial display
            UpdateDisplay();
            
            // Hide instructions on startup
            InstructionsOverlay.IsVisible = false;
            
            // Focus the window for keyboard input
            this.Activated += (s, e) => this.Focus();
        }
        
        private void SetupEventHandlers()
        {
            // Keyboard handlers
            this.KeyDown += OnKeyDown;
            
            // Mouse wheel for zoom
            CurrentImage.PointerWheelChanged += OnMouseWheel;
            
            // Mouse drag for pan
            CurrentImage.PointerPressed += OnMousePressed;
            CurrentImage.PointerMoved += OnMouseMoved;
            CurrentImage.PointerReleased += OnMouseReleased;
            
            // Double click to reset zoom
            CurrentImage.DoubleTapped += OnDoubleTapped;
        }
        
        private void LoadPhotos()
        {
            try
            {
                photos = Directory.GetFiles(sourceFolder, "*.DNG")
                    .OrderBy(f => f)
                    .ToList();
                totalPhotos = photos.Count;
                
                UpdateStats();
            }
            catch (Exception ex)
            {
                StatsText.Text = $"Error: {ex.Message}";
            }
        }
        
        private void UpdateDisplay()
        {
            if (currentIndex >= photos.Count)
            {
                FileText.Text = "All photos sorted!";
                CurrentImage.Source = null;
                return;
            }
            
            string currentPhoto = photos[currentIndex];
            string fileName = Path.GetFileName(currentPhoto);
            
            // Update file info
            FileText.Text = fileName;
            
            // Try to load the image
            try
            {
                // Note: Avalonia might not support .DNG directly
                // For now, we'll try to load it or use a placeholder
                CurrentImage.Source = new Bitmap(currentPhoto);
            }
            catch
            {
                // If we can't load the DNG, show a placeholder
                FileText.Text = $"{fileName} (Preview not available)";
            }
            
            UpdateStats();
            ResetZoom();
        }
        
        private void UpdateStats()
        {
            StatsText.Text = $"Total: {totalPhotos}\n" +
                            $"Good: {goodCount}\n" +
                            $"Very Good: {veryGoodCount}\n" +
                            $"Sorted Out: {sortedOutCount}";
            
            ProgressText.Text = $"{currentIndex + 1}/{totalPhotos}";
            ProgressBar.Value = totalPhotos > 0 ? (double)(currentIndex + 1) / totalPhotos * 100 : 0;
        }
        
        #region Zoom/Pan Handlers
        
        private void OnMouseWheel(object sender, PointerWheelEventArgs e)
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
        
        private void OnMousePressed(object sender, PointerPressedEventArgs e)
        {
            if (currentScale > 1.0 && e.GetCurrentPoint(CurrentImage).Properties.IsLeftButtonPressed)
            {
                isDragging = true;
                lastMousePosition = e.GetPosition(CurrentImage);
                e.Handled = true;
            }
        }
        
        private void OnMouseMoved(object sender, PointerEventArgs e)
        {
            if (isDragging)
            {
                var currentPosition = e.GetPosition(CurrentImage);
                var delta = currentPosition - lastMousePosition;
                
                // Apply translation
                if (CurrentImage.RenderTransform is TransformGroup transformGroup)
                {
                    if (transformGroup.Children[1] is TranslateTransform translateTransform)
                    {
                        translateTransform.X += delta.X;
                        translateTransform.Y += delta.Y;
                    }
                }
                
                lastMousePosition = currentPosition;
                e.Handled = true;
            }
        }
        
        private void OnMouseReleased(object sender, PointerReleasedEventArgs e)
        {
            isDragging = false;
        }
        
        private void OnDoubleTapped(object sender, RoutedEventArgs e)
        {
            ResetZoom();
        }
        
        private void ZoomIn()
        {
            currentScale = Math.Min(currentScale * 1.1, 5.0);
            ApplyZoom();
        }
        
        private void ZoomOut()
        {
            currentScale = Math.Max(currentScale / 1.1, 0.2);
            ApplyZoom();
        }
        
        private void ApplyZoom()
        {
            var transform = CurrentImage.RenderTransform as TransformGroup;
            if (transform != null)
            {
                var scaleTransform = transform.Children[0] as ScaleTransform;
                if (scaleTransform != null)
                {
                    scaleTransform.ScaleX = currentScale;
                    scaleTransform.ScaleY = currentScale;
                }
            }
        }
        
        private void ResetZoom()
        {
            currentScale = 1.0;
            currentRotation = 0.0;
            ApplyZoom();
            ApplyRotation();
            
            // Reset translation
            var transform = CurrentImage.RenderTransform as TransformGroup;
            if (transform != null)
            {
                var translateTransform = transform.Children[1] as TranslateTransform;
                if (translateTransform != null)
                {
                    translateTransform.X = 0;
                    translateTransform.Y = 0;
                }
            }
        }
        
        private void RotateClockwise()
        {
            currentRotation = (currentRotation + 90) % 360;
            ApplyRotation();
        }
        
        private void RotateCounterClockwise()
        {
            currentRotation = (currentRotation - 90) % 360;
            if (currentRotation < 0) currentRotation += 360;
            ApplyRotation();
        }
        
        private void ApplyRotation()
        {
            var transform = CurrentImage.RenderTransform as TransformGroup;
            if (transform != null && transform.Children.Count > 2)
            {
                var rotateTransform = transform.Children[2] as RotateTransform;
                if (rotateTransform != null)
                {
                    rotateTransform.Angle = currentRotation;
                }
            }
        }
        
        #endregion
        
        #region Button Click Handlers
        
        private void SortOut_Click(object sender, RoutedEventArgs e)
        {
            SortOutCurrent();
        }
        
        private void Good_Click(object sender, RoutedEventArgs e)
        {
            MoveToGood();
        }
        
        private void VeryGood_Click(object sender, RoutedEventArgs e)
        {
            MoveToVeryGood();
        }
        
        private void Quit_Click(object sender, RoutedEventArgs e)
        {
            ShowFinalStats();
            Close();
        }
        
        private void ZoomIn_Click(object sender, RoutedEventArgs e)
        {
            ZoomIn();
        }
        
        private void ZoomOut_Click(object sender, RoutedEventArgs e)
        {
            ZoomOut();
        }
        
        private void ResetZoom_Click(object sender, RoutedEventArgs e)
        {
            ResetZoom();
        }
        
        private void RotateClockwise_Click(object sender, RoutedEventArgs e)
        {
            RotateClockwise();
        }
        
        private void RotateCounterClockwise_Click(object sender, RoutedEventArgs e)
        {
            RotateCounterClockwise();
        }
        
        private void StartSorting_Click(object sender, RoutedEventArgs e)
        {
            InstructionsOverlay.IsVisible = false;
            
            if (photos.Count == 0)
            {
                var dialog = new Window()
                {
                    Title = "No Photos",
                    Width = 300,
                    Height = 150,
                    Content = new TextBlock 
                    { 
                        Text = "No .DNG files found in the source folder.",
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                    }
                };
                dialog.ShowDialog(this);
            }
        }
        
        private void ToggleInstructions_Click(object sender, RoutedEventArgs e)
        {
            InstructionsOverlay.IsVisible = !InstructionsOverlay.IsVisible;
        }
        
        #endregion
        
        #region Sorting Logic
        
        private void SortOutCurrent()
        {
            if (currentIndex >= photos.Count) return;
            
            string currentPhoto = photos[currentIndex];
            string fileName = Path.GetFileName(currentPhoto);
            string destination = Path.Combine(sortedOutFolder, fileName);
            
            try
            {
                File.Move(currentPhoto, destination);
                sortedOutCount++;
                currentIndex++;
                
                UpdateDisplay();
                
                // Show brief feedback
                FileText.Text = $"{fileName} → Sorted out";
            }
            catch (Exception ex)
            {
                FileText.Text = $"Error: {ex.Message}";
            }
        }
        
        private void MoveToGood()
        {
            if (currentIndex >= photos.Count) return;
            
            string currentPhoto = photos[currentIndex];
            string fileName = Path.GetFileName(currentPhoto);
            string destination = Path.Combine(goodFolder, fileName);
            
            try
            {
                File.Move(currentPhoto, destination);
                goodCount++;
                currentIndex++;
                
                UpdateDisplay();
                
                // Show brief feedback
                FileText.Text = $"{fileName} → Good";
            }
            catch (Exception ex)
            {
                FileText.Text = $"Error: {ex.Message}";
            }
        }
        
        private void MoveToVeryGood()
        {
            if (currentIndex >= photos.Count) return;
            
            string currentPhoto = photos[currentIndex];
            string fileName = Path.GetFileName(currentPhoto);
            string destination = Path.Combine(veryGoodFolder, fileName);
            
            try
            {
                File.Move(currentPhoto, destination);
                veryGoodCount++;
                currentIndex++;
                
                UpdateDisplay();
                
                // Show brief feedback
                FileText.Text = $"{fileName} → Very Good";
            }
            catch (Exception ex)
            {
                FileText.Text = $"Error: {ex.Message}";
            }
        }
        
        private void ShowFinalStats()
        {
            string stats = $"Final Statistics:\n\n" +
                          $"Total photos: {totalPhotos}\n" +
                          $"Sorted out: {sortedOutCount}\n" +
                          $"Good: {goodCount}\n" +
                          $"Very Good: {veryGoodCount}\n\n" +
                          $"Photos moved to 'sorted out' folder: {sortedOutFolder}\n" +
                          $"Photos moved to 'good' folder: {goodFolder}\n" +
                          $"Photos moved to 'very good' folder: {veryGoodFolder}";
            
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
        
        #endregion
        
        #region Keyboard Handlers
        
        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Left:
                    SortOutCurrent();
                    e.Handled = true;
                    break;
                    
                case Key.Right:
                    MoveToGood();
                    e.Handled = true;
                    break;
                    
                case Key.Up:
                    MoveToVeryGood();
                    e.Handled = true;
                    break;
                    
                case Key.Q:
                    ShowFinalStats();
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
    }
}