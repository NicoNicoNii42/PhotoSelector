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

            Point pointerInView = e.GetPosition(_imageContainer);
            Matrix? toViewBefore = CurrentImage.TransformToVisual(_imageContainer);
            Point? anchorLocal = null;
            if (toViewBefore.HasValue && toViewBefore.Value.TryInvert(out Matrix inverse))
                anchorLocal = inverse.Transform(pointerInView);
            
            _currentScale = newScale;
            ApplyZoom();

            if (anchorLocal.HasValue &&
                CurrentImage.RenderTransform is TransformGroup transformGroup &&
                transformGroup.Children[0] is TranslateTransform translateTransform)
            {
                KeepLocalPointUnderPointer(anchorLocal.Value, pointerInView, translateTransform);
                ClampCurrentTranslation();
                UpdateViewboxDisplay();
            }

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

        private void KeepLocalPointUnderPointer(Point localAnchor, Point targetInView, TranslateTransform translateTransform)
        {
            const double epsilon = 0.25;
            const int maxIterations = 4;

            for (int i = 0; i < maxIterations; i++)
            {
                Matrix? toViewNow = CurrentImage.TransformToVisual(_imageContainer);
                if (!toViewNow.HasValue)
                    return;

                Point mapped = toViewNow.Value.Transform(localAnchor);
                Vector error = targetInView - mapped;
                if (Math.Abs(error.X) < epsilon && Math.Abs(error.Y) < epsilon)
                    return;

                const double probe = 1.0;
                Vector basisX = SampleMappedWithTranslationDelta(localAnchor, translateTransform, probe, 0) - mapped;
                Vector basisY = SampleMappedWithTranslationDelta(localAnchor, translateTransform, 0, probe) - mapped;

                double det = basisX.X * basisY.Y - basisX.Y * basisY.X;
                if (Math.Abs(det) < 1e-9)
                    return;

                double deltaTx = (error.X * basisY.Y - error.Y * basisY.X) / det;
                double deltaTy = (basisX.X * error.Y - basisX.Y * error.X) / det;
                translateTransform.X += deltaTx;
                translateTransform.Y += deltaTy;
            }
        }

        private Point SampleMappedWithTranslationDelta(Point localAnchor, TranslateTransform translateTransform, double deltaX, double deltaY)
        {
            translateTransform.X += deltaX;
            translateTransform.Y += deltaY;

            Point mapped = localAnchor;
            Matrix? matrix = CurrentImage.TransformToVisual(_imageContainer);
            if (matrix.HasValue)
                mapped = matrix.Value.Transform(localAnchor);

            translateTransform.X -= deltaX;
            translateTransform.Y -= deltaY;
            return mapped;
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

