using Robust.Shared.GameStates;

namespace Content.Shared._Starlight.Magic.Components;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(raiseAfterAutoHandleState: true)]
public sealed partial class WizardTrashGolemVisualComponent : Component
{
    [DataField, AutoNetworkedField]
    public List<string> DisplayItems = new();
}
