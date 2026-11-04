using Robust.Shared.GameStates;

namespace Content.Shared._Starlight.Magic.Components;

[RegisterComponent]
public sealed partial class WizardStopTimeAffectedComponent : Component
{
    public bool AddedStasis;
}

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WizardStopTimeAwareComponent : Component
{
    [AutoNetworkedField]
    public bool Active = true;
}
