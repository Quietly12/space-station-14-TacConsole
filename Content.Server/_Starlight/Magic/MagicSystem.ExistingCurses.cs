using Content.Shared._Funkystation.WashingMachine;
using Content.Shared._Starlight.Magic.Components;
using Content.Shared.Cluwne;
using Content.Shared.Slippery;
using Content.Shared.StepTrigger.Components;

namespace Content.Server.Magic;

public sealed partial class MagicSystem
{
    private const string CluwneCurseIndicator = "ActionWizardCluwneCurseIndicator";
    private const string SlipperyCurseIndicator = "ActionWizardSlipperyCurseIndicator";

    private void InitializeExistingCurses()
    {
        SubscribeLocalEvent<WizardCluwneCurseComponent, ComponentStartup>(OnWizardCluwneCurseStartup);
        SubscribeLocalEvent<WizardCluwneCurseComponent, ComponentShutdown>(OnWizardCluwneCurseShutdown);
        SubscribeLocalEvent<CluwneComponent, ComponentShutdown>(OnWizardCluwneRemoved);
        SubscribeLocalEvent<WizardSlipperyCurseComponent, ComponentStartup>(OnWizardSlipperyCurseStartup);
        SubscribeLocalEvent<WizardSlipperyCurseComponent, WashingMachineWashedEvent>(OnWizardSlipperyWashed);
        SubscribeLocalEvent<WizardSlipperyCurseComponent, ComponentShutdown>(OnWizardSlipperyCurseShutdown);
    }

    private void OnWizardCluwneCurseStartup(Entity<WizardCluwneCurseComponent> ent, ref ComponentStartup args)
    {
        _wizardActions.AddAction(ent, ref ent.Comp.IndicatorAction, CluwneCurseIndicator);
    }

    private void OnWizardCluwneRemoved(Entity<CluwneComponent> ent, ref ComponentShutdown args)
    {
        RemComp<WizardCluwneCurseComponent>(ent);
    }

    private void OnWizardCluwneCurseShutdown(Entity<WizardCluwneCurseComponent> ent, ref ComponentShutdown args)
    {
        if (ent.Comp.IndicatorAction is { } action)
            _wizardActions.RemoveAction(action);
    }

    private void OnWizardSlipperyCurseStartup(Entity<WizardSlipperyCurseComponent> ent, ref ComponentStartup args)
    {
        _wizardActions.AddAction(ent, ref ent.Comp.IndicatorAction, SlipperyCurseIndicator);
    }

    private void OnWizardSlipperyWashed(Entity<WizardSlipperyCurseComponent> ent, ref WashingMachineWashedEvent args)
    {
        if (!args.Items.Contains(ent.Owner))
            return;

        RemComp<WizardSlipperyCurseComponent>(ent);
        RemComp<SlipperyComponent>(ent);
        RemComp<StepTriggerComponent>(ent);
    }

    private void OnWizardSlipperyCurseShutdown(Entity<WizardSlipperyCurseComponent> ent, ref ComponentShutdown args)
    {
        if (ent.Comp.IndicatorAction is { } action)
            _wizardActions.RemoveAction(action);
    }
}
