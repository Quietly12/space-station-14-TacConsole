using System.Linq;
using System.Numerics;
using Content.Shared._Starlight.Magic.Components;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Graphics.RSI;
using Robust.Shared.Prototypes;

namespace Content.Client._Starlight.Magic;

public sealed class WizardTrashGolemVisualSystem : EntitySystem
{
    private static readonly Vector2[] ItemOffsets =
    [
        new(0.00f, -0.30f), new(-0.17f, -0.24f), new(0.17f, -0.24f),
        new(-0.22f, -0.10f), new(0.22f, -0.10f), new(0.00f, 0.01f),
        new(-0.18f, 0.13f), new(0.18f, 0.13f), new(-0.12f, 0.27f),
        new(0.12f, 0.27f),
    ];

    [Dependency] private SpriteSystem _sprites = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WizardTrashGolemVisualComponent, ComponentStartup>(OnStartup, after: [typeof(SpriteSystem)]);
        SubscribeLocalEvent<WizardTrashGolemVisualComponent, AfterAutoHandleStateEvent>(OnState);
    }

    private void OnStartup(Entity<WizardTrashGolemVisualComponent> ent, ref ComponentStartup args) =>
        UpdateVisuals(ent);

    private void OnState(Entity<WizardTrashGolemVisualComponent> ent, ref AfterAutoHandleStateEvent args) =>
        UpdateVisuals(ent);

    private void UpdateVisuals(Entity<WizardTrashGolemVisualComponent> ent)
    {
        if (!TryComp<SpriteComponent>(ent, out var sprite))
            return;

        // This mob has one base layer. Everything above it belongs to its carried items.
        for (var i = sprite.AllLayers.Count() - 1; i >= 1; i--)
            _sprites.RemoveLayer((ent, sprite), i);

        for (var i = 0; i < ent.Comp.DisplayItems.Count && i < ItemOffsets.Length; i++)
        {
            var id = ent.Comp.DisplayItems[i];
            if (string.IsNullOrEmpty(id) || !_prototypes.TryIndex<EntityPrototype>(id, out var proto))
                continue;

            var texture = _sprites.GetPrototypeTextures(proto).FirstOrDefault();
            if (texture == null)
                continue;

            var layer = texture is RSI.State state
                ? _sprites.AddRsiLayer((ent, sprite), state.StateId, state.RSI)
                : _sprites.AddTextureLayer((ent, sprite), texture.Default);
            _sprites.LayerSetOffset((ent, sprite), layer, ItemOffsets[i]);
            _sprites.LayerSetScale((ent, sprite), layer, new Vector2(0.55f));
            _sprites.LayerSetRotation((ent, sprite), layer, Angle.FromDegrees((i * 53) % 90 - 45));
        }
    }
}
