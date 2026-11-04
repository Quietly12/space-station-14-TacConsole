using Robust.Shared.GameStates;

namespace Content.Shared._Starlight.Magic.Components;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class WizardMinorIllusionComponent : Component
{
    [DataField, AutoNetworkedField] public string CreaturePrototype = "MobMouse";
    [DataField, AutoNetworkedField] public Color ShimmerColor = Color.White;
    [ViewVariables] public TimeSpan ExpiresAt;
}

[RegisterComponent]
public sealed partial class WizardDeadListeningComponent : Component
{
    [ViewVariables] public TimeSpan ExpiresAt;
    [ViewVariables] public EntityUid Action;
}
