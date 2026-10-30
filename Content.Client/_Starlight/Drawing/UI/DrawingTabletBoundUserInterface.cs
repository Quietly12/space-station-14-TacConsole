using Content.Shared._Starlight.Drawing;
using Robust.Client.UserInterface;
using Robust.Shared.Timing;
using CancellationTokenSource = System.Threading.CancellationTokenSource;

namespace Content.Client._Starlight.Drawing.UI;

public sealed class DrawingTabletBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    // Seconds to wait for a response before requesting authoritative state.
    private const int EditResponseTimeoutSeconds = 3;
    // Synchronization attempts use the fast interval, then retry slowly if the connection is lost.
    private const int FastSyncAttemptCount = 4;
    private const int FastSyncRetrySeconds = 1;
    private const int SlowSyncRetrySeconds = 5;

    private DrawingTabletWindow? _window;
    private readonly Queue<DrawingStroke> _pending = [];
    private DrawingTabletAction? _action;
    private bool _sending;
    private int _revision;
    private DrawingTabletState? _document;
    private bool _synchronizing;
    private int _snapshotAttempts;
    private Guid _editRequest;
    private Guid _snapshotRequest;
    private CancellationTokenSource? _snapshotRetry;
    private CancellationTokenSource? _editTimeout;

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<DrawingTabletWindow>();
        _window.RestoreGeometry();
        _window.StrokeCompleted += stroke => { _pending.Enqueue(stroke); SendNext(); };
        _window.ActionRequested += action => { _action = action; SendNext(); };
        _window.ShowNotice(Loc.GetString("drawing-tablet-loading"));
        Synchronize();
    }

    private void Synchronize()
    {
        if (_synchronizing)
            return;
        _synchronizing = true;
        if (_document != null)
            _window?.ShowNotice(Loc.GetString("drawing-tablet-reconnecting"));
        // Preserve the unfinished stroke before replacing the canvas.
        _window?.FinishStroke();
        _window?.SetPending(_pending.Count, true);
        _snapshotAttempts = 0;
        _snapshotRequest = Guid.NewGuid();
        _snapshotRetry?.Cancel();
        _snapshotRetry?.Dispose();
        _snapshotRetry = new CancellationTokenSource();
        _editTimeout?.Cancel();
        RequestSnapshot();
    }

    private void RequestSnapshot()
    {
        _snapshotAttempts++;
        SendMessage(new DrawingTabletRequestMessage(resync: _document != null, requestId: _snapshotRequest));
        // If the reply is lost, requests the drawing again.
        Timer.Spawn(TimeSpan.FromSeconds(_snapshotAttempts < FastSyncAttemptCount ? FastSyncRetrySeconds : SlowSyncRetrySeconds), () =>
        {
            if (!_synchronizing || !IsOpened)
                return;
            if (_snapshotAttempts >= FastSyncAttemptCount)
                _window?.ShowNotice(Loc.GetString("drawing-tablet-reconnecting"));
            RequestSnapshot();
        }, _snapshotRetry!.Token);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _snapshotRetry?.Cancel();
            _snapshotRetry?.Dispose();
            _snapshotRetry = null;
            _editTimeout?.Cancel();
            _editTimeout?.Dispose();
            _editTimeout = null;
        }
        base.Dispose(disposing);
    }

    private void SendNext()
    {
        _window?.SetPending(_pending.Count, _action != null || _synchronizing);
        if (_sending || _synchronizing)
            return;
        if (_pending.Count == 0 && _action == null)
            return;
        _sending = true;
        _editRequest = Guid.NewGuid();
        _editTimeout?.Cancel();
        _editTimeout?.Dispose();
        _editTimeout = new CancellationTokenSource();
        Timer.Spawn(TimeSpan.FromSeconds(EditResponseTimeoutSeconds), () =>
        {
            if (_sending && IsOpened)
                Synchronize();
        }, _editTimeout.Token);
        if (_pending.TryPeek(out var stroke))
            SendMessage(new DrawingTabletStrokeMessage(stroke, _revision, _editRequest));
        else if (_action is { } action)
            SendMessage(new DrawingTabletActionMessage(action, _revision, _editRequest));
    }

    private void ApplyDocument(DrawingTabletDocumentMessage message)
    {
        // Replies from earlier requests are ignored so they cannot replace newer drawing state.
        if (message.RequestId != Guid.Empty && message.RequestId != _snapshotRequest && message.RequestId != _editRequest)
            return;
        var drawing = message.Document;
        if (drawing.Revision < _revision)
            return;
        var discard = message.RejectPending || drawing.Overheated;
        if (!discard && _pending.Count > 0 && _sending)
        {
            if (drawing.LastAcceptedStroke == _editRequest)
                _pending.Dequeue();
            else if (drawing.Revision != _revision)
                discard = true;
        }
        if (_synchronizing)
            _window?.ShowNotice(string.Empty);
        if (discard)
        {
            if (!message.RejectPending && !drawing.Overheated && _pending.Count > 0)
                _window?.ShowNotice(Loc.GetString("drawing-tablet-recovery-changed"));
            _pending.Clear();
        }
        _action = null;
        _sending = false;
        _editRequest = Guid.Empty;
        _snapshotRequest = Guid.Empty;
        _editTimeout?.Cancel();
        _snapshotRetry?.Cancel();
        _synchronizing = false;
        _revision = drawing.Revision;
        _document = drawing;
        _window?.SetDocument(drawing);
        foreach (var stroke in _pending)
            _window?.RestorePendingStroke(stroke);
        SendNext();
    }

    private void ApplyHistory(DrawingTabletHistoryMessage history)
    {
        if (_synchronizing || history.RequestId != _editRequest)
            return;
        var changed = history.Revision == history.PreviousRevision + 1;
        if (_document == null || history.PreviousRevision != _revision || _action != history.Action
            || _pending.Count != 0 || (history.Revision != _revision && !changed)
            || history.Action is not (DrawingTabletAction.Undo or DrawingTabletAction.Redo)
            || changed && (history.Action == DrawingTabletAction.Undo ? _document.Strokes.Count == 0 : history.RestoredStroke == null))
        {
            Synchronize();
            return;
        }
        if (changed)
        {
            if (history.Action == DrawingTabletAction.Undo)
                _document.Strokes.RemoveAt(_document.Strokes.Count - 1);
            else
                _document.Strokes.Add(history.RestoredStroke!);
        }
        ApplyDocument(new DrawingTabletDocumentMessage(new DrawingTabletState(_document.Strokes, history.Revision, history.CanRedo,
            _document.MaxStrokes, _document.MaxPoints, _document.Overheated, _document.LastAcceptedStroke, _document.MaxRenderWork), history.RequestId));
    }

    protected override void ReceiveMessage(BoundUserInterfaceMessage message)
    {
        base.ReceiveMessage(message);
        switch (message)
        {
            case DrawingTabletDocumentMessage document:
                ApplyDocument(document);
                break;
            case DrawingTabletHistoryMessage history:
                ApplyHistory(history);
                break;
            case DrawingTabletAckMessage ack:
                if (_synchronizing || !_sending || ack.RequestId != _editRequest)
                    break;
                var strokePending = _pending.Count > 0;
                if (_document == null || ack.Revision != _revision + (strokePending ? 1 : 0))
                {
                    Synchronize();
                    break;
                }
                _revision = ack.Revision;
                _sending = false;
                _editRequest = Guid.Empty;
                _editTimeout?.Cancel();
                if (strokePending)
                    _document.Strokes.Add(_pending.Dequeue());
                else
                    _action = null;
                SendNext();
                break;
            case DrawingTabletNoticeMessage notice:
                _window?.ShowNotice(notice.Text);
                break;
        }
    }
}
