using Robust.Shared.GameStates;

namespace Content.Shared._Starlight.Magic.Components;

/// <summary>
/// Selects the apprentice status icon in place of the ordinary wizard icon.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WizardApprenticeIconComponent : Component
{
    public override bool SessionSpecific => true;
}
