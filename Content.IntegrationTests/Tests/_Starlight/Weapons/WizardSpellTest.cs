using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Tests.Interaction;
using Content.Server.Magic;
using Content.Server.Mind;
using Content.Server.Ghost.Roles.Components;
using Content.Server.Roles;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Cargo.Systems;
using Content.Server.Decals;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.Station.Systems;
using Content.Server._Starlight.Thaven;
using Content.Server.Storage.EntitySystems;
using Content.Shared._Funkystation.Fluids;
using Content.Shared._Starlight.Computers.RemoteControl;
using Content.Shared._Starlight.Magic.Components;
using Content.Shared._Starlight.Magic.Events;
using Content.Shared._Starlight.Thaven;
using Content.Shared.Actions;
using Content.Shared.Actions.Components;
using Content.Shared.Atmos;
using Content.Shared.ActionBlocker;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Chemistry.Components;
using Content.Shared.Charges.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Cargo.Components;
using Content.Shared.Body.Components;
using Content.Shared.FixedPoint;
using Content.Shared.Fluids.Components;
using Content.Shared.Gravity;
using Content.Shared.Ghost;
using Content.Shared.Electrocution;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Inventory;
using Content.Shared.Item;
using Content.Shared.Magic.Components;
using Content.Shared._Starlight.Actions.Components;
using Content.Shared.Weapons.Hitscan.Events;
using Content.Shared.Weapons.Melee.Events;
using Content.Shared.IgnitionSource;
using Robust.Shared.Physics;
using Content.Shared.Mindshield.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Roles.Components;
using Content.Shared.Maps;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Nutrition;
using Content.Shared.NPC.Systems;
using Content.Shared.Overlays;
using Content.Shared.Projectiles;
using Content.Shared.Physics;
using Content.Shared.Pointing;
using Content.Shared.RatKing;
using Content.Shared.Stunnable;
using Content.Shared.Store;
using Content.Shared.Store.Components;
using Content.Shared.Tag;
using Content.Shared.Throwing;
using Content.Shared.Trigger.Components.Effects;
using Content.Shared.Tools.Components;
using Content.Shared.Verbs;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Melee;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;
using Robust.Shared.Containers;

namespace Content.IntegrationTests.Tests._Starlight.Weapons;

public sealed class WizardSpellTest : InteractionTest
{
    protected override string PlayerPrototype => "MobHuman";

    [Test]
    public async Task TrashGolemStoresGroundItemsAndDropsThemOnDeath()
    {
        await Server.WaitAssertion(() =>
        {
            var map = Server.System<SharedMapSystem>();
            var steel = new Tile(TileMan["FloorSteel"].TileId);
            for (var x = -1; x <= 3; x++)
            for (var y = -2; y <= 2; y++)
                map.SetTile(MapData.Grid, new Vector2i(x, y), steel);

            var center = SEntMan.GetComponent<TransformComponent>(SPlayer).Coordinates;
            var pen = SEntMan.SpawnEntity("Pen", center);
            var wrench = SEntMan.SpawnEntity("Wrench", center.Offset(new Vector2(1, 0)));
            var distant = SEntMan.SpawnEntity("Screwdriver", center.Offset(new Vector2(3, 0)));
            var bystander = SEntMan.SpawnEntity("MobHuman", center.Offset(new Vector2(0, 1)));
            // Even an item-like caster must never be gathered as trash.
            SEntMan.EnsureComponent<ItemComponent>(SPlayer);
            SEntMan.EnsureComponent<ItemComponent>(bystander);

            Assert.That(Server.System<MagicSystem>().TryCastTrashGolem(SPlayer), Is.True);
            var golem = SEntMan.EntityQuery<WizardTrashGolemComponent>().Last().Owner;
            Assert.That(SEntMan.GetComponent<TransformComponent>(golem).Coordinates.Position,
                Is.Not.EqualTo(center.Position));
            var containers = Server.System<SharedContainerSystem>();
            Assert.That(containers.TryGetContainer(golem, "wizard-trash-golem-items", out var contents), Is.True);
            Assert.That(contents.ContainedEntities, Does.Contain(pen));
            Assert.That(contents.ContainedEntities, Does.Contain(wrench));
            Assert.That(contents.ContainedEntities, Does.Not.Contain(distant));
            Assert.That(contents.ContainedEntities, Does.Not.Contain(bystander));
            Assert.That(contents.ContainedEntities, Does.Not.Contain(SPlayer));
            Assert.That(SEntMan.GetComponent<TransformComponent>(SPlayer).ParentUid, Is.EqualTo(MapData.Grid.Owner));
            Assert.That(SEntMan.GetComponent<TransformComponent>(bystander).ParentUid, Is.EqualTo(MapData.Grid.Owner));

            var pricing = Server.System<PricingSystem>();
            var value = Math.Max(0, pricing.GetPrice(pen)) +
                Math.Max(0, pricing.GetPrice(wrench));
            Assert.That(SEntMan.GetComponent<WizardTrashGolemComponent>(golem).MaterialValue, Is.EqualTo(value));
            Assert.That(pricing.GetPrice(golem), Is.EqualTo(value));
            Assert.That(Server.System<Content.Server.Examine.ExamineSystem>()
                .GetExamineText(golem, SPlayer, out _).ToString(), Does.Contain($"Valued at: [{value:F0}]"));
            Assert.That(SEntMan.GetComponent<MetaDataComponent>(golem).EntityName, Does.Contain("ragged trash golem"));
            var thresholds = SEntMan.GetComponent<MobThresholdsComponent>(golem);
            Assert.That(thresholds.Thresholds.Single(entry => entry.Value == MobState.Dead).Key,
                Is.EqualTo(FixedPoint2.New(40 + (int) (value / 50))));
            var melee = SEntMan.GetComponent<MeleeWeaponComponent>(golem);
            Assert.That(melee.Damage.DamageDict["Blunt"], Is.EqualTo(FixedPoint2.New(6 + (int) (value / 250))));

            var master = SEntMan.GetComponent<WizardTrashGolemMasterComponent>(SPlayer);
            var actions = Server.System<SharedActionsSystem>();
            actions.PerformAction(ToServer(Player), actions.GetAction(master.AttackAction!.Value)!.Value);
            Assert.That(master.Order, Is.EqualTo(RatKingOrderType.Loose));
            var htn = SEntMan.GetComponent<HTNComponent>(golem);
            Assert.That(htn.Blackboard.TryGetValue<RatKingOrderType>(NPCBlackboard.CurrentOrders,
                out var order, SEntMan), Is.True);
            Assert.That(order, Is.EqualTo(RatKingOrderType.Loose));

            actions.PerformAction(ToServer(Player), actions.GetAction(master.FollowAction!.Value)!.Value);
            Assert.That(master.Order, Is.EqualTo(RatKingOrderType.Follow));
            var pointed = new AfterPointedAtEvent(bystander);
            SEntMan.EventBus.RaiseLocalEvent(SPlayer, ref pointed);
            Assert.That(master.Order, Is.EqualTo(RatKingOrderType.CheeseEm));
            Assert.That(htn.Blackboard.TryGetValue<EntityUid>(NPCBlackboard.CurrentOrderedTarget,
                out var orderedTarget, SEntMan), Is.True);
            Assert.That(orderedTarget, Is.EqualTo(bystander));

            SEntMan.DeleteEntity(golem);
            Assert.That(SEntMan.Deleted(pen), Is.False);
            Assert.That(SEntMan.Deleted(wrench), Is.False);
            Assert.That(SEntMan.GetComponent<TransformComponent>(pen).ParentUid, Is.EqualTo(MapData.Grid.Owner));
            Assert.That(SEntMan.GetComponent<TransformComponent>(wrench).ParentUid, Is.EqualTo(MapData.Grid.Owner));

            var expensive = SEntMan.SpawnEntity("Pen", center);
            SEntMan.EnsureComponent<StaticPriceComponent>(expensive).Price = 6000;
            Assert.That(Server.System<MagicSystem>().TryCastTrashGolem(SPlayer), Is.True);
            var illustrious = SEntMan.EntityQuery<WizardTrashGolemComponent>().Last().Owner;
            Assert.That(SEntMan.GetComponent<MetaDataComponent>(illustrious).EntityName,
                Does.Contain("illustrious trash golem"));
            Assert.That(SEntMan.GetComponent<WizardTrashGolemComponent>(illustrious).MaterialValue, Is.GreaterThanOrEqualTo(6000));
            Assert.That(pricing.GetPrice(illustrious),
                Is.EqualTo(SEntMan.GetComponent<WizardTrashGolemComponent>(illustrious).MaterialValue));
            Assert.That(SEntMan.GetComponent<MobThresholdsComponent>(illustrious).Thresholds
                .Single(entry => entry.Value == MobState.Dead).Key, Is.EqualTo(FixedPoint2.New(80)));
            Assert.That(SEntMan.GetComponent<MeleeWeaponComponent>(illustrious).Damage.DamageDict["Blunt"],
                Is.EqualTo(FixedPoint2.New(14)));
            SEntMan.DeleteEntity(illustrious);
        });
    }

    [Test]
    public async Task WizardGasBallsReleaseFromOneSource()
    {
        await Server.WaitAssertion(() =>
        {
            foreach (var (prototype, gasType, moles) in new[]
                     {
                         ("WizardSleepingCloudBall", Gas.NitrousOxide, 1566f),
                         ("WizardCombustBall", Gas.Hydrogen, 2496f),
                         ("WizardMagicalSaunaBall", Gas.WaterVapor, 315f),
                         ("WizardStinkCloudBall", Gas.Ammonia, 1566f),
                     })
            {
                var ball = SEntMan.SpawnEntity(prototype, MapData.GridCoords);
                var release = SEntMan.GetComponent<ReleaseGasOnTriggerComponent>(ball);
                Assert.That(release.Air.GetMoles(gasType), Is.EqualTo(moles).Within(0.01f));
                Assert.That(SEntMan.HasComponent<WizardGasWindOnReleaseComponent>(ball), Is.True);
                SEntMan.DeleteEntity(ball);
            }
            var combust = SEntMan.SpawnEntity("WizardCombustBall", MapData.GridCoords);
            var combustGas = SEntMan.GetComponent<ReleaseGasOnTriggerComponent>(combust).Air;
            Assert.That(combustGas.GetMoles(Gas.Oxygen), Is.EqualTo(1248f).Within(0.01f));
            Assert.That(combustGas.GetMoles(Gas.Plasma), Is.EqualTo(20f).Within(0.01f));
            SEntMan.DeleteEntity(combust);
        });
    }

    [Test]
    public async Task TrashGolemLooseOrderAttacksNearbyCrew()
    {
        EntityUid bystander = default;
        EntityUid golem = default;
        await Server.WaitAssertion(() =>
        {
            var map = Server.System<SharedMapSystem>();
            var steel = new Tile(TileMan["FloorSteel"].TileId);
            for (var x = -2; x <= 3; x++)
            for (var y = -2; y <= 3; y++)
                map.SetTile(MapData.Grid, new Vector2i(x, y), steel);

            var center = SEntMan.GetComponent<TransformComponent>(SPlayer).Coordinates;
            SEntMan.SpawnEntity("Wrench", center);
            bystander = SEntMan.SpawnEntity("MobHuman", center.Offset(new Vector2(0, 1)));
            Assert.That(Server.System<MagicSystem>().TryCastTrashGolem(SPlayer), Is.True);
            golem = SEntMan.EntityQuery<WizardTrashGolemComponent>().Last().Owner;
            var master = SEntMan.GetComponent<WizardTrashGolemMasterComponent>(SPlayer);
            var actions = Server.System<SharedActionsSystem>();
            actions.PerformAction(ToServer(Player), actions.GetAction(master.AttackAction!.Value)!.Value);
            Assert.That(master.Order, Is.EqualTo(RatKingOrderType.Loose));
        });

        await Server.WaitRunTicks(150);
        await Server.WaitAssertion(() =>
        {
            Assert.That(Server.System<DamageableSystem>().GetAllDamage(bystander).GetTotal(),
                Is.GreaterThan(FixedPoint2.Zero));
            SEntMan.DeleteEntity(golem);
        });
    }

    [Test]
    public async Task StopTimeExemptionFollowsMindSwap()
    {
        EntityUid newBody = default;
        await Server.WaitAssertion(() =>
        {
            var minds = Server.System<MindSystem>();
            var wizardMind = minds.CreateMind(ServerSession.UserId);
            minds.TransferTo(wizardMind, SPlayer);
            Server.System<SharedActionsSystem>().AddAction(SPlayer, "ActionWizardStopTime");

            newBody = SEntMan.SpawnEntity("MobHuman", MapData.GridCoords.Offset(new Vector2(1, 0)));
            var otherMind = minds.CreateMind(null);
            minds.TransferTo(otherMind, newBody);

            Assert.That(Server.System<MagicSystem>().TryCastStopTime(SPlayer), Is.True);
            Assert.That(SEntMan.HasComponent<WizardStopTimeAffectedComponent>(newBody), Is.True);

            minds.TransferTo(wizardMind, newBody);
            minds.TransferTo(otherMind, SPlayer);
        });

        await Server.WaitRunTicks(2);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<WizardStopTimeAwareComponent>(newBody), Is.True);
            Assert.That(SEntMan.HasComponent<WizardStopTimeAffectedComponent>(newBody), Is.False);
            Assert.That(SEntMan.HasComponent<WizardStopTimeAffectedComponent>(SPlayer), Is.True);
            Assert.That(SEntMan.HasComponent<WizardStopTimeAwareComponent>(SPlayer), Is.False);
        });

        await Server.WaitRunTicks(610);
    }

    [Test]
    public async Task StopTimeFreezesNonHoldersAndReleasesHeldHitscan()
    {
        EntityUid target = default;
        EntityUid ammo = default;
        EntityUid physicalProjectile = default;
        EntityUid movingProjectile = default;
        await Server.WaitAssertion(() =>
        {
            var map = Server.System<SharedMapSystem>();
            var steel = new Tile(TileMan["FloorSteel"].TileId);
            for (var x = -3; x <= 5; x++)
            for (var y = -3; y <= 3; y++)
                map.SetTile(MapData.Grid, new Vector2i(x, y), steel);

            target = SEntMan.SpawnEntity("MobHuman", MapData.GridCoords.Offset(new Vector2(1.5f, 0.5f)));
            var secondCaster = SEntMan.SpawnEntity("MobHuman", MapData.GridCoords.Offset(new Vector2(0.5f, 1.5f)));
            var outsider = SEntMan.SpawnEntity("MobHuman", MapData.GridCoords.Offset(new Vector2(4.5f, 0.5f)));
            Server.System<SharedActionsSystem>().AddAction(secondCaster, "ActionWizardStopTime");

            var magic = Server.System<MagicSystem>();
            movingProjectile = SEntMan.SpawnEntity("BulletLaser",
                MapData.GridCoords.Offset(new Vector2(0.5f, 2.5f)));
            SEntMan.GetComponent<Robust.Shared.Spawners.TimedDespawnComponent>(movingProjectile).Lifetime = 30f;
            Server.System<Robust.Shared.Physics.Systems.SharedPhysicsSystem>()
                .SetCanCollide(movingProjectile, false);
            Server.System<Content.Server.Weapons.Ranged.Systems.GunSystem>().ShootProjectile(
                movingProjectile, Vector2.UnitX, Vector2.Zero, SPlayer, SPlayer, 10f);
            Assert.That(magic.TryCastStopTime(SPlayer), Is.True);
            Assert.That(SEntMan.GetComponent<PhysicsComponent>(movingProjectile).BodyType,
                Is.EqualTo(BodyType.Static));
            Assert.That(SEntMan.HasComponent<StasisFrozenComponent>(target), Is.True);
            Assert.That(SEntMan.HasComponent<WizardStopTimeAffectedComponent>(target), Is.True);
            Assert.That(SEntMan.HasComponent<WizardStopTimeAwareComponent>(SPlayer), Is.True);
            Assert.That(SEntMan.HasComponent<WizardStopTimeAwareComponent>(secondCaster), Is.True);
            Assert.That(SEntMan.HasComponent<StasisFrozenComponent>(secondCaster), Is.False);
            Assert.That(SEntMan.HasComponent<WizardStopTimeAffectedComponent>(outsider), Is.False);

            ammo = SEntMan.SpawnEntity("RedLaser", MapData.GridCoords);
            var from = SEntMan.GetComponent<TransformComponent>(SPlayer).Coordinates;
            var to = SEntMan.GetComponent<TransformComponent>(target).Coordinates;
            var direction = Vector2.Normalize(Transform.ToMapCoordinates(to).Position -
                                              Transform.ToMapCoordinates(from).Position);
            var shot = new HitscanTraceEvent
            {
                FromCoordinates = from,
                ToCoordinates = to,
                ShotDirection = direction,
                Gun = SPlayer,
                Shooter = SPlayer,
                Target = target,
            };
            Assert.That(magic.TrySuspendHitscanShot(ammo, ref shot), Is.True);
            Assert.That(Server.System<DamageableSystem>().GetAllDamage(target).DamageDict.ContainsKey("Heat"), Is.False);

            var spellAttempt = new TimeStopProjectileSpellAttemptEvent(SPlayer, "ProjectileFireball", from, to, 25f);
            SEntMan.EventBus.RaiseEvent(EventSource.Local, ref spellAttempt);
            Assert.That(spellAttempt.Suspended, Is.True);
            Assert.That(SEntMan.EntityQuery<MetaDataComponent>()
                .Any(meta => meta.EntityPrototype?.ID == "ProjectileFireball"), Is.False);

            physicalProjectile = SEntMan.SpawnEntity("BulletLaser", MapData.GridCoords);
            Assert.That(magic.TrySuspendGunProjectile(physicalProjectile, SPlayer, SPlayer,
                direction, Vector2.Zero, 20f), Is.True);
            Assert.That(SEntMan.GetComponent<PhysicsComponent>(physicalProjectile).BodyType,
                Is.EqualTo(BodyType.Static));

            var wandFireball = SEntMan.SpawnEntity("ProjectileFireball", MapData.GridCoords);
            Assert.That(magic.TrySuspendGunProjectile(wandFireball, SPlayer, SPlayer,
                direction, Vector2.Zero, 20f), Is.True);
            Assert.That(SEntMan.GetComponent<IgnitionSourceComponent>(wandFireball).Ignited, Is.False);
            Assert.That(SEntMan.GetComponent<PhysicsComponent>(wandFireball).CanCollide, Is.False);
        });

        await Server.WaitRunTicks(160);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.Deleted(movingProjectile), Is.False);
            Assert.That(SEntMan.GetComponent<PhysicsComponent>(movingProjectile).BodyType,
                Is.EqualTo(BodyType.Static));
        });

        await Server.WaitRunTicks(450);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<WizardStopTimeAffectedComponent>(target), Is.False);
            Assert.That(SEntMan.HasComponent<StasisFrozenComponent>(target), Is.False);
            Assert.That(SEntMan.HasComponent<WizardStopTimeAwareComponent>(SPlayer), Is.False);
            Assert.That(SEntMan.GetComponent<PhysicsComponent>(movingProjectile).BodyType,
                Is.EqualTo(BodyType.Dynamic));
            Assert.That(Server.System<DamageableSystem>().GetAllDamage(target).DamageDict["Heat"],
                Is.GreaterThan(FixedPoint2.Zero));
            Assert.That(SEntMan.EntityQuery<MetaDataComponent>()
                .Any(meta => meta.EntityPrototype?.ID == "EffectWizardStoppedFireball"), Is.False);
        });
    }

    [Test]
    public async Task DominationFollowsMindAndMindshieldCuresCurrentBody()
    {
        EntityUid mind = default;
        await Server.WaitAssertion(() =>
        {
            var minds = Server.System<MindSystem>();
            var roles = Server.System<RoleSystem>();
            var wizard = SEntMan.SpawnEntity("MobHuman", MapData.GridCoords.Offset(new Vector2(2, 0)));
            var newBody = SEntMan.SpawnEntity("MobHuman", MapData.GridCoords.Offset(new Vector2(3, 0)));
            mind = minds.CreateMind(ServerSession.UserId);
            minds.TransferTo(mind, SPlayer);

            Assert.That(Server.System<MagicSystem>().TryDominateMind(wizard, SPlayer), Is.True);
            Assert.That(SEntMan.HasComponent<WizardDominationComponent>(mind), Is.True);
            Assert.That(roles.MindHasRole<WizardDominationRoleComponent>(mind), Is.True);

            minds.TransferTo(mind, newBody);
            Assert.That(SEntMan.HasComponent<WizardDominationComponent>(mind), Is.True);
            Assert.That(SEntMan.HasComponent<WizardDominationComponent>(SPlayer), Is.False);
            Assert.That(roles.MindHasRole<WizardDominationRoleComponent>(mind), Is.True);

            SEntMan.AddComponent<MindShieldComponent>(newBody);
        });

        await Server.WaitRunTicks(2);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<WizardDominationComponent>(mind), Is.False);
            Assert.That(Server.System<RoleSystem>().MindHasRole<WizardDominationRoleComponent>(mind), Is.False);
        });
    }

    [Test]
    public async Task ApprenticeKitFitsInWizardBackpack()
    {
        await Server.WaitAssertion(() =>
        {
            var storage = Server.System<StorageSystem>();
            foreach (var wand in new[]
                     {
                         "WeaponWandRandomCreature",
                         "WeaponWandPolymorphMonkey",
                         "WeaponWandPolymorphDoor",
                         "WeaponWandPolymorphCarp",
                     })
            {
                var bag = SEntMan.SpawnEntity("ClothingBackpackWizard", MapData.GridCoords);
                foreach (var prototype in new[]
                         {
                             "WizardsApprenticeGrimoire",
                             wand,
                         })
                {
                    var item = SEntMan.SpawnEntity(prototype, MapData.GridCoords);
                    Assert.That(storage.Insert(bag, item, out _, playSound: false), Is.True,
                        $"The apprentice kit cannot fit {prototype} in its backpack.");
                }
            }
        });
    }

    [Test]
    public async Task RandomCreatureWandUsesOneChargeAndSummonSparesCaster()
    {
        EntityUid wand = default;
        await Server.WaitAssertion(() =>
        {
            Server.System<TagSystem>().AddTag(SPlayer, "Wizard");
            wand = SEntMan.SpawnEntity("WeaponWandRandomCreature", MapData.GridCoords);
            Assert.That(HandSys.TryPickupAnyHand(SPlayer, wand), Is.True);
            var ammo = SEntMan.GetComponent<BasicEntityAmmoProviderComponent>(wand);
            Assert.That(ammo.Count, Is.EqualTo(5));
        });

        await Server.WaitRunTicks(45);
        await Server.WaitAssertion(() =>
        {
            var ammo = SEntMan.GetComponent<BasicEntityAmmoProviderComponent>(wand);
            var knownCreatures = Server.System<MagicSystem>().GetRandomCreatureWandPool();
            Assert.That(knownCreatures.Count, Is.GreaterThan(50));
            Assert.That(knownCreatures, Does.Contain("MobAngryBee"));
            Assert.That(knownCreatures, Does.Contain("MobCarp"));
            Assert.That(knownCreatures, Does.Contain("MobSpiderSpace"));
            Assert.That(knownCreatures, Does.Contain("MobCobraSpace"));
            Assert.That(knownCreatures, Does.Contain("MobKangarooSpace"));
            Assert.That(knownCreatures, Does.Not.Contain("MobGlockroach"));
            Assert.That(knownCreatures, Does.Not.Contain("MobXenoQueen"));
            Assert.That(knownCreatures, Does.Not.Contain("MobRatKing"));
            Assert.That(knownCreatures, Does.Not.Contain("MobBluespaceHarvesterMiGo"));
            Assert.That(knownCreatures, Does.Not.Contain("MobElder"));
            Assert.That(knownCreatures, Does.Not.Contain("MobBehonkerElectrical"));
            Assert.That(knownCreatures, Does.Not.Contain("MobPathfindDummy"));
            Assert.That(knownCreatures, Does.Not.Contain("MobCluwneBeast"));
            var before = SEntMan.EntityQuery<MetaDataComponent>()
                .Count(meta => knownCreatures.Contains(meta.EntityPrototype?.ID));
            var gun = SEntMan.GetComponent<GunComponent>(wand);
            var target = MapData.GridCoords.Offset(new Vector2(3, 0));
            Assert.That(SGun.AttemptShoot(SPlayer, (wand, gun), target), Is.True);
            Assert.That(ammo.Count, Is.EqualTo(4));

            var summoned = SEntMan.EntityQuery<MetaDataComponent>()
                .Where(meta => knownCreatures.Contains(meta.EntityPrototype?.ID)).ToArray();
            Assert.That(summoned.Length, Is.EqualTo(before + 1));
            Assert.That(Server.System<NpcFactionSystem>().IsIgnored(summoned[^1].Owner, SPlayer), Is.True);
            Assert.That(SEntMan.HasComponent<GhostRoleComponent>(summoned[^1].Owner), Is.False);
            Assert.That(SEntMan.HasComponent<GhostTakeoverAvailableComponent>(summoned[^1].Owner), Is.False);
            Assert.That(Server.System<NpcFactionSystem>().IsMember(summoned[^1].Owner,
                "SimpleHostile"), Is.True);
        });
    }

    [Test]
    public async Task ChainLightningDamagesThreeTargetsAndNotCaster()
    {
        await Server.WaitAssertion(() =>
        {
            var first = SEntMan.SpawnEntity("MobHuman", MapData.GridCoords.Offset(new Vector2(2, 0)));
            var second = SEntMan.SpawnEntity("MobHuman", MapData.GridCoords.Offset(new Vector2(3, 0)));
            var third = SEntMan.SpawnEntity("MobHuman", MapData.GridCoords.Offset(new Vector2(4, 0)));
            var magic = Server.System<MagicSystem>();
            var damage = Server.System<DamageableSystem>();
            Assert.That(magic.TryCastChainLightning(SPlayer, first), Is.True);
            Assert.That(damage.GetAllDamage(first).DamageDict["Shock"], Is.EqualTo(FixedPoint2.New(40)));
            Assert.That(damage.GetAllDamage(second).DamageDict["Shock"], Is.EqualTo(FixedPoint2.New(30)));
            Assert.That(damage.GetAllDamage(third).DamageDict["Shock"], Is.EqualTo(FixedPoint2.New(20)));
            Assert.That(SEntMan.HasComponent<ElectrocutedComponent>(first), Is.True);
            Assert.That(damage.GetAllDamage(SPlayer).DamageDict.ContainsKey("Shock"), Is.False);
        });
    }

    [Test]
    public async Task ChainLightningBounceStopsAtWall()
    {
        EntityUid first = default;
        EntityUid blocked = default;
        await Server.WaitAssertion(() =>
        {
            var map = Server.System<SharedMapSystem>();
            var steel = new Tile(TileMan["FloorSteel"].TileId);
            for (var x = -3; x <= 6; x++)
            for (var y = -2; y <= 2; y++)
                map.SetTile(MapData.Grid, new Vector2i(x, y), steel);

            first = SEntMan.SpawnEntity("MobHuman", MapData.GridCoords.Offset(new Vector2(1, 0)));
            blocked = SEntMan.SpawnEntity("MobHuman", MapData.GridCoords.Offset(new Vector2(3, 0)));
            SEntMan.SpawnEntity("WallSolid", MapData.GridCoords.Offset(new Vector2(2, 0)));
        });
        await Server.WaitRunTicks(2);
        await Server.WaitAssertion(() =>
        {
            var wallTile = Server.System<TurfSystem>().GetTileRef(MapData.GridCoords.Offset(new Vector2(2, 0)));
            Assert.That(wallTile, Is.Not.Null);
            Assert.That(Server.System<TurfSystem>().IsTileBlocked(wallTile!.Value, CollisionGroup.Impassable),
                Is.True);
            var firstTile = Server.System<TurfSystem>().GetTileRef(SEntMan.GetComponent<TransformComponent>(first).Coordinates);
            var blockedTile = Server.System<TurfSystem>().GetTileRef(SEntMan.GetComponent<TransformComponent>(blocked).Coordinates);
            Assert.That(firstTile, Is.Not.Null);
            Assert.That(blockedTile, Is.Not.Null);
            Assert.That(firstTile!.Value.GridIndices.X, Is.LessThan(wallTile.Value.GridIndices.X));
            Assert.That(blockedTile!.Value.GridIndices.X, Is.GreaterThan(wallTile.Value.GridIndices.X));
            Assert.That(Server.System<MagicSystem>().TryCastChainLightning(SPlayer, first), Is.True);
            var damage = Server.System<DamageableSystem>();
            Assert.That(damage.GetAllDamage(first).DamageDict["Shock"], Is.EqualTo(FixedPoint2.New(40)));
            Assert.That(damage.GetAllDamage(blocked).DamageDict.ContainsKey("Shock"), Is.False);
        });
    }

    [Test]
    public async Task ChainLightningStillDamagesInsulatedTarget()
    {
        await Server.WaitAssertion(() =>
        {
            var target = SEntMan.SpawnEntity("MobHuman", MapData.GridCoords.Offset(new Vector2(2, 0)));
            var gloves = SEntMan.SpawnEntity("ClothingHandsGlovesColorYellow",
                SEntMan.GetComponent<TransformComponent>(target).Coordinates);
            Assert.That(Server.System<InventorySystem>().TryEquip(target, gloves, "gloves"), Is.True);
            Assert.That(Server.System<MagicSystem>().TryCastChainLightning(SPlayer, target), Is.True);
            Assert.That(Server.System<DamageableSystem>().GetAllDamage(target).DamageDict["Shock"],
                Is.EqualTo(FixedPoint2.New(40)));
            Assert.That(SEntMan.HasComponent<ElectrocutedComponent>(target), Is.True);
        });
    }

    [Test]
    public async Task DimensionDoorCreatesSafeLinkedPortalsAndClosesFromSameAction()
    {
        EntityUid actionUid = default;
        await Server.WaitAssertion(() =>
        {
            var map = Server.System<SharedMapSystem>();
            var steel = new Tile(TileMan["FloorSteel"].TileId);
            for (var x = -2; x <= 2; x++)
            for (var y = -2; y <= 2; y++)
                map.SetTile(MapData.Grid, new Vector2i(x, y), steel);

            var station = SEntMan.SpawnEntity("TestStation", MapCoordinates.Nullspace);
            Server.System<StationSystem>().AddMainGridToStation(station, MapData.Grid);
            var air = new GasMixture(Atmospherics.CellVolume) { Temperature = Atmospherics.T20C };
            air.AdjustMoles(Gas.Oxygen, Atmospherics.OxygenMolesStandard);
            air.AdjustMoles(Gas.Nitrogen, Atmospherics.NitrogenMolesStandard);
            Server.System<AtmosphereSystem>().SetMapAtmosphere(MapData.MapUid, false, air);

            var actions = Server.System<SharedActionsSystem>();
            actionUid = actions.AddAction(SPlayer, "ActionWizardDimensionDoor")!.Value;
            var target = MapData.GridCoords.Offset(new Vector2(1, 0));
            Assert.That(Server.System<MagicSystem>().TryCastDimensionDoor(SPlayer, actionUid, target), Is.True);
            Assert.That(SEntMan.HasComponent<InstantActionComponent>(actionUid), Is.True);
            Assert.That(SEntMan.HasComponent<WorldTargetActionComponent>(actionUid), Is.False);
            Assert.That(SEntMan.EntityQuery<MetaDataComponent>()
                .Count(meta => meta.EntityPrototype?.ID is "WizardDimensionDoorEntrance" or "WizardDimensionDoorExit"),
                Is.EqualTo(2));

            actions.PerformAction(ToServer(Player), actions.GetAction(actionUid)!.Value);
            Assert.That(actions.GetAction(actionUid)!.Value.Comp.Enabled, Is.False);
        });

        await Server.WaitRunTicks(320);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.EntityQuery<MetaDataComponent>()
                .Count(meta => meta.EntityPrototype?.ID is "WizardDimensionDoorEntrance" or "WizardDimensionDoorExit"),
                Is.Zero);
            Assert.That(SEntMan.HasComponent<WorldTargetActionComponent>(actionUid), Is.True);
            Assert.That(SEntMan.HasComponent<InstantActionComponent>(actionUid), Is.False);
        });
    }

    [Test]
    public async Task WizardMageHandCanTakeAndReleaseRemoteControl()
    {
        await Server.WaitAssertion(() =>
        {
            var actions = Server.System<SharedActionsSystem>();
            var actionUid = actions.AddAction(SPlayer, "ActionWizardMageHand");
            Assert.That(actionUid, Is.Not.Null);
            var action = actions.GetAction(actionUid)!.Value;

            actions.PerformAction(ToServer(Player), action);
            var spell = SEntMan.GetComponent<WizardMageHandComponent>(SPlayer);
            var hand = spell.Hand;
            Assert.That(SEntMan.HasComponent<GhostComponent>(hand), Is.False);
            var console = SEntMan.GetComponent<RemoteControlConsoleComponent>(spell.Console);
            Assert.That(console.RemoteBrain, Is.EqualTo(hand));
            Assert.That(console.Controller, Is.EqualTo(SPlayer));
            Assert.That(SEntMan.GetComponent<RelayInputMoverComponent>(SPlayer).RelayEntity,
                Is.EqualTo(hand));
            Assert.That(Server.System<SharedRemoteControlConsoleSystem>()
                .TryGetBody((spell.Console, console), out var body), Is.True);
            Assert.That(body, Is.EqualTo(hand));

            actions.PerformAction(ToServer(Player), action);
            Assert.That(SEntMan.HasComponent<WizardMageHandComponent>(SPlayer), Is.False);
        });
    }

    [Test]
    public async Task WizardMageHandDamageEndsRemoteControl()
    {
        await Server.WaitAssertion(() =>
        {
            var actions = Server.System<SharedActionsSystem>();
            var actionUid = actions.AddAction(SPlayer, "ActionWizardMageHand");
            Assert.That(actionUid, Is.Not.Null);
            actions.PerformAction(ToServer(Player), actions.GetAction(actionUid)!.Value);

            var hand = SEntMan.GetComponent<WizardMageHandComponent>(SPlayer).Hand;
            var thresholds = SEntMan.GetComponent<MobThresholdsComponent>(hand);
            Assert.That(thresholds.Thresholds.Single(entry => entry.Value == MobState.Dead).Key,
                Is.EqualTo(FixedPoint2.New(1)));

            Server.System<DamageableSystem>().TryChangeDamage(hand,
                new DamageSpecifier { DamageDict = { ["Blunt"] = FixedPoint2.New(1) } },
                ignoreResistances: true);
            Assert.That(SEntMan.HasComponent<WizardMageHandComponent>(SPlayer), Is.False);
            Assert.That(SEntMan.TryGetComponent<RelayInputMoverComponent>(SPlayer, out var relay) &&
                relay.RelayEntity == hand, Is.False);
        });
    }

    [Test]
    public async Task WizardArcaneChalkWritesOnFloor()
    {
        await Server.WaitAssertion(() =>
        {
            var magic = Server.System<MagicSystem>();
            var minds = Server.System<MindSystem>();
            minds.TransferTo(minds.CreateMind(ServerSession.UserId), SPlayer);
            var click = MapData.GridCoords.Offset(new Vector2(0.7f, 0.7f));
            Assert.That(magic.TryPlaceArcaneChalk(SPlayer, click, "Hello"), Is.True);
            var mark = SEntMan.EntityQuery<MetaDataComponent>()
                .Single(meta => meta.EntityPrototype?.ID == "WizardArcaneChalkMark");
            Assert.That(mark.EntityDescription, Does.Contain("Hello"));
            Assert.That(mark.EntityDescription, Does.Contain(SEntMan.GetComponent<MetaDataComponent>(SPlayer).EntityName));
            var markPosition = Server.System<SharedTransformSystem>().GetMapCoordinates(mark.Owner);
            var clickPosition = Server.System<SharedTransformSystem>().ToMapCoordinates(click);
            Assert.That(Vector2.DistanceSquared(markPosition.Position, clickPosition.Position), Is.LessThan(0.001f));

            var markComp = SEntMan.GetComponent<WizardArcaneChalkMarkComponent>(mark.Owner);
            var decalOrigin = SEntMan.GetComponent<TransformComponent>(mark.Owner).Coordinates.Position - new Vector2(0.5f);
            var decal = Server.System<DecalSystem>().GetDecalsInRange(markComp.GridUid, decalOrigin, 1f)
                .Single(entry => entry.Index == markComp.DecalId).Decal;
            Assert.That(decal.Cleanable, Is.True);
            Assert.That(Vector2.DistanceSquared(decal.Coordinates, decalOrigin), Is.LessThan(0.001f));

            var chalk = SEntMan.SpawnEntity("WizardArcaneChalk", MapData.GridCoords);
            Assert.That(SEntMan.GetComponent<LimitedChargesComponent>(chalk).MaxCharges, Is.EqualTo(1));
            Assert.That(magic.TryPlaceArcaneChalk(SPlayer, click, "Another message"), Is.True);
            Assert.That(SEntMan.EntityQuery<MetaDataComponent>()
                .Count(meta => meta.EntityPrototype?.ID == "WizardArcaneChalkMark"), Is.EqualTo(2));
        });
    }

    [Test]
    public async Task WizardTelepathyOpensDialogForSelfTarget()
    {
        await Server.WaitAssertion(() =>
        {
            var actions = Server.System<SharedActionsSystem>();
            var actionUid = actions.AddAction(SPlayer, "ActionWizardTelepathy");
            Assert.That(actionUid, Is.Not.Null);
            var action = actions.GetAction(actionUid);
            var ev = actions.GetEvent(actionUid!.Value) as PsychicWhisperEvent;
            Assert.That(action, Is.Not.Null);
            Assert.That(ev, Is.Not.Null);

            ev!.Target = SPlayer;
            actions.PerformAction(ToServer(Player), action!.Value, ev);
            Assert.That(ev.Handled, Is.True);
        });
    }

    [Test]
    public async Task WizardMinorIllusionCreatesNoncollidingCreatureImage()
    {
        EntityUid illusionUid = default;
        var puffCount = 0;
        await Server.WaitAssertion(() =>
        {
            var actions = Server.System<SharedActionsSystem>();
            var before = SEntMan.EntityQuery<WizardMinorIllusionComponent>().Count();
            var actionUid = actions.AddAction(SPlayer, "ActionWizardMinorIllusion");
            Assert.That(actionUid, Is.Not.Null);

            var action = actions.GetAction(actionUid)!.Value;
            var ev = actions.GetEvent(actionUid!.Value) as MinorIllusionSpellEvent;
            Assert.That(ev, Is.Not.Null);
            ev!.Target = MapData.GridCoords.Offset(new Vector2(2, 0));
            actions.PerformAction(ToServer(Player), action, ev);
            Assert.That(ev!.Handled, Is.True);

            var illusions = SEntMan.EntityQuery<WizardMinorIllusionComponent>().ToList();
            Assert.That(illusions, Has.Count.EqualTo(before + 1));
            Assert.That(illusions[^1].CreaturePrototype, Is.Not.Empty);
            illusionUid = illusions[^1].Owner;
            Assert.That(SEntMan.HasComponent<PhysicsComponent>(illusionUid), Is.False);
            illusions[^1].ExpiresAt = TimeSpan.Zero;
            puffCount = SEntMan.EntityQuery<MetaDataComponent>()
                .Count(meta => meta.EntityPrototype?.ID == "WizardMinorIllusionPuff");
        });

        await Server.WaitRunTicks(2);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.Deleted(illusionUid), Is.True);
            Assert.That(SEntMan.EntityQuery<MetaDataComponent>()
                .Count(meta => meta.EntityPrototype?.ID == "WizardMinorIllusionPuff"),
                Is.EqualTo(puffCount + 1));
        });
    }

    [Test]
    public async Task WizardSpeakWithDeadTogglesOffEarly()
    {
        await Server.WaitAssertion(() =>
        {
            var actions = Server.System<SharedActionsSystem>();
            var actionUid = actions.AddAction(SPlayer, "ActionWizardSpeakWithDead");
            Assert.That(actionUid, Is.Not.Null);
            var action = actions.GetAction(actionUid)!.Value;

            actions.PerformAction(ToServer(Player), action);
            Assert.That(SEntMan.HasComponent<WizardDeadListeningComponent>(SPlayer), Is.True);
            Assert.That(action.Comp.Toggled, Is.True);

            actions.PerformAction(ToServer(Player), action);
            Assert.That(SEntMan.HasComponent<WizardDeadListeningComponent>(SPlayer), Is.False);
            Assert.That(action.Comp.Toggled, Is.False);
        });
    }

    [Test]
    public async Task WizardCantripsGiveTwoFreePicksThenChargeOneWizcoin()
    {
        await Server.WaitAssertion(() =>
        {
            var grimoire = SEntMan.SpawnEntity("WizardsGrimoire", MapData.GridCoords);
            var store = SEntMan.GetComponent<StoreComponent>(grimoire);
            var choices = new[]
            {
                "SpellbookSpellSummonCheese",
                "SpellbookCantripSummonDuck",
                "SpellbookCantripSummonBee",
                "SpellbookCantripSummonBeer",
                "SpellbookCantripTelepathy",
                "SpellbookCantripMinorIllusion",
                "SpellbookCantripSpeakWithDead",
                "SpellbookCantripMageHand",
                "SpellbookCantripArcaneChalk",
            };

            foreach (var id in choices)
            {
                var listing = store.FullListingsCatalog.Single(x => x.ID == id);
                Assert.That(listing.Cost["WizCoin"].Value, Is.Zero, id);
            }

            var originalBalance = store.Balance["WizCoin"];
            for (var i = 0; i < 3; i++)
            {
                SEntMan.EventBus.RaiseLocalEvent(grimoire,
                    new StoreBuyListingMessage(choices[i]) { Actor = SPlayer });

                var purchased = store.FullListingsCatalog.Single(x => x.ID == choices[i]);
                Assert.That(purchased.PurchaseAmount, Is.EqualTo(1), choices[i]);
                Assert.That(store.Balance["WizCoin"],
                    Is.EqualTo(originalBalance - FixedPoint2.New(i == 2 ? 1 : 0)));
            }

            Assert.That(store.FullListingsCatalog.Single(x => x.ID == choices[3]).Cost["WizCoin"],
                Is.EqualTo(FixedPoint2.New(1)));

            SEntMan.EventBus.RaiseLocalEvent(grimoire,
                new StoreRequestRefundMessage { Actor = SPlayer });
            Assert.That(store.Balance["WizCoin"], Is.EqualTo(originalBalance));
            foreach (var id in choices)
            {
                var listing = store.FullListingsCatalog.Single(x => x.ID == id);
                Assert.That(listing.Cost["WizCoin"].Value, Is.Zero, id);
            }
        });
    }

    [Test]
    public async Task SimpleWizardCantripsSummonTheirExpectedEntities()
    {
        await Server.WaitAssertion(() =>
        {
            var actions = Server.System<SharedActionsSystem>();
            var cases = new[]
            {
                (Action: "ActionSummonDuck", Entity: "MobDuckMallard"),
                (Action: "ActionSummonBee", Entity: "MobBee"),
                (Action: "ActionSummonBeer", Entity: "DrinkMagicalAleBottle"),
            };

            foreach (var (actionId, entityId) in cases)
            {
                var before = SEntMan.EntityQuery<MetaDataComponent>()
                    .Count(meta => meta.EntityPrototype?.ID == entityId);
                var actionUid = actions.AddAction(SPlayer, actionId);
                Assert.That(actionUid, Is.Not.Null, actionId);
                var action = actions.GetAction(actionUid);
                Assert.That(action, Is.Not.Null, actionId);

                actions.PerformAction(ToServer(Player), action!.Value);

                var after = SEntMan.EntityQuery<MetaDataComponent>()
                    .Count(meta => meta.EntityPrototype?.ID == entityId);
                Assert.That(after, Is.EqualTo(before + 1), actionId);
            }
        });
    }

    [Test]
    public async Task DoomThrowObjectiveEndsCurse()
    {
        await Server.WaitAssertion(() =>
        {
            var magic = Server.System<MagicSystem>();
            Assert.That(magic.TryApplyDoomCurse(SPlayer), Is.True);
            var curse = SEntMan.GetComponent<DoomCurseComponent>(SPlayer);
            Assert.That(SEntMan.EntityQuery<MetaDataComponent>()
                .Any(meta => meta.EntityPrototype?.ID?.StartsWith("EffectWizardDoom") == true), Is.False);
            curse.Objective = DoomObjective.Throw;
            var thrown = SEntMan.SpawnEntity("ToolboxEmergency", MapData.GridCoords);
            var ev = new ThrowEvent(SPlayer, thrown);
            SEntMan.EventBus.RaiseLocalEvent(SPlayer, ref ev);
            Assert.That(SEntMan.HasComponent<DoomCurseComponent>(SPlayer), Is.False);
        });
    }

    [Test]
    public async Task DoomExpiresWithOrdinaryDeath()
    {
        await Server.WaitAssertion(() =>
        {
            Assert.That(Server.System<MagicSystem>().TryApplyDoomCurse(SPlayer), Is.True);
            SEntMan.GetComponent<DoomCurseComponent>(SPlayer).ExpiresAt = TimeSpan.Zero;
        });

        await Server.WaitRunTicks(2);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<DoomCurseComponent>(SPlayer), Is.False);
            Assert.That(Server.System<Content.Shared.Mobs.Systems.MobStateSystem>().IsAlive(SPlayer), Is.False);
            var damage = Server.System<DamageableSystem>();
            Assert.That(damage.GetAllDamage(SPlayer).DamageDict.TryGetValue("Asphyxiation", out var asphyxiation), Is.True);
            Assert.That(asphyxiation, Is.EqualTo(damage.GetTotalDamage(SPlayer)));
            Assert.That(Server.System<Content.Shared.Mobs.Systems.MobThresholdSystem>()
                .TryGetThresholdForState(SPlayer, Content.Shared.Mobs.MobState.Dead, out var threshold), Is.True);
            Assert.That(damage.GetTotalDamage(SPlayer), Is.EqualTo(threshold));
        });
    }

    [Test]
    public async Task DoomGetPunchedObjectiveRequiresAnotherPersonToHitUnarmed()
    {
        await Server.WaitAssertion(() =>
        {
            Assert.That(Server.System<MagicSystem>().TryApplyDoomCurse(SPlayer), Is.True);
            SEntMan.GetComponent<DoomCurseComponent>(SPlayer).Objective = DoomObjective.GetPunched;

            var attacker = SEntMan.SpawnEntity("MobHuman", MapData.GridCoords.Offset(new Vector2(1, 0)));
            var weapon = SEntMan.SpawnEntity("ToolboxEmergency", MapData.GridCoords);
            var coordinates = SEntMan.GetComponent<TransformComponent>(SPlayer).Coordinates;

            SEntMan.EventBus.RaiseLocalEvent(SPlayer, new AttackedEvent(weapon, attacker, coordinates));
            Assert.That(SEntMan.HasComponent<DoomCurseComponent>(SPlayer), Is.True);

            SEntMan.EventBus.RaiseLocalEvent(SPlayer, new AttackedEvent(attacker, attacker, coordinates));
            Assert.That(SEntMan.HasComponent<DoomCurseComponent>(SPlayer), Is.False);
        });
    }

    [Test]
    public async Task InteriorRedecoratingChangesOnlyInteriorApprovedStructures()
    {
        await Server.WaitAssertion(() =>
        {
            var map = Server.System<SharedMapSystem>();
            var steel = new Tile(TileMan["FloorSteel"].TileId);
            for (var x = -3; x <= 3; x++)
            for (var y = -3; y <= 3; y++)
                map.SetTile(MapData.Grid, new Vector2i(x, y), steel);
            map.SetTile(MapData.Grid, new Vector2i(2, 2), new Tile(TileMan["FloorWhite"].TileId));

            SEntMan.SpawnEntity("WallSolid", MapData.GridCoords.Offset(new Vector2(1.5f, 0.5f)));
            SEntMan.SpawnEntity("Window", MapData.GridCoords.Offset(new Vector2(-1.5f, 0.5f)));
            SEntMan.SpawnEntity("WindowDirectional", MapData.GridCoords.Offset(new Vector2(-1.5f, -1.5f)));
            SEntMan.SpawnEntity("AirlockServiceLocked", MapData.GridCoords.Offset(new Vector2(0.5f, 1.5f)));
            var external = SEntMan.SpawnEntity("AirlockExternalLocked", MapData.GridCoords.Offset(new Vector2(2.5f, 0.5f)));
            var welded = SEntMan.SpawnEntity("AirlockServiceLocked", MapData.GridCoords.Offset(new Vector2(0.5f, 2.5f)));
            SEntMan.GetComponent<WeldableComponent>(welded).IsWelded = true;
            var edge = SEntMan.SpawnEntity("WallSolid", MapData.GridCoords.Offset(new Vector2(3.5f, 0.5f)));

            Assert.That(Server.System<MagicSystem>().TryInteriorRedecorating(SPlayer), Is.True);
            Assert.That(SEntMan.EntityQuery<MetaDataComponent>()
                .Count(meta => meta.EntityPrototype?.ID == "WallWood"), Is.GreaterThan(0));
            Assert.That(SEntMan.EntityQuery<MetaDataComponent>()
                .Count(meta => meta.EntityPrototype?.ID == "WoodDoor"), Is.GreaterThan(0));
            Assert.That(SEntMan.EntityQuery<MetaDataComponent>()
                .Count(meta => meta.EntityPrototype?.ID == "ClockworkWindow"), Is.GreaterThan(0));
            Assert.That(SEntMan.EntityQuery<MetaDataComponent>()
                .Count(meta => meta.EntityPrototype?.ID == "WindowClockworkDirectional"), Is.GreaterThan(0));
            Assert.That(SEntMan.GetComponent<MetaDataComponent>(external).EntityPrototype?.ID,
                Is.EqualTo("AirlockExternalLocked"));
            Assert.That(SEntMan.GetComponent<MetaDataComponent>(welded).EntityPrototype?.ID,
                Is.EqualTo("AirlockServiceLocked"));
            Assert.That(SEntMan.GetComponent<MetaDataComponent>(edge).EntityPrototype?.ID,
                Is.EqualTo("WallSolid"));
            var grid = SEntMan.GetComponent<MapGridComponent>(MapData.Grid);
            var recolored = map.GetTileRef(MapData.Grid, grid, new Vector2i(2, 2));
            Assert.That(TileMan[recolored.Tile.TypeId].ID, Is.EqualTo("FloorWood"));
            var boundary = map.GetTileRef(MapData.Grid, grid, new Vector2i(3, 0));
            Assert.That(TileMan[boundary.Tile.TypeId].ID, Is.EqualTo("FloorSteel"));
        });
    }

    [Test]
    public async Task SummonBeesCreatesFiveHostileBees()
    {
        await Server.WaitAssertion(() =>
        {
            Assert.That(Server.System<MagicSystem>().TrySummonBeeSwarm(SPlayer), Is.True);
            Assert.That(SEntMan.EntityQuery<MetaDataComponent>()
                .Count(meta => meta.EntityPrototype?.ID == "MobAngryBee"), Is.EqualTo(5));
        });
    }

    [Test]
    public async Task SnailLeavesALubricantTrailWhenMoving()
    {
        await Server.WaitAssertion(() =>
        {
            var magic = Server.System<MagicSystem>();
            Assert.That(magic.TryApplySnailCurse(SPlayer), Is.True);
            Assert.That(SEntMan.EntityQuery<PuddleComponent>().Count(), Is.Zero);
            Transform.SetCoordinates(SPlayer, MapData.GridCoords.Offset(new Vector2(1.5f, 0.5f)));
        });

        await Server.WaitRunTicks(2);
        await Server.WaitAssertion(() =>
        {
            var curse = SEntMan.GetComponent<SnailCurseComponent>(SPlayer);
            var current = Transform.ToMapCoordinates(SEntMan.GetComponent<TransformComponent>(SPlayer).Coordinates);
            Assert.That(Vector2.Distance(curse.LastTrailPosition, current.Position), Is.LessThan(0.1f),
                "The Snail update did not process movement.");
            Assert.That(SEntMan.EntityQuery<PuddleComponent>().Count(), Is.GreaterThan(0),
                "Moving under the snail curse did not leave a puddle.");
        });
    }

    [Test]
    public async Task SnailSelfForcesCrawlingAndExpires()
    {
        await Server.WaitAssertion(() =>
        {
            var magic = Server.System<MagicSystem>();
            Assert.That(magic.TryApplySnailCurse(SPlayer), Is.True);
            Assert.That(SEntMan.HasComponent<KnockedDownComponent>(SPlayer), Is.True);
            Assert.That(SEntMan.HasComponent<SnailCurseComponent>(SPlayer), Is.True);
            Assert.That(magic.TryApplySnailCurse(SPlayer), Is.False);
            SEntMan.GetComponent<SnailCurseComponent>(SPlayer).ExpiresAt = TimeSpan.Zero;
        });

        await Server.WaitRunTicks(2);
        await Server.WaitAssertion(() => Assert.That(SEntMan.HasComponent<SnailCurseComponent>(SPlayer), Is.False));
    }

    [Test]
    public async Task NoirSelfIsCuredByDrinkingFromMetamorphicGlass()
    {
        await Server.WaitAssertion(() =>
        {
            var magic = Server.System<MagicSystem>();
            Assert.That(magic.TryApplyNoirCurse(SPlayer), Is.True);
            Assert.That(SEntMan.HasComponent<NoirOverlayComponent>(SPlayer), Is.True);
            Assert.That(magic.TryApplyNoirCurse(SPlayer), Is.False);

            var glass = SEntMan.SpawnEntity("DrinkGlass", SEntMan.GetComponent<TransformComponent>(SPlayer).Coordinates);
            var drank = new IngestedEvent(SPlayer, SPlayer, new Solution("Water", FixedPoint2.New(1)), false, false);
            SEntMan.EventBus.RaiseLocalEvent(glass, ref drank);

            Assert.That(SEntMan.HasComponent<NoirCurseComponent>(SPlayer), Is.False);
            Assert.That(SEntMan.HasComponent<NoirOverlayComponent>(SPlayer), Is.False);
        });
    }

    [Test]
    public async Task AncientCurseStepsOnGridAndHolyWaterCuresIt()
    {
        await Server.WaitAssertion(() =>
        {
            var map = Server.System<SharedMapSystem>();
            var floor = new Tile(TileMan["FloorSteel"].TileId);
            map.SetTile(MapData.Grid, new Vector2i(1, 0), floor);
            map.SetTile(MapData.Grid, new Vector2i(2, 0), floor);

            var magic = Server.System<MagicSystem>();
            Assert.That(magic.TryApplyAncientCurse(SPlayer), Is.True);
            Assert.That(Server.System<ActionBlockerSystem>().CanMove(SPlayer), Is.False);
            Assert.That(magic.TryAncientStep(SPlayer, new Vector2i(1, 0)), Is.True);
        });

        await Server.WaitRunTicks(Pair.SecondsToTicks(0.4f));
        await Server.WaitAssertion(() =>
        {
            Assert.That(Transform.ToMapCoordinates(SEntMan.GetComponent<TransformComponent>(SPlayer).Coordinates)
                .Position.X, Is.EqualTo(1.5f).Within(0.1f));
            Assert.That(SEntMan.GetComponent<TransformComponent>(SPlayer).LocalRotation,
                Is.EqualTo(new Angle(Vector2.UnitX)));

            SEntMan.SpawnEntity("WallSolid", MapData.GridCoords.Offset(new Vector2(2.5f, 0.5f)));
            Assert.That(Server.System<MagicSystem>().TryAncientStep(SPlayer, new Vector2i(1, 0)), Is.False);

            var splash = new SpilledOnEvent(SPlayer, new Solution("Holywater", FixedPoint2.New(1)));
            SEntMan.EventBus.RaiseLocalEvent(SPlayer, splash);
            Assert.That(SEntMan.HasComponent<AncientCurseComponent>(SPlayer), Is.False);
            Assert.That(Server.System<ActionBlockerSystem>().CanMove(SPlayer), Is.True);
        });
    }

    [Test]
    public async Task AncientCurseRepeatsTileStepsFromHeldMovementInput()
    {
        await Server.WaitAssertion(() =>
        {
            var floor = new Tile(TileMan["FloorSteel"].TileId);
            Server.System<SharedMapSystem>().SetTile(MapData.Grid, new Vector2i(1, 0), floor);
            Assert.That(Server.System<MagicSystem>().TryApplyAncientCurse(SPlayer), Is.True);
            SEntMan.GetComponent<InputMoverComponent>(SPlayer).HeldMoveButtons = MoveButtons.Right;
        });

        await Server.WaitRunTicks(3);
        await Server.WaitAssertion(() => Assert.That(
            Transform.ToMapCoordinates(SEntMan.GetComponent<TransformComponent>(SPlayer).Coordinates)
                .Position.X, Is.LessThan(1.5f)));
        await Server.WaitRunTicks(Pair.SecondsToTicks(0.4f));
        await Server.WaitAssertion(() => Assert.That(
            Transform.ToMapCoordinates(SEntMan.GetComponent<TransformComponent>(SPlayer).Coordinates)
                .Position.X, Is.EqualTo(1.5f).Within(0.1f)));
    }

    [Test]
    public async Task MoodsCurseAddsOneMoodUntilFiveUnitsOfPsicodine()
    {
        await Server.WaitAssertion(() =>
        {
            var magic = Server.System<MagicSystem>();
            Assert.That(magic.TryApplyMoodsCurse(SPlayer), Is.True);
            Assert.That(SEntMan.HasComponent<WizardMoodsCurseComponent>(SPlayer), Is.True);
            Assert.That(SEntMan.GetComponent<ThavenMoodsComponent>(SPlayer).FollowsSharedMoods, Is.False);
            Assert.That(SEntMan.GetComponent<ThavenMoodsComponent>(SPlayer).Moods.Count, Is.EqualTo(1));

            var bloodstream = SEntMan.GetComponent<BloodstreamComponent>(SPlayer);
            var solutions = Server.System<SharedSolutionContainerSystem>();
            Assert.That(solutions.TryGetSolution(SPlayer, bloodstream.BloodSolutionName,
                out var bloodSolution), Is.True);
            Assert.That(solutions.TryAddReagent(bloodSolution!.Value, "Psicodine", FixedPoint2.New(4)), Is.True);
        });

        await Server.WaitRunTicks(35);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<WizardMoodsCurseComponent>(SPlayer), Is.True);
            var bloodstream = SEntMan.GetComponent<BloodstreamComponent>(SPlayer);
            var solutions = Server.System<SharedSolutionContainerSystem>();
            Assert.That(solutions.TryGetSolution(SPlayer, bloodstream.BloodSolutionName,
                out var bloodSolution), Is.True);
            Assert.That(solutions.TryAddReagent(bloodSolution!.Value, "Psicodine", FixedPoint2.New(5)), Is.True);
        });

        await Server.WaitRunTicks(330);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<WizardMoodsCurseComponent>(SPlayer), Is.False);
            Assert.That(SEntMan.HasComponent<ThavenMoodsComponent>(SPlayer), Is.False);
        });
    }

    [Test]
    public async Task MoodsSpellRerollsExistingThavenMoodsPermanently()
    {
        await Server.WaitAssertion(() =>
        {
            var thavenMoods = SEntMan.EnsureComponent<ThavenMoodsComponent>(SPlayer);
            var system = Server.System<ThavenMoodsSystem>();
            system.SetMoods((SPlayer, thavenMoods), []);

            Assert.That(Server.System<MagicSystem>().TryApplyMoodsCurse(SPlayer), Is.True);
            Assert.That(SEntMan.GetComponent<ThavenMoodsComponent>(SPlayer).Moods.Count, Is.EqualTo(1));
            Assert.That(SEntMan.HasComponent<WizardMoodsCurseComponent>(SPlayer), Is.False);

            Assert.That(SEntMan.HasComponent<ThavenMoodsComponent>(SPlayer), Is.True);
        });
    }

    [Test]
    public async Task MassLocationSwapMovesEveryNearbyMob()
    {
        await Server.WaitAssertion(() =>
        {
            var first = SEntMan.SpawnEntity("MobHuman", MapData.GridCoords.Offset(new Vector2(0.15f, 0)));
            var second = SEntMan.SpawnEntity("MobHuman", MapData.GridCoords.Offset(new Vector2(0.3f, 0)));
            var before = new[] { SPlayer, first, second }
                .Select(uid => SEntMan.GetComponent<TransformComponent>(uid).Coordinates)
                .ToArray();

            Assert.That(Server.System<MagicSystem>().TryMassLocationSwap(SPlayer), Is.True);

            var after = new[] { SPlayer, first, second }
                .Select(uid => SEntMan.GetComponent<TransformComponent>(uid).Coordinates)
                .ToArray();
            Assert.That(after, Is.EquivalentTo(before));
            for (var i = 0; i < before.Length; i++)
                Assert.That(after[i], Is.Not.EqualTo(before[i]), "A participant stayed in the same place.");
        });
    }

    [Test]
    public async Task PerspectiveSelfUsesTheNormalCurseEffect()
    {
        await Server.WaitAssertion(() =>
        {
            var magic = Server.System<MagicSystem>();
            Assert.That(magic.TryApplyPerspectiveCurse(SPlayer), Is.True);
            Assert.That(SEntMan.HasComponent<PerspectiveCurseComponent>(SPlayer), Is.True);
            Assert.That(magic.TryApplyPerspectiveCurse(SPlayer), Is.False,
                "Applying the test curse twice should not reset its original camera angle.");
        });
    }

    [Test]
    public async Task MagicMissileTargetsStandingUnpossessedMobsButSkipsProneMobs()
    {
        await Server.WaitAssertion(() =>
        {
            var target = SEntMan.SpawnEntity("MobHuman", MapData.GridCoords.Offset(new Vector2(3, 0)));
            var prone = SEntMan.SpawnEntity("MobHuman", MapData.GridCoords.Offset(new Vector2(0, 3)));
            Assert.That(Server.System<SharedStunSystem>().TryKnockdown(prone, TimeSpan.FromSeconds(5), force: true), Is.True);
            var actions = Server.System<SharedActionsSystem>();
            var actionUid = actions.AddAction(SPlayer, "ActionWizardMagicMissile");
            Assert.That(actionUid, Is.Not.Null);
            SEntMan.RemoveComponent<MagicComponent>(actionUid!.Value);
            var action = actions.GetAction(actionUid);
            Assert.That(action, Is.Not.Null);

            actions.PerformAction(ToServer(Player), action!.Value);

            var missiles = SEntMan.EntityQuery<MagicMissileHomingComponent>().ToArray();
            Assert.That(missiles, Has.Length.EqualTo(1), "Casting near an unpossessed mob should create one missile.");
            Assert.That(missiles[0].Target, Is.EqualTo(target));
        });
    }

    [Test]
    public async Task ArcaneBarrageUpgradeStaggersThirtyOrbsAcrossTwoTargets()
    {
        EntityUid first = default;
        EntityUid second = default;
        await Server.WaitAssertion(() =>
        {
            first = SEntMan.SpawnEntity("MobHuman", MapData.GridCoords.Offset(new Vector2(3, 0)));
            second = SEntMan.SpawnEntity("MobHuman", MapData.GridCoords.Offset(new Vector2(0, 3)));
            var actions = Server.System<SharedActionsSystem>();
            var actionUid = actions.AddAction(SPlayer, "ActionWizardArcaneBarrage");
            Assert.That(actionUid, Is.Not.Null);
            Assert.That(Server.System<ActionUpgradeSystem>().TryUpgradeAction(actionUid, out var upgraded), Is.True);
            Assert.That(upgraded, Is.Not.Null);
            SEntMan.RemoveComponent<MagicComponent>(upgraded!.Value);

            var action = actions.GetAction(upgraded);
            Assert.That(action, Is.Not.Null);
            actions.PerformAction(ToServer(Player), action!.Value);

            Assert.That(SEntMan.EntityQuery<MetaDataComponent>()
                .Count(meta => meta.EntityPrototype?.ID == "EffectWizardArcaneBarrageRise"), Is.EqualTo(3));
            Assert.That(SEntMan.EntityQuery<WizardBarrageOrbComponent>().ToArray(), Is.Empty);
        });

        await Server.WaitRunTicks(Pair.SecondsToTicks(0.7f));
        await Server.WaitAssertion(() =>
        {
            var orbs = SEntMan.EntityQuery<WizardBarrageOrbComponent>().ToArray();
            var rising = SEntMan.EntityQuery<MetaDataComponent>()
                .Count(meta => meta.EntityPrototype?.ID == "EffectWizardArcaneBarrageRise");
            Assert.That(orbs.Length + rising, Is.EqualTo(30),
                $"Rising effects: {rising}; tick period: {Server.Timing.TickPeriod.TotalSeconds}");
            Assert.That(orbs, Is.Not.Empty);
            Assert.That(orbs.Count(orb => orb.Target == first) - orbs.Count(orb => orb.Target == second),
                Is.InRange(-1, 1));
        });
    }

    [Test]
    public async Task LightningBarrageStartsWithThreeRisingTeslaBalls()
    {
        await Server.WaitAssertion(() =>
        {
            SEntMan.SpawnEntity("MobHuman", MapData.GridCoords.Offset(new Vector2(3, 0)));
            var actions = Server.System<SharedActionsSystem>();
            var actionUid = actions.AddAction(SPlayer, "ActionWizardLightningBarrage");
            Assert.That(actionUid, Is.Not.Null);
            SEntMan.RemoveComponent<MagicComponent>(actionUid!.Value);
            var action = actions.GetAction(actionUid);
            Assert.That(action, Is.Not.Null);
            actions.PerformAction(ToServer(Player), action!.Value);

            Assert.That(SEntMan.EntityQuery<MetaDataComponent>()
                .Count(meta => meta.EntityPrototype?.ID == "EffectWizardLightningBarrageRise"), Is.EqualTo(3));
            Assert.That(SEntMan.EntityQuery<WizardBarrageOrbComponent>().ToArray(), Is.Empty);
        });
    }

    [Test]
    public async Task LightningBarrageElectrocutesAfterFifteenBounceHitsUnlessInsulated()
    {
        await Server.WaitAssertion(() =>
        {
            var uninsulated = SEntMan.SpawnEntity("MobHuman", MapData.GridCoords.Offset(new Vector2(2, 0)));
            var insulated = SEntMan.SpawnEntity("MobHuman", MapData.GridCoords.Offset(new Vector2(0, 2)));
            var gloves = SEntMan.SpawnEntity("ClothingHandsGlovesColorYellow",
                SEntMan.GetComponent<TransformComponent>(insulated).Coordinates);
            Assert.That(Server.System<InventorySystem>().TryEquip(insulated, gloves, "gloves"), Is.True);

            void ApplyBounce(EntityUid target)
            {
                var orb = SEntMan.SpawnEntity(null, SEntMan.GetComponent<TransformComponent>(target).Coordinates);
                var component = SEntMan.AddComponent<WizardBarrageOrbComponent>(orb);
                component.Target = target;
                component.Caster = SPlayer;
                component.Lightning = true;
                component.BounceCount = 2;
                component.VolleyId = 17;
                var hit = new ProjectileHitEvent(new DamageSpecifier(), target, SPlayer);
                SEntMan.EventBus.RaiseLocalEvent(orb, ref hit);
            }

            for (var i = 0; i < 14; i++)
            {
                ApplyBounce(uninsulated);
                ApplyBounce(insulated);
            }

            Assert.That(SEntMan.HasComponent<ElectrocutedComponent>(uninsulated), Is.False);
            Assert.That(SEntMan.HasComponent<ElectrocutedComponent>(insulated), Is.False);

            ApplyBounce(uninsulated);
            ApplyBounce(insulated);
            Assert.That(SEntMan.HasComponent<ElectrocutedComponent>(uninsulated), Is.True);
            Assert.That(SEntMan.HasComponent<ElectrocutedComponent>(insulated), Is.False);
        });
    }

    [Test]
    public async Task LightningBarrageStopsAfterTwoNearbyBounces()
    {
        EntityUid second = default;
        await Server.WaitAssertion(() =>
        {
            var first = SEntMan.SpawnEntity("MobHuman", MapData.GridCoords.Offset(new Vector2(2, 0)));
            second = SEntMan.SpawnEntity("MobHuman", MapData.GridCoords.Offset(new Vector2(3, 0)));
            SEntMan.SpawnEntity("MobHuman", MapData.GridCoords.Offset(new Vector2(4, 0)));
            var initial = SEntMan.SpawnEntity(null, SEntMan.GetComponent<TransformComponent>(first).Coordinates);
            var component = SEntMan.AddComponent<WizardBarrageOrbComponent>(initial);
            component.Target = first;
            component.Caster = SPlayer;
            component.Lightning = true;
            component.VolleyId = 19;

            var firstHit = new ProjectileHitEvent(new DamageSpecifier(), first, SPlayer);
            SEntMan.EventBus.RaiseLocalEvent(initial, ref firstHit);
            Assert.That(SEntMan.EntityQuery<WizardBarrageOrbComponent>()
                .Any(orb => orb.BounceCount == 1), Is.False);
        });

        await Server.WaitRunTicks(8);
        await Server.WaitAssertion(() =>
        {
            var firstBounce = SEntMan.EntityQuery<WizardBarrageOrbComponent>()
                .Single(orb => orb.BounceCount == 1);
            Assert.That(firstBounce.Target, Is.EqualTo(second));

            var firstBounceUid = SEntMan.EntityQuery<WizardBarrageOrbComponent>()
                .Single(orb => orb.BounceCount == 1).Owner;
            var secondHit = new ProjectileHitEvent(new DamageSpecifier(), second, SPlayer);
            SEntMan.EventBus.RaiseLocalEvent(firstBounceUid, ref secondHit);
            Assert.That(SEntMan.EntityQuery<WizardBarrageOrbComponent>()
                .Any(orb => orb.BounceCount == 2), Is.False);
        });

        await Server.WaitRunTicks(8);
        await Server.WaitAssertion(() =>
        {
            var secondBounce = SEntMan.EntityQuery<WizardBarrageOrbComponent>()
                .Single(orb => orb.BounceCount == 2);
            Assert.That(secondBounce.Target, Is.Not.EqualTo(second));
            var thirdHit = new ProjectileHitEvent(new DamageSpecifier(), secondBounce.Target, SPlayer);
            SEntMan.EventBus.RaiseLocalEvent(secondBounce.Owner, ref thirdHit);
            Assert.That(SEntMan.EntityQuery<WizardBarrageOrbComponent>()
                .Any(orb => orb.BounceCount > 2), Is.False);
        });
    }

    [Test]
    public async Task StaticElectricityGloveVerbOnlyAppearsWhileCursed()
    {
        await Server.WaitAssertion(() =>
        {
            var gloves = SEntMan.SpawnEntity("ClothingHandsGlovesColorYellow",
                SEntMan.GetComponent<TransformComponent>(SPlayer).Coordinates);
            Assert.That(HandSys.TryPickupAnyHand(SPlayer, gloves), Is.True);

            GetVerbsEvent<AlternativeVerb> GetVerbs()
            {
                var ev = new GetVerbsEvent<AlternativeVerb>(SPlayer, gloves, null, null,
                    true, true, true, []);
                SEntMan.EventBus.RaiseLocalEvent(gloves, ev);
                return ev;
            }

            Assert.That(GetVerbs().Verbs.Any(verb => verb.Text == "Eat insulated gloves"), Is.False);
            Assert.That(Server.System<MagicSystem>().TryApplyStaticElectricityCurse(SPlayer), Is.True);
            Assert.That(GetVerbs().Verbs.Any(verb => verb.Text == "Eat insulated gloves"), Is.True);
            SEntMan.RemoveComponent<StaticElectricityCurseComponent>(SPlayer);
            Assert.That(GetVerbs().Verbs.Any(verb => verb.Text == "Eat insulated gloves"), Is.False);
        });
    }

    [Test]
    public async Task EatingInsulatedGlovesCuresStaticElectricity()
    {
        EntityUid gloves = default;
        await Server.WaitAssertion(() =>
        {
            gloves = SEntMan.SpawnEntity("ClothingHandsGlovesColorYellow",
                SEntMan.GetComponent<TransformComponent>(SPlayer).Coordinates);
            Assert.That(HandSys.TryPickupAnyHand(SPlayer, gloves), Is.True);
            Assert.That(Server.System<MagicSystem>().TryApplyStaticElectricityCurse(SPlayer), Is.True);

            var ev = new GetVerbsEvent<AlternativeVerb>(SPlayer, gloves, null, null,
                true, true, true, []);
            SEntMan.EventBus.RaiseLocalEvent(gloves, ev);
            ev.Verbs.Single(verb => verb.Text == "Eat insulated gloves").Act?.Invoke();
        });

        await Server.WaitRunTicks(90);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<StaticElectricityCurseComponent>(SPlayer), Is.False);
            Assert.That(SEntMan.Deleted(gloves), Is.True);
        });
    }

    [TestCase(MobState.Critical)]
    [TestCase(MobState.Dead)]
    public async Task ForceFeedingGlovesCuresStaticElectricityInCritOrDeath(MobState state)
    {
        EntityUid target = default;
        EntityUid gloves = default;
        await Server.WaitAssertion(() =>
        {
            target = SEntMan.SpawnEntity("MobHuman", MapData.GridCoords.Offset(new Vector2(1, 0)));
            gloves = SEntMan.SpawnEntity("ClothingHandsGlovesColorYellow", MapData.GridCoords);
            Assert.That(HandSys.TryPickupAnyHand(SPlayer, gloves), Is.True);
            Assert.That(Server.System<MagicSystem>().TryApplyStaticElectricityCurse(target), Is.True);
            Server.System<MobStateSystem>().ChangeMobState(target, state);

            var interact = new InteractUsingEvent(SPlayer, gloves, target,
                SEntMan.GetComponent<TransformComponent>(target).Coordinates);
            SEntMan.EventBus.RaiseLocalEvent(target, interact);
            Assert.That(interact.Handled, Is.True);
            Assert.That(SEntMan.HasComponent<StaticElectricityCurseComponent>(target), Is.True);
        });

        await Server.WaitRunTicks(90);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<StaticElectricityCurseComponent>(target), Is.False);
            Assert.That(SEntMan.Deleted(gloves), Is.True);
        });
    }

    [Test]
    public async Task ContactWithStaticElectricityCurseShocksTheToucher()
    {
        await Server.WaitAssertion(() =>
        {
            var toucher = SEntMan.SpawnEntity("MobHuman", MapData.GridCoords.Offset(new Vector2(1, 0)));
            Assert.That(Server.System<MagicSystem>().TryApplyStaticElectricityCurse(SPlayer), Is.True);
            SEntMan.EventBus.RaiseLocalEvent(SPlayer, new ContactInteractionEvent(toucher));
            Assert.That(SEntMan.HasComponent<ElectrocutedComponent>(toucher), Is.True);
        });
    }

    [Test]
    public async Task InsulatedGlovesBlockStaticElectricityCurse()
    {
        await Server.WaitAssertion(() =>
        {
            var toucher = SEntMan.SpawnEntity("MobHuman", MapData.GridCoords.Offset(new Vector2(1, 0)));
            var gloves = SEntMan.SpawnEntity("ClothingHandsGlovesColorYellow",
                SEntMan.GetComponent<TransformComponent>(toucher).Coordinates);
            Assert.That(Server.System<InventorySystem>().TryEquip(toucher, gloves, "gloves"), Is.True);
            Assert.That(Server.System<MagicSystem>().TryApplyStaticElectricityCurse(SPlayer), Is.True);
            SEntMan.EventBus.RaiseLocalEvent(SPlayer, new ContactInteractionEvent(toucher));
            Assert.That(SEntMan.HasComponent<ElectrocutedComponent>(toucher), Is.False);
        });
    }

    [Test]
    public async Task StaticElectricityObjectContactSparksOncePerCooldown()
    {
        await Server.WaitAssertion(() =>
        {
            var item = SEntMan.SpawnEntity("Crowbar", MapData.GridCoords.Offset(new Vector2(1, 0)));
            Assert.That(Server.System<MagicSystem>().TryApplyStaticElectricityCurse(SPlayer), Is.True);

            int CountSparks() => SEntMan.EntityQuery<MetaDataComponent>()
                .Count(meta => meta.EntityPrototype?.ID == "EffectSparks");

            var before = CountSparks();
            SEntMan.EventBus.RaiseLocalEvent(SPlayer, new ContactInteractionEvent(item));
            Assert.That(CountSparks(), Is.EqualTo(before + 1));
            SEntMan.EventBus.RaiseLocalEvent(SPlayer, new ContactInteractionEvent(item));
            Assert.That(CountSparks(), Is.EqualTo(before + 1));
        });
    }

    [Test]
    public async Task LocalizedZeroGravityExpiresAfterThirtySeconds()
    {
        await AddGravity();
        await Server.WaitAssertion(() =>
        {
            Assert.That(Server.System<MagicSystem>().TryLocalizedZeroGravity(SPlayer), Is.True);
            Assert.That(SEntMan.HasComponent<WizardZeroGravityComponent>(SPlayer), Is.True);
        });

        await Server.WaitRunTicks(960);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<WizardZeroGravityComponent>(SPlayer), Is.False);
            Assert.That(Server.System<SharedGravitySystem>().IsWeightless(SPlayer), Is.False);
        });
    }

    [Test]
    public async Task LocalizedZeroGravityTracksAreaAndEndsWhenLeaving()
    {
        await AddGravity();
        EntityUid target = default;
        bool initiallyWeightless = false;
        await Server.WaitAssertion(() =>
        {
            target = SEntMan.SpawnEntity("MobHuman", MapData.GridCoords.Offset(new Vector2(1, 0)));
            initiallyWeightless = Server.System<SharedGravitySystem>().IsWeightless(target);
            Assert.That(initiallyWeightless, Is.False, "The fixture must start with gravity for this test.");
            Assert.That(Server.System<MagicSystem>().TryLocalizedZeroGravity(SPlayer), Is.True);
            Assert.That(SEntMan.HasComponent<WizardZeroGravityComponent>(SPlayer), Is.True);
            Assert.That(Server.System<SharedGravitySystem>().IsWeightless(target), Is.True);
            Transform.SetCoordinates(target, MapData.GridCoords.Offset(new Vector2(10, 0)));
        });

        await Server.WaitRunTicks(20);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<WizardZeroGravityComponent>(target), Is.False);
            Assert.That(Server.System<SharedGravitySystem>().IsWeightless(target), Is.EqualTo(initiallyWeightless));
        });
    }

    [Test]
    public async Task FastExitFromZeroGravityKnocksDownButSlowExitDoesNot()
    {
        await AddGravity();
        EntityUid fast = default;
        EntityUid slow = default;
        await Server.WaitAssertion(() =>
        {
            fast = SEntMan.SpawnEntity("MobHuman", MapData.GridCoords.Offset(new Vector2(5.4f, 0)));
            slow = SEntMan.SpawnEntity("MobHuman", MapData.GridCoords.Offset(new Vector2(5.4f, 1)));
            Assert.That(Server.System<MagicSystem>().TryLocalizedZeroGravity(SPlayer), Is.True);
            Assert.That(SEntMan.HasComponent<WizardZeroGravityComponent>(fast), Is.True);
            Assert.That(SEntMan.HasComponent<WizardZeroGravityComponent>(slow), Is.True);

            Transform.SetCoordinates(fast, MapData.GridCoords.Offset(new Vector2(6.2f, 0)));
            Transform.SetCoordinates(slow, MapData.GridCoords.Offset(new Vector2(5.7f, 1)));
        });

        await Server.WaitRunTicks(20);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<WizardZeroGravityComponent>(fast), Is.False);
            Assert.That(SEntMan.HasComponent<WizardZeroGravityComponent>(slow), Is.False);
            Assert.That(SEntMan.HasComponent<KnockedDownComponent>(fast), Is.True);
            Assert.That(SEntMan.HasComponent<KnockedDownComponent>(slow), Is.False);
        });
    }

    [Test]
    public async Task GreaseSpillsFuelAcrossFloorTiles()
    {
        await Server.WaitAssertion(() =>
        {
            var map = Server.System<SharedMapSystem>();
            var steel = new Tile(TileMan["FloorSteel"].TileId);
            for (var x = -3; x <= 3; x++)
            for (var y = -3; y <= 3; y++)
                map.SetTile(MapData.Grid, new Vector2i(x, y), steel);

            Assert.That(Server.System<MagicSystem>().TryCastGrease(SPlayer,
                MapData.GridCoords.Offset(new Vector2(0.5f, 0.5f))), Is.True);
            Assert.That(SEntMan.EntityQuery<PuddleComponent>().Count(), Is.GreaterThanOrEqualTo(20));
        });
    }

    [Test]
    public async Task ConeOfColdFreezesThrowsAndLeavesIceUnderMovingTarget()
    {
        EntityUid target = default;
        EntityUid overlay = default;
        var iceBeforeMove = 0;
        await Server.WaitAssertion(() =>
        {
            var map = Server.System<SharedMapSystem>();
            var steel = new Tile(TileMan["FloorSteel"].TileId);
            for (var x = -1; x <= 6; x++)
            for (var y = -2; y <= 2; y++)
                map.SetTile(MapData.Grid, new Vector2i(x, y), steel);

            target = SEntMan.SpawnEntity("MobHuman", MapData.GridCoords.Offset(new Vector2(1, 0)));
            var actions = Server.System<SharedActionsSystem>();
            var actionUid = actions.AddAction(SPlayer, "ActionWizardConeOfCold");
            Assert.That(actionUid, Is.Not.Null);
            SEntMan.RemoveComponent<MagicComponent>(actionUid!.Value);
            var action = actions.GetAction(actionUid);
            var ev = actions.GetEvent(actionUid.Value) as ConeOfColdSpellEvent;
            Assert.That(action, Is.Not.Null);
            Assert.That(ev, Is.Not.Null);
            ev!.Target = MapData.GridCoords.Offset(new Vector2(3, 0));
            actions.PerformAction(ToServer(Player), action!.Value, ev);

            Assert.That(ev.Handled, Is.True);
            Assert.That(SEntMan.HasComponent<StunnedComponent>(target), Is.True);
            Assert.That(SEntMan.HasComponent<ThrownItemComponent>(target), Is.True);
            var frozen = SEntMan.GetComponent<WizardColdFreezeComponent>(target);
            Assert.That(frozen.Overlay, Is.Not.Null);
            overlay = frozen.Overlay!.Value;
            Assert.That(SEntMan.GetComponent<TransformComponent>(overlay).ParentUid,
                Is.EqualTo(target));
            iceBeforeMove = SEntMan.EntityQuery<MetaDataComponent>()
                .Count(meta => meta.EntityPrototype?.ID == "IceCrust");
            Assert.That(iceBeforeMove, Is.EqualTo(9));

            var transform = Server.System<SharedTransformSystem>();
            var position = transform.GetMapCoordinates(target);
            transform.SetMapCoordinates(target, new MapCoordinates(position.Position + new Vector2(2, 0), position.MapId));
        });

        await Server.WaitRunTicks(2);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.EntityQuery<MetaDataComponent>()
                .Count(meta => meta.EntityPrototype?.ID == "IceCrust"), Is.GreaterThan(iceBeforeMove));
        });

        await Server.WaitRunTicks(Pair.SecondsToTicks(4.2f));
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<WizardColdFreezeComponent>(target), Is.False);
            Assert.That(SEntMan.Deleted(overlay), Is.True);
        });
    }

    [Test]
    public async Task ConeOfColdFollowsDiagonalAim()
    {
        await Server.WaitAssertion(() =>
        {
            var map = Server.System<SharedMapSystem>();
            var steel = new Tile(TileMan["FloorSteel"].TileId);
            for (var x = -3; x <= 5; x++)
            for (var y = -3; y <= 5; y++)
                map.SetTile(MapData.Grid, new Vector2i(x, y), steel);

            var inLine = SEntMan.SpawnEntity("MobHuman", MapData.GridCoords.Offset(new Vector2(2, 2)));
            var outside = SEntMan.SpawnEntity("MobHuman", MapData.GridCoords.Offset(new Vector2(3, -1)));
            var actions = Server.System<SharedActionsSystem>();
            var actionUid = actions.AddAction(SPlayer, "ActionWizardConeOfCold");
            Assert.That(actionUid, Is.Not.Null);
            SEntMan.RemoveComponent<MagicComponent>(actionUid!.Value);
            var action = actions.GetAction(actionUid);
            var ev = actions.GetEvent(actionUid.Value) as ConeOfColdSpellEvent;
            Assert.That(action, Is.Not.Null);
            Assert.That(ev, Is.Not.Null);
            ev!.Target = MapData.GridCoords.Offset(new Vector2(3, 3));
            actions.PerformAction(ToServer(Player), action!.Value, ev);

            Assert.That(ev.Handled, Is.True);
            Assert.That(SEntMan.HasComponent<WizardColdFreezeComponent>(inLine), Is.True);
            Assert.That(SEntMan.HasComponent<WizardColdFreezeComponent>(outside), Is.False);
        });
    }

    [Test]
    public async Task TornadoPullsTargetButSparesCaster()
    {
        EntityUid target = default;
        await Server.WaitAssertion(() =>
        {
            var map = Server.System<SharedMapSystem>();
            var steel = new Tile(TileMan["FloorSteel"].TileId);
            for (var x = -3; x <= 9; x++)
            for (var y = -3; y <= 3; y++)
                map.SetTile(MapData.Grid, new Vector2i(x, y), steel);

            target = SEntMan.SpawnEntity("MobHuman", MapData.GridCoords.Offset(new Vector2(1.5f, 0.5f)));
            Assert.That(Server.System<MagicSystem>().TryCastTornado(SPlayer,
                MapData.GridCoords.Offset(new Vector2(7.5f, 0.5f))), Is.True);
        });

        await Server.WaitRunTicks(2);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<PhysicsComponent>(target).LinearVelocity.LengthSquared(),
                Is.GreaterThan(0.001f));
            Assert.That(SEntMan.GetComponent<PhysicsComponent>(SPlayer).LinearVelocity.LengthSquared(),
                Is.LessThan(0.001f));
        });
    }

    [Test]
    public async Task SummonGunWandFiresForTenSecondsAndUsesOneOfSevenCharges()
    {
        EntityUid wand = default;
        await Server.WaitAssertion(() =>
        {
            Server.System<TagSystem>().AddTag(SPlayer, "Wizard");
            wand = SEntMan.SpawnEntity("WeaponStaffSummonGun", MapData.GridCoords);
            Assert.That(HandSys.TryPickupAnyHand(SPlayer, wand), Is.True);
            Assert.That(SEntMan.GetComponent<RechargeBasicEntityAmmoComponent>(wand).RechargeSound, Is.Null);
        });

        await Server.WaitRunTicks(45);
        await Server.WaitAssertion(() =>
        {
            var ammo = SEntMan.GetComponent<BasicEntityAmmoProviderComponent>(wand);
            var gun = SEntMan.GetComponent<GunComponent>(wand);
            var target = SEntMan.GetComponent<TransformComponent>(SPlayer).Coordinates.Offset(new Vector2(3, 0));
            Assert.That(SGun.AttemptShoot(SPlayer, (wand, gun), target), Is.True);
            Assert.That(ammo.Count, Is.EqualTo(6));
            Assert.That(SEntMan.EntityQuery<MetaDataComponent>()
                .Count(meta => meta.EntityPrototype?.ID is "EffectWizardSpectralMosin" or
                    "EffectWizardSpectralRevolver" or "EffectWizardSpectralRifle" or
                    "EffectWizardSpectralSmg"), Is.EqualTo(1));
            Assert.That(SEntMan.EntityQuery<MetaDataComponent>()
                .Count(meta => meta.EntityPrototype?.ID == "ProjectileWizardSpectralBullet"), Is.EqualTo(1));
            Assert.That(SEntMan.EntityQuery<MetaDataComponent>()
                .Count(meta => meta.EntityPrototype?.ID == "EffectWizardSpectralMuzzle"), Is.EqualTo(1));
            Assert.That(SEntMan.EntityQuery<ProjectileComponent>()
                .Any(projectile => SEntMan.GetComponent<MetaDataComponent>(projectile.Owner).EntityPrototype?.ID ==
                    "ProjectileWizardSpectralBullet" && !projectile.IgnoreShooter), Is.True);
        });

        await Server.WaitRunTicks(320);
        await Server.WaitAssertion(() =>
            Assert.That(SEntMan.EntityQuery<MetaDataComponent>()
                .Count(meta => meta.EntityPrototype?.ID is "EffectWizardSpectralMosin" or
                    "EffectWizardSpectralRevolver" or "EffectWizardSpectralRifle" or
                    "EffectWizardSpectralSmg"), Is.Zero));
    }
}
