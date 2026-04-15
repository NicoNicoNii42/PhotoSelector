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
    public partial class MainWindow : Window
    {

        #region Navigation Methods
        
        /// <summary>
        /// Moves to the next photo in the list.
        /// </summary>
        private void MoveToNext()
        {
            if (_photos.Count == 0)
                return;
            
            if (_currentIndex < _photos.Count - 1)
            {
                _currentIndex++;
                UpdateDisplay();
                FileText.Text = "→ Next photo";
            }
            else if (_currentIndex == _photos.Count - 1)
            {
                _currentIndex = 0;
                UpdateDisplay();
                FileText.Text = "↻ Wrapped to first photo";
            }
        }
        
        /// <summary>
        /// Moves to the previous photo in the list.
        /// </summary>
        private void MoveToPrevious()
        {
            if (_photos.Count == 0)
                return;
            
            if (_currentIndex > 0)
            {
                _currentIndex--;
                UpdateDisplay();
                FileText.Text = "← Previous photo";
            }
            else if (_currentIndex == 0)
            {
                _currentIndex = _photos.Count - 1;
                UpdateDisplay();
                FileText.Text = "↺ Wrapped to last photo";
            }
        }
        
        #endregion
        
        #region Sorting Methods
        
        /// <summary>
        /// Sorts the current photo into the "sorted out" folder.
        /// </summary>
        private void SortOutCurrent()
        {
            MovePhotoToFolder(_sortedOutFolder, "Sorted out");
        }
        
        /// <summary>
        /// Sorts the current photo into the "good" folder.
        /// </summary>
        private void MoveToGood()
        {
            MovePhotoToFolder(_goodFolder, "Good");
        }
        
        /// <summary>
        /// Sorts the current photo into the "very good" folder.
        /// </summary>
        private void MoveToVeryGood()
        {
            MovePhotoToFolder(_veryGoodFolder, "Very Good");
        }
        
        /// <summary>
        /// Moves the current photo to the specified folder.
        /// </summary>
        /// <param name="destinationFolder">The destination folder path.</param>
        /// <param name="actionName">The name of the action for feedback.</param>
        private void MovePhotoToFolder(string destinationFolder, string actionName)
        {
            if (_currentIndex >= _photos.Count) return;
            
            string currentPhoto = _photos[_currentIndex];
            string fileName = Path.GetFileName(currentPhoto);
            string destination = Path.Combine(destinationFolder, fileName);
            
            try
            {
                string currentFull = Path.GetFullPath(currentPhoto);
                string destinationFull = Path.GetFullPath(destination);
                if (string.Equals(currentFull, destinationFull, StringComparison.OrdinalIgnoreCase))
                {
                    MoveToNext();
                    FileText.Text = $"{fileName} already in {actionName}";
                    return;
                }

                File.Move(currentPhoto, destination);
                
                if (string.Equals(destinationFolder, _goodFolder, StringComparison.OrdinalIgnoreCase))
                    _sessionMovesToGood++;
                else if (string.Equals(destinationFolder, _veryGoodFolder, StringComparison.OrdinalIgnoreCase))
                    _sessionMovesToVeryGood++;
                else if (string.Equals(destinationFolder, _sortedOutFolder, StringComparison.OrdinalIgnoreCase))
                    _sessionMovesToSortedOut++;
                
                RefreshStatisticsBucketCounts();
                
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
        ///   Space / 0   - Reset zoom
        ///   
        /// Application:
        ///   H           - Toggle help overlay
        ///   Escape      - Quit with statistics
        /// </summary>
        private async void OnKeyDown(object? sender, KeyEventArgs e)
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
                    
                case Key.Space:
                case Key.D0:
                case Key.NumPad0:
                    ResetZoom();
                    e.Handled = true;
                    break;
                    
                case Key.Escape:
                    await QuitWithFinalStatsAsync();
                    e.Handled = true;
                    break;
            }
        }
        
        #endregion
        
        #region UI Event Handlers
        
        private void SortOut_Click(object? sender, RoutedEventArgs e) => SortOutCurrent();
        private void Good_Click(object? sender, RoutedEventArgs e) => MoveToGood();
        private void VeryGood_Click(object? sender, RoutedEventArgs e) => MoveToVeryGood();
        
        private void WorkingFolderCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_workingFolderComboUpdating)
                return;
            if (sender is not ComboBox cb || cb.SelectedItem is not ComboBoxItem item || item.Tag is not string path)
                return;
            // Defer off the selection event so SetWorkingFolder never runs inside Avalonia's combo state machine.
            Dispatcher.UIThread.Post(() => SetWorkingFolder(path), DispatcherPriority.Background);
        }
        
        private async void Quit_Click(object? sender, RoutedEventArgs e)
        {
            await QuitWithFinalStatsAsync();
        }
        
        private void StartSorting_Click(object? sender, RoutedEventArgs e)
        {
            InstructionsOverlay.IsVisible = false;
            
            if (_photos.Count == 0)
            {
                ShowError($"No files matching '{AppConfig.FileExtension}' were found in the working folder.");
            }
        }
        
        #endregion
        
        #region Utility Methods
        
        private async Task ShowTextDialogAsync(string title, string text, double width, double height, bool scroll)
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
            await dialog.ShowDialog(this);
        }

        private void ShowTextDialog(string title, string text, double width, double height, bool scroll) =>
            _ = ShowTextDialogAsync(title, text, width, height, scroll);
        
        /// <summary>
        /// Shows final statistics in a dialog window, including persistent statistics.
        /// </summary>
        private async Task ShowFinalStatisticsAsync()
        {
            int sessionTotalProcessed = _sessionMovesToGood + _sessionMovesToVeryGood + _sessionMovesToSortedOut;

            // Merge current session statistics with persistent statistics
            var mergedStats = StatisticsManager.MergeStatistics(
                _persistentStats,
                _sessionMovesToGood,
                _sessionMovesToVeryGood,
                _sessionMovesToSortedOut,
                sessionTotalProcessed);
            
            // Save the merged statistics
            StatisticsManager.SaveStatistics(mergedStats);
            
            string stats = $"📊 FINAL STATISTICS\n" +
                          $"==================\n\n" +
                          
                          $"📂 Working folder:\n" +
                          $"   {_workingFolder}\n\n" +
                          
                          $"📈 Current Session:\n" +
                          $"   • Photos processed: {sessionTotalProcessed}\n" +
                          $"   • Sorted out: {_sessionMovesToSortedOut}\n" +
                          $"   • Good: {_sessionMovesToGood}\n" +
                          $"   • Very Good: {_sessionMovesToVeryGood}\n\n" +
                          
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
            
            await ShowTextDialogAsync("Photo Sorter - Complete", stats, 600, 450, scroll: true);
        }

        private async Task QuitWithFinalStatsAsync()
        {
            await ShowFinalStatisticsAsync();
            Close();
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
