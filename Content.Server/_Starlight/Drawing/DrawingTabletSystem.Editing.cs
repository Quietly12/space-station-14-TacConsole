using System.Linq;
using Content.Shared._Starlight.Drawing;
using Content.Shared.Database;
using Robust.Server.GameObjects;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server._Starlight.Drawing;

/// <summary> Handles drawing edits, synchronization, access checks, and overheating.</summary>
public sealed partial class DrawingTabletSystem : EntitySystem
{
    // Wait this long before sending another requested drawing snapshot.
    private const int MinimumResyncIntervalSeconds = 1;
    private const int OverheatNoticeIntervalSeconds = 1;

    private sealed class PlayerBudget
    {
        public float Tokens = DrawingTabletSettings.RequestBudgetCapacity;
        public TimeSpan Updated;
        public TimeSpan OverheatedUntil;
    }
    private readonly Dictionary<NetUserId, PlayerBudget> _budgets = [];
    private readonly HashSet<EntityUid> _hotTablets = [];

    private PlayerBudget? GetBudget(EntityUid actor)
    {
        if (!TryComp<ActorComponent>(actor, out var player))
            return null;
        var id = player.PlayerSession.UserId;
        if (!_budgets.TryGetValue(id, out var budget))
            _budgets[id] = budget = new PlayerBudget { Updated = _timing.CurTime };
        return budget;
    }

    private bool IsOverheated(DrawingTabletComponent comp, EntityUid actor)
        => comp.OverheatedUntil > _timing.CurTime || GetBudget(actor)?.OverheatedUntil > _timing.CurTime;

    private void HeatNotice(EntityUid uid, DrawingTabletComponent comp, EntityUid actor)
    {
        if (_timing.CurTime < comp.NextHeatNotice)
            return;
        comp.NextHeatNotice = _timing.CurTime + TimeSpan.FromSeconds(OverheatNoticeIntervalSeconds);
        Notice(uid, actor, "drawing-tablet-overheated");
        _popup.PopupEntity(Loc.GetString("drawing-tablet-overheated"), uid, actor);
    }

    private void OnDocumentRequested(EntityUid uid, DrawingTabletComponent comp, DrawingTabletRequestMessage args)
    {
        if (comp.Editor == args.Actor && (!comp.InitialSnapshotSent
            || _timing.CurTime >= comp.NextSnapshot))
        {
            comp.InitialSnapshotSent = true;
            Snapshot(uid, comp, args.RequestId);
        }
    }

    private void OnOpened(EntityUid uid, DrawingTabletComponent comp, BoundUIOpenedEvent args)
    {
        comp.Editor = args.Actor;
        comp.InitialSnapshotSent = false;
        if (GetBudget(args.Actor) is { } budget && budget.OverheatedUntil > comp.OverheatedUntil)
        {
            comp.OverheatedUntil = budget.OverheatedUntil;
            _hotTablets.Add(uid);
        }
        Recount(comp);
        if (IsOverheated(comp, args.Actor))
            HeatNotice(uid, comp, args.Actor);
        _admin.Add(LogType.Action, LogImpact.Low, $"{ToPrettyString(args.Actor):actor} opened drawing tablet {ToPrettyString(uid):tablet}");
    }

    private void OnClosed(EntityUid uid, DrawingTabletComponent comp, BoundUIClosedEvent args)
    {
        if (comp.Editor == args.Actor)
            comp.Editor = null;
    }

    /// <summary> Sends the current drawing to its editor; rejectPending discards unconfirmed client strokes.</summary>
    private void Snapshot(EntityUid uid, DrawingTabletComponent comp, Guid requestId = default, bool rejectPending = false)
    {
        comp.InitialSnapshotSent = true;
        comp.NextSnapshot = _timing.CurTime + TimeSpan.FromSeconds(MinimumResyncIntervalSeconds);
        if (comp.Editor is { } editor)
            _ui.ServerSendUiMessage(uid, DrawingTabletUiKey.Key,
                new DrawingTabletDocumentMessage(new DrawingTabletState(new(comp.Strokes), comp.Revision, comp.Redo.Count > 0, comp.MaxStrokes, comp.MaxPoints, IsOverheated(comp, editor), comp.LastAcceptedStroke, comp.MaxRenderWork), requestId, rejectPending), editor);
    }

    private void Notice(EntityUid uid, EntityUid actor, string key)
        => _ui.ServerSendUiMessage(uid, DrawingTabletUiKey.Key, new DrawingTabletNoticeMessage(Loc.GetString(key)), actor);

    /// <summary> Checks a tablet action, spends its request budget, and triggers overheat or resync when needed.</summary>
    private bool CanEdit(EntityUid uid, DrawingTabletComponent comp, EntityUid actor, int revision, Guid requestId)
    {
        if (comp.Editor != actor || !_ui.IsUiOpen(uid, DrawingTabletUiKey.Key, actor))
            return false;
        var now = _timing.CurTime;
        var budget = GetBudget(actor);
        if (IsOverheated(comp, actor))
        {
            HeatNotice(uid, comp, actor);
            return false;
        }
        var updated = budget?.Updated ?? comp.BudgetUpdated;
        var tokens = Math.Min(DrawingTabletSettings.RequestBudgetCapacity, (budget?.Tokens ?? comp.EditTokens) + (float) (now - updated).TotalSeconds * DrawingTabletSettings.RequestsRestoredPerSecond);
        if (budget != null)
        {
            budget.Updated = now;
            budget.Tokens = Math.Max(0, tokens - 1);
        }
        else
        {
            comp.BudgetUpdated = now;
            comp.EditTokens = Math.Max(0, tokens - 1);
        }
        if (tokens < 1)
        {
            comp.OverheatedUntil = now + TimeSpan.FromSeconds(DrawingTabletSettings.OverheatDurationSeconds);
            if (budget != null)
                budget.OverheatedUntil = comp.OverheatedUntil;
            _hotTablets.Add(uid);
            // Lets admins investigate rapid drawing input, like an automated script trying to print an image too fast.
            // Also triggers if someone malicious steals the tablet and spams the undo button too quickly.
            _admin.Add(LogType.Action, LogImpact.Medium,
                $"{ToPrettyString(actor):actor} overheated drawing tablet {ToPrettyString(uid):tablet} by exceeding {DrawingTabletSettings.RequestsRestoredPerSecond} requests/second for too long; locked for {DrawingTabletSettings.OverheatDurationSeconds / 60f} minutes.");
            HeatNotice(uid, comp, actor);
            // Send the confirmed drawing once so the client removes strokes rejected by overheating.
            Snapshot(uid, comp, requestId, rejectPending: true);
            if (budget != null)
            {
                var tablets = EntityQueryEnumerator<DrawingTabletComponent>();
                while (tablets.MoveNext(out var otherUid, out var other))
                {
                    if (otherUid == uid || other.Editor is not { } editor || GetBudget(editor) != budget)
                        continue;
                    other.OverheatedUntil = comp.OverheatedUntil;
                    _hotTablets.Add(otherUid);
                    Snapshot(otherUid, other, rejectPending: true);
                }
            }
            return false;
        }
        if (revision != comp.Revision)
        {
            if (now >= comp.NextSnapshot)
            {
                Notice(uid, actor, "drawing-tablet-resynced");
                Snapshot(uid, comp, requestId, rejectPending: true);
            }
            return false;
        }
        return true;
    }

    private void OnStroke(EntityUid uid, DrawingTabletComponent comp, DrawingTabletStrokeMessage args)
    {
        if (!CanEdit(uid, comp, args.Actor, args.Revision, args.RequestId))
            return;
        if (!DrawingRaster.Validate(args.Stroke, out var work))
        {
            Notice(uid, args.Actor, "drawing-tablet-invalid-stroke");
            Snapshot(uid, comp, args.RequestId, rejectPending: true);
            return;
        }
        if (comp.Strokes.Count >= comp.MaxStrokes || comp.TotalPoints + args.Stroke.Points.Count > comp.MaxPoints
            || (long) comp.RenderWork + work > comp.MaxRenderWork)
        {
            var full = DrawingTabletComplexity.Calculate(comp.Strokes.Count, comp.TotalPoints, comp.RenderWork,
                comp.MaxStrokes, comp.MaxPoints, comp.MaxRenderWork) >= comp.MaxPoints;
            Notice(uid, args.Actor, full ? "drawing-tablet-full" : "drawing-tablet-stroke-too-large");
            Snapshot(uid, comp, args.RequestId, rejectPending: true);
            return;
        }
        comp.Strokes.Add(args.Stroke);
        comp.LastAcceptedStroke = args.RequestId;
        comp.TotalPoints += args.Stroke.Points.Count;
        comp.RenderWork += work;
        comp.Redo.Clear();
        Changed(comp);
        _ui.ServerSendUiMessage(uid, DrawingTabletUiKey.Key, new DrawingTabletAckMessage(comp.Revision, args.RequestId), args.Actor);
    }

    private static void Changed(DrawingTabletComponent comp)
    {
        comp.Revision++;
        comp.PrintedImage = null;
        comp.PrintedThumbnail = null;
    }

    private void OnTabletAction(EntityUid uid, DrawingTabletComponent comp, DrawingTabletActionMessage args)
    {
        if (!CanEdit(uid, comp, args.Actor, args.Revision, args.RequestId))
            return;
        var previousRevision = comp.Revision;
        DrawingStroke? restoredStroke = null;
        switch (args.Action)
        {
            case DrawingTabletAction.Print:
                BeginPrint(uid, comp, args.Actor, args.RequestId);
                return;
            case DrawingTabletAction.Undo when comp.Strokes.Count > 0:
                comp.Redo.Add(comp.Strokes[^1]);
                comp.Strokes.RemoveAt(comp.Strokes.Count - 1);
                Changed(comp);
                break;
            case DrawingTabletAction.Redo when comp.Redo.Count > 0:
                restoredStroke = comp.Redo[^1];
                comp.Strokes.Add(restoredStroke);
                comp.Redo.RemoveAt(comp.Redo.Count - 1);
                Changed(comp);
                break;
            case DrawingTabletAction.Clear:
                if (!HasClearAccess(args.Actor, comp))
                {
                    Notice(uid, args.Actor, "drawing-tablet-clear-denied");
                    _ui.ServerSendUiMessage(uid, DrawingTabletUiKey.Key, new DrawingTabletAckMessage(comp.Revision, args.RequestId), args.Actor);
                    return;
                }
                comp.Strokes.Clear();
                comp.Redo.Clear();
                Changed(comp);
                break;
        }
        Recount(comp);
        if (args.Action is DrawingTabletAction.Undo or DrawingTabletAction.Redo)
        {
            _ui.ServerSendUiMessage(uid, DrawingTabletUiKey.Key,
                new DrawingTabletHistoryMessage(previousRevision, comp.Revision, args.Action, restoredStroke, comp.Redo.Count > 0, args.RequestId), args.Actor);
            return;
        }
        Snapshot(uid, comp, args.RequestId);
    }

    private bool HasClearAccess(EntityUid actor, DrawingTabletComponent comp)
    {
        if (!comp.RequiresClearAccess)
            return true;

        //Clearing requires an ID card with the tablet's configured access.
        foreach (var item in _access.FindPotentialAccessItems(actor))
        {
            if (_idCards.TryGetIdCard(item, out var card)
                && _access.FindAccessTags(card.Owner).Contains(comp.ClearAccess))
                return true;
        }
        return false;
    }

    private static void Recount(DrawingTabletComponent comp)
    {
        comp.TotalPoints = 0;
        comp.RenderWork = 0;
        foreach (var stroke in comp.Strokes)
        {
            if (!DrawingRaster.Validate(stroke, out var work))
                continue;
            comp.TotalPoints += stroke.Points.Count;
            comp.RenderWork += work;
        }
    }

    private void UpdateOverheatedTablets()
    {
        foreach (var uid in _hotTablets.ToArray())
        {
            if (!TryComp<DrawingTabletComponent>(uid, out var tablet))
            {
                _hotTablets.Remove(uid);
                continue;
            }
            if (_timing.CurTime < tablet.OverheatedUntil)
                continue;
            _hotTablets.Remove(uid);
            tablet.OverheatedUntil = TimeSpan.Zero;
            Snapshot(uid, tablet);
        }
    }
}
