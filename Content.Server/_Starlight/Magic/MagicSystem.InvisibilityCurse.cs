using Content.Shared._Starlight.Magic.Components;
using Content.Shared._Starlight.Magic.Events;
using Content.Shared.Damage.Systems;
using Content.Shared.Interaction.Events;
using Content.Shared.Stealth;
using Content.Shared.Stealth.Components;

namespace Content.Server.Magic;

public sealed partial class MagicSystem
{
    private const string InvisibilityCurseIndicator = "ActionWizardInvisibilityCurseIndicator";
    private const float InvisibilityCurseVisibility = -1f;

    [Dependency] private SharedStealthSystem _invisibilityStealth = default!;

    private void InitializeInvisibilityCurse()
    {
        SubscribeLocalEvent<WizardInvisibilityCurseSpellEvent>(OnInvisibilityCurseSpell);
        SubscribeLocalEvent<WizardInvisibilityCurseComponent, DamageChangedEvent>(OnInvisibilityCurseDamage);
        SubscribeLocalEvent<WizardInvisibilityCurseComponent, AttackAttemptEvent>(OnInvisibilityCurseAttack);
        SubscribeLocalEvent<WizardInvisibilityCurseComponent, ComponentShutdown>(OnInvisibilityCurseShutdown);
    }

    private void OnInvisibilityCurseSpell(WizardInvisibilityCurseSpellEvent args)
    {
        if (args.Handled || !PassesSpellPrerequisites(args.Action, args.Performer))
            return;

        args.Handled = TryApplyInvisibilityCurse(args.Target);
    }

    public bool TryApplyInvisibilityCurse(EntityUid target)
    {
        if (!_wizardMobState.IsAlive(target) || HasComp<WizardInvisibilityCurseComponent>(target))
            return false;

        var curse = EnsureComp<WizardInvisibilityCurseComponent>(target);
        curse.HadStealth = TryComp<StealthComponent>(target, out var stealth);
        if (stealth != null)
        {
            curse.PreviousStealthEnabled = stealth.Enabled;
            curse.PreviousVisibility = _invisibilityStealth.GetVisibility(target, stealth);
        }
        else
        {
            stealth = EnsureComp<StealthComponent>(target);
        }

        _invisibilityStealth.SetEnabled(target, true, stealth);
        _invisibilityStealth.SetVisibility(target, InvisibilityCurseVisibility, stealth);
        _wizardActions.AddAction(target, ref curse.IndicatorAction, InvisibilityCurseIndicator);
        return true;
    }

    private void OnInvisibilityCurseDamage(Entity<WizardInvisibilityCurseComponent> ent, ref DamageChangedEvent args)
    {
        if (args.DamageIncreased)
            RemComp<WizardInvisibilityCurseComponent>(ent);
    }

    private void OnInvisibilityCurseAttack(Entity<WizardInvisibilityCurseComponent> ent, ref AttackAttemptEvent args)
    {
        // Ranged attacks also call CanAttack, with no target supplied.
        if (!args.Disarm && args.Target != ent.Owner)
            RemComp<WizardInvisibilityCurseComponent>(ent);
    }

    private void OnInvisibilityCurseShutdown(Entity<WizardInvisibilityCurseComponent> ent, ref ComponentShutdown args)
    {
        if (ent.Comp.IndicatorAction is { } action)
            _wizardActions.RemoveAction(action);

        if (TerminatingOrDeleted(ent.Owner) || !TryComp<StealthComponent>(ent.Owner, out var stealth))
            return;

        if (!ent.Comp.HadStealth)
        {
            RemComp<StealthComponent>(ent);
            return;
        }

        _invisibilityStealth.SetEnabled(ent, ent.Comp.PreviousStealthEnabled, stealth);
        _invisibilityStealth.SetVisibility(ent, ent.Comp.PreviousVisibility, stealth);
    }
}
