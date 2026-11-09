// ReSharper disable CheckNamespace

using System.Linq;
using Content.Shared._Starlight.Shuttles.BUIStates;
using Content.Shared.Shuttles.BUIStates;
using Content.Shared.Shuttles.Components;
using Robust.Shared.Player;

namespace Content.Server.Shuttles.Systems;

public sealed partial class RadarConsoleSystem
{
    // Prevent repeated opening from forcing a full docking snapshot.
    private readonly Dictionary<(EntityUid Scanner, EntityUid Viewer), (TimeSpan NextUpdate, bool Pending)> _radarRequests = new();
    private readonly List<(EntityUid Scanner, EntityUid Viewer)> _readyRadarRequests = new();

    /// <summary>
    /// Previous UI had 250ms refresh rate that prevented abuse. Replicated behavior.
    /// The first request is immediate; repeats within 250 ms share one deferred refresh.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnRequestState(EntityUid uid, RadarConsoleComponent component, RadarConsoleRequestStateMessage args)
    {
        var key = (uid, args.Actor);
        if (_radarRequests.TryGetValue(key, out var request) && request.NextUpdate > _timing.CurTime)
        {
            // Keep one refresh pending so reopening the window does not leave it without docking data.
            _radarRequests[key] = (request.NextUpdate, true);
            return;
        }

        _radarRequests[key] = (_timing.CurTime + _activeUpdateInterval, false);
        SendInitialRadarState(uid, component);
    }

    /// <summary>
    /// Send deferred updates after the 250 ms cooldown.
    /// Discard requests if the viewer disconnected or closed the window, and clear expired tracking entries.
    /// </summary>
    private void UpdateRadarRequests()
    {
        _readyRadarRequests.Clear();
        foreach (var (key, request) in _radarRequests)
        {
            if (request.NextUpdate <= _timing.CurTime)
                _readyRadarRequests.Add(key);
        }

        foreach (var key in _readyRadarRequests)
        {
            _radarRequests.Remove(key, out var request);
            if (!request.Pending ||
                !HasComp<ActorComponent>(key.Viewer) ||
                !TryComp<RadarConsoleComponent>(key.Scanner, out var component) ||
                !_uiSystem.IsUiOpen(key.Scanner, RadarConsoleUiKey.Key, key.Viewer))
                continue;

            _radarRequests[key] = (_timing.CurTime + _activeUpdateInterval, false);
            SendInitialRadarState(key.Scanner, component);
        }
    }

    private void SendInitialRadarState(EntityUid uid, RadarConsoleComponent component)
    {
        // Send a full docking snapshot because the newly opened window may not have it.
        _radarDockingStateSystem.RemoveConsoleState(uid);
        component.LastInterfaceUpdateTime = _timing.CurTime - _activeUpdateInterval - TimeSpan.FromTicks(1);
        UpdateState(uid, component);
    }

    private bool HasRadarViewers(EntityUid uid) =>
        _uiSystem.GetActors(uid, RadarConsoleUiKey.Key).Any(actor => HasComp<ActorComponent>(actor));

    private void SendRadarState(EntityUid uid, NavInterfaceState state, DockingPortStates? docks)
    {
        _uiSystem.ServerSendUiMessage(uid, RadarConsoleUiKey.Key,
            new RadarConsoleUpdateMessage(new NavBoundUserInterfaceState(state, docks)));
    }
}
