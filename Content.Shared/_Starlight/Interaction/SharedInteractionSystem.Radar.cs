// ReSharper disable CheckNamespace
using Content.Shared._Starlight.Shuttles.BUIStates;
using Content.Shared.Ghost;
using Content.Shared.Shuttles.BUIStates;
using Content.Shared.Shuttles.Components;

namespace Content.Shared.Interaction;

public abstract partial class SharedInteractionSystem
{
    // Allows ghost mass scanners to update through message-based delivery
    private bool IsObserverRadarSync(BoundUserInterfaceMessageAttempt ev) =>
        ev.Actor == ev.Target &&
        HasComp<GhostComponent>(ev.Actor) &&
        HasComp<RadarConsoleComponent>(ev.Target) &&
        ev.UiKey is RadarConsoleUiKey.Key &&
        ev.Message is RadarConsoleUpdateMessage or RadarConsoleRequestStateMessage or DockingPortStatesAcknowledgedMessage;
}
