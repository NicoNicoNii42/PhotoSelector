using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace PhotoSorterAvalonia;

/// <summary>
/// Bakes EXIF orientation tags into pixel data on the UI thread (requires rendering).
/// </summary>
internal static class BitmapOrientationHelper
{
    /// <summary>
    /// Normalizes EXIF orientations 1-8 into upright pixel data. Must run on the UI thread (RenderTargetBitmap.Render).
    /// </summary>
    internal static Bitmap NormalizeBitmapExifOnUiThread(Bitmap decoded, int exifOrientation)
    {
        if (exifOrientation == 1)
            return decoded;

        try
        {
            var baked = BakeOrientationByRendering(decoded, exifOrientation);
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
    /// Renders an oriented copy of the bitmap.
    /// </summary>
    private static Bitmap BakeOrientationByRendering(Bitmap source, int exifOrientation)
    {
        var ps = source.PixelSize;
        bool swapDimensions = exifOrientation is 5 or 6 or 7 or 8;
        int outW = swapDimensions ? ps.Height : ps.Width;
        int outH = swapDimensions ? ps.Width : ps.Height;

        var imageControl = new Image
        {
            Source = source,
            Stretch = Stretch.None,
            Width = ps.Width,
            Height = ps.Height,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        imageControl.RenderTransformOrigin = new RelativePoint(0, 0, RelativeUnit.Absolute);
        imageControl.RenderTransform = new MatrixTransform(GetExifOrientationMatrix(exifOrientation, ps.Width, ps.Height));

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

    private static Matrix GetExifOrientationMatrix(int exifOrientation, int width, int height)
    {
        return exifOrientation switch
        {
            2 => new Matrix(-1, 0, 0, 1, width, 0),
            3 => new Matrix(-1, 0, 0, -1, width, height),
            4 => new Matrix(1, 0, 0, -1, 0, height),
            5 => new Matrix(0, 1, 1, 0, 0, 0),
            6 => new Matrix(0, 1, -1, 0, height, 0),
            7 => new Matrix(0, -1, -1, 0, height, width),
            8 => new Matrix(0, -1, 1, 0, 0, width),
            _ => Matrix.Identity,
        };
    }
}
