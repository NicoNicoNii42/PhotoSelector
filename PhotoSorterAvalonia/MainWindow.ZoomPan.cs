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
            
            if (transformGroup.Children[1] is ScaleTransform scaleTransform)
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
                transformGroup.Children[0] is TranslateTransform translateTransform)
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
            if (transformGroup.Children[0] is not TranslateTransform translateTransform) return;
            
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
            if (transformGroup.Children[0] is not TranslateTransform translateTransform) return;
            
            double newX = translateTransform.X + deltaX;
            double newY = translateTransform.Y + deltaY;
            
            ClampTranslation(ref newX, ref newY);
            
            translateTransform.X = newX;
            translateTransform.Y = newY;
        }
        
        /// <summary>
        /// Clamps translation so the transformed image keeps the Viewbox filled (no empty margins when zoomed).
        /// Uses the real transform chain (origin, Viewbox scale, rotation) instead of span/viewport algebra.
        /// </summary>
        /// <param name="x">The X translation value to clamp.</param>
        /// <param name="y">The Y translation value to clamp.</param>
        private void ClampTranslation(ref double x, ref double y)
        {
            if (CurrentImage.Source is not Bitmap imageSource)
                return;
            if (CurrentImage.RenderTransform is not TransformGroup transformGroup)
                return;
            if (transformGroup.Children[0] is not TranslateTransform translateTransform)
                return;
            
            double w = CurrentImage.Bounds.Width;
            double h = CurrentImage.Bounds.Height;
            if (w < 1 || h < 1)
            {
                Size logical = GetBitmapLogicalSize(imageSource);
                w = logical.Width;
                h = logical.Height;
            }
            
            if (w < 1e-6 || h < 1e-6)
                return;
            
            double vw = _imageContainer.Bounds.Width;
            double vh = _imageContainer.Bounds.Height;
            if (vw < 1 || vh < 1)
                return;
            
            Rect local = new Rect(0, 0, w, h);
            Rect view = new Rect(0, 0, vw, vh);
            const double eps = 0.5;
            
            translateTransform.X = x;
            translateTransform.Y = y;
            
            for (int iter = 0; iter < 16; iter++)
            {
                Matrix? toView = CurrentImage.TransformToVisual(_imageContainer);
                if (!toView.HasValue)
                    return;
                
                GetTransformedImageBoundsInViewBox(toView.Value, local, out double minX, out double maxX, out double minY, out double maxY);
                
                double contentW = maxX - minX;
                double contentH = maxY - minY;
                bool wide = contentW > view.Width + eps;
                bool tall = contentH > view.Height + eps;
                
                if (!wide && !tall)
                {
                    x = 0;
                    y = 0;
                    translateTransform.X = 0;
                    translateTransform.Y = 0;
                    return;
                }
                
                bool moved = false;
                if (wide)
                {
                    if (minX > view.Left + eps)
                    {
                        x += view.Left - minX;
                        moved = true;
                    }
                    else if (maxX < view.Right - eps)
                    {
                        x += view.Right - maxX;
                        moved = true;
                    }
                }
                else
                    x = 0;
                
                if (tall)
                {
                    if (minY > view.Top + eps)
                    {
                        y += view.Top - minY;
                        moved = true;
                    }
                    else if (maxY < view.Bottom - eps)
                    {
                        y += view.Bottom - maxY;
                        moved = true;
                    }
                }
                else
                    y = 0;
                
                translateTransform.X = x;
                translateTransform.Y = y;
                
                if (!moved)
                    break;
            }
        }
        
        private static void GetTransformedImageBoundsInViewBox(Matrix toViewBox, Rect local, out double minX, out double maxX, out double minY, out double maxY)
        {
            Point p0 = toViewBox.Transform(local.TopLeft);
            Point p1 = toViewBox.Transform(local.TopRight);
            Point p2 = toViewBox.Transform(local.BottomRight);
            Point p3 = toViewBox.Transform(local.BottomLeft);
            minX = Math.Min(Math.Min(p0.X, p1.X), Math.Min(p2.X, p3.X));
            maxX = Math.Max(Math.Max(p0.X, p1.X), Math.Max(p2.X, p3.X));
            minY = Math.Min(Math.Min(p0.Y, p1.Y), Math.Min(p2.Y, p3.Y));
            maxY = Math.Max(Math.Max(p0.Y, p1.Y), Math.Max(p2.Y, p3.Y));
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
