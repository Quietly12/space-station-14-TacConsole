namespace Content.Shared._Starlight.Magic.Components;

[RegisterComponent]
public sealed partial class WizardMageHandComponent : Component
{
    [ViewVariables] public EntityUid Hand;
    [ViewVariables] public EntityUid Console;
    [ViewVariables] public EntityUid Action;
    [ViewVariables] public TimeSpan ExpiresAt;
    [ViewVariables] public TimeSpan ControlGraceEndsAt;
    [ViewVariables] public bool HasControlled;
}

[RegisterComponent]
public sealed partial class WizardMageHandBodyComponent : Component
{
    [ViewVariables] public EntityUid Caster;
}
