using System.Linq;
using System.Numerics;
using Content.Client.Graphics;
using Content.Shared._Starlight.Magic.Components;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._Starlight.Magic;

public sealed class WizardMinorIllusionSystem : EntitySystem
{
    [Dependency] private SpriteSystem _sprites = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WizardMinorIllusionComponent, ComponentStartup>(OnStartup, after: [typeof(SpriteSystem)]);
        SubscribeLocalEvent<WizardMinorIllusionComponent, AfterAutoHandleStateEvent>(OnState);
        SubscribeLocalEvent<WizardMinorIllusionComponent, BeforePostShaderRenderEvent>(OnShaderRender);
    }

    private void OnStartup(Entity<WizardMinorIllusionComponent> ent, ref ComponentStartup args) =>
        UpdateAppearance(ent);

    private void OnState(Entity<WizardMinorIllusionComponent> ent, ref AfterAutoHandleStateEvent args) =>
        UpdateAppearance(ent);

    private void OnShaderRender(Entity<WizardMinorIllusionComponent> ent, ref BeforePostShaderRenderEvent args) =>
        UpdateShader(ent);

    private void UpdateAppearance(Entity<WizardMinorIllusionComponent> ent)
    {
        if (!ent.Comp.Running || !TryComp<SpriteComponent>(ent, out var sprite))
            return;

        var source = Spawn(ent.Comp.CreaturePrototype, MapCoordinates.Nullspace);
        if (TryComp<SpriteComponent>(source, out var sourceSprite) && sourceSprite.AllLayers.Any())
            _sprites.CopySprite((source, sourceSprite), (ent, sprite));
        else
        {
            for (var i = sprite.AllLayers.Count() - 1; i >= 0; i--)
                _sprites.RemoveLayer((ent, sprite), i);
            _sprites.AddLayer((ent, sprite), new PrototypeLayerData
            {
                RsiPath = "Mobs/Animals/mouse.rsi",
                State = "mouse-0",
            }, null);
        }

        QueueDel(source);
        _sprites.SetDrawDepth((ent, sprite), (int)Content.Shared.DrawDepth.DrawDepth.Mobs);
        sprite.NoRotation = true;
        UpdateShader(ent);
    }

    private void UpdateShader(Entity<WizardMinorIllusionComponent> ent)
    {
        if (!TryComp<SpriteComponent>(ent, out var sprite) || !sprite.AllLayers.Any())
            return;

        var shader = _prototypes.Index<ShaderPrototype>("WizardIllusion").InstanceUnique();
        var tint = ent.Comp.ShimmerColor;
        shader.SetParameter("tint", new Vector3(tint.R, tint.G, tint.B));
        shader.SetParameter("alpha", 0.97f);
        shader.SetParameter("texHeight", sprite.AllLayers.Max(x => (float) x.PixelSize.Y));
        shader.SetParameter("t", (float) _timing.CurTime.TotalSeconds * 0.12f);
        _sprites.SetPostShader((ent, sprite), new SpriteComponent.PostShaderArgs(ContentPostShaderIds.WizardIllusion, shader)
        {
            RaiseShaderEvent = true,
            Before = ContentPostShaderIds.BeforeOutlines,
        });
        sprite.RaiseShaderEvent = true;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var time = (float) _timing.CurTime.TotalSeconds;
        var query = EntityQueryEnumerator<WizardMinorIllusionComponent, SpriteComponent>();
        while (query.MoveNext(out var uid, out _, out var sprite))
        {
            // -NR-A tiny idle wander keeps the visual within its tile and away from wall collisions.
            _sprites.SetOffset((uid, sprite), new Vector2(MathF.Sin(time * 2f) * 0.12f,
                MathF.Cos(time * 1.4f) * 0.09f));
        }
    }
}
