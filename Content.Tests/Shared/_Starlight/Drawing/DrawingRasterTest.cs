using System;
using System.Collections.Generic;
using System.Numerics;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Content.Shared._Starlight.Drawing;
using NUnit.Framework;
using Robust.Shared.Maths;

namespace Content.Tests.Shared._Starlight.Drawing;

[TestFixture]
public sealed class DrawingRasterTest
{
    [Test]
    public void ComplexityFollowsTheMostUsedBudget()
    {
        const int strokes = 1000;
        const int points = 32_000;
        const int work = 32_000_000;

        Assert.That(DrawingTabletComplexity.Calculate(0, 0, 0, strokes, points, work), Is.Zero);
        Assert.That(DrawingTabletComplexity.Calculate(1, 1, 36, strokes, points, work), Is.EqualTo(32));
        Assert.That(DrawingTabletComplexity.Calculate(500, 100, 24_000_000, strokes, points, work), Is.EqualTo(24_000));
        Assert.That(DrawingTabletComplexity.Calculate(1000, 0, 0, strokes, points, work), Is.EqualTo(points));
        Assert.That(DrawingTabletComplexity.Calculate(0, points, 0, strokes, points, work), Is.EqualTo(points));
        Assert.That(DrawingTabletComplexity.Calculate(0, 0, work, strokes, points, work), Is.EqualTo(points));
        Assert.That(DrawingTabletComplexity.Calculate(1, 1, 1, 8000, 128_000, 128_000_000), Is.EqualTo(16));
    }

    [TestCase(1)]
    [TestCase(3)]
    [TestCase(5)]
    [TestCase(15)]
    public void PixelSquarePaintsExactDimensions(int size)
    {
        var pixels = DrawingRaster.Blank();
        DrawingRaster.Apply(pixels, new DrawingStroke
        {
            Brush = DrawingBrush.PixelSquare, Size = (byte) size, Color = Color.Black,
            Points = new List<Vector2> { new(90, 90) },
        });
        var painted = 0;
        for (var i = 0; i < pixels.Length; i += 3)
            if (pixels[i] == 0) painted++;
        Assert.That(painted, Is.EqualTo(size * size));
        const int first = 90;
        Assert.That(pixels[(first * DrawingRaster.CanvasWidthPixels + first) * 3], Is.Zero);
        Assert.That(pixels[((first + size - 1) * DrawingRaster.CanvasWidthPixels + first + size - 1) * 3], Is.Zero);
    }

    [Test]
    public void PixelGridSizesFitBothCanvasDimensions()
    {
        Assert.That(DrawingRaster.PixelGridSizes, Is.EqualTo(new byte[] { 1, 3, 5, 15 }));
        foreach (var size in DrawingRaster.PixelGridSizes)
        {
            Assert.That(DrawingRaster.CanvasWidthPixels % size, Is.Zero);
            Assert.That(DrawingRaster.CanvasHeightPixels % size, Is.Zero);
        }
        var stroke = new DrawingStroke { Brush = DrawingBrush.PixelSquare, Size = 2, Points = [Vector2.Zero] };
        Assert.That(DrawingRaster.Validate(stroke, out _), Is.False);
        stroke.Size = 3;
        stroke.Points[0] = new Vector2(1, 0);
        Assert.That(DrawingRaster.Validate(stroke, out _), Is.False);
    }

    [Test]
    public void PixelStrokeFillsWholeCellsBetweenSamples()
    {
        var stroke = new DrawingStroke
        {
            Brush = DrawingBrush.PixelSquare, Size = 5, Color = Color.Black,
            Points = [new Vector2(0, 0), new Vector2(20, 0), new Vector2(20, 10)],
        };
        var pixels = DrawingRaster.Render([stroke]);
        var painted = 0;
        for (var i = 0; i < pixels.Length; i += 3)
            if (pixels[i] == 0) painted++;
        Assert.That(painted, Is.EqualTo(7 * 25));
        Assert.That(pixels[(5 * DrawingRaster.CanvasWidthPixels + 19) * 3], Is.EqualTo(255));
        Assert.That(pixels[(10 * DrawingRaster.CanvasWidthPixels + 24) * 3], Is.Zero);
    }

    [Test]
    public void PixelDiagonalStaysOnTheGrid()
    {
        var stroke = new DrawingStroke
        {
            Brush = DrawingBrush.PixelSquare, Size = 5, Color = Color.Black,
            Points = [Vector2.Zero, new Vector2(15, 15)],
        };
        var pixels = DrawingRaster.Render([stroke]);
        var painted = 0;
        for (var i = 0; i < pixels.Length; i += 3)
            if (pixels[i] == 0) painted++;
        Assert.That(painted, Is.EqualTo(4 * 25));
        Assert.That(pixels[(5 * DrawingRaster.CanvasWidthPixels + 0) * 3], Is.EqualTo(255));
    }

    [Test]
    public void PooledConcurrentRendersMatchOriginalPixels()
    {
        // Golden output captured from the original full-canvas coverage renderer.
        // Includes clipping, fractional coordinates, repeated samples, erasing and both shapes.
        const string expected = "B0E9F840467308994A5BC1121F70C8D83B7FCE7EB0A0D274292EB552B093812F";
        var random = new Random(314159);
        var strokes = new List<DrawingStroke>();
        for (var i = 0; i < 1000; i++)
        {
            var stroke = new DrawingStroke
            {
                Size = (byte) random.Next(1, 65),
                Opacity = (byte) random.Next(1, 101),
                Brush = i % 2 == 0 ? DrawingBrush.Circle : DrawingBrush.Square,
                Erase = i % 7 == 0,
                Color = new Color((byte) random.Next(256), (byte) random.Next(256), (byte) random.Next(256)),
            };
            for (var j = 0; j < 4; j++)
                stroke.Points.Add(new Vector2(random.NextSingle() * 254, random.NextSingle() * 329));
            stroke.Points.Add(stroke.Points[^1]);
            strokes.Add(stroke);
        }

        Parallel.For(0, 8, _ =>
        {
            var pixels = DrawingRaster.Render(strokes);
            Assert.That(Convert.ToHexString(SHA256.HashData(pixels)), Is.EqualTo(expected));
        });

        // The editor's individual-stroke path must agree, even when the pool contains used buffers.
        var incremental = DrawingRaster.Blank();
        foreach (var stroke in strokes)
            DrawingRaster.Apply(incremental, stroke);
        Assert.That(Convert.ToHexString(SHA256.HashData(incremental)), Is.EqualTo(expected));
        Assert.That(DrawingRaster.Render([]), Is.EqualTo(DrawingRaster.Blank()));
    }

    private static DrawingStroke Stroke(params Vector2[] points) => new()
    {
        Points = new List<Vector2>(points), Color = Color.Black, Size = 6,
    };

    [Test]
    public void BrushShapeAndOpacityAreIndependentOfRepeatedSamples()
    {
        var stroke = Stroke(new Vector2(10, 10), new Vector2(10, 10), new Vector2(10, 10));
        stroke.Opacity = 50;
        var round = DrawingRaster.Render([stroke]);
        Assert.That(round[(10 * DrawingRaster.CanvasWidthPixels + 10) * 3], Is.EqualTo(128));
        Assert.That(round[(13 * DrawingRaster.CanvasWidthPixels + 13) * 3], Is.EqualTo(255));
        stroke.Brush = DrawingBrush.Square;
        var square = DrawingRaster.Render([stroke]);
        Assert.That(square[(13 * DrawingRaster.CanvasWidthPixels + 13) * 3], Is.EqualTo(128));
    }

    [Test]
    public void LongSegmentHasNoGapsAndEraserRestoresPaper()
    {
        var stroke = Stroke(new Vector2(1, 30), new Vector2(240, 30));
        var pixels = DrawingRaster.Render([stroke]);
        for (var x = 1; x <= 240; x++)
            Assert.That(pixels[(30 * DrawingRaster.CanvasWidthPixels + x) * 3], Is.Zero);
        stroke.Erase = true;
        DrawingRaster.Apply(pixels, stroke);
        Assert.That(pixels, Is.EqualTo(DrawingRaster.Blank()));
    }

    [Test]
    public void InvalidAndExcessiveWorkCannotReachRasterizer()
    {
        Assert.That(DrawingRaster.Validate(Stroke(new Vector2(float.NaN, 0)), out _), Is.False);
        Assert.That(DrawingRaster.Validate(Stroke(new Vector2(-1, 0)), out _), Is.False);
        Assert.That(DrawingRaster.Validate(Stroke(new Vector2(255, 0)), out _), Is.False);
        var stroke = Stroke(Vector2.Zero);
        stroke.Color = new Color(float.PositiveInfinity, 0, 0);
        Assert.That(DrawingRaster.Validate(stroke, out _), Is.False);
        stroke.Color = Color.Black;
        stroke.Size = 64;
        for (var i = 0; i < 255; i++)
            stroke.Points.Add(i % 2 == 0 ? new Vector2(254, 329) : Vector2.Zero);
        Assert.That(DrawingRaster.Validate(stroke, out _), Is.False);
    }

    [Test]
    public void PrintEncodingIsLosslessAndBoundedForSolidAndNoisyImages()
    {
        var pixels = DrawingRaster.Blank();
        var encoded = DrawingRaster.Encode(pixels);
        Assert.That(encoded.Length, Is.LessThan(30));
        Assert.That(DrawingRaster.TryDecode(encoded, out var decoded), Is.True);
        Assert.That(decoded, Is.EqualTo(pixels));
        new Random(42).NextBytes(pixels);
        encoded = DrawingRaster.Encode(pixels);
        Assert.That(encoded.Length, Is.EqualTo(DrawingRaster.ByteCount + 1));
        Assert.That(DrawingRaster.TryDecode(encoded, out decoded), Is.True);
        Assert.That(decoded, Is.EqualTo(pixels));
    }

    [Test]
    public void MalformedImageCannotExpandBeyondFixedPaperSize()
    {
        Assert.That(DrawingRaster.TryDecode([], out _), Is.False);
        Assert.That(DrawingRaster.TryDecode([0, 1, 2], out _), Is.False);
        Assert.That(DrawingRaster.TryDecode([1, 0, 0, 1, 2, 3], out _), Is.False);
        Assert.That(DrawingRaster.TryDecode([1, 255, 255, 1, 2, 3, 255, 255, 1, 2, 3], out _), Is.False);
        Assert.That(DrawingRaster.TryDecode(new byte[DrawingRaster.ByteCount + 2], out _), Is.False);
    }

    [Test]
    public void PrintedSnapshotDoesNotChangeWithSubsequentDrawing()
    {
        var original = DrawingRaster.Blank();
        var encoded = DrawingRaster.Encode(original);
        var thumb = DrawingRaster.Thumbnail(original);
        DrawingRaster.Apply(original, Stroke(new Vector2(20, 20), new Vector2(30, 30)));
        Assert.That(DrawingRaster.TryDecode(encoded, out var print), Is.True);
        Assert.That(print, Is.EqualTo(DrawingRaster.Blank()));
        Assert.That(thumb.Length, Is.EqualTo(DrawingRaster.ThumbnailWidthPixels * DrawingRaster.ThumbnailHeightPixels * 3));
        Assert.That(thumb, Is.All.EqualTo(255));
    }
}
