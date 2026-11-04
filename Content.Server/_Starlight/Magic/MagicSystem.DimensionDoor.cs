using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server.Atmos.EntitySystems;
using Content.Shared._Starlight.Magic.Events;
using Content.Shared.Actions.Components;
using Content.Shared.Maps;
using Content.Shared.Physics;
using Content.Shared.Station;
using Content.Shared.Station.Components;
using Content.Shared.Teleportation.Systems;
using Robust.Shared.Audio;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server.Magic;

public sealed partial class MagicSystem
{
    private const float DimensionDoorPlacementRange = 1.5f;
    private static readonly TimeSpan DimensionDoorClosingTime = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan DimensionDoorFlickerInterval = TimeSpan.FromSeconds(0.5);
    private static readonly SoundSpecifier DimensionDoorClosingSound =
        new SoundPathSpecifier("/Audio/_Starlight/Magic/teleport_diss.ogg");

    [Dependency] private SharedStationSystem _dimensionDoorStations = default!;
    [Dependency] private AtmosphereSystem _dimensionDoorAtmosphere = default!;
    [Dependency] private LinkedEntitySystem _dimensionDoorLinks = default!;
    [Dependency] private SharedPointLightSystem _dimensionDoorLights = default!;

    private sealed class DimensionDoorPair
    {
        public EntityUid Caster;
        public EntityUid Action;
        public EntityUid Entrance;
        public EntityUid Exit;
        public TimeSpan? ClosesAt;
        public TimeSpan NextFlicker;
        public bool LightOn = true;
    }

    private readonly Dictionary<EntityUid, DimensionDoorPair> _dimensionDoors = new();

    private void InitializeDimensionDoor()
    {
        SubscribeLocalEvent<DimensionDoorSpellEvent>(OnDimensionDoorSpell);
        SubscribeLocalEvent<CloseDimensionDoorSpellEvent>(OnCloseDimensionDoorSpell);
    }

    private void OnDimensionDoorSpell(DimensionDoorSpellEvent args)
    {
        if (args.Handled || !PassesSpellPrerequisites(args.Action, args.Performer))
            return;

        args.Handled = TryCastDimensionDoor(args.Performer, args.Action.Owner, args.Target);
    }

    private void OnCloseDimensionDoorSpell(CloseDimensionDoorSpellEvent args)
    {
        if (args.Handled || !_dimensionDoors.TryGetValue(args.Performer, out var pair) ||
            pair.Action != args.Action.Owner)
            return;

        BeginDimensionDoorCollapse(pair);
        args.Handled = true;
    }

    public bool TryCastDimensionDoor(EntityUid caster, EntityUid action, EntityCoordinates target)
    {
        if (!_wizardMobState.IsAlive(caster) || _dimensionDoors.ContainsKey(caster))
            return false;

        var origin = _wizardTransform.GetMapCoordinates(caster);
        var destination = _wizardTransform.ToMapCoordinates(target);
        if (origin.MapId != destination.MapId ||
            Vector2.DistanceSquared(origin.Position, destination.Position) >
            DimensionDoorPlacementRange * DimensionDoorPlacementRange)
            return false;

        var station = _dimensionDoorStations.GetOwningStation(caster);
        var entryTile = _redecoratingTurf.GetTileRef(target);
        if (station is not { } stationUid || !TryComp<StationDataComponent>(stationUid, out var stationData) ||
            entryTile is not { } entry || _dimensionDoorStations.GetOwningStation(entry.GridUid) != stationUid ||
            !IsSafeDimensionDoorTile(entry))
            return false;

        if (!TryFindDimensionDoorExit(stationData, entry, out var exit))
            return false;

        var entrance = Spawn("WizardDimensionDoorEntrance", _redecoratingTurf.GetTileCenter(entry));
        var exitPortal = Spawn("WizardDimensionDoorExit", exit);
        _dimensionDoorLinks.TryLink(entrance, exitPortal);
        _dimensionDoors[caster] = new DimensionDoorPair
        {
            Caster = caster,
            Action = action,
            Entrance = entrance,
            Exit = exitPortal,
        };

        // The purchased action itself becomes the close button, preserving its hotbar slot.
        RemComp<WorldTargetActionComponent>(action);
        RemComp<TargetActionComponent>(action);
        EnsureComp<InstantActionComponent>(action);
        _wizardActions.SetEvent(action, new CloseDimensionDoorSpellEvent());
        _wizardActions.SetToggled(action, true);
        return true;
    }

    private bool IsSafeDimensionDoorTile(TileRef tile)
    {
        if (tile.Tile.IsEmpty || _redecoratingTurf.IsSpace(tile) ||
            _redecoratingTurf.IsTileBlocked(tile, CollisionGroup.MobMask))
            return false;

        var map = Transform(tile.GridUid).MapUid;
        return map is { } mapUid &&
            _dimensionDoorAtmosphere.IsTileMixtureProbablySafe(tile.GridUid, mapUid, tile.GridIndices);
    }

    private bool TryFindDimensionDoorExit(StationDataComponent station, TileRef entrance, out EntityCoordinates exit)
    {
        exit = default;
        var found = 0;
        var grids = station.MainGrids.Count > 0 ? station.MainGrids : station.Grids;
        foreach (var gridUid in grids)
        {
            if (!TryComp<MapGridComponent>(gridUid, out var grid))
                continue;

            foreach (var tile in _redecoratingMap.GetAllTiles(gridUid, grid))
            {
                if (tile.GridUid == entrance.GridUid && tile.GridIndices == entrance.GridIndices)
                    continue;
                if (!IsSafeDimensionDoorTile(tile) || _random.Next(++found) != 0)
                    continue;

                exit = _redecoratingTurf.GetTileCenter(tile);
            }
        }

        return found > 0;
    }

    private void BeginDimensionDoorCollapse(DimensionDoorPair pair)
    {
        if (pair.ClosesAt != null)
            return;

        pair.ClosesAt = _wizardTiming.CurTime + DimensionDoorClosingTime;
        pair.NextFlicker = _wizardTiming.CurTime;
        if (!TerminatingOrDeleted(pair.Entrance))
            _wizardAudio.PlayPvs(DimensionDoorClosingSound, pair.Entrance);
        if (!TerminatingOrDeleted(pair.Exit))
            _wizardAudio.PlayPvs(DimensionDoorClosingSound, pair.Exit);
        if (!TerminatingOrDeleted(pair.Action))
            _wizardActions.SetEnabled(pair.Action, false);
    }

    private void UpdateDimensionDoors()
    {
        foreach (var (caster, pair) in _dimensionDoors.ToArray())
        {
            if (pair.ClosesAt == null &&
                (TerminatingOrDeleted(caster) || !_wizardMobState.IsAlive(caster) ||
                 !_mind.TryGetMind(caster, out _, out var mind) || mind.CurrentEntity != caster ||
                 TerminatingOrDeleted(pair.Action) || !_wizardActions.GetActions(caster).Any(a => a.Owner == pair.Action) ||
                 TerminatingOrDeleted(pair.Entrance) ||
                 TerminatingOrDeleted(pair.Exit)))
                BeginDimensionDoorCollapse(pair);

            if (pair.ClosesAt is not { } closesAt)
                continue;

            if (_wizardTiming.CurTime >= closesAt)
            {
                if (!TerminatingOrDeleted(pair.Entrance))
                    QueueDel(pair.Entrance);
                if (!TerminatingOrDeleted(pair.Exit))
                    QueueDel(pair.Exit);
                if (!TerminatingOrDeleted(pair.Action))
                {
                    RemComp<InstantActionComponent>(pair.Action);
                    EnsureComp<TargetActionComponent>(pair.Action);
                    EnsureComp<WorldTargetActionComponent>(pair.Action);
                    _wizardActions.SetEvent(pair.Action, new DimensionDoorSpellEvent());
                    _wizardActions.SetToggled(pair.Action, false);
                    _wizardActions.SetEnabled(pair.Action, true);
                }
                _dimensionDoors.Remove(caster);
                continue;
            }

            if (_wizardTiming.CurTime < pair.NextFlicker)
                continue;
            pair.NextFlicker = _wizardTiming.CurTime + DimensionDoorFlickerInterval;
            pair.LightOn = !pair.LightOn;
            if (!TerminatingOrDeleted(pair.Entrance))
            {
                _dimensionDoorLights.SetEnabled(pair.Entrance, pair.LightOn);
                Spawn("WizardDimensionDoorFlicker", Transform(pair.Entrance).Coordinates);
            }
            if (!TerminatingOrDeleted(pair.Exit))
            {
                _dimensionDoorLights.SetEnabled(pair.Exit, pair.LightOn);
                Spawn("WizardDimensionDoorFlicker", Transform(pair.Exit).Coordinates);
            }
        }
    }
}
