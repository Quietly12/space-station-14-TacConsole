using Robust.Shared.GameStates;

namespace Content.Shared._Starlight.Magic.Components;

/// <summary>
/// Marks a dominated body for members of its wizard's domination group.
/// The wizard mind ID stays server-side; clients receive only the marker's presence.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WizardDominationIconComponent : Component
{
    public EntityUid WizardMind;
    public EntityUid? IndicatorAction;

    public override bool SessionSpecific => true;
}
