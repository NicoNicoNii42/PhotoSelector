using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace PhotoSorterAvalonia;

/// <summary>
/// Bakes common EXIF orientation tags into pixel data on the UI thread (requires rendering).
/// </summary>
internal static class BitmapOrientationHelper
{
    /// <summary>
    /// Normalizes EXIF orientations 1/3/6/8 into upright pixel data. Must run on the UI thread (RenderTargetBitmap.Render).
    /// </summary>
    internal static Bitmap NormalizeBitmapExifOnUiThread(Bitmap decoded, int exifOrientation)
    {
        double angle = exifOrientation switch
        {
            1 => 0,
            3 => 180,
            6 => 90,
            8 => 270,
            _ => 0
        };

        if (angle == 0)
            return decoded;

        try
        {
            var baked = BakeOrientationByRendering(decoded, angle);
            decoded.Dispose();
            return baked;
        }
        catch (Exception ex)
        {
            ImageDecoder.LogDiagnostic($"Bake EXIF orientation failed; showing decoded pixels. Orientation={exifOrientation}, Error='{ex.Message}'");
            return decoded;
        }
    }

    /// <summary>
    /// Renders a rotated copy of the bitmap (same angles as the former EXIF RenderTransform mapping).
    /// </summary>
    private static Bitmap BakeOrientationByRendering(Bitmap source, double angleDeg)
    {
        var ps = source.PixelSize;
        bool swapDimensions = angleDeg is 90 or 270;
        int outW = swapDimensions ? ps.Height : ps.Width;
        int outH = swapDimensions ? ps.Width : ps.Height;

        var imageControl = new Image
        {
            Source = source,
            Stretch = Stretch.None,
            Width = ps.Width,
            Height = ps.Height,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        imageControl.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
        imageControl.RenderTransform = new RotateTransform { Angle = angleDeg };

        var container = new Grid
        {
            Width = outW,
            Height = outH,
            Background = Brushes.Transparent,
        };
        container.Children.Add(imageControl);

        var layoutSize = new Size(outW, outH);
        container.Measure(layoutSize);
        container.Arrange(new Rect(layoutSize));
        container.UpdateLayout();

        var rtb = new RenderTargetBitmap(new PixelSize(outW, outH), source.Dpi);
        rtb.Render(container);
        return rtb;
    }
}
