using Robust.Shared.GameStates;

namespace Content.Shared._Starlight.Magic.Mime;

[RegisterComponent, NetworkedComponent]
public sealed partial class MimeMagicActionComponent : Component;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class MimeFingerGunActionComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntityUid? Gun;
}

[RegisterComponent, NetworkedComponent]
public sealed partial class MimeFingerGunItemComponent : Component
{
    public EntityUid? Action;
    public EntityUid? Holder;
}

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class MimeLassoActionComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntityUid? Lasso;
}

[RegisterComponent, NetworkedComponent]
public sealed partial class MimeLassoItemComponent : Component
{
    public EntityUid? Action;
    public EntityUid? Holder;
}

[RegisterComponent]
public sealed partial class ForbiddenMimeSpellbookComponent : Component;
