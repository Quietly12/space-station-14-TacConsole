using System.Numerics;
using Content.Shared._Starlight.Magic.Events;
using Content.Shared.Damage;
using Content.Shared.Mobs.Components;
using Content.Shared.Physics;
using Content.Shared.Throwing;
using Robust.Shared.Audio;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;

namespace Content.Server.Magic;

public sealed partial class MagicSystem
{
    private const int HellfireConeLength = 2;
    private const int ColdConeLength = 3;
    private const int ColdConeDamage = 30;
    private const float ColdConeThrowSpeed = 10f; // Matches Repulse.
    private const float ColdConeRepulseRange = 5f;
    private const float HellfirePlasmaMolesPerTile = 6.25f; // Four cone tiles: 25 moles total.
    private static readonly SoundSpecifier ColdConeCastSound =
        new SoundPathSpecifier("/Audio/_Starlight/Magic/cone_of_cold.ogg");
    private static readonly TimeSpan ColdFreezeDuration = TimeSpan.FromSeconds(4);
    [Dependency] private ThrowingSystem _coldConeThrow = default!;

    private void InitializeElementalCones()
    {
        SubscribeLocalEvent<ConeOfHellfireSpellEvent>(OnConeOfHellfire);
        SubscribeLocalEvent<ConeOfColdSpellEvent>(OnConeOfCold);
    }

    private void OnConeOfHellfire(ConeOfHellfireSpellEvent args)
    {
        if (args.Handled || !PassesSpellPrerequisites(args.Action, args.Performer))
            return;
        args.Handled = TryCastElementalCone(args.Performer, args.Target, cold: false);
    }

    private void OnConeOfCold(ConeOfColdSpellEvent args)
    {
        if (args.Handled || !PassesSpellPrerequisites(args.Action, args.Performer))
            return;
        args.Handled = TryCastElementalCone(args.Performer, args.Target, cold: true);
        if (args.Handled)
            _wizardAudio.PlayPvs(ColdConeCastSound, args.Performer);
    }

    private bool TryCastElementalCone(EntityUid caster, EntityCoordinates target, bool cold)
    {
        if (!_wizardMobState.IsAlive(caster))
            return false;

        var start = _redecoratingTurf.GetTileRef(Transform(caster).Coordinates);
        if (start is not { } casterTile || casterTile.Tile.IsEmpty ||
            !TryComp<MapGridComponent>(casterTile.GridUid, out var grid))
            return false;

        var origin = _wizardTransform.GetMapCoordinates(caster);
        var aim = _wizardTransform.ToMapCoordinates(target);
        if (origin.MapId != aim.MapId)
            return false;

        var delta = aim.Position - origin.Position;
        if (delta.LengthSquared() < 0.01f)
            return false;

        if (cold)
            return TryCastColdCone(caster, casterTile, grid, Vector2.Normalize(delta));

        var forward = MathF.Abs(delta.X) >= MathF.Abs(delta.Y)
            ? new Vector2i(Math.Sign(delta.X), 0)
            : new Vector2i(0, Math.Sign(delta.Y));
        var side = new Vector2i(-forward.Y, forward.X);
        var affected = false;
        for (var distance = 1; distance <= HellfireConeLength; distance++)
        {
            // Narrow near the caster, widening to three tiles at the end.
            var halfWidth = distance == HellfireConeLength ? 1 : 0;
            for (var lateral = -halfWidth; lateral <= halfWidth; lateral++)
            {
                var indices = casterTile.GridIndices + forward * distance + side * lateral;
                if (!_redecoratingMap.TryGetTileRef(casterTile.GridUid, grid, indices, out var floor) ||
                    floor.Tile.IsEmpty || _redecoratingTurf.IsSpace(floor) ||
                    _redecoratingTurf.IsTileBlocked(floor, CollisionGroup.Impassable))
                    continue;

                var center = _redecoratingTurf.GetTileCenter(floor);
                if (!_wizardInteraction.InRangeUnobstructed(origin,
                        _wizardTransform.ToMapCoordinates(center), HellfireConeLength + 1,
                        CollisionGroup.Impassable))
                    continue;

                if (_dimensionDoorAtmosphere.GetTileMixture(floor.GridUid, null, floor.GridIndices, excite: true) is { } air)
                {
                    air.AdjustMoles(Content.Shared.Atmos.Gas.Plasma, HellfirePlasmaMolesPerTile);
                    air.Temperature = MathF.Max(air.Temperature, 800f);
                    Spawn("EffectSparks", center);
                    _dimensionDoorAtmosphere.HotspotExpose(floor.GridUid, floor.GridIndices,
                        800f, 500f, caster);
                }

                affected = true;
            }
        }

        return affected;
    }

    private bool TryCastColdCone(EntityUid caster, TileRef casterTile, MapGridComponent grid, Vector2 forward)
    {
        var origin = _wizardTransform.ToMapCoordinates(_redecoratingTurf.GetTileCenter(casterTile));
        var affected = false;
        for (var dx = -ColdConeLength - 1; dx <= ColdConeLength + 1; dx++)
        for (var dy = -ColdConeLength - 1; dy <= ColdConeLength + 1; dy++)
        {
            if (dx == 0 && dy == 0)
                continue;

            var indices = casterTile.GridIndices + new Vector2i(dx, dy);
            if (!_redecoratingMap.TryGetTileRef(casterTile.GridUid, grid, indices, out var floor) ||
                floor.Tile.IsEmpty || _redecoratingTurf.IsSpace(floor) ||
                _redecoratingTurf.IsTileBlocked(floor, CollisionGroup.Impassable))
                continue;

            var center = _redecoratingTurf.GetTileCenter(floor);
            var destination = _wizardTransform.ToMapCoordinates(center);
            var offset = destination.Position - origin.Position;
            var distance = Vector2.Dot(offset, forward);
            var sideways = MathF.Abs(offset.X * forward.Y - offset.Y * forward.X);
            if (distance < 0.5f || distance > ColdConeLength + 0.5f || sideways > 1.5f ||
                !_wizardInteraction.InRangeUnobstructed(origin, destination, ColdConeLength + 1.5f,
                    CollisionGroup.Impassable))
                continue;

            Spawn("IceCrust", center);
            HitColdConeTargets(caster, indices, casterTile.GridUid, forward);
            affected = true;
        }

        return affected;
    }

    private void HitColdConeTargets(EntityUid caster, Vector2i indices, EntityUid gridUid, Vector2 forward)
    {
        foreach (var other in _wizardLookup.GetEntitiesInRange<MobStateComponent>(
                     Transform(caster).Coordinates, ColdConeLength + 2, LookupFlags.Uncontained))
        {
            if (other.Owner == caster || !_wizardMobState.IsAlive(other) ||
                Transform(other).GridUid != gridUid ||
                _wizardTransform.GetGridTilePositionOrDefault(other.Owner) != indices)
                continue;

            var damage = new DamageSpecifier();
            damage.DamageDict.Add("Cold", ColdConeDamage);
            _doomDamage.TryChangeDamage(other.Owner, damage, origin: caster);
            if (_wizardStun.TryAddStunDuration(other.Owner, ColdFreezeDuration))
                ApplyColdFreeze(other.Owner);

            if (HasComp<PhysicsComponent>(other))
            {
                var casterPos = _wizardTransform.GetMapCoordinates(caster).Position;
                var targetPos = _wizardTransform.GetMapCoordinates(other.Owner).Position;
                var throwDistance = MathF.Max(0.5f, ColdConeRepulseRange - Vector2.Distance(casterPos, targetPos));
                _coldConeThrow.TryThrow(other.Owner, forward * throwDistance,
                    ColdConeThrowSpeed, caster, compensateFriction: true, recoil: false, playSound: false, doSpin: false);
            }
        }
    }

    private void ApplyColdFreeze(EntityUid target)
    {
        var frozen = EnsureComp<WizardColdFreezeComponent>(target);
        frozen.ExpiresAt = _wizardTiming.CurTime + ColdFreezeDuration;
        if (frozen.Overlay is not { } overlay || Deleted(overlay))
        {
            overlay = Spawn("EffectWizardColdFreeze", Transform(target).Coordinates);
            _wizardTransform.SetParent(overlay, target);
            _wizardTransform.SetLocalPosition(overlay, Vector2.Zero);
            frozen.Overlay = overlay;
        }

        if (_redecoratingTurf.GetTileRef(Transform(target).Coordinates) is { } floor)
        {
            frozen.LastGrid = floor.GridUid;
            frozen.LastTile = floor.GridIndices;
        }
    }

    private void UpdateElementalCones()
    {
        var query = EntityQueryEnumerator<WizardColdFreezeComponent>();
        while (query.MoveNext(out var target, out var frozen))
        {
            if (_wizardTiming.CurTime >= frozen.ExpiresAt)
            {
                if (frozen.Overlay is { } overlay && !Deleted(overlay))
                    QueueDel(overlay);
                RemCompDeferred<WizardColdFreezeComponent>(target);
                continue;
            }

            if (_redecoratingTurf.GetTileRef(Transform(target).Coordinates) is not { } floor ||
                floor.Tile.IsEmpty || _redecoratingTurf.IsSpace(floor) ||
                frozen.LastGrid == floor.GridUid && frozen.LastTile == floor.GridIndices)
                continue;

            frozen.LastGrid = floor.GridUid;
            frozen.LastTile = floor.GridIndices;
            Spawn("IceCrust", _redecoratingTurf.GetTileCenter(floor));
        }
    }
}
