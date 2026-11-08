#nullable enable
#pragma warning disable RA0002 // Read radar update timestamps to verify idle and detached behavior.
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server.Mind;
using Content.Shared.Damage.Components;
using Content.Shared.Shuttles.BUIStates;
using Content.Shared.Shuttles.Components;
using Robust.Shared;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._Starlight.Shuttles;

[TestFixture]
public sealed class RadarViewerUpdatesTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    [Test]
    public async Task UpdatesOnlyWhileViewedAndRefreshesOnReopen()
    {
        var map = await Pair.CreateTestMap();
        EntityUid viewer = default, scanner = default, dock = default, otherBody = default;
        NetEntity scannerNet = default, dockNet = default;
        await Server.WaitAssertion(() =>
        {
            Server.CfgMan.SetCVar(CVars.NetPVS, true);
            Server.CfgMan.SetCVar(CVars.NetPvsAsync, false);
            viewer = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            SEntMan.EnsureComponent<GodmodeComponent>(viewer);
            otherBody = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            SEntMan.EnsureComponent<GodmodeComponent>(otherBody);
            Server.System<MindSystem>().ControlMob(ServerSession!.UserId, viewer);
            dock = SEntMan.SpawnEntity("AirlockShuttle", map.GridCoords);
            scanner = SEntMan.SpawnEntity("HandHeldMassScanner", map.GridCoords);
            scannerNet = SEntMan.GetNetEntity(scanner);
            dockNet = SEntMan.GetNetEntity(dock);
        });
        await RunSeconds(12);
        await AssertNoCachedState();
        await Server.WaitAssertion(() => Assert.That(SEntMan.GetComponent<RadarConsoleComponent>(scanner).LastInterfaceUpdateTime, Is.EqualTo(TimeSpan.Zero)));
        await Client.WaitAssertion(() => Assert.That(Client.System<RadarUpdateCaptureSystem>().Updates, Is.Empty));

        await OpenAndCheckSnapshot();
        await AssertNoCachedState(); // Even an active radar must not place its payload in replicated UI component state.
        await Client.WaitAssertion(() => Assert.That(Client.System<RadarUpdateCaptureSystem>().Updates.Last().DockingPortStates, Is.Null,
            "After acknowledgement, live updates should omit the docking snapshot."));

        await Server.WaitAssertion(() => Server.System<SharedUserInterfaceSystem>().CloseUi(scanner, RadarConsoleUiKey.Key, viewer));
        await RunSeconds(0.5f);
        TimeSpan lastUpdate = default;
        await Server.WaitAssertion(() =>
        {
            lastUpdate = SEntMan.GetComponent<RadarConsoleComponent>(scanner).LastInterfaceUpdateTime;
            Server.System<Robust.Server.GameObjects.ServerMetaDataSystem>().SetEntityName(dock, "Renamed test dock");
        });
        await Client.WaitAssertion(() => Client.System<RadarUpdateCaptureSystem>().Updates.Clear());
        await RunSeconds(12);
        await Server.WaitAssertion(() => Assert.That(SEntMan.GetComponent<RadarConsoleComponent>(scanner).LastInterfaceUpdateTime, Is.EqualTo(lastUpdate)));
        await Client.WaitAssertion(() => Assert.That(Client.System<RadarUpdateCaptureSystem>().Updates, Is.Empty));
        await OpenAndCheckSnapshot("Renamed test dock");

        // Detaching the viewer can leave a UI actor entry behind. It must not keep radar updates alive.
        await Server.WaitAssertion(() => Server.System<MindSystem>().ControlMob(ServerSession!.UserId, otherBody));
        await RunSeconds(0.5f);
        await Server.WaitAssertion(() => lastUpdate = SEntMan.GetComponent<RadarConsoleComponent>(scanner).LastInterfaceUpdateTime);
        await Client.WaitAssertion(() => Client.System<RadarUpdateCaptureSystem>().Updates.Clear());
        await RunSeconds(1);
        await Server.WaitAssertion(() => Assert.That(SEntMan.GetComponent<RadarConsoleComponent>(scanner).LastInterfaceUpdateTime, Is.EqualTo(lastUpdate)));
        await Client.WaitAssertion(() => Assert.That(Client.System<RadarUpdateCaptureSystem>().Updates, Is.Empty));

        async Task OpenAndCheckSnapshot(string? expectedName = null)
        {
            await Client.WaitAssertion(() => Client.System<RadarUpdateCaptureSystem>().Updates.Clear());
            await Server.WaitAssertion(() => Server.System<SharedUserInterfaceSystem>().OpenUi(scanner, RadarConsoleUiKey.Key, viewer));
            await RunSeconds(1);
            await Client.WaitAssertion(() =>
            {
                var updates = Client.System<RadarUpdateCaptureSystem>().Updates;
                Assert.That(updates, Is.Not.Empty);
                var snapshot = updates.FirstOrDefault(s => s.DockingPortStates != null)?.DockingPortStates;
                Assert.That(snapshot, Is.Not.Null);
                Assert.That(snapshot!.Docks.Values.SelectMany(x => x).Any(d => d.Entity == dockNet), Is.True);
                if (expectedName != null)
                    Assert.That(snapshot.Docks.Values.SelectMany(x => x).Any(d => d.Name == expectedName), Is.True);
            });
        }

        async Task AssertNoCachedState()
        {
            await Server.WaitAssertion(() => Assert.That(Server.System<SharedUserInterfaceSystem>().TryGetUiState<NavBoundUserInterfaceState>(scanner, RadarConsoleUiKey.Key, out _), Is.False));
            await Client.WaitAssertion(() =>
            {
                Assert.That(CEntMan.TryGetEntity(scannerNet, out var uid), Is.True);
                Assert.That(Client.System<SharedUserInterfaceSystem>().TryGetUiState<NavBoundUserInterfaceState>(uid!.Value, RadarConsoleUiKey.Key, out _), Is.False);
            });
        }
    }
}

public sealed class RadarUpdateCaptureSystem : EntitySystem
{
    public readonly List<NavBoundUserInterfaceState> Updates = new();

    public override void Initialize() => SubscribeLocalEvent<RadarConsoleComponent, RadarConsoleUpdateMessage>(OnUpdate);

    private void OnUpdate(EntityUid uid, RadarConsoleComponent component, RadarConsoleUpdateMessage args) => Updates.Add(args.State);
}
