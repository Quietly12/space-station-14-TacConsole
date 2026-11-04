using System.Numerics;
using Content.Shared.Actions.Components;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.Player;
using Robust.Shared.Enums;
using Robust.Shared.Map.Components;

namespace Content.Client._Starlight.Magic;

/// <summary>Shows the area or travel direction of a selected wizard spell before it is cast.</summary>
public sealed class WizardSpellTargetOverlay : Robust.Client.Graphics.Overlay
{
    private readonly IEntityManager _entities;
    private readonly IInputManager _input;
    private readonly IEyeManager _eye;
    private readonly IPlayerManager _player;
    private readonly SharedTransformSystem _transform;
    private readonly SharedMapSystem _map;
    private readonly EntityUid _action;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    public WizardSpellTargetOverlay(IEntityManager entities, IInputManager input,
        IEyeManager eye, IPlayerManager player, EntityUid action)
    {
        _entities = entities;
        _input = input;
        _eye = eye;
        _player = player;
        _action = action;
        _transform = entities.System<SharedTransformSystem>();
        _map = entities.System<SharedMapSystem>();
        ZIndex = (int) Content.Shared.DrawDepth.DrawDepth.FloorObjects;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (_player.LocalEntity is not { } player ||
            !_entities.TryGetComponent(_action, out TargetActionComponent? target))
            return;

        var caster = _transform.GetMapCoordinates(player);
        var mouse = _eye.PixelToMap(_input.MouseScreenPosition);
        if (caster.MapId != args.MapId || mouse.MapId != caster.MapId)
            return;

        if (!_map.TryFindGridAt(mouse, out var gridUid, out var grid) ||
            !_map.TryFindGridAt(caster, out var casterGridUid, out _))
            return;
        var mouseTile = _map.GetTileRef(gridUid, grid, mouse).GridIndices;
        var inRange = target.Range <= 0 ||
            Vector2.DistanceSquared(caster.Position, mouse.Position) <= target.Range * target.Range;
        var color = inRange ? Color.FromHex("#44FF6633") : Color.FromHex("#FF444433");
        var id = _entities.GetComponent<MetaDataComponent>(_action).EntityPrototype?.ID ?? string.Empty;

        // The cold cone follows the exact aim direction; hellfire still snaps to a cardinal direction.
        if (id is "ActionWizardConeOfCold" or "ActionWizardConeOfHellfire")
        {
            if (casterGridUid != gridUid)
                return;
            var delta = mouse.Position - caster.Position;
            if (delta.LengthSquared() < 0.01f)
                return;
            var forward = id == "ActionWizardConeOfCold"
                ? Vector2.Normalize(delta)
                : MathF.Abs(delta.X) >= MathF.Abs(delta.Y)
                    ? new Vector2(Math.Sign(delta.X), 0)
                    : new Vector2(0, Math.Sign(delta.Y));
            var length = id == "ActionWizardConeOfCold" ? 3 : 2;
            args.WorldHandle.DrawLine(caster.Position, caster.Position + forward * length, color);
            return;
        }

        if (id is "ActionWizardMeteorSwarm" or "ActionWizardGrease")
        {
            args.WorldHandle.SetTransform(_transform.GetWorldMatrix(gridUid));
            var width = id == "ActionWizardMeteorSwarm" ? 7 : 4;
            var lower = (width - 1) / 2;
            for (var x = -lower; x < width - lower; x++)
            for (var y = -lower; y < width - lower; y++)
                DrawTile(args.WorldHandle, mouseTile + new Vector2i(x, y), color);
            return;
        }

        if (id == "ActionWizardTornado")
        {
            if (casterGridUid != gridUid)
                return;
            var delta = mouse.Position - caster.Position;
            if (delta.LengthSquared() < 0.01f)
                return;
            var forward = Vector2.Normalize(delta);
            args.WorldHandle.DrawLine(caster.Position, caster.Position + forward * 8, color);
            return;
        }

        args.WorldHandle.SetTransform(_transform.GetWorldMatrix(gridUid));
        DrawTile(args.WorldHandle, mouseTile, color);
    }

    private static void DrawTile(DrawingHandleWorld handle, Vector2i tile, Color color)
    {
        handle.DrawRect(new Box2(tile.X, tile.Y, tile.X + 1, tile.Y + 1), color);
    }
}
