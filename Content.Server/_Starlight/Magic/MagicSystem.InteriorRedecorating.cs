using System.Collections.Generic;
using Content.Shared._Starlight.Magic.Events;
using Content.Shared.Maps;
using Content.Shared.Doors.Components;
using Content.Shared.Tools.Components;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server.Magic;

public sealed partial class MagicSystem
{
    private const float RedecoratingRadius = 4f;
    private static readonly HashSet<string> RedecoratingSourceFloors = new()
    {
        "FloorSteel", "FloorSteelCheckerLight", "FloorSteelCheckerDark", "FloorSteelMini",
        "FloorSteelMono", "FloorSteelOffset", "FloorSteelSlatsContinuous", "FloorSteelDirty",
        "FloorWhite", "FloorWhiteMini", "FloorWhiteMono", "FloorWhiteOffset",
        "FloorDark", "FloorDarkMini", "FloorDarkMono", "FloorDarkOffset", "FloorMono",
    };

    private static readonly HashSet<string> RedecoratingAirlocks = new()
    {
        "Airlock", "AirlockServiceLocked", "AirlockTheatreLocked", "AirlockServiceTheatreLocked",
        "AirlockChapelLocked", "AirlockJanitorLocked", "AirlockKitchenLocked", "AirlockBarLocked",
        "AirlockBarKitchenLocked", "AirlockHydroponicsLocked", "AirlockEngineeringLocked",
        "AirlockCargoLocked", "AirlockMedicalLocked", "AirlockScienceLocked", "AirlockCommandLocked",
        "AirlockSecurityLocked", "AirlockMaint",
    };

    [Dependency] private SharedMapSystem _redecoratingMap = default!;
    [Dependency] private TileSystem _redecoratingTiles = default!;
    [Dependency] private TurfSystem _redecoratingTurf = default!;
    [Dependency] private ITileDefinitionManager _redecoratingTileDefs = default!;

    private void InitializeInteriorRedecorating()
    {
        SubscribeLocalEvent<InteriorRedecoratingSpellEvent>(OnInteriorRedecoratingSpell);
    }

    private void OnInteriorRedecoratingSpell(InteriorRedecoratingSpellEvent args)
    {
        if (args.Handled || !PassesSpellPrerequisites(args.Action, args.Performer))
            return;

        args.Handled = TryInteriorRedecorating(args.Performer);
    }

    public bool TryInteriorRedecorating(EntityUid caster)
    {
        if (!_wizardMobState.IsAlive(caster))
            return false;

        var origin = _wizardTransform.GetMapCoordinates(caster);
        if (!_redecoratingMap.TryFindGridAt(origin, out var gridUid, out var grid))
            return false;

        if (!_redecoratingTileDefs.TryGetDefinition("FloorWood", out var floorDef))
            return false;

        var changed = false;
        foreach (var tile in _redecoratingMap.GetTilesIntersecting(gridUid, grid,
                     new Circle(origin.Position, RedecoratingRadius), ignoreEmpty: true))
        {
            if (!IsSafeRedecoratingTile(tile, grid))
                continue;

            var source = _redecoratingTileDefs[tile.Tile.TypeId].ID;
            if (!RedecoratingSourceFloors.Contains(source))
                continue;

            changed |= _redecoratingTiles.ReplaceTile(tile, (ContentTileDefinition) floorDef);
        }

        // Only exact, approved prototypes are replaced. This leaves machinery, windows,
        // firelocks, reinforced walls and every exterior or special airlock untouched.
        var structures = new List<EntityUid>();
        foreach (var entity in _wizardLookup.GetEntitiesInRange<TransformComponent>(Transform(caster).Coordinates,
                     RedecoratingRadius, LookupFlags.Uncontained))
        {
            var uid = entity.Owner;
            if (uid == caster || Transform(uid).GridUid != gridUid)
                continue;

            var id = Prototype(uid)?.ID;
            if (id is "WallSolid" or "Grille" or "Window" or "WindowDirectional" ||
                (id != null && RedecoratingAirlocks.Contains(id)))
                structures.Add(uid);
        }

        foreach (var uid in structures)
        {
            if (Deleted(uid) || !TryComp<TransformComponent>(uid, out var xform))
                continue;

            var tile = _redecoratingTurf.GetTileRef(xform.Coordinates);
            if (tile is not { } floor || !IsSafeRedecoratingTile(floor, grid))
                continue;

            var id = Prototype(uid)?.ID;
            if (id != null && RedecoratingAirlocks.Contains(id) &&
                ((CompOrNull<DoorComponent>(uid)?.State != DoorState.Closed) ||
                 (CompOrNull<DoorBoltComponent>(uid)?.BoltsDown ?? false) ||
                 (CompOrNull<WeldableComponent>(uid)?.IsWelded ?? false)))
                continue;
            string? replacement = id switch
            {
                "WallSolid" => "WallWood",
                "Grille" => "ClockworkGrille",
                "Window" => "ClockworkWindow",
                "WindowDirectional" => "WindowClockworkDirectional",
                _ when id != null && RedecoratingAirlocks.Contains(id) => "WoodDoor",
                _ => null,
            };
            if (replacement == null)
                continue;

            var newUid = Spawn(replacement, xform.Coordinates);
            _wizardTransform.SetLocalRotation(newUid, xform.LocalRotation);
            QueueDel(uid);
            changed = true;
        }

        return changed;
    }

    private bool IsSafeRedecoratingTile(TileRef tile, MapGridComponent grid)
    {
        if (tile.Tile.IsEmpty || _redecoratingTurf.IsSpace(tile))
            return false;

        var directions = new[] { new Vector2i(1, 0), new Vector2i(-1, 0),
            new Vector2i(0, 1), new Vector2i(0, -1) };
        foreach (var offset in directions)
        {
            if (!_redecoratingMap.TryGetTileRef(tile.GridUid, grid, tile.GridIndices + offset, out var neighbor) ||
                neighbor.Tile.IsEmpty || _redecoratingTurf.IsSpace(neighbor))
                return false;
        }

        return true;
    }
}
