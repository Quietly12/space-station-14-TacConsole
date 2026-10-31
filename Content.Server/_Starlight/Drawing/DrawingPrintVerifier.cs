using System.Numerics;
using System.Security.Cryptography;
using Content.Shared._Starlight.Drawing;

namespace Content.Server._Starlight.Drawing;

/// <summary>-NR- Checks random uploaded pixels against the server's accepted stroke paths.</summary>
public static class DrawingPrintVerifier
{
    private readonly record struct StrokeBounds(DrawingStroke Stroke, float Left, float Top, float Right, float Bottom);

    public static bool TryVerify(byte[] image, IReadOnlyList<DrawingStroke> strokes, out byte[] thumbnail)
    {
        thumbnail = [];
        if (!DrawingRaster.TryDecode(image, out var pixels))
            return false;
        var bounds = BuildBounds(strokes);

        for (var i = 0; i < DrawingTabletSettings.PrintVerificationPixels; i++)
        {
            var index = RandomNumberGenerator.GetInt32(DrawingRaster.CanvasWidthPixels * DrawingRaster.CanvasHeightPixels);
            var x = index % DrawingRaster.CanvasWidthPixels;
            var y = index / DrawingRaster.CanvasWidthPixels;
            var expected = Sample(bounds, x, y);
            var offset = index * 3;
            if (pixels[offset] != expected.R || pixels[offset + 1] != expected.G || pixels[offset + 2] != expected.B)
                return false;
        }

        thumbnail = DrawingRaster.Thumbnail(pixels);
        return true;
    }

    public static (byte R, byte G, byte B) Sample(IReadOnlyList<DrawingStroke> strokes, int x, int y)
        => Sample(BuildBounds(strokes), x, y);

    private static StrokeBounds[] BuildBounds(IReadOnlyList<DrawingStroke> strokes)
    {
        var result = new StrokeBounds[strokes.Count];
        for (var i = 0; i < strokes.Count; i++)
        {
            var stroke = strokes[i];
            var left = float.MaxValue;
            var top = float.MaxValue;
            var right = float.MinValue;
            var bottom = float.MinValue;
            foreach (var point in stroke.Points)
            {
                left = Math.Min(left, point.X);
                top = Math.Min(top, point.Y);
                right = Math.Max(right, point.X);
                bottom = Math.Max(bottom, point.Y);
            }
            var padding = stroke.Brush == DrawingBrush.PixelSquare ? stroke.Size : stroke.Size / 2f;
            result[i] = new StrokeBounds(stroke,
                stroke.Brush == DrawingBrush.PixelSquare ? left : left - padding,
                stroke.Brush == DrawingBrush.PixelSquare ? top : top - padding,
                right + padding, bottom + padding);
        }
        return result;
    }

    private static (byte R, byte G, byte B) Sample(StrokeBounds[] strokes, int x, int y)
    {
        byte red = 255, green = 255, blue = 255;
        foreach (var bounds in strokes)
        {
            if (x < bounds.Left || x > bounds.Right || y < bounds.Top || y > bounds.Bottom)
                continue;
            var stroke = bounds.Stroke;
            if (!Covers(stroke, x, y))
                continue;
            var color = stroke.Erase ? Color.White : stroke.Color;
            var alpha = stroke.Opacity;
            red = Blend(red, (int) MathF.Round(color.R * 255), alpha);
            green = Blend(green, (int) MathF.Round(color.G * 255), alpha);
            blue = Blend(blue, (int) MathF.Round(color.B * 255), alpha);
        }
        return (red, green, blue);
    }

    private static byte Blend(int previous, int ink, int alpha)
        => (byte) ((previous * (100 - alpha) + ink * alpha + 50) / 100);

    private static bool Covers(DrawingStroke stroke, int x, int y)
    {
        var points = stroke.Points;
        if (points.Count == 0)
            return false;
        if (StampCovers(stroke, points[0], x, y))
            return true;

        var radius = stroke.Size / 2f;
        var spacing = Math.Max(1f, stroke.Size / 4f);
        for (var i = 1; i < points.Count; i++)
        {
            var start = points[i - 1];
            var end = points[i];
            if (stroke.Brush == DrawingBrush.PixelSquare)
            {
                var cellX = x / stroke.Size;
                var cellY = y / stroke.Size;
                var fromX = (int) start.X / stroke.Size;
                var fromY = (int) start.Y / stroke.Size;
                var toX = (int) end.X / stroke.Size;
                var toY = (int) end.Y / stroke.Size;
                if (cellX < Math.Min(fromX, toX) || cellX > Math.Max(fromX, toX)
                    || cellY < Math.Min(fromY, toY) || cellY > Math.Max(fromY, toY))
                    continue;
                var dx = Math.Abs(toX - fromX);
                var dy = Math.Abs(toY - fromY);
                var stepX = fromX < toX ? 1 : -1;
                var stepY = fromY < toY ? 1 : -1;
                var error = dx - dy;
                while (fromX != toX || fromY != toY)
                {
                    var twiceError = 2 * error;
                    if (twiceError > -dy) { error -= dy; fromX += stepX; }
                    if (twiceError < dx) { error += dx; fromY += stepY; }
                    if (fromX == cellX && fromY == cellY)
                        return true;
                }
                continue;
            }

            var low = 0f;
            var high = 1f;
            if (!LimitAxis(start.X, end.X, x - radius, x + radius, ref low, ref high)
                || !LimitAxis(start.Y, end.Y, y - radius, y + radius, ref low, ref high))
                continue;
            var steps = Math.Max(1, (int) MathF.Ceiling(Vector2.Distance(start, end) / spacing));
            var first = Math.Max(1, (int) MathF.Ceiling(low * steps) - 2);
            var last = Math.Min(steps, (int) MathF.Floor(high * steps) + 2);
            for (var j = first; j <= last; j++)
            {
                if (StampCovers(stroke, Vector2.Lerp(start, end, j / (float) steps), x, y))
                    return true;
            }
        }
        return false;
    }

    private static bool LimitAxis(float start, float end, float minimum, float maximum, ref float low, ref float high)
    {
        var delta = end - start;
        if (delta == 0)
            return start >= minimum && start <= maximum;
        var a = (minimum - start) / delta;
        var b = (maximum - start) / delta;
        low = Math.Max(low, Math.Min(a, b));
        high = Math.Min(high, Math.Max(a, b));
        return low <= high;
    }

    private static bool StampCovers(DrawingStroke stroke, Vector2 point, int x, int y)
    {
        if (stroke.Brush == DrawingBrush.PixelSquare)
            return x >= point.X && x < point.X + stroke.Size && y >= point.Y && y < point.Y + stroke.Size;
        var radius = stroke.Size / 2f;
        if (x < point.X - radius || x > point.X + radius || y < point.Y - radius || y > point.Y + radius)
            return false;
        return stroke.Brush != DrawingBrush.Circle || Vector2.DistanceSquared(new Vector2(x, y), point) <= radius * radius;
    }
}
