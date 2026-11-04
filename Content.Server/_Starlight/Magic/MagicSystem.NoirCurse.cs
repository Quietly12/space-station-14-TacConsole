using Content.Shared._Starlight.Magic.Components;
using Content.Shared._Starlight.Magic.Events;
using Content.Shared.Nutrition;
using Content.Shared.Overlays;
using Content.Shared.Tag;
using Robust.Shared.Prototypes;

namespace Content.Server.Magic;

public sealed partial class MagicSystem
{
    private const string NoirCurseIndicator = "ActionWizardNoirCurseIndicator";
    private static readonly ProtoId<TagPrototype> MetamorphicGlassTag = "DrinkMetamorphic";

    private void InitializeNoirCurse()
    {
        SubscribeLocalEvent<NoirCurseSpellEvent>(OnNoirCurseSpell);
        SubscribeLocalEvent<NoirCureGlassComponent, IngestedEvent>(OnNoirCureGlassDrunk);
        SubscribeLocalEvent<NoirCurseComponent, ComponentShutdown>(OnNoirCurseShutdown);
    }

    private void OnNoirCurseSpell(NoirCurseSpellEvent args)
    {
        if (args.Handled || !PassesSpellPrerequisites(args.Action, args.Performer))
            return;

        args.Handled = TryApplyNoirCurse(args.Target);
    }

    public bool TryApplyNoirCurse(EntityUid target)
    {
        if (!_wizardMobState.IsAlive(target) || HasComp<NoirCurseComponent>(target))
            return false;

        var curse = EnsureComp<NoirCurseComponent>(target);
        curse.AddedOverlay = !HasComp<NoirOverlayComponent>(target);
        if (curse.AddedOverlay)
            EnsureComp<NoirOverlayComponent>(target);

        _wizardActions.AddAction(target, ref curse.IndicatorAction, NoirCurseIndicator);
        return true;
    }

    private void OnNoirCureGlassDrunk(Entity<NoirCureGlassComponent> ent, ref IngestedEvent args)
    {
        if (args.Split.Volume <= 0 ||
            !_tag.HasTag(ent.Owner, MetamorphicGlassTag) ||
            !HasComp<NoirCurseComponent>(args.Target))
            return;

        RemComp<NoirCurseComponent>(args.Target);
    }

    private void OnNoirCurseShutdown(Entity<NoirCurseComponent> ent, ref ComponentShutdown args)
    {
        if (TerminatingOrDeleted(ent.Owner))
            return;

        if (ent.Comp.AddedOverlay)
            RemComp<NoirOverlayComponent>(ent.Owner);
        if (ent.Comp.IndicatorAction is { } action)
            _wizardActions.RemoveAction(action);
    }
}
