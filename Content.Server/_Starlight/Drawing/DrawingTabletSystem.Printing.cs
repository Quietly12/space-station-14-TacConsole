using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Content.Shared._Starlight.Drawing;
using Content.Shared.Database;
using Content.Shared.Examine;
using Content.Shared.Interaction;
using Content.Shared.Paper;
using Content.Shared.Stacks;

namespace Content.Server._Starlight.Drawing;

/// <summary>Handles paper loading, print jobs, and printed artwork report IDs.</summary>
public sealed partial class DrawingTabletSystem : EntitySystem
{
    // Admin number format. Results in AW-01, AW-02 - Ect.
    private const string ArtworkReportNumberFormat = "D2";
    private long _nextArtworkReportNumber;

    private sealed record RenderedPrint(byte[] Image, byte[] Thumbnail);
    private sealed record PrintJob(EntityUid Actor, string ArtistName, int Revision, int Strokes, Task<RenderedPrint> Task);
    private readonly Dictionary<EntityUid, PrintJob> _printing = [];

    private void OnPrintStartup(EntityUid uid, DrawingPrintComponent comp, ComponentStartup args)
    {
        comp.ReportId = "AW-" + (++_nextArtworkReportNumber).ToString(ArtworkReportNumberFormat, CultureInfo.InvariantCulture);
    }

    private void OnPrintExamine(EntityUid uid, DrawingPrintComponent comp, ExaminedEvent args)
        => args.PushText(Loc.GetString("drawing-print-report-id", ("id", comp.ReportId)), priority: -100);

    private void BeginPrint(EntityUid uid, DrawingTabletComponent comp, EntityUid actor, Guid requestId)
    {
        _ui.ServerSendUiMessage(uid, DrawingTabletUiKey.Key, new DrawingTabletAckMessage(comp.Revision, requestId), actor);
        if (comp.Paper <= 0)
        {
            Notice(uid, actor, "drawing-tablet-no-paper");
            return;
        }
        if (_printing.ContainsKey(uid) || _printing.Count >= DrawingTabletSettings.MaximumSimultaneousPrintJobs || _timing.CurTime < comp.NextPrint)
        {
            Notice(uid, actor, "drawing-tablet-printer-busy");
            return;
        }
        comp.Paper--;
        comp.NextPrint = _timing.CurTime + TimeSpan.FromSeconds(DrawingTabletSettings.PrintCooldownSeconds);
        // Snapshots the stroke list while sharing accepted stroke objects that remain unchanged.
        // The background worker renders those strokes without accessing entities or components.
        var strokes = comp.Strokes.ToArray();
        var task = comp.PrintedImage != null && comp.PrintedThumbnail != null
            ? Task.FromResult(new RenderedPrint(comp.PrintedImage, comp.PrintedThumbnail))
            : Task.Run(() =>
            {
                var pixels = DrawingRaster.Render(strokes);
                return new RenderedPrint(DrawingRaster.Encode(pixels), DrawingRaster.Thumbnail(pixels));
            });
        _printing.Add(uid, new PrintJob(actor, Name(actor), comp.Revision, strokes.Length, task));
        Notice(uid, actor, "drawing-tablet-printing");
    }

    private void FinishPrintJobs()
    {
        foreach (var (uid, job) in _printing.ToArray())
        {
            if (!job.Task.IsCompleted)
                continue;
            _printing.Remove(uid);
            if (!TryComp<DrawingTabletComponent>(uid, out var comp))
            {
                _ = job.Task.Exception;
                continue;
            }
            if (!job.Task.IsCompletedSuccessfully)
            {
                comp.Paper = Math.Min(comp.PaperCapacity, comp.Paper + 1);
                Log.Error($"Drawing tablet render failed: {job.Task.Exception}");
                continue;
            }
            //IsCompletedSuccessfully above guarantees this read cannot block the simulation thread.
#pragma warning disable RA0004
            var result = job.Task.Result;
#pragma warning restore RA0004
            if (comp.Revision == job.Revision)
            {
                comp.PrintedImage = result.Image;
                comp.PrintedThumbnail = result.Thumbnail;
            }
            var print = Spawn("DrawingTabletPaper", Transform(uid).Coordinates);
            _metadata.SetEntityDescription(print, Loc.GetString("drawing-print-description", ("artist", job.ArtistName)));
            _audio.PlayPvs(comp.PrintSound, uid);
            var image = Comp<DrawingPrintComponent>(print);
            image.ArtistName = job.ArtistName;
            image.Image = result.Image;
            image.Thumbnail = result.Thumbnail;
            Dirty(print, image);
            if (Exists(job.Actor))
                _hands.TryPickupAnyHand(job.Actor, print);
            var hash = Convert.ToHexString(SHA256.HashData(result.Image));
            _admin.Add(LogType.Action, LogImpact.Low,
                $"{ToPrettyString(job.Actor):actor} printed {ToPrettyString(print):print} from {ToPrettyString(uid):tablet}, revision {job.Revision}, {job.Strokes} strokes, image {hash}; consumed one sheet; Artwork #{image.ReportId[3..]} ({image.ReportId})");
            if (Exists(job.Actor) && _ui.IsUiOpen(uid, DrawingTabletUiKey.Key, job.Actor))
                Notice(uid, job.Actor, "drawing-tablet-printed");
        }
    }

    private void OnInsertPaper(EntityUid uid, DrawingTabletComponent comp, InteractUsingEvent args)
    {
        if (args.Handled || !TryComp<PaperComponent>(args.Used, out var paper)
            || paper.Content.Length != 0 || paper.StampedBy.Count != 0)
            return;
        var remaining = comp.PaperCapacity - comp.Paper;
        if (remaining <= 0)
        {
            args.Handled = true;
            return;
        }
        var count = 1;
        if (TryComp<StackComponent>(args.Used, out var stack))
        {
            if (stack.StackTypeId != "SheetPrinter")
                return;
            count = Math.Min(remaining, _stacks.GetCount((args.Used, stack)));
            if (!_stacks.TryUse((args.Used, stack), count))
                return;
        }
        else
            QueueDel(args.Used);
        comp.Paper += count;
        args.Handled = true;
        _popup.PopupEntity(Loc.GetString("drawing-tablet-paper-loaded", ("count", count)), uid, args.User);
    }

    private void OnExamine(EntityUid uid, DrawingTabletComponent comp, ExaminedEvent args)
        => args.PushMarkup(Loc.GetString("drawing-tablet-paper-count", ("count", comp.Paper), ("capacity", comp.PaperCapacity)));

    private void OnPrintRequested(EntityUid uid, DrawingPrintComponent comp, DrawingPrintRequestMessage args)
        => _ui.ServerSendUiMessage(uid, DrawingPrintUiKey.Key, new DrawingPrintImageMessage(comp.Image, comp.ArtistName), args.Actor);
}
