using System.Linq;
using System.Collections.Generic;
using Stopwatch = System.Diagnostics.Stopwatch;
using System.IO;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Shared._Starlight.Drawing;
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
    public async Task ReportPrintWorkerBatchSpikes()
    {
        var strokes = CreateTrafficStrokes(DrawingTabletSettings.MaximumDrawingStrokes);
        var expected = DrawingRaster.Render(strokes);
        var unpacedTimes = await Task.Run(() =>
        {
            var times = new double[3];
            for (var trial = 0; trial < times.Length; trial++)
            {
                var start = Stopwatch.GetTimestamp();
                var pixels = DrawingRaster.Render(strokes);
                DrawingRaster.Encode(pixels);
                DrawingRaster.Thumbnail(pixels);
                times[trial] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            }
            return times;
        });
        Array.Sort(unpacedTimes);
        TestContext.Out.WriteLine($"-NR- Drawing tablet worker unpaced 1000 strokes/32000 points: median burst {unpacedTimes[1]:F2} ms, highest observed {unpacedTimes[2]:F2} ms");
        var peakSamples = new double[3];
        var activeSamples = new double[3];
        var wallSamples = new double[3];
        var batchCount = 0;
        for (var trial = 0; trial < peakSamples.Length; trial++)
        {
            var measurement = await Task.Run(async () =>
            {
                var wallStart = Stopwatch.GetTimestamp();
                var batches = new List<double>();
                var batchStrokes = 0;
                var batchWork = 0;
                using var render = new DrawingRaster.RenderSession();
                var batchStart = Stopwatch.GetTimestamp();
                foreach (var stroke in strokes)
                {
                    Assert.That(DrawingRaster.Validate(stroke, out var work), Is.True);
                    if (batchStrokes >= DrawingTabletSettings.PrintBatchStrokeLimit
                        || batchStrokes > 0 && batchWork + work > DrawingTabletSettings.PrintBatchWorkLimit)
                    {
                        batches.Add(Stopwatch.GetElapsedTime(batchStart).TotalMilliseconds);
                        await Task.Delay(DrawingTabletSettings.PrintBatchPauseMilliseconds);
                        batchStrokes = 0;
                        batchWork = 0;
                        batchStart = Stopwatch.GetTimestamp();
                    }
                    render.Apply(stroke);
                    batchStrokes++;
                    batchWork += work;
                }
                DrawingRaster.Encode(render.Pixels);
                DrawingRaster.Thumbnail(render.Pixels);
                batches.Add(Stopwatch.GetElapsedTime(batchStart).TotalMilliseconds);
                Assert.That(render.Pixels, Is.EqualTo(expected));
                var remaining = TimeSpan.FromMilliseconds(DrawingTabletSettings.MinimumPrintDurationMilliseconds)
                    - Stopwatch.GetElapsedTime(wallStart);
                if (remaining > TimeSpan.Zero)
                    await Task.Delay(remaining);
                return (Count: batches.Count, Peak: batches.Max(), Active: batches.Sum(), Wall: Stopwatch.GetElapsedTime(wallStart).TotalMilliseconds);
            });
            batchCount = measurement.Count;
            peakSamples[trial] = measurement.Peak;
            activeSamples[trial] = measurement.Active;
            wallSamples[trial] = measurement.Wall;
        }
        Array.Sort(peakSamples);
        Array.Sort(activeSamples);
        Array.Sort(wallSamples);
        TestContext.Out.WriteLine($"-NR- Drawing tablet worker 1000 strokes/32000 points: {batchCount} batches, median peak batch {peakSamples[1]:F2} ms, highest observed batch {peakSamples[2]:F2} ms, median active render time {activeSamples[1]:F2} ms, median print time {wallSamples[1]:F0} ms");

        var dense = new DrawingStroke
        {
            Brush = DrawingBrush.Square,
            Size = 64,
            Points = Enumerable.Repeat(new Vector2(128, 128), 200).ToList(),
        };
        Assert.That(DrawingRaster.Validate(dense, out var denseWork), Is.True);
        var denseTimes = await Task.Run(() =>
        {
            var times = new double[3];
            for (var trial = 0; trial < times.Length; trial++)
            {
                using var render = new DrawingRaster.RenderSession();
                var start = Stopwatch.GetTimestamp();
                render.Apply(dense);
                times[trial] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            }
            return times;
        });
        Array.Sort(denseTimes);
        TestContext.Out.WriteLine($"-NR- Drawing tablet worker dense stroke work={denseWork}: median batch {denseTimes[1]:F2} ms, highest observed {denseTimes[2]:F2} ms");
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
