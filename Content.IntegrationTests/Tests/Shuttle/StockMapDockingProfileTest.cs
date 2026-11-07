using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using Content.IntegrationTests.Fixtures;
using Content.Server.NodeContainer.EntitySystems;
using Content.Server.NodeContainer.Nodes;
using Content.Server.Power.Nodes;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Server._Starlight.Power;
using Content.Server._Starlight.Atmos.EntitySystems;
using Content.Shared._Starlight.CCVar;
using Content.Shared.Atmos;
using Content.Shared.NodeContainer;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Utility;
using Robust.Shared.Random;

namespace Content.IntegrationTests.Tests.Shuttle;

/// <summary>Profiles actual mapped utility connections; no forced links or map edits.</summary>
[NonParallelizable]
public sealed class StockMapDockingProfileTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = false, ServerSeed = 42042, ClientSeed = 42042 };

    [TestCase("Both")]
    [TestCase("Power")]
    [TestCase("Pipes")]
    [TestCase("Neither")]
    [Explicit("Stock-map docking diagnostic; run alone, outside the normal test suite.")]
    public Task ProfileReach(string mode) => Run("Reach", mode);

    [TestCase("Both")]
    [TestCase("Power")]
    [TestCase("Pipes")]
    [TestCase("Neither")]
    [Explicit("Stock-map docking diagnostic; run alone, outside the normal test suite.")]
    public Task ProfileMing(string mode) => Run("Ming", mode);

    [TestCase("Both")]
    [TestCase("Power")]
    [TestCase("Pipes")]
    [TestCase("Neither")]
    [Explicit("Stock-map docking diagnostic; run alone, outside the normal test suite.")]
    public Task ProfileFlotilla(string mode) => Run("Flotilla", mode);

    private async Task Run(string name, string mode)
    {
        var powerEnabled = mode is "Both" or "Power";
        var pipesEnabled = mode is "Both" or "Pipes";
        var em = Server.EntMan;
        var maps = em.System<SharedMapSystem>();
        var loader = em.System<MapLoaderSystem>();
        var transforms = em.System<SharedTransformSystem>();
        var groups = em.System<NodeGroupSystem>();
        var docking = em.System<DockingSystem>();
        var cables = em.System<CableDockingSystem>();
        var pipes = em.System<PipeDockingSystem>();
        Entity<MapComponent>? map = null;
        Entity<MapGridComponent> station = default, shuttle = default;
        Entity<DockingComponent> portA = default, portB = default;
        var links = new List<(Node A, Node B)>();
        var originalGroups = new List<Node[]>();
        var recorder = new NetworkConnectionProfiler();
        var rows = new List<string>();
        object metadata = new { name };
        var oldHV = Server.CfgMan.GetCVar(StarlightCCVars.DockHV);
        var oldMV = Server.CfgMan.GetCVar(StarlightCCVars.DockMV);
        var oldLV = Server.CfgMan.GetCVar(StarlightCCVars.DockLV);
        var oldPipes = Server.CfgMan.GetCVar(StarlightCCVars.DockPipes);
        var cycleSetting = Environment.GetEnvironmentVariable("REAL_MAP_CYCLES") ?? "6";
        Assert.That(int.TryParse(cycleSetting, out var cycles) && cycles >= 1 && cycles <= 50,
            Is.True, "REAL_MAP_CYCLES must be an integer from 1 to 50.");
        const int windowTicks = 3;
        var ticks = Pair.SecondsToTicks(60);
        try
        {
            await Server.WaitAssertion(() =>
            {
                Server.ResolveDependency<IRobustRandom>().SetSeed(42042);
                Server.CfgMan.SetCVar(StarlightCCVars.DockHV, true);
                Server.CfgMan.SetCVar(StarlightCCVars.DockMV, false);
                Server.CfgMan.SetCVar(StarlightCCVars.DockLV, false);
                Server.CfgMan.SetCVar(StarlightCCVars.DockPipes, true);
                Assert.That(loader.TryLoadMap(new ResPath($"/Maps/_Starlight/Stations/{name}.yml"), out map, out var grids,
                    DeserializationOptions.Default with { InitializeMaps = true, PauseMaps = false }), Is.True);
                Assert.That(grids!.Count, Is.EqualTo(1));
                station = grids.Single();
                Assert.That(loader.TryLoadGrid(map!.Value.Comp.MapId,
                    new ResPath("/Maps/_Starlight/Shuttles/Cargo/cargo_downpour.yml"), out var loaded), Is.True);
                shuttle = loaded!.Value;
                transforms.SetWorldPosition(shuttle, new Vector2(2000, 2000));
            });
            await Server.WaitRunTicks(5);
            await Server.WaitAssertion(() =>
            {
                var stationPorts = Ports(station.Owner);
                var shuttlePorts = Ports(shuttle.Owner);
                var stationPosition = name switch
                {
                    "Reach" => new Vector2(-21.5f, 7.5f),
                    "Ming" => new Vector2(-27.5f, -90.5f),
                    "Flotilla" => new Vector2(-77.5f, 11.5f),
                    _ => throw new ArgumentOutOfRangeException(nameof(name))
                };
                // Pin the mapped ports across utility modes; never silently select another dock.
                portA = stationPorts.Single(p => em.GetComponent<TransformComponent>(p).LocalPosition == stationPosition);
                portB = shuttlePorts.Single(p => em.GetComponent<TransformComponent>(p).LocalPosition == new Vector2(3.5f, -0.5f));
                links = Links(portA, portB);
                Assert.That(links.Count(p => p.A is CableNode), Is.EqualTo(1), "Stock port must offer power");
                Assert.That(links.Count(p => p.A is PipeNode), Is.EqualTo(name == "Ming" ? 0 : 4),
                    "Mapped pipe connections changed; inspect the map instead of interpreting missing links as faster rebuilds.");
                if (name == "Ming")
                    TestContext.Out.WriteLine("NO PIPE CONNECTION: this Ming port offers power only. Pipes mode is a no-pipe-link control, not a pipe benchmark.");
                originalGroups = links.SelectMany(p => new[] { p.A.NodeGroup!, p.B.NodeGroup! }).Distinct().Select(g => g.Nodes.ToArray()).ToList();
                var aX = em.GetComponent<TransformComponent>(portA);
                var bX = em.GetComponent<TransformComponent>(portB);
                var rotation = transforms.GetWorldRotation(aX) + Angle.FromDegrees(180) - bX.LocalRotation;
                transforms.SetWorldRotation(shuttle, rotation);
                var rotatedLocal = Vector2.Transform(bX.LocalPosition, Matrix3x2.CreateRotation((float)rotation.Theta));
                transforms.SetWorldPosition(shuttle, transforms.GetWorldPosition(aX) + transforms.GetWorldRotation(aX).ToWorldVec() - rotatedLocal);
                var activeGroups = links.Where(p => p.A is CableNode ? powerEnabled : pipesEnabled)
                    .SelectMany(p => new[] { p.A.NodeGroup!, p.B.NodeGroup! }).ToHashSet();
                metadata = new
                {
                    name, mode, powerEnabled, pipesEnabled, cycles, intervalSeconds = 60, pausedMap = false, randomSeed = 42042,
                    stationGrid = station.Owner.ToString(), shuttleGrid = shuttle.Owner.ToString(),
                    stationPort = aX.LocalPosition.ToString(), stationPortRotation = aX.LocalRotation.Theta,
                    shuttlePort = bX.LocalPosition.ToString(), shuttlePortRotation = bX.LocalRotation.Theta,
                    stationPorts = stationPorts.Count, shuttlePorts = shuttlePorts.Count,
                    cableLinks = links.Count(p => p.A is CableNode), pipeLinks = links.Count(p => p.A is PipeNode),
                    activeCableLinks = powerEnabled ? links.Count(p => p.A is CableNode) : 0,
                    activePipeLinks = pipesEnabled ? links.Count(p => p.A is PipeNode) : 0,
                    noMappedPipeConnection = !links.Any(p => p.A is PipeNode),
                    stockMappedConnections = true,
                    shuttleMap = "/Maps/_Starlight/Shuttles/Cargo/cargo_downpour.yml",
                    availableGroups = originalGroups.Select(ns => new { kind = ns[0].NodeGroupID.ToString(), side = em.GetComponent<TransformComponent>(ns[0].Owner).GridUid == station.Owner ? "station" : "shuttle", nodes = ns.Length }).ToArray(),
                    affectedGroups = originalGroups.Where(ns => activeGroups.Contains(ns[0].NodeGroup!))
                        .Select(ns => new { kind = ns[0].NodeGroupID.ToString(), side = em.GetComponent<TransformComponent>(ns[0].Owner).GridUid == station.Owner ? "station" : "shuttle", nodes = ns.Length }).ToArray(),
                    eventTimingDisabled = Environment.GetEnvironmentVariable("SS14_TEST_DISABLE_EVENT_TIMING"),
                    tieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"),
                    frequency = Stopwatch.Frequency, runtime = Environment.Version.ToString()
                };
                Server.CfgMan.SetCVar(StarlightCCVars.DockHV, powerEnabled);
                Server.CfgMan.SetCVar(StarlightCCVars.DockPipes, pipesEnabled);
                Check(false);
            });
            await Server.WaitRunTicks(3);
            for (var cycle = -1; cycle < cycles; cycle++)
            foreach (var docked in new[] { true, false })
            {
                var label = docked ? "dock" : "undock";
                await Capture(cycle, label, () =>
                {
                    if (docked) docking.Dock(portA, portB);
                    else docking.Undock(portA);
                });
                await Server.WaitAssertion(() => Check(docked));
                await Server.WaitRunTicks(Math.Max(0, ticks - windowTicks * 2));
                await Capture(cycle, label + "-idle", null);
                await Server.WaitAssertion(() => Check(docked));
            }

            List<Entity<DockingComponent>> Ports(EntityUid grid)
            {
                var result = new List<Entity<DockingComponent>>();
                var query = em.AllEntityQueryEnumerator<DockingComponent, TransformComponent>();
                while (query.MoveNext(out var uid, out var dock, out var xf))
                    if (xf.GridUid == grid && !dock.Docked) result.Add((uid, dock));
                return result.OrderBy(d => em.GetComponent<TransformComponent>(d).LocalPosition.X)
                    .ThenBy(d => em.GetComponent<TransformComponent>(d).LocalPosition.Y).ToList();
            }
            List<(Node A, Node B)> Links(Entity<DockingComponent> a, Entity<DockingComponent> b)
            {
                var result = new List<(Node, Node)>();
                foreach (var ca in cables.GetDockCableNodes(a))
                foreach (var cb in cables.GetDockCableNodes(b))
                    if (cables.CanConnect(ca, cb)) result.Add((ca, cb));
                var dirA = em.GetComponent<TransformComponent>(a).LocalRotation.GetCardinalDir().ToPipeDirection();
                var dirB = em.GetComponent<TransformComponent>(b).LocalRotation.GetCardinalDir().ToPipeDirection();
                foreach (var pa in pipes.GetTilePipes(a))
                foreach (var pb in pipes.GetTilePipes(b))
                    if (pa.CurrentPipeDirection.HasDirection(dirA) && pb.CurrentPipeDirection.HasDirection(dirB) && PipeDockingSystem.CanConnect(pa, pb)) result.Add((pa, pb));
                return result;
            }
            void Check(bool docked)
            {
                Assert.That(maps.IsPaused(map!.Value.Comp.MapId), Is.False, "Stock-map profiling requires active simulation.");
                Assert.That(portA.Comp.Docked, Is.EqualTo(docked));
                Assert.That(portB.Comp.Docked, Is.EqualTo(docked));
                foreach (var (a,b) in links)
                    Assert.That(ReferenceEquals(a.NodeGroup,b.NodeGroup), Is.EqualTo(docked && (a is CableNode ? powerEnabled : pipesEnabled)));
                foreach (var ns in originalGroups)
                {
                    Assert.That(ns.All(n => ReferenceEquals(n.NodeGroup, ns[0].NodeGroup)), Is.True);
                    if (!docked) Assert.That(ns[0].NodeGroup!.Nodes.Count, Is.EqualTo(ns.Length));
                }
                var expected = originalGroups.Sum(ns => ns.Length);
                var actual = originalGroups.Select(ns => ns[0].NodeGroup!).Distinct().Sum(g => g.Nodes.Count);
                Assert.That(actual, Is.EqualTo(expected), "No unrelated network must join or disappear");
            }
            async Task Capture(int cycle, string window, Action? action)
            {
                recorder.Samples.Clear();
                var gcBefore = new int[3];
                await Server.WaitPost(() =>
                {
                    for (var gen = 0; gen < 3; gen++) gcBefore[gen] = GC.CollectionCount(gen);
                    groups.ConnectionProfiler = recorder;
                    if (action != null) { using var scope = recorder.Measure("operation.total"); action(); }
                });
                await Server.WaitRunTicks(windowTicks);
                await Server.WaitAssertion(() =>
                {
                    groups.ConnectionProfiler = null;
                    Assert.That(recorder.DroppedSamples, Is.Zero);
                    Assert.That(groups.PauseUpdating, Is.False);
                    if (cycle < 0) return;
                    foreach (var s in recorder.Samples)
                        rows.Add(FormattableString.Invariant($"{cycle},{window},{s.Phase},{s.Start},{s.Elapsed},{s.Bytes},{s.Items},{GC.CollectionCount(0)-gcBefore[0]},{GC.CollectionCount(1)-gcBefore[1]},{GC.CollectionCount(2)-gcBefore[2]}"));
                });
            }
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(TestContext.CurrentContext.WorkDirectory, $"stock-map-dock-error-{name}-{mode}-{Guid.NewGuid():N}.txt"), ex.ToString());
            throw;
        }
        finally
        {
            await Server.WaitPost(() =>
            {
                groups.ConnectionProfiler = null;
                if (map != null) maps.DeleteMap(map.Value.Comp.MapId);
                Server.CfgMan.SetCVar(StarlightCCVars.DockHV, oldHV);
                Server.CfgMan.SetCVar(StarlightCCVars.DockMV, oldMV);
                Server.CfgMan.SetCVar(StarlightCCVars.DockLV, oldLV);
                Server.CfgMan.SetCVar(StarlightCCVars.DockPipes, oldPipes);
            });
            var stem = Path.Combine(TestContext.CurrentContext.WorkDirectory,$"stock-map-dock-{name}-{mode}-{Guid.NewGuid():N}");
            File.WriteAllLines(stem+".csv", new[] {"cycle,window,phase,start_timestamp,elapsed_timestamp_ticks,thread_bytes,items,window_gc0,window_gc1,window_gc2"}.Concat(rows));
            File.WriteAllText(stem+".json", JsonSerializer.Serialize(metadata,new JsonSerializerOptions { WriteIndented = true }));
            TestContext.AddTestAttachment(stem+".csv"); TestContext.AddTestAttachment(stem+".json");
            TestContext.Out.WriteLine(JsonSerializer.Serialize(metadata));
        }
    }
}
