using System.Globalization;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Content.Shared._Starlight.Drawing;
using Content.Shared.Database;
using Content.Shared.Examine;
using Content.Shared.Interaction;
using Content.Shared.Paper;
using Content.Shared.Stacks;
using Prometheus;

namespace Content.Server._Starlight.Drawing;

/// <summary>Handles paper loading, print jobs, and printed artwork report IDs.</summary>
public sealed partial class DrawingTabletSystem : EntitySystem
{
    // Admin number format. Results in AW-01, AW-02 - Ect.
    private const string ArtworkReportNumberFormat = "D2";
    private long _nextArtworkReportNumber;

    private sealed record RenderedPrint(byte[] Image, byte[] Thumbnail);
    private sealed record PrintJob(EntityUid Actor, string ArtistName, int Revision, int Strokes, Task<RenderedPrint?> Task);
    private readonly Dictionary<EntityUid, PrintJob> _printing = [];

    private void OnPrintStartup(EntityUid uid, DrawingPrintComponent comp, ComponentStartup args)
    {
        comp.ReportId = "AW-" + (++_nextArtworkReportNumber).ToString(ArtworkReportNumberFormat, CultureInfo.InvariantCulture);
    }

    private void OnPrintExamine(EntityUid uid, DrawingPrintComponent comp, ExaminedEvent args)
        => args.PushText(Loc.GetString("drawing-print-report-id", ("id", comp.ReportId)), priority: -100);

    private void OnPrintUpload(EntityUid uid, DrawingTabletComponent comp, DrawingTabletPrintUploadMessage args)
    {
        using var timer = _printUploadDuration.NewTimer();
        if (!CanEdit(uid, comp, args.Actor, args.Revision, args.RequestId))
            return;
        BeginPrint(uid, comp, args.Actor, args.RequestId, args.Image);
    }

    private void BeginPrint(EntityUid uid, DrawingTabletComponent comp, EntityUid actor, Guid requestId, byte[] image)
    {
        _ui.ServerSendUiMessage(uid, DrawingTabletUiKey.Key, new DrawingTabletAckMessage(comp.Revision, requestId), actor);
        if (image == null || image.Length is < 1 or > DrawingRaster.ByteCount + 1)
        {
            Notice(uid, actor, "drawing-tablet-image-rejected");
            return;
        }
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
        // -NR- The worker checks immutable accepted strokes without accessing entities.
        var strokes = comp.Strokes.ToArray();
        var task = comp.PrintedImage != null && comp.PrintedThumbnail != null && image.AsSpan().SequenceEqual(comp.PrintedImage)
            ? ReturnCachedPrint(comp.PrintedImage, comp.PrintedThumbnail)
            : Task.Run(() => VerifyPrint(strokes, image));
        _printing.Add(uid, new PrintJob(actor, Name(actor), comp.Revision, strokes.Length, task));
        _audio.PlayPvs(comp.PrintSound, uid);
        Notice(uid, actor, "drawing-tablet-printing");
    }

    private static async Task<RenderedPrint> ReturnCachedPrint(byte[] image, byte[] thumbnail)
    {
        await Task.Delay(DrawingTabletSettings.MinimumPrintDurationMilliseconds);
        return new RenderedPrint(image, thumbnail);
    }

    private async Task<RenderedPrint?> VerifyPrint(DrawingStroke[] strokes, byte[] image)
    {
        var printStarted = Stopwatch.GetTimestamp();
        var activeStarted = Stopwatch.GetTimestamp();
        var valid = DrawingPrintVerifier.TryVerify(image, strokes, out var thumbnail);
        _verificationDuration.Observe(Stopwatch.GetElapsedTime(activeStarted).TotalSeconds);

        var remaining = TimeSpan.FromMilliseconds(DrawingTabletSettings.MinimumPrintDurationMilliseconds)
            - Stopwatch.GetElapsedTime(printStarted);
        if (remaining > TimeSpan.Zero)
            await Task.Delay(remaining);
        return valid ? new RenderedPrint(image, thumbnail) : null;
    }

    private void FinishPrintJobs()
    {
        foreach (var (uid, job) in _printing.ToArray())
        {
            if (!job.Task.IsCompleted)
                continue;
            using var timer = _printCompletionDuration.NewTimer();
            _printing.Remove(uid);
            if (!TryComp<DrawingTabletComponent>(uid, out var comp))
            {
                _ = job.Task.Exception;
                continue;
            }
            if (!job.Task.IsCompletedSuccessfully)
            {
                comp.Paper = Math.Min(comp.PaperCapacity, comp.Paper + 1);
                if (job.Task.Exception != null)
                    Log.Error($"Drawing tablet verification failed: {job.Task.Exception}");
                if (Exists(job.Actor) && _ui.IsUiOpen(uid, DrawingTabletUiKey.Key, job.Actor))
                    Notice(uid, job.Actor, "drawing-tablet-image-rejected");
                continue;
            }
            //IsCompletedSuccessfully above guarantees this read cannot block the simulation thread.
#pragma warning disable RA0004
            var result = job.Task.Result;
#pragma warning restore RA0004
            if (result == null)
            {
                comp.Paper = Math.Min(comp.PaperCapacity, comp.Paper + 1);
                _admin.Add(LogType.Action, LogImpact.Low,
                    $"-NR- {ToPrettyString(job.Actor):actor} submitted an unverifiable drawing tablet print from {ToPrettyString(uid):tablet}, revision {job.Revision}.");
                if (Exists(job.Actor) && _ui.IsUiOpen(uid, DrawingTabletUiKey.Key, job.Actor))
                    Notice(uid, job.Actor, "drawing-tablet-image-rejected");
                continue;
            }
            if (comp.Revision == job.Revision)
            {
                comp.PrintedImage = result.Image;
                comp.PrintedThumbnail = result.Thumbnail;
            }
            var print = Spawn("DrawingTabletPaper", Transform(uid).Coordinates);
            _metadata.SetEntityDescription(print, Loc.GetString("drawing-print-description", ("artist", job.ArtistName)));
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
    {
        using var timer = _printViewDuration.NewTimer();
        _ui.ServerSendUiMessage(uid, DrawingPrintUiKey.Key, new DrawingPrintImageMessage(comp.Image, comp.ArtistName), args.Actor);
    }
}
