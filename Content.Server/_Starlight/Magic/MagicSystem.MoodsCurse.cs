using System.Linq;
using Content.Server._Starlight.Thaven;
using Content.Shared._Starlight.Magic.Components;
using Content.Shared._Starlight.Magic.Events;
using Content.Shared._Starlight.Thaven;
using Content.Shared._Starlight.Railroading.Events;
using Content.Shared.FixedPoint;
using Robust.Shared.Player;

namespace Content.Server.Magic;

public sealed partial class MagicSystem
{
    private const string MoodsCurseIndicator = "ActionWizardMoodsCurseIndicator";
    private const int MoodsCurseCount = 1;
    private static readonly FixedPoint2 MoodsPsicodineCureAmount = FixedPoint2.New(5);

    [Dependency] private ThavenMoodsSystem _wizardThavenMoods = default!;

    private void InitializeMoodsCurse()
    {
        SubscribeLocalEvent<MoodsCurseSpellEvent>(OnMoodsCurseSpell);
        SubscribeLocalEvent<WizardMoodsCurseComponent, RailroadingReagentMetabolizedEvent>(OnMoodsPsicodineMetabolized);
        SubscribeLocalEvent<WizardMoodsCurseComponent, ComponentShutdown>(OnMoodsCurseShutdown);
    }

    private void OnMoodsPsicodineMetabolized(Entity<WizardMoodsCurseComponent> ent,
        ref RailroadingReagentMetabolizedEvent args)
    {
        if (args.Reagent.Reagent.Prototype != "Psicodine")
            return;

        ent.Comp.PsicodineMetabolized += args.Reagent.Quantity;
        if (ent.Comp.PsicodineMetabolized >= MoodsPsicodineCureAmount)
            RemComp<WizardMoodsCurseComponent>(ent);
    }

    private void OnMoodsCurseSpell(MoodsCurseSpellEvent args)
    {
        if (args.Handled || !PassesSpellPrerequisites(args.Action, args.Performer))
            return;

        args.Handled = TryApplyMoodsCurse(args.Target);
    }

    public bool TryApplyMoodsCurse(EntityUid target)
    {
        if (!_wizardMobState.IsAlive(target) || !HasComp<ActorComponent>(target) ||
            HasComp<WizardMoodsCurseComponent>(target))
            return false;

        var thaven = TryComp<ThavenMoodsComponent>(target, out var moods);
        moods ??= EnsureComp<ThavenMoodsComponent>(target);
        var previous = _wizardThavenMoods.GetActiveMoods((target, moods), includeShared: false).ToList();

        if (!thaven)
            _wizardThavenMoods.PrepareForWizardCurse((target, moods));

        _wizardThavenMoods.SetMoods((target, moods), [], notify: false);
        for (var i = 0; i < MoodsCurseCount; i++)
        {
            if (_wizardThavenMoods.TryAddRandomMood((target, moods), notify: false))
                continue;

            if (thaven)
                _wizardThavenMoods.SetMoods((target, moods), previous);
            else
                RemComp<ThavenMoodsComponent>(target);
            return false;
        }

        _wizardThavenMoods.NotifyMoodChange((target, moods));
        if (!thaven)
        {
            var curse = EnsureComp<WizardMoodsCurseComponent>(target);
            _wizardActions.AddAction(target, ref curse.IndicatorAction, MoodsCurseIndicator);
        }

        return true;
    }

    private void OnMoodsCurseShutdown(Entity<WizardMoodsCurseComponent> ent, ref ComponentShutdown args)
    {
        if (TerminatingOrDeleted(ent))
            return;

        if (ent.Comp.IndicatorAction is { } action)
            _wizardActions.RemoveAction(action);
        RemComp<ThavenMoodsComponent>(ent);
    }
}
