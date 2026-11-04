using Robust.Shared.Prototypes;

namespace Content.Shared._Starlight.Magic.Components;

[RegisterComponent]
public sealed partial class WizardInvisibilityCurseComponent : Component
{
    public EntityUid? IndicatorAction;
    public bool HadStealth;
    public bool PreviousStealthEnabled;
    public float PreviousVisibility;
}

[RegisterComponent]
public sealed partial class WizardDominationComponent : Component
{
    public EntityUid WizardMind;
}

[RegisterComponent]
public sealed partial class WizardDominationRoleComponent : Component;

[RegisterComponent]
public sealed partial class WizardApprenticeRoleComponent : Component
{
    public EntityUid? WizardMind;
    public string WizardName = string.Empty;
}

[RegisterComponent]
public sealed partial class WizardApprenticeshipContractComponent : Component
{
    public EntityUid? Wizard;
    public EntityUid? WizardMind;
    public string? WizardName;
}

[RegisterComponent]
public sealed partial class WizardRandomCurseWandComponent : Component;

[RegisterComponent]
public sealed partial class WizardRandomCurseProjectileComponent : Component
{
    public EntityUid? Caster;
}
