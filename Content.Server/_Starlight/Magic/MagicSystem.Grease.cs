using Content.Shared._Starlight.Magic.Events;
using Content.Shared.Chemistry.Components;
using Content.Shared.FixedPoint;
using Content.Shared.Maps;
using Robust.Shared.Map;
using Content.Shared.Physics;

namespace Content.Server.Magic;

public sealed partial class MagicSystem
{
    private const float GreaseCastRange = 5f;
    private const int GreaseTileRadius = 2;
    private static readonly FixedPoint2 GreaseFuelPerTile = FixedPoint2.New(30);

    private void InitializeGrease()
    {
        SubscribeLocalEvent<GreaseSpellEvent>(OnGreaseSpell);
    }

    private void OnGreaseSpell(GreaseSpellEvent args)
    {
        if (args.Handled || !PassesSpellPrerequisites(args.Action, args.Performer))
            return;

        args.Handled = TryCastGrease(args.Performer, args.Target);
    }

    public bool TryCastGrease(EntityUid caster, EntityCoordinates target)
    {
        if (!_wizardMobState.IsAlive(caster))
            return false;

        var origin = _wizardTransform.GetMapCoordinates(caster);
        var destination = _wizardTransform.ToMapCoordinates(target);
        if (origin.MapId != destination.MapId ||
            (origin.Position - destination.Position).LengthSquared() > GreaseCastRange * GreaseCastRange ||
            !_wizardInteraction.InRangeUnobstructed(origin, destination, GreaseCastRange))
            return false;

        var center = _redecoratingTurf.GetTileRef(target);
        if (center is not { } centerTile || centerTile.Tile.IsEmpty || _redecoratingTurf.IsSpace(centerTile) ||
            !_redecoratingMap.TryFindGridAt(destination, out var gridUid, out var grid) ||
            centerTile.GridUid != gridUid)
            return false;

        var spilled = false;
        for (var x = 1 - GreaseTileRadius; x <= GreaseTileRadius; x++)
        for (var y = 1 - GreaseTileRadius; y <= GreaseTileRadius; y++)
        {
            if (!_redecoratingMap.TryGetTileRef(gridUid, grid,
                    centerTile.GridIndices + new Vector2i(x, y), out var tile) ||
                tile.Tile.IsEmpty || _redecoratingTurf.IsSpace(tile))
                continue;

            var tileCenter = _redecoratingTurf.GetTileCenter(tile);
            if (!_wizardInteraction.InRangeUnobstructed(destination,
                    _wizardTransform.ToMapCoordinates(tileCenter), GreaseTileRadius * 2,
                    CollisionGroup.Impassable))
                continue;

            spilled |= _wizardPuddles.TrySpillAt(tileCenter,
                new Solution("WeldingFuel", GreaseFuelPerTile), out _, sound: false);
        }

        return spilled;
    }
}
