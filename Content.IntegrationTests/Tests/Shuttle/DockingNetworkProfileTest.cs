using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server.NodeContainer.EntitySystems;
using Content.Server.NodeContainer.Nodes;
using Content.Server.Power.Nodes;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared._Starlight.CCVar;
using Content.Shared.Atmos;
using Content.Shared.NodeContainer;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests.Shuttle;

/// <summary>Real dock events and naturally scheduled rebuilds; no ForceUpdate inside measured windows.</summary>
[NonParallelizable]
public sealed class DockingNetworkProfileTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = false, ServerSeed = 42042, ClientSeed = 42042 };

    [TestPrototypes]
    private const string Prototypes = """
- type: entity
  id: DockProfileSupplier
  components:
  - type: Transform
    anchored: true
  - type: NodeContainer
    nodes:
      power: !type:CableDeviceNode
        nodeGroupID: HVPower
  - type: PowerSupplier
    voltage: High
    supplyRate: 100000
- type: entity
  id: DockProfileConsumer
  components:
  - type: Transform
    anchored: true
  - type: NodeContainer
    nodes:
      power: !type:CableDeviceNode
        nodeGroupID: HVPower
  - type: PowerConsumer
    voltage: High
    drawRate: 100
""";

    [TestCase(true, true)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(false, false)]
    public Task SmallNetworksMergeAndSplit(bool cables, bool pipes)
        => Run(4, 2, 0.2f, cables, pipes);

    [TestCase(true, true)]
    [Explicit("Large diagnostic workload; 60 simulated seconds between dock/undock operations.")]
    public Task ProfileLargeNetworks(bool cables, bool pipes)
        => Run(ReadSetting("DOCK_PROFILE_SIZE", 64, 4, 128),
            ReadSetting("DOCK_PROFILE_CYCLES", 8, 1, 50), 60, cables, pipes);

    private static int ReadSetting(string name, int fallback, int min, int max)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (value == null)
            return fallback;
        Assert.That(int.TryParse(value, out var parsed) && parsed >= min && parsed <= max,
            $"{name} must be an integer in [{min}, {max}].");
        return parsed;
    }

    private async Task Run(int size, int cycles, float interval, bool cables, bool pipes)
    {
        var map = await Pair.CreateTestMap();
        var em = Server.EntMan;
        var groups = em.System<NodeGroupSystem>();
        var docking = em.System<DockingSystem>();
        var mapSystem = em.System<SharedMapSystem>();
        var transform = em.System<SharedTransformSystem>();
        var docks = new Entity<DockingComponent>[2];
        var cableNodes = new CableNode[2];
        var pipeNodes = new PipeNode[2];
        // Reuse storage: allocating a large recorder every window would itself provoke GC.
        var recorder = new NetworkConnectionProfiler();
        var rows = new List<string>();
        var summaries = new List<(string Window, NetworkConnectionProfiler.Sample Sample)>();
        var oldHV = Server.CfgMan.GetCVar(StarlightCCVars.DockHV);
        var oldPipes = Server.CfgMan.GetCVar(StarlightCCVars.DockPipes);
        var seeded = false;
        var ticks = Pair.SecondsToTicks(interval);
        const int windowTicks = 3;

        try
        {
            await Server.WaitAssertion(() =>
            {
                Server.CfgMan.SetCVar(StarlightCCVars.DockHV, cables);
                Server.CfgMan.SetCVar(StarlightCCVars.DockPipes, pipes);
                for (var side = 0; side < 2; side++)
                {
                    var grid = mapSystem.CreateGridEntity(map.MapId);
                    transform.SetLocalPosition(grid, new Vector2(0, side == 0 ? 0 : -size));
                    var tiles = new List<(Vector2i, Tile)>();
                    for (var x = 0; x < size; x++)
                    for (var y = 0; y < size; y++)
                        tiles.Add((new Vector2i(x, y), map.Tile.Tile));
                    mapSystem.SetTiles(grid.Owner, grid.Comp, tiles);
                    var portY = side == 0 ? 0 : size - 1;
                    var portCoords = new EntityCoordinates(grid, new Vector2(0.5f, portY + 0.5f));
                    var dock = em.SpawnEntity("AirlockShuttle", portCoords);
                    transform.SetLocalRotation(dock, side == 0 ? Angle.Zero : Angle.FromDegrees(180));
                    docks[side] = (dock, em.GetComponent<DockingComponent>(dock));

                    for (var x = 0; x < size; x++)
                    for (var y = 0; y < size; y++)
                    {
                        var coords = new EntityCoordinates(grid, new Vector2(x + 0.5f, y + 0.5f));
                        var cable = em.SpawnEntity("CableHV", coords);
                        var pipe = em.SpawnEntity("GasPipeFourway", coords);
                        if (x == 0 && y == portY)
                        {
                            cableNodes[side] = em.GetComponent<NodeContainerComponent>(cable).Nodes.Values.OfType<CableNode>().Single();
                            pipeNodes[side] = em.GetComponent<NodeContainerComponent>(pipe).Nodes.Values.OfType<PipeNode>().Single();
                        }
                        // One real power device per row, in addition to the cable/pipe lattice.
                        if (x == size - 1)
                            em.SpawnEntity(y == 0 ? "DockProfileSupplier" : "DockProfileConsumer", coords);
                    }
                }
            });
            await Server.WaitRunTicks(windowTicks);
            await Server.WaitAssertion(() =>
            {
                Check(false);
                pipeNodes[0].Air.SetMoles(Gas.Nitrogen, 1000);
                pipeNodes[1].Air.SetMoles(Gas.Nitrogen, 2000);
                seeded = true;
            });

            // First complete cycle exercises both paths before retaining any timing samples.
            for (var cycle = -1; cycle < cycles; cycle++)
            foreach (var docked in new[] { true, false })
            {
                var label = docked ? "dock" : "undock";
                await Capture(cycle, label, () =>
                {
                    if (docked)
                        docking.Dock(docks[0], docks[1]);
                    else
                        docking.Undock(docks[0]);
                });
                await Server.WaitAssertion(() => Check(docked));
                // Include a settled baseline, ending at the next 60-second boundary.
                await Server.WaitRunTicks(Math.Max(0, ticks - 2 * windowTicks));
                await Capture(cycle, label + "-idle", null);
                await Server.WaitAssertion(() => Check(docked));
            }

            async Task Capture(int cycle, string window, Action action)
            {
                recorder.Samples.Clear();
                var gcBefore = new int[3];
                await Server.WaitPost(() =>
                {
                    for (var gen = 0; gen < 3; gen++) gcBefore[gen] = GC.CollectionCount(gen);
                    groups.ConnectionProfiler = recorder;
                    if (action != null)
                    {
                        using var scope = recorder.Measure("operation.total");
                        action();
                    }
                });
                await Server.WaitRunTicks(windowTicks);
                await Server.WaitAssertion(() =>
                {
                    groups.ConnectionProfiler = null;
                    var gc0 = GC.CollectionCount(0) - gcBefore[0];
                    var gc1 = GC.CollectionCount(1) - gcBefore[1];
                    var gc2 = GC.CollectionCount(2) - gcBefore[2];
                    Assert.That(recorder.DroppedSamples, Is.Zero);
                    Assert.That(groups.PauseUpdating, Is.False, "Explosion pause invalidates this benchmark.");
                    Assert.That(mapSystem.IsPaused(map.MapId), Is.False, "The synthetic benchmark must remain unpaused.");
                    if (cycle < 0) return;
                    foreach (var sample in recorder.Samples)
                    {
                        rows.Add(FormattableString.Invariant($"{cycle},{window},{sample.Phase},{sample.Start},{sample.Elapsed},{sample.Bytes},{sample.Items},{gc0},{gc1},{gc2}"));
                        summaries.Add((window, sample));
                    }
                });
            }

            void Check(bool docked)
            {
                Assert.That(docks[0].Comp.Docked, Is.EqualTo(docked));
                Assert.That(docks[1].Comp.Docked, Is.EqualTo(docked));
                Assert.That(ReferenceEquals(cableNodes[0].NodeGroup, cableNodes[1].NodeGroup), Is.EqualTo(docked && cables));
                Assert.That(ReferenceEquals(pipeNodes[0].NodeGroup, pipeNodes[1].NodeGroup), Is.EqualTo(docked && pipes));
                for (var side = 0; side < 2; side++)
                {
                    Assert.That(cableNodes[side].NodeGroup!.Nodes.Count, Is.EqualTo((size * size + size) * (docked && cables ? 2 : 1)));
                    Assert.That(pipeNodes[side].NodeGroup!.Nodes.Count, Is.EqualTo(size * size * (docked && pipes ? 2 : 1)));
                }
                var moles = pipeNodes[0].Air.TotalMoles;
                if (!ReferenceEquals(pipeNodes[0].NodeGroup, pipeNodes[1].NodeGroup))
                    moles += pipeNodes[1].Air.TotalMoles;
                // Initial setup has empty pipes; thereafter gas must survive every merge/split.
                Assert.That(moles, Is.EqualTo(seeded ? 3000 : 0).Within(0.1));
            }
        }
        finally
        {
            await Server.WaitPost(() =>
            {
                groups.ConnectionProfiler = null;
                Server.CfgMan.SetCVar(StarlightCCVars.DockHV, oldHV);
                Server.CfgMan.SetCVar(StarlightCCVars.DockPipes, oldPipes);
            });
            var path = Path.Combine(TestContext.CurrentContext.WorkDirectory,
                $"dock-profile-{size}-{cables}-{pipes}-{Guid.NewGuid():N}.csv");
            File.WriteAllLines(path, new[] { "cycle,window,phase,start_timestamp,elapsed_timestamp_ticks,thread_bytes,items,window_gc0,window_gc1,window_gc2" }.Concat(rows));
            TestContext.AddTestAttachment(path);
            TestContext.Out.WriteLine($"size={size}, cycles={cycles}, interval={interval}s simulated, frequency={Stopwatch.Frequency}, runtime={Environment.Version}, CPU count={Environment.ProcessorCount}, event timing disabled={Environment.GetEnvironmentVariable("SS14_TEST_DISABLE_EVENT_TIMING")}, tiered compilation={Environment.GetEnvironmentVariable("DOTNET_TieredCompilation")}; inclusive scopes, server-thread allocation only.");
            foreach (var group in summaries.GroupBy(s => (s.Window, s.Sample.Phase)))
            {
                var times = group.Select(s => s.Sample.Elapsed * 1000d / Stopwatch.Frequency).Order().ToArray();
                TestContext.Out.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"{group.Key}: n={times.Length}, total={times.Sum():F4}ms, median={(times.Length % 2 == 0 ? (times[times.Length / 2 - 1] + times[times.Length / 2]) / 2 : times[times.Length / 2]):F4}ms, p95={times[(int)Math.Ceiling(times.Length * 0.95) - 1]:F4}ms, max={times[^1]:F4}ms, bytes={group.Sum(s => s.Sample.Bytes)}"));
            }
        }
    }
}
