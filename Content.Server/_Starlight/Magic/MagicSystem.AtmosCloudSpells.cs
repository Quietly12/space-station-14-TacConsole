using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Shared._Starlight.Magic.Events;
using Content.Shared.Atmos;
using Content.Shared.Physics;
using Content.Shared.Trigger.Components.Effects;
using Content.Shared.Trigger;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server.Magic;

[RegisterComponent]
public sealed partial class WizardGasSparkOnFinishComponent : Component
{
    public bool BeganRelease;
    public bool StartSparked;
}

[RegisterComponent]
public sealed partial class WizardGasWindOnReleaseComponent : Component
{
    public TimeSpan NextPulse;
}

public sealed partial class MagicSystem
{
    // The cloud balls use the air grenade's staged release from one tile.
    private const float WizardGasSpellRange = 5f;
    private const float AsphyxiateDurationSeconds = 20f;
    private const float AsphyxiateTickSeconds = 0.5f;
    private const float AsphyxiateOxygenMolesPerTick = 5f;

    private readonly Dictionary<EntityUid, TimeSpan> _asphyxiateFlames = new();
    private TimeSpan _nextAsphyxiateTick;
    private TimeSpan _nextAsphyxiateWind;
    private static readonly (Vector2 Offset, string Prototype)[] AsphyxiateWindDirections =
    [
        (new Vector2(0, 1), "EffectWizardGasWindInNorth"),
        (new Vector2(1, 0), "EffectWizardGasWindInEast"),
        (new Vector2(0, -1), "EffectWizardGasWindInSouth"),
        (new Vector2(-1, 0), "EffectWizardGasWindInWest"),
    ];

    private void InitializeAtmosCloudSpells()
    {
        SubscribeLocalEvent<SleepingCloudSpellEvent>(OnSleepingCloud);
        SubscribeLocalEvent<MagicalSaunaSpellEvent>(OnMagicalSauna);
        SubscribeLocalEvent<StinkCloudSpellEvent>(OnStinkCloud);
        SubscribeLocalEvent<CombustSpellEvent>(OnCombust);
        SubscribeLocalEvent<AsphyxiateSpellEvent>(OnAsphyxiate);
        SubscribeLocalEvent<WizardGasSparkOnFinishComponent, TriggerEvent>(OnWizardGasSparkStarted);
    }

    private void OnSleepingCloud(SleepingCloudSpellEvent args)
    {
        if (args.Handled || !PassesSpellPrerequisites(args.Action, args.Performer))
            return;

        args.Handled = TryConjureWizardGasBall(args.Performer, "WizardSleepingCloudBall");
    }

    private void OnCombust(CombustSpellEvent args)
    {
        if (args.Handled || !PassesSpellPrerequisites(args.Action, args.Performer))
            return;

        args.Handled = TryConjureWizardGasBall(args.Performer, "WizardCombustBall");
    }

    private void OnMagicalSauna(MagicalSaunaSpellEvent args)
    {
        if (args.Handled || !PassesSpellPrerequisites(args.Action, args.Performer))
            return;
        args.Handled = TryConjureWizardGasBall(args.Performer, "WizardMagicalSaunaBall");
    }

    private void OnStinkCloud(StinkCloudSpellEvent args)
    {
        if (args.Handled || !PassesSpellPrerequisites(args.Action, args.Performer))
            return;
        args.Handled = TryConjureWizardGasBall(args.Performer, "WizardStinkCloudBall");
    }

    private void OnWizardGasSparkStarted(Entity<WizardGasSparkOnFinishComponent> ent, ref TriggerEvent args)
    {
        ent.Comp.BeganRelease = true;
    }

    private bool TryConjureWizardGasBall(EntityUid caster, string prototype)
    {
        if (!_wizardMobState.IsAlive(caster))
            return false;

        var ball = Spawn(prototype, Transform(caster).Coordinates);
        if (_doomHands.TryPickupAnyHand(caster, ball, checkActionBlocker: false))
            return true;

        QueueDel(ball);
        _doomPopup.PopupEntity(Loc.GetString("wizard-gas-ball-hands-full"), caster, caster);
        return false;
    }

    private void OnAsphyxiate(AsphyxiateSpellEvent args)
    {
        if (args.Handled || !PassesSpellPrerequisites(args.Action, args.Performer) ||
            !TryGetWizardAtmosTarget(args.Performer, args.Target, out var tile))
            return;

        var flame = Spawn("EffectWizardAsphyxiateFlame", _redecoratingTurf.GetTileCenter(tile));
        _asphyxiateFlames[flame] = _wizardTiming.CurTime + TimeSpan.FromSeconds(AsphyxiateDurationSeconds);
        args.Handled = true;
    }

    private bool TryGetWizardAtmosTarget(EntityUid caster, EntityCoordinates target, out TileRef tileEntity)
    {
        tileEntity = default;
        if (!_wizardMobState.IsAlive(caster))
            return false;

        var origin = _wizardTransform.GetMapCoordinates(caster);
        var destination = _wizardTransform.ToMapCoordinates(target);
        if (origin.MapId != destination.MapId ||
            (origin.Position - destination.Position).LengthSquared() > WizardGasSpellRange * WizardGasSpellRange ||
            !_wizardInteraction.InRangeUnobstructed(origin, destination, WizardGasSpellRange,
                CollisionGroup.Impassable))
            return false;

        var tile = _redecoratingTurf.GetTileRef(target);
        if (tile is not { } floor || floor.Tile.IsEmpty || _redecoratingTurf.IsSpace(floor))
            return false;

        tileEntity = floor;
        return true;
    }

    private void UpdateAsphyxiate()
    {
        if (_asphyxiateFlames.Count == 0 || _wizardTiming.CurTime < _nextAsphyxiateTick)
            return;

        _nextAsphyxiateTick = _wizardTiming.CurTime + TimeSpan.FromSeconds(AsphyxiateTickSeconds);
        var windPulse = _wizardTiming.CurTime >= _nextAsphyxiateWind;
        if (windPulse)
            _nextAsphyxiateWind = _wizardTiming.CurTime + TimeSpan.FromSeconds(1);
        foreach (var (flame, expiry) in _asphyxiateFlames.ToArray())
        {
            if (Deleted(flame) || _wizardTiming.CurTime >= expiry)
            {
                _asphyxiateFlames.Remove(flame);
                if (!Deleted(flame))
                    QueueDel(flame);
                continue;
            }

            if (windPulse)
            {
                var flameCoordinates = Transform(flame).Coordinates;
                foreach (var (offset, prototype) in AsphyxiateWindDirections)
                    Spawn(prototype, flameCoordinates.Offset(offset));
            }

            var center = _redecoratingTurf.GetTileRef(Transform(flame).Coordinates);
            if (center is not { } tile || !TryComp<MapGridComponent>(tile.GridUid, out var grid))
                continue;
            for (var dx = -1; dx <= 1; dx++)
            for (var dy = -1; dy <= 1; dy++)
            {
                var indices = tile.GridIndices + new Vector2i(dx, dy);
                if (!_redecoratingMap.TryGetTileRef(tile.GridUid, grid, indices, out var floor) ||
                    floor.Tile.IsEmpty || _redecoratingTurf.IsSpace(floor))
                    continue;

                var air = _dimensionDoorAtmosphere.GetTileMixture(tile.GridUid, null, indices, excite: true);
                if (air is null)
                    continue;
                air.AdjustMoles(Gas.Oxygen, -MathF.Min(air.GetMoles(Gas.Oxygen), AsphyxiateOxygenMolesPerTick));
            }
        }
    }

    private void UpdateWizardGasSparks()
    {
        var query = EntityQueryEnumerator<WizardGasSparkOnFinishComponent>();
        while (query.MoveNext(out var uid, out var marker))
        {
            if (!marker.BeganRelease)
                continue;

            var releasing = TryComp<ReleaseGasOnTriggerComponent>(uid, out var gas);
            if (!marker.StartSparked && releasing && gas!.StartingTotalMoles > 0 &&
                gas.Air.TotalMoles < gas.StartingTotalMoles)
            {
                SparkWizardGasBall(uid);
                marker.StartSparked = true;
            }

            if (releasing && gas!.Air.TotalMoles > 0)
                continue;

            SparkWizardGasBall(uid);
            RemComp<WizardGasSparkOnFinishComponent>(uid);
        }
    }

    private void UpdateWizardGasWind()
    {
        var query = EntityQueryEnumerator<WizardGasWindOnReleaseComponent>();
        while (query.MoveNext(out var uid, out var wind))
        {
            if (!TryComp<ReleaseGasOnTriggerComponent>(uid, out var gas))
            {
                RemCompDeferred<WizardGasWindOnReleaseComponent>(uid);
                continue;
            }

            if (!gas.Active || gas.Air.TotalMoles <= 0 || wind.NextPulse > _wizardTiming.CurTime)
                continue;

            Spawn("EffectWizardGasWindOut", Transform(uid).Coordinates);
            wind.NextPulse = _wizardTiming.CurTime + gas.ReleaseInterval;
        }
    }

    private void SparkWizardGasBall(EntityUid uid)
    {
        Spawn("EffectSparks", Transform(uid).Coordinates);
        if (_redecoratingTurf.GetTileRef(Transform(uid).Coordinates) is { } tile &&
            !tile.Tile.IsEmpty && !_redecoratingTurf.IsSpace(tile))
            _dimensionDoorAtmosphere.HotspotExpose(tile.GridUid, tile.GridIndices,
                800f, 500f, uid);
    }
}
