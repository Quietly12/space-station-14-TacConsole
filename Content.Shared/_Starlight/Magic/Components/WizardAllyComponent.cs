using Robust.Shared.GameStates;

namespace Content.Shared._Starlight.Magic.Components;

/// <summary>
/// Marks a wizard or apprentice for the private ally status icon.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WizardAllyComponent : Component
{
    public override bool SessionSpecific => true;
}
