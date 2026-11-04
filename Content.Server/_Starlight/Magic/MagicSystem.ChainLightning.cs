using System.Collections.Generic;
using System.Numerics;
using Content.Server.Lightning;
using Content.Shared._Starlight.Magic.Events;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.Electrocution;
using Content.Shared.Inventory;
using Content.Shared.Mobs.Components;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Content.Shared.Physics;

namespace Content.Server.Magic;

public sealed partial class MagicSystem
{
    private const float ChainLightningInitialRange = 5f;
    private const float ChainLightningBounceRange = 3f;
    private const int ChainLightningInitialDamage = 40;
    private const int ChainLightningDamageDrop = 10;
    private const int ChainLightningMaxBounces = 2;
    private static readonly TimeSpan ChainLightningShockTime = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan ChainLightningInsulatedShockTime = TimeSpan.FromSeconds(1);

    [Dependency] private LightningSystem _chainLightningVisuals = default!;
    [Dependency] private InventorySystem _chainLightningInventory = default!;

    private void InitializeChainLightning()
    {
        SubscribeLocalEvent<ChainLightningSpellEvent>(OnChainLightningSpell);
    }

    private void OnChainLightningSpell(ChainLightningSpellEvent args)
    {
        if (args.Handled || !PassesSpellPrerequisites(args.Action, args.Performer))
            return;

        args.Handled = TryCastChainLightning(args.Performer, args.Target);
    }

    public bool TryCastChainLightning(EntityUid caster, EntityUid target)
    {
        if (caster == target || !_wizardMobState.IsAlive(caster) || !_wizardMobState.IsAlive(target) ||
            !HasComp<MobStateComponent>(target) ||
            !CanChainLightningReach(caster, target, ChainLightningInitialRange))
            return false;

        var hit = new HashSet<EntityUid> { caster };
        var from = caster;
        for (var hop = 0; hop <= ChainLightningMaxBounces; hop++)
        {
            hit.Add(target);
            _chainLightningVisuals.ShootLightning(from, target, triggerLightningEvents: false);

            var insulated = _chainLightningInventory.TryGetSlotEntity(target, "gloves", out var gloves) &&
                TryComp<InsulatedComponent>(gloves, out var insulation) && insulation.Coefficient <= 0f;
            // The electrocution API requires at least one point of shock damage to apply its stun.
            // Count that point toward the spell's exact per-hop damage.
            var electrocuted = _wizardElectrocution.TryDoElectrocution(target, caster, 1,
                insulated ? ChainLightningInsulatedShockTime : ChainLightningShockTime,
                refresh: true, ignoreInsulation: true);
            var damage = new DamageSpecifier();
            damage.DamageDict.Add("Shock", ChainLightningInitialDamage - hop * ChainLightningDamageDrop -
                                           (electrocuted ? 1 : 0));
            _doomDamage.TryChangeDamage(target, damage, origin: caster);

            if (hop == ChainLightningMaxBounces)
                break;

            from = target;
            if (FindNextChainLightningTarget(from, caster, hit) is not { } next)
                break;
            target = next;
        }

        return true;
    }

    private EntityUid? FindNextChainLightningTarget(EntityUid from, EntityUid caster, HashSet<EntityUid> hit)
    {
        EntityUid? closest = null;
        var distance = ChainLightningBounceRange * ChainLightningBounceRange;
        var origin = _wizardTransform.ToMapCoordinates(Transform(from).Coordinates);
        foreach (var candidate in _wizardLookup.GetEntitiesInRange<MobStateComponent>(
                     Transform(from).Coordinates, ChainLightningBounceRange, LookupFlags.Uncontained))
        {
            if (candidate.Owner == caster || hit.Contains(candidate.Owner) ||
                !_wizardMobState.IsAlive(candidate))
                continue;

            var destination = _wizardTransform.ToMapCoordinates(Transform(candidate).Coordinates);
            if (destination.MapId != origin.MapId ||
                !CanChainLightningReach(from, candidate.Owner, ChainLightningBounceRange))
                continue;

            var current = Vector2.DistanceSquared(origin.Position, destination.Position);
            if (current >= distance)
                continue;
            distance = current;
            closest = candidate.Owner;
        }

        return closest;
    }

    private bool CanChainLightningReach(EntityUid from, EntityUid target, float range)
    {
        var origin = _wizardTransform.ToMapCoordinates(Transform(from).Coordinates);
        var destination = _wizardTransform.ToMapCoordinates(Transform(target).Coordinates);
        if (origin.MapId != destination.MapId ||
            Vector2.DistanceSquared(origin.Position, destination.Position) > range * range ||
            !_wizardInteraction.InRangeUnobstructed(origin, destination, range,
                CollisionGroup.Impassable | CollisionGroup.InteractImpassable, checkAccess: true))
            return false;

        // Check crossed grid tiles too. The interaction raycast can miss anchored walls.
        var fromTile = _redecoratingTurf.GetTileRef(Transform(from).Coordinates);
        var toTile = _redecoratingTurf.GetTileRef(Transform(target).Coordinates);
        if (fromTile is not { } start || toTile is not { } end || start.GridUid != end.GridUid ||
            !TryComp<MapGridComponent>(start.GridUid, out var grid))
            return true;

        var startPosition = new Vector2(start.GridIndices.X + 0.5f, start.GridIndices.Y + 0.5f);
        var endPosition = new Vector2(end.GridIndices.X + 0.5f, end.GridIndices.Y + 0.5f);
        var delta = endPosition - startPosition;
        var samples = Math.Max(1, (int) Math.Ceiling(delta.Length() * 4));
        for (var i = 1; i < samples; i++)
        {
            var point = startPosition + delta * i / samples;
            var indices = new Vector2i((int) Math.Floor(point.X), (int) Math.Floor(point.Y));
            if (_redecoratingMap.TryGetTileRef(start.GridUid, grid, indices, out var tile) &&
                _redecoratingTurf.IsTileBlocked(tile,
                    CollisionGroup.Impassable | CollisionGroup.InteractImpassable))
                return false;
        }

        return true;
    }
}
