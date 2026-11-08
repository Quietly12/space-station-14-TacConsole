using Content.Shared.Shuttles.BUIStates;
using Robust.Shared.Serialization;

namespace Content.Shared._Starlight.Shuttles.BUIStates;

[Serializable, NetSerializable]
public sealed class RadarConsoleUpdateMessage(NavBoundUserInterfaceState state) : BoundUserInterfaceMessage
{
    public NavBoundUserInterfaceState State = state;
}

[Serializable, NetSerializable]
public sealed class RadarConsoleRequestStateMessage : BoundUserInterfaceMessage;
