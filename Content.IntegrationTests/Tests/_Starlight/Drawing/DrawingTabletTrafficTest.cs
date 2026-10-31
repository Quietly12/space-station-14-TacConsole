using System.Linq;
using System.Collections.Generic;
using Stopwatch = System.Diagnostics.Stopwatch;
using System.IO;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Shared._Starlight.Drawing;
using Content.Server._Starlight.Drawing;
using Robust.Shared.Maths;
using Robust.Shared.Serialization;

namespace Content.IntegrationTests.Tests._Starlight.Drawing;

public sealed class DrawingTabletTrafficTest : GameTest
{
    [Test]
    public async Task ReportSerializedTabletPayloadSizes()
    {
        await Server.WaitAssertion(() =>
        {
            var serializer = Server.ResolveDependency<IRobustSerializer>();
            using var stream = new MemoryStream();

            long Size(object message)
            {
                stream.SetLength(0);
                serializer.Serialize(stream, message);
                return stream.Length;
            }

            var stroke = new DrawingStroke
            {
                Brush = DrawingBrush.Circle,
                Size = 4,
                Color = Color.Red,
                Points = new List<Vector2>(),
            };
            var results = new List<string>();
            foreach (var pointCount in new[] { 8, 32, 256 })
            {
                stroke.Points.Clear();
                for (var point = 0; point < pointCount; point++)
                    stroke.Points.Add(new Vector2(12 + point % 200, 12 + point % 300));
                Assert.That(DrawingRaster.Validate(stroke, out _), Is.True);
                results.Add($"stroke points={pointCount} payload={Size(new DrawingTabletStrokeMessage(stroke, 0, Guid.NewGuid()))} B");
            }

            var strokes = CreateTrafficStrokes(DrawingTabletSettings.MaximumDrawingStrokes);
            foreach (var count in new[] { 0, 100, 1000 })
            {
                var document = new DrawingTabletState(strokes.GetRange(0, count), count, false, 1000);
                results.Add($"open strokes={count} payload={Size(new DrawingTabletDocumentMessage(document))} B");
            }
            results.Add($"ack payload={Size(new DrawingTabletAckMessage(1000, Guid.NewGuid()))} B");
            results.Add($"reject payload={Size(new DrawingTabletStrokeRejectedMessage(1000, Guid.NewGuid()))} B");

            foreach (var count in new[] { 100, 1000 })
            {
                var sample = strokes.GetRange(0, count);
                var times = new long[3];
                var allocations = new long[3];
                for (var trial = 0; trial < times.Length + 1; trial++)
                {
                    var before = GC.GetAllocatedBytesForCurrentThread();
                    var start = Stopwatch.GetTimestamp();
                    var pixels = DrawingRaster.Render(sample);
                    DrawingRaster.Encode(pixels);
                    DrawingRaster.Thumbnail(pixels);
                    if (trial == 0)
                        continue;
                    times[trial - 1] = Stopwatch.GetElapsedTime(start).Ticks;
                    allocations[trial - 1] = GC.GetAllocatedBytesForCurrentThread() - before;
                }
                Array.Sort(times);
                Array.Sort(allocations);
                results.Add($"print strokes={count} median={TimeSpan.FromTicks(times[1]).TotalMilliseconds:F2} ms allocated={allocations[1]} B");
            }

            var image = DrawingRaster.Encode(DrawingRaster.Render(strokes));
            results.Add($"print upload payload={Size(new DrawingTabletPrintUploadMessage(image, strokes.Count))} B");
            using (var batched = new DrawingRaster.RenderSession())
            {
                foreach (var currentStroke in strokes)
                    batched.Apply(currentStroke);
                Assert.That(batched.Pixels, Is.EqualTo(DrawingRaster.Render(strokes)));
            }
            results.Add($"printed image payload={Size(new DrawingPrintImageMessage(image, "Artist"))} B");
            results.Add($"maximum printed image payload={Size(new DrawingPrintImageMessage(new byte[DrawingRaster.ByteCount + 1], "Artist"))} B");
            foreach (var result in results)
                TestContext.Out.WriteLine($"-NR- Drawing tablet traffic {result}");
        });
    }

    [Test]
    public async Task VerifyUploadedPrintAndMeasureCost()
    {
        var brushCases = new List<DrawingStroke>
        {
            new() { Points = [new Vector2(0, 0), new Vector2(254, 329)], Brush = DrawingBrush.Circle, Size = 9, Color = Color.Red, Opacity = 40 },
            new() { Points = [new Vector2(120, 5), new Vector2(120, 290)], Brush = DrawingBrush.Square, Size = 8, Color = Color.Blue, Opacity = 65 },
            new() { Points = [new Vector2(0, 0), new Vector2(250, 325)], Brush = DrawingBrush.PixelSquare, Size = 5, Color = Color.Green },
            new() { Points = [new Vector2(40, 40), new Vector2(100, 90)], Brush = DrawingBrush.Circle, Size = 15, Erase = true, Opacity = 70 },
        };
        foreach (var stroke in brushCases)
            Assert.That(DrawingRaster.Validate(stroke, out _), Is.True);
        var brushPixels = DrawingRaster.Render(brushCases);
        for (var y = 0; y < DrawingRaster.CanvasHeightPixels; y += 3)
        for (var x = 0; x < DrawingRaster.CanvasWidthPixels; x += 3)
        {
            var expected = DrawingPrintVerifier.Sample(brushCases, x, y);
            var offset = (y * DrawingRaster.CanvasWidthPixels + x) * 3;
            Assert.That(expected, Is.EqualTo((brushPixels[offset], brushPixels[offset + 1], brushPixels[offset + 2])));
        }
        var strokes = CreateTrafficStrokes(DrawingTabletSettings.MaximumDrawingStrokes);
        var pixels = DrawingRaster.Render(strokes);
        var image = DrawingRaster.Encode(pixels);
        for (var y = 0; y < DrawingRaster.CanvasHeightPixels; y += 11)
        for (var x = 0; x < DrawingRaster.CanvasWidthPixels; x += 13)
        {
            var expected = DrawingPrintVerifier.Sample(strokes, x, y);
            var offset = (y * DrawingRaster.CanvasWidthPixels + x) * 3;
            Assert.That(expected, Is.EqualTo((pixels[offset], pixels[offset + 1], pixels[offset + 2])));
        }
        Assert.That(DrawingPrintVerifier.TryVerify(image, strokes, out var thumbnail), Is.True);
        Assert.That(thumbnail, Is.EqualTo(DrawingRaster.Thumbnail(pixels)));
        var forged = DrawingRaster.Encode(DrawingRaster.Blank());
        Assert.That(DrawingPrintVerifier.TryVerify(forged, strokes, out _), Is.False);

        var oldTimes = new double[7];
        var newTimes = new double[7];
        await Task.Run(() =>
        {
            for (var i = 0; i < oldTimes.Length; i++)
            {
                var start = Stopwatch.GetTimestamp();
                var rendered = DrawingRaster.Render(strokes);
                DrawingRaster.Encode(rendered);
                DrawingRaster.Thumbnail(rendered);
                oldTimes[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                start = Stopwatch.GetTimestamp();
                Assert.That(DrawingPrintVerifier.TryVerify(image, strokes, out _), Is.True);
                newTimes[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            }
        });
        Array.Sort(oldTimes);
        Array.Sort(newTimes);
        TestContext.Out.WriteLine($"-NR- Drawing tablet print CPU: full render median {oldTimes[3]:F3} ms, upload verification median {newTimes[3]:F3} ms, ratio {oldTimes[3] / newTimes[3]:F1}x");

        var repeatedPath = new List<Vector2>(32);
        for (var i = 0; i < 32; i++)
            repeatedPath.Add(i % 2 == 0 ? new Vector2(0, 0) : new Vector2(254, 329));
        var dense = new List<DrawingStroke>();
        for (var i = 0; i < 250; i++)
            dense.Add(new DrawingStroke { Points = repeatedPath, Brush = DrawingBrush.Circle, Size = 1, Color = Color.Red });
        Assert.That(dense.Sum(stroke => { Assert.That(DrawingRaster.Validate(stroke, out var work), Is.True); return work; }),
            Is.LessThanOrEqualTo(DrawingTabletSettings.MaximumDrawingRenderWork));
        var denseStart = Stopwatch.GetTimestamp();
        var densePixels = DrawingRaster.Render(dense);
        var denseImage = DrawingRaster.Encode(densePixels);
        DrawingRaster.Thumbnail(densePixels);
        var denseRenderMs = Stopwatch.GetElapsedTime(denseStart).TotalMilliseconds;
        denseStart = Stopwatch.GetTimestamp();
        Assert.That(DrawingPrintVerifier.TryVerify(denseImage, dense, out _), Is.True);
        var denseVerifyMs = Stopwatch.GetElapsedTime(denseStart).TotalMilliseconds;
        TestContext.Out.WriteLine($"-NR- Drawing tablet dense print CPU: full render {denseRenderMs:F3} ms, upload verification {denseVerifyMs:F3} ms, ratio {denseRenderMs / denseVerifyMs:F1}x");
    }

    private static List<DrawingStroke> CreateTrafficStrokes(int count)
    {
        var strokes = new List<DrawingStroke>(count);
        for (var i = 0; i < count; i++)
        {
            var points = new List<Vector2>(32);
            for (var j = 0; j < 32; j++)
                points.Add(new Vector2(12 + (i * 17 + j) % 230, 12 + (i * 31 + j) % 305));
            strokes.Add(new DrawingStroke { Points = points, Brush = DrawingBrush.Circle, Size = 4, Color = Color.Red });
        }
        return strokes;
    }
}
