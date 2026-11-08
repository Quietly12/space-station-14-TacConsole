using Content.Shared._Starlight.Shuttles.BUIStates;
using Robust.Shared.Serialization;

namespace Content.Shared.Shuttles.BUIStates;

/// <summary>
/// Wrapper around <see cref="NavInterfaceState"/>
/// </summary>
[Serializable, NetSerializable]
public sealed class NavBoundUserInterfaceState : BoundUserInterfaceState
{
    public NavInterfaceState State;
    public DockingPortStates? DockingPortStates; // Starlight: null means unchanged

    public NavBoundUserInterfaceState(NavInterfaceState state, DockingPortStates? dockingPortStates) // Starlight: +dockingPortStates
    {
        State = state;
        DockingPortStates = dockingPortStates; // Starlight
    }
}

/// <summary>Live radar data sent only to players viewing this interface.</summary>
[Serializable, NetSerializable]
public sealed class RadarConsoleUpdateMessage(NavBoundUserInterfaceState state) : BoundUserInterfaceMessage
{
    public NavBoundUserInterfaceState State = state;
}

/// <summary>Requests the initial snapshot after the client radar window is ready.</summary>
[Serializable, NetSerializable]
public sealed class RadarConsoleRequestStateMessage : BoundUserInterfaceMessage;
