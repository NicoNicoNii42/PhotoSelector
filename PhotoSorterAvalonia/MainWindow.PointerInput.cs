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
    }
}

