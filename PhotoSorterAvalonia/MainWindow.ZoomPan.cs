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
    }
}
