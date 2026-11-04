using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Tests.Interaction;
using Content.Server._Starlight.Magic;
using Content.Shared._Starlight.Magic.Mime;
using Content.Shared.Abilities.Mime;
using Content.Shared.Actions;
using Content.Shared.Actions.Components;
using Content.Shared.ActionBlocker;
using Content.Shared.Damage;
using Content.Shared.Interaction.Components;
using Content.Shared.Interaction.Events;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Systems;
using Content.Shared.Projectiles;
using Content.Shared.Speech.Muting;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._Starlight.Weapons;

public sealed class MimeMagicTest : InteractionTest
{
    [Test]
    public async Task BrokenVowStopsDrawnFingerGunFromFiring()
    {
        EntityUid gun = default;
        await Server.WaitAssertion(() =>
        {
            SEntMan.EnsureComponent<MimePowersComponent>(SPlayer);
            var actions = Server.System<SharedActionsSystem>();
            var actionUid = actions.AddAction(SPlayer, "ActionMimeFingerGun");
            Assert.That(actionUid, Is.Not.Null);
            gun = SEntMan.GetComponent<MimeFingerGunActionComponent>(actionUid!.Value).Gun!.Value;
            actions.PerformAction(ToServer(Player), actions.GetAction(actionUid)!.Value);
            Assert.That(HandSys.IsHolding(SPlayer, gun), Is.True);
        });

        await Server.WaitRunTicks(20);
        await Server.WaitAssertion(() =>
        {
            var mime = SEntMan.GetComponent<MimePowersComponent>(SPlayer);
            mime.Enabled = false;
            mime.VowBroken = true;
            var ammo = SEntMan.GetComponent<BasicEntityAmmoProviderComponent>(gun);
            var before = ammo.Count;
            var target = SEntMan.GetComponent<TransformComponent>(SPlayer).Coordinates.Offset(new Vector2(3, 0));
            Assert.That(SGun.AttemptShoot(SPlayer, (gun, SEntMan.GetComponent<GunComponent>(gun)), target), Is.False);
            Assert.That(ammo.Count, Is.EqualTo(before));
        });
    }

    [Test]
    public async Task FingerGunTogglesInHandAndKeepsItsAmmo()
    {
        EntityUid gun = default;
        Entity<ActionComponent> action = default;
        await Server.WaitAssertion(() =>
        {
            SEntMan.EnsureComponent<MimePowersComponent>(SPlayer);
            var actions = Server.System<SharedActionsSystem>();
            var actionUid = actions.AddAction(SPlayer, "ActionMimeFingerGun");
            Assert.That(actionUid, Is.Not.Null);
            var foundAction = actions.GetAction(actionUid);
            Assert.That(foundAction, Is.Not.Null);
            action = foundAction!.Value;

            var fingerGun = SEntMan.GetComponent<MimeFingerGunActionComponent>(actionUid!.Value);
            Assert.That(fingerGun.Gun, Is.Not.Null);
            gun = fingerGun.Gun!.Value;
            var ammo = SEntMan.GetComponent<BasicEntityAmmoProviderComponent>(gun);
            Assert.That(ammo.Count, Is.EqualTo(5));
            Assert.That(HandSys.IsHolding(SPlayer, gun), Is.False);

            actions.PerformAction(ToServer(Player), action);
            Assert.That(HandSys.IsHolding(SPlayer, gun), Is.True, "The finger gun was not drawn into the hand.");
            Assert.That(HandSys.TryDrop(SPlayer), Is.False, "The drawn finger gun can be dropped.");
            Assert.That(Server.System<ActionBlockerSystem>().CanThrow(SPlayer, gun), Is.False,
                "The finger gun can be thrown.");
        });

        // Selecting a gun starts its normal first-shot delay (about 0.45 seconds).
        await Server.WaitRunTicks(20);
        await Server.WaitAssertion(() =>
        {
            var ammo = SEntMan.GetComponent<BasicEntityAmmoProviderComponent>(gun);
            var target = SEntMan.GetComponent<TransformComponent>(SPlayer).Coordinates.Offset(new Vector2(3, 0));

            var gunComp = SEntMan.GetComponent<GunComponent>(gun);
            Assert.That(SGun.AttemptShoot(SPlayer, (gun, gunComp), target), Is.True,
                "A drawn finger gun should fire after its normal draw delay.");
            Assert.That(ammo.Count, Is.EqualTo(4), "A drawn finger gun did not fire one shot.");

            Server.System<SharedActionsSystem>().PerformAction(ToServer(Player), action);
            Assert.That(HandSys.IsHolding(SPlayer, gun), Is.False, "The finger gun stayed in the hand after holstering.");
            Assert.That(ammo.Count, Is.EqualTo(4), "Holstering reset the ammunition.");
        });

        await Server.WaitRunTicks(315);
        await Server.WaitAssertion(() => Assert.That(
            SEntMan.GetComponent<BasicEntityAmmoProviderComponent>(gun).Count,
            Is.EqualTo(4), "The finger gun recharged before its new twenty-second interval."));

        await Server.WaitRunTicks(315);
        await Server.WaitAssertion(() =>
        {
            var ammo = SEntMan.GetComponent<BasicEntityAmmoProviderComponent>(gun);
            Assert.That(ammo.Count, Is.EqualTo(5), "The stowed finger gun did not recharge its shot.");

            Server.System<SharedActionsSystem>().PerformAction(ToServer(Player), action);
            Assert.That(HandSys.IsHolding(SPlayer, gun), Is.True);
            SEntMan.RemoveComponent<UnremoveableComponent>(gun);
            Assert.That(HandSys.TryDrop(SPlayer), Is.True);
        });

        await Server.WaitRunTicks(2);
        await Server.WaitAssertion(() =>
        {
            Assert.That(HandSys.IsHolding(SPlayer, gun), Is.False);
            Assert.That(SEntMan.EntityExists(gun), Is.True, "A forcibly dropped finger gun was deleted.");
            Server.System<SharedActionsSystem>().PerformAction(ToServer(Player), action);
            Assert.That(HandSys.IsHolding(SPlayer, gun), Is.True,
                "A forcibly dropped finger gun did not return to its action.");
        });
    }

    [Test]
    public async Task LassoTogglesInHandAndCannotBeUsedWithFingerGun()
    {
        EntityUid lasso = default;
        EntityUid fingerGun = default;
        Entity<ActionComponent> lassoAction = default;
        Entity<ActionComponent> fingerGunAction = default;
        await Server.WaitAssertion(() =>
        {
            SEntMan.EnsureComponent<MimePowersComponent>(SPlayer);
            var actions = Server.System<SharedActionsSystem>();
            var lassoActionUid = actions.AddAction(SPlayer, "ActionMimeInvisibleHook");
            var fingerGunActionUid = actions.AddAction(SPlayer, "ActionMimeFingerGun");
            Assert.That(lassoActionUid, Is.Not.Null);
            Assert.That(fingerGunActionUid, Is.Not.Null);
            lassoAction = actions.GetAction(lassoActionUid)!.Value;
            fingerGunAction = actions.GetAction(fingerGunActionUid)!.Value;
            lasso = SEntMan.GetComponent<MimeLassoActionComponent>(lassoActionUid!.Value).Lasso!.Value;
            fingerGun = SEntMan.GetComponent<MimeFingerGunActionComponent>(fingerGunActionUid!.Value).Gun!.Value;

            actions.PerformAction(ToServer(Player), lassoAction);
            Assert.That(HandSys.IsHolding(SPlayer, lasso), Is.True);
            Assert.That(HandSys.TryDrop(SPlayer), Is.False);
            Assert.That(Server.System<ActionBlockerSystem>().CanThrow(SPlayer, lasso), Is.False);

            actions.PerformAction(ToServer(Player), fingerGunAction);
            Assert.That(HandSys.IsHolding(SPlayer, fingerGun), Is.False);
        });

        await Server.WaitRunTicks(20);
        await Server.WaitAssertion(() =>
        {
            var ammo = SEntMan.GetComponent<BasicEntityAmmoProviderComponent>(lasso);
            var target = SEntMan.GetComponent<TransformComponent>(SPlayer).Coordinates.Offset(new Vector2(3, 0));
            var gun = SEntMan.GetComponent<GunComponent>(lasso);
            Assert.That(SGun.AttemptShoot(SPlayer, (lasso, gun), target), Is.True);
            Assert.That(ammo.Count, Is.Zero);

            var actions = Server.System<SharedActionsSystem>();
            actions.PerformAction(ToServer(Player), lassoAction);
            Assert.That(HandSys.IsHolding(SPlayer, lasso), Is.False);
            actions.PerformAction(ToServer(Player), fingerGunAction);
            Assert.That(HandSys.IsHolding(SPlayer, fingerGun), Is.True);
            actions.PerformAction(ToServer(Player), lassoAction);
            Assert.That(HandSys.IsHolding(SPlayer, lasso), Is.False);
        });
    }

    [Test]
    public async Task FingerGunMutesBrieflyOnHit()
    {
        EntityUid target = default;
        await Server.WaitAssertion(() =>
        {
            target = SEntMan.SpawnEntity("MobHuman", MapData.GridCoords.Offset(new Vector2(3, 0)));
            var bullet = SEntMan.SpawnEntity("ProjectileMimeFingerGun", SEntMan.GetComponent<TransformComponent>(target).Coordinates);
            var hit = new ProjectileHitEvent(new DamageSpecifier(), target, SPlayer);
            SEntMan.EventBus.RaiseLocalEvent(bullet, ref hit);
        });

        await Server.WaitRunTicks(45);
        await Server.WaitAssertion(() => Assert.That(SEntMan.HasComponent<MutedComponent>(target), Is.True,
            "The finger gun did not mute its target after the toxin was metabolized."));

        await Server.WaitRunTicks(210);
        await Server.WaitAssertion(() => Assert.That(SEntMan.HasComponent<MutedComponent>(target), Is.False,
            "A single finger gun bullet muted its target for too long."));
    }

    [Test]
    public async Task BookRequiresMimeAndDoesNotGrantDuplicates()
    {
        EntityUid first = default;
        await Server.WaitAssertion(() =>
        {
            first = SEntMan.SpawnEntity("BookForbiddenMimeMagic", SEntMan.GetComponent<TransformComponent>(SPlayer).Coordinates);
            Assert.That(HandSys.TryPickupAnyHand(SPlayer, first), Is.True);

            var use = new UseInHandEvent(SPlayer);
            SEntMan.EventBus.RaiseLocalEvent(first, use);
            Assert.That(use.Handled, Is.True);
        });

        await Server.WaitRunTicks(30);
        await Server.WaitAssertion(() =>
        {
            Assert.That(CountMimeActions(), Is.Zero, "A non-mime learned the forbidden spells.");
            var mime = SEntMan.EnsureComponent<MimePowersComponent>(SPlayer);
            mime.VowBroken = true;
            var brokenVowUse = new UseInHandEvent(SPlayer);
            SEntMan.EventBus.RaiseLocalEvent(first, brokenVowUse);
            Assert.That(brokenVowUse.Handled, Is.True, "A mime with a broken vow started learning forbidden spells.");
            mime.VowBroken = false;
            SEntMan.EventBus.RaiseLocalEvent(first, new UseInHandEvent(SPlayer));
        });

        await Server.WaitRunTicks(40);
        await Server.WaitAssertion(() =>
        {
            Assert.That(CountMimeActions(), Is.EqualTo(3), "The mime did not learn all three spells.");
            Assert.That(HandSys.TryDrop(SPlayer), Is.True);
            var second = SEntMan.SpawnEntity("BookForbiddenMimeMagic", SEntMan.GetComponent<TransformComponent>(SPlayer).Coordinates);
            Assert.That(HandSys.TryPickupAnyHand(SPlayer, second), Is.True);
            SEntMan.EventBus.RaiseLocalEvent(second, new UseInHandEvent(SPlayer));
        });

        await Server.WaitRunTicks(40);
        await Server.WaitAssertion(() => Assert.That(CountMimeActions(), Is.EqualTo(3),
            "A second book granted duplicate mime spells."));
    }

    [Test]
    public async Task AnotherPersonsGripBlocksTheInvisibleHook()
    {
        EntityUid target = default;
        EntityUid grabber = default;
        Vector2 initial = default;
        await Server.WaitAssertion(() =>
        {
            SEntMan.EnsureComponent<MimePowersComponent>(SPlayer);
            var coordinates = MapData.GridCoords.Offset(new Vector2(3, 0));
            target = SEntMan.SpawnEntity("MobHuman", coordinates);
            grabber = SEntMan.SpawnEntity("MobHuman", coordinates);

            var pulling = Server.System<PullingSystem>();
            Assert.That(pulling.TryStartPull(grabber, target), Is.True);
            Assert.That(pulling.GetPuller(target), Is.EqualTo(grabber));

            initial = Transform.ToMapCoordinates(SEntMan.GetComponent<TransformComponent>(target).Coordinates).Position;
            HitWithHook(target);
            Assert.That(SEntMan.HasComponent<MimeHookPullingComponent>(SPlayer), Is.False,
                "The mime was rooted even though another person held the target.");
        });

        await Server.WaitRunTicks(12);
        await Server.WaitAssertion(() =>
        {
            var heldPosition = Transform.ToMapCoordinates(SEntMan.GetComponent<TransformComponent>(target).Coordinates).Position;
            Assert.That(Vector2.Distance(initial, heldPosition), Is.LessThan(0.5f),
                "The hook dragged a target who was already grabbed by another person.");

            var pulling = Server.System<PullingSystem>();
            Assert.That(pulling.TryStopPull(target, SEntMan.GetComponent<PullableComponent>(target)), Is.True);
            SEntMan.DeleteEntity(grabber);
            HitWithHook(target);
            Assert.That(SEntMan.HasComponent<MimeHookPullingComponent>(SPlayer), Is.True,
                "The mime could move during the invisible hook pull.");
        });

        await Server.WaitRunTicks(5);
        await Server.WaitAssertion(() =>
        {
            var freePosition = Transform.ToMapCoordinates(SEntMan.GetComponent<TransformComponent>(target).Coordinates).Position;
            Assert.That(Vector2.Distance(initial, freePosition), Is.GreaterThan(0.5f),
                "The hook did not pull the target after the grip was released.");
        });

        await Server.WaitRunTicks(150);
        await Server.WaitAssertion(() => Assert.That(SEntMan.HasComponent<MimeHookPullingComponent>(SPlayer), Is.False,
            "The mime remained rooted after the invisible hook pull ended."));

        void HitWithHook(EntityUid hitTarget)
        {
            var projectile = SEntMan.SpawnEntity("ProjectileMimeInvisibleHook", SEntMan.GetComponent<TransformComponent>(hitTarget).Coordinates);
            var hit = new ProjectileHitEvent(new DamageSpecifier(), hitTarget, SPlayer);
            SEntMan.EventBus.RaiseLocalEvent(projectile, ref hit);
        }
    }

    private int CountMimeActions()
    {
        var ids = new HashSet<string>
        {
            "ActionMimeInvisibleBarrier",
            "ActionMimeFingerGun",
            "ActionMimeInvisibleHook",
        };
        return Server.System<SharedActionsSystem>().GetActions(SPlayer)
            .Count(action => SEntMan.GetComponent<MetaDataComponent>(action).EntityPrototype is { } proto && ids.Contains(proto.ID));
    }
}
