using System.Numerics;
using Robust.Shared.Serialization;

namespace Content.Shared._Starlight.Drawing;

[Serializable, NetSerializable]
public enum DrawingBrush : byte { Circle, Square, PixelSquare }

/// <summary> Stores points and brush settings for a single drawing stroke.</summary>
[Serializable, NetSerializable, DataDefinition]
public sealed partial class DrawingStroke
{
    [DataField] public List<Vector2> Points = [];
    [DataField] public DrawingBrush Brush;
    [DataField] public Color Color = Color.Black;
    [DataField] public byte Size = 4;
    [DataField] public byte Opacity = 100;
    [DataField] public bool Erase;
}

/// <summary> Validates strokes, renders drawings into pixels, creates paper thumbnails, and encodes printed images.</summary>
public static class DrawingRaster
{
    // Image-format: Changing these dimensions requires updating the printed-image format or images will fail to decode.
    // Artwork is carefully limited so maximum-complexity artwork can only send about 252 KB of data when opening a paper.
    // Canvas has the same aspect ratio as the standard 510 x 660 paper window.
    public const int CanvasWidthPixels = 255;
    public const int CanvasHeightPixels = 330;

    // Pixel sizes divide both canvas dimensions so every grid cell is complete.
    public static readonly byte[] PixelGridSizes = CreatePixelGridSizes();
    // Thumbnail fits the paper sprite's artwork area.
    public const int ThumbnailWidthPixels = 10;
    public const int ThumbnailHeightPixels = 12;

    // Maximum network payload
    public const int MaximumPointsPerStroke = 256;
    public const int MaximumRenderWorkPerStroke = 1_000_000;
    public const int MaximumCachedCoverageBuffers = 4;

    // Upper limit for the vector brush and for selectable pixel cell sizes.
    public const byte MaximumBrushSize = 64;

    public const int ByteCount = CanvasWidthPixels * CanvasHeightPixels * 3;

    private static byte[] CreatePixelGridSizes()
    {
        var sizes = new List<byte>();
        for (var size = 1; size <= MaximumBrushSize; size++)
        {
            if (CanvasWidthPixels % size == 0 && CanvasHeightPixels % size == 0)
                sizes.Add((byte) size);
        }
        return sizes.ToArray();
    }

    // Reuse coverage arrays that track which pixels each stroke has already painted.
    // Each render takes its own array; the lock protects the shared pool.
    private static readonly object CoverageLock = new();
    private static readonly Stack<int[]> CoveragePool = new();

    private static int[] RentCoverage()
    {
        lock (CoverageLock)
        {
            if (CoveragePool.TryPop(out var coverage))
                return coverage;
        }
        return new int[CanvasWidthPixels * CanvasHeightPixels];
    }

    private static void ReturnCoverage(int[] coverage)
    {
        lock (CoverageLock)
        {
            if (CoveragePool.Count < MaximumCachedCoverageBuffers)
                CoveragePool.Push(coverage);
        }
    }

    public static byte[] Blank()
    {
        var pixels = new byte[ByteCount];
        Array.Fill(pixels, (byte) 255);
        return pixels;
    }
/// <summary>Checks a stroke's data and returns its estimated rendering cost</summary>
    public static bool Validate(DrawingStroke? stroke, out int work)
    {
        work = 0;
        if (stroke == null || stroke.Points == null || stroke.Points.Count is < 1 or > MaximumPointsPerStroke
            || !Enum.IsDefined(stroke.Brush) || stroke.Size < 1 || stroke.Size > MaximumBrushSize || stroke.Opacity is < 1 or > 100
            || !float.IsFinite(stroke.Color.R) || !float.IsFinite(stroke.Color.G) || !float.IsFinite(stroke.Color.B)
            || stroke.Color.R is < 0 or > 1 || stroke.Color.G is < 0 or > 1 || stroke.Color.B is < 0 or > 1)
            return false;

        var pixelArt = stroke.Brush == DrawingBrush.PixelSquare;
        if (pixelArt && (CanvasWidthPixels % stroke.Size != 0 || CanvasHeightPixels % stroke.Size != 0))
            return false;
        var area = pixelArt ? stroke.Size * stroke.Size : (stroke.Size + 2) * (stroke.Size + 2);
        var spacing = Math.Max(1f, stroke.Size / 4f);
        work = area;
        for (var i = 0; i < stroke.Points.Count; i++)
        {
            var point = stroke.Points[i];
            if (!float.IsFinite(point.X) || !float.IsFinite(point.Y)
                || point.X < 0 || point.X >= CanvasWidthPixels || point.Y < 0 || point.Y >= CanvasHeightPixels)
                return false;
            if (pixelArt && (point.X % stroke.Size != 0 || point.Y % stroke.Size != 0))
                return false;
            if (i > 0)
            {
                var steps = pixelArt
                    ? Math.Max(Math.Abs((int) (point.X - stroke.Points[i - 1].X)), Math.Abs((int) (point.Y - stroke.Points[i - 1].Y))) / stroke.Size
                    : Math.Max(1, (int) MathF.Ceiling(Vector2.Distance(stroke.Points[i - 1], point) / spacing));
                work += steps * area;
            }
            if (work > MaximumRenderWorkPerStroke)
                return false;
        }
        return true;
    }

    public static void Apply(byte[] pixels, DrawingStroke stroke)
    {
        var coverage = RentCoverage();
        try
        {
            Array.Clear(coverage, 0, CanvasWidthPixels * CanvasHeightPixels);
            Apply(pixels, stroke, coverage, 1);
        }
        finally
        {
            ReturnCoverage(coverage);
        }
    }

/// <summary>Paints one stroke onto the image.</summary>
    private static void Apply(byte[] pixels, DrawingStroke stroke, int[] coverage, int generation)
    {
        //Stop if the image has the wrong size or the stroke fails the drawing limit checks.
        if (pixels.Length != ByteCount || !Validate(stroke, out _))
            throw new ArgumentException("Invalid drawing raster or stroke.");

        var radius = stroke.Size / 2f;
        var spacing = Math.Max(1f, stroke.Size / 4f);
        var color = stroke.Erase ? Color.White : stroke.Color;
        var r = (int) MathF.Round(color.R * 255);
        var g = (int) MathF.Round(color.G * 255);
        var b = (int) MathF.Round(color.B * 255);
        var alpha = stroke.Opacity;
        Stamp(stroke.Points[0]);
        for (var i = 1; i < stroke.Points.Count; i++)
        {
            var start = stroke.Points[i - 1];
            var end = stroke.Points[i];
            if (stroke.Brush == DrawingBrush.PixelSquare)
            {
                // The path connects sampled positions with a continuous line of whole grid cells.
                var x = (int) start.X / stroke.Size;
                var y = (int) start.Y / stroke.Size;
                var endX = (int) end.X / stroke.Size;
                var endY = (int) end.Y / stroke.Size;
                var dx = Math.Abs(endX - x);
                var dy = Math.Abs(endY - y);
                var stepX = x < endX ? 1 : -1;
                var stepY = y < endY ? 1 : -1;
                var error = dx - dy;
                while (x != endX || y != endY)
                {
                    var twiceError = 2 * error;
                    if (twiceError > -dy)
                    {
                        error -= dy;
                        x += stepX;
                    }
                    if (twiceError < dx)
                    {
                        error += dx;
                        y += stepY;
                    }
                    Stamp(new Vector2(x * stroke.Size, y * stroke.Size));
                }
            }
            else
            {
                var steps = Math.Max(1, (int) MathF.Ceiling(Vector2.Distance(start, end) / spacing));
                for (var j = 1; j <= steps; j++)
                    Stamp(Vector2.Lerp(start, end, j / (float) steps));
            }
        }

        return;

        byte Blend(int old, int ink) => (byte) ((old * (100 - alpha) + ink * alpha + 50) / 100);

        void Stamp(Vector2 point)
        {
            // Vector points use pixel centers; pixel-art points use the top-left corners of grid cells.
            var left = Math.Max(0, (int) MathF.Ceiling(point.X - radius));
            var right = Math.Min(CanvasWidthPixels - 1, (int) MathF.Floor(point.X + radius));
            var top = Math.Max(0, (int) MathF.Ceiling(point.Y - radius));
            var bottom = Math.Min(CanvasHeightPixels - 1, (int) MathF.Floor(point.Y + radius));
            if (stroke.Brush == DrawingBrush.PixelSquare)
            {
                left = (int) point.X;
                top = (int) point.Y;
                right = left + stroke.Size - 1;
                bottom = top + stroke.Size - 1;
            }
            for (var y = top; y <= bottom; y++)
            for (var x = left; x <= right; x++)
            {
                if (stroke.Brush == DrawingBrush.Circle && Vector2.DistanceSquared(new Vector2(x, y), point) > radius * radius)
                    continue;
                var index = y * CanvasWidthPixels + x;
                if (coverage[index] == generation)
                    continue;
                // Paint each pixel once per stroke, so overlapping stamps do not multiply darkness.
                coverage[index] = generation;
                var offset = index * 3;
                pixels[offset] = Blend(pixels[offset], r);
                pixels[offset + 1] = Blend(pixels[offset + 1], g);
                pixels[offset + 2] = Blend(pixels[offset + 2], b);
            }
        }
    }

    public static byte[] Render(IEnumerable<DrawingStroke> strokes)
    {
        var pixels = Blank();
        // Use a new generation number for each stroke instead of clearing the array between strokes.
        var coverage = RentCoverage();
        try
        {
            Array.Clear(coverage, 0, CanvasWidthPixels * CanvasHeightPixels);
            var generation = 0;
            foreach (var stroke in strokes)
            {
                if (generation == int.MaxValue)
                {
                    Array.Clear(coverage, 0, CanvasWidthPixels * CanvasHeightPixels);
                    generation = 0;
                }
                Apply(pixels, stroke, coverage, ++generation);
            }
        }
        finally
        {
            ReturnCoverage(coverage);
        }
        return pixels;
    }

    public static byte[] Thumbnail(byte[] pixels)
    {
        var result = new byte[ThumbnailWidthPixels * ThumbnailHeightPixels * 3];
        for (var y = 0; y < ThumbnailHeightPixels; y++)
        for (var x = 0; x < ThumbnailWidthPixels; x++)
        {
            var x0 = x * CanvasWidthPixels / ThumbnailWidthPixels;
            var x1 = (x + 1) * CanvasWidthPixels / ThumbnailWidthPixels;
            var y0 = y * CanvasHeightPixels / ThumbnailHeightPixels;
            var y1 = (y + 1) * CanvasHeightPixels / ThumbnailHeightPixels;
            for (var channel = 0; channel < 3; channel++)
            {
                var sum = 0;
                for (var py = y0; py < y1; py++)
                for (var px = x0; px < x1; px++)
                    sum += pixels[(py * CanvasWidthPixels + px) * 3 + channel];
                result[(y * ThumbnailWidthPixels + x) * 3 + channel] = (byte) (sum / ((x1 - x0) * (y1 - y0)));
            }
        }
        return result;
    }

    /// <summary>Compression system to reduce server overhead. Uses bounded lossless RGB run encoding; noisy pictures fall back to raw RGB.</summary>
    public static byte[] Encode(byte[] pixels)
    {
        if (pixels.Length != ByteCount)
            throw new ArgumentException("Invalid drawing size.");
        var encoded = new List<byte> { 1 };
        for (var offset = 0; offset < pixels.Length;)
        {
            var count = 1;
            while (count < ushort.MaxValue && offset + count * 3 < pixels.Length
                && pixels[offset] == pixels[offset + count * 3]
                && pixels[offset + 1] == pixels[offset + count * 3 + 1]
                && pixels[offset + 2] == pixels[offset + count * 3 + 2])
                count++;
            encoded.Add((byte) count);
            encoded.Add((byte) (count >> 8));
            encoded.Add(pixels[offset]);
            encoded.Add(pixels[offset + 1]);
            encoded.Add(pixels[offset + 2]);
            offset += count * 3;
        }
        if (encoded.Count <= ByteCount)
            return encoded.ToArray();
        var raw = new byte[ByteCount + 1];
        pixels.CopyTo(raw, 1);
        return raw;
    }

    public static bool TryDecode(byte[] data, out byte[] pixels)
    {
        pixels = [];
        if (data.Length is < 1 or > ByteCount + 1)
            return false;
        if (data[0] == 0)
        {
            if (data.Length != ByteCount + 1)
                return false;
            pixels = data[1..];
            return true;
        }
        if (data[0] != 1 || (data.Length - 1) % 5 != 0)
            return false;
        var output = new byte[ByteCount];
        var pos = 0;
        for (var i = 1; i < data.Length; i += 5)
        {
            var count = data[i] | data[i + 1] << 8;
            if (count == 0 || pos + count * 3 > ByteCount)
                return false;
            for (var j = 0; j < count; j++)
            {
                output[pos++] = data[i + 2];
                output[pos++] = data[i + 3];
                output[pos++] = data[i + 4];
            }
        }
        if (pos != ByteCount)
            return false;
        pixels = output;
        return true;
    }
}
