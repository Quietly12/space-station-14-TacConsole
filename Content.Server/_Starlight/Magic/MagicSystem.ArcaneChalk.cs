using System.Numerics;
using Content.Server.Administration;
using Content.Server.Administration.Logs;
using Content.Server.Decals;
using Content.Shared._Starlight.Magic.Components;
using Content.Shared._Starlight.Magic.Events;
using Content.Shared.Charges.Systems;
using Content.Shared.Database;
using Content.Shared.Decals;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Maps;
using Robust.Shared.Map;
using Robust.Shared.Audio;
using Robust.Shared.Player;
using Robust.Shared.Utility;

namespace Content.Server.Magic;

public sealed partial class MagicSystem
{
    private const int ArcaneChalkMaxCharacters = 60;
    private const float ArcaneChalkRange = 1.5f;
    private static readonly SoundSpecifier ArcaneChalkSummonSound = new SoundPathSpecifier("/Audio/_Starlight/Magic/magic_chalk.ogg");

    [Dependency] private QuickDialogSystem _chalkDialog = default!;
    [Dependency] private SharedChargesSystem _chalkCharges = default!;
    [Dependency] private DecalSystem _chalkDecals = default!;
    [Dependency] private IAdminLogManager _chalkAdmin = default!;

    private void InitializeArcaneChalk()
    {
        SubscribeLocalEvent<ArcaneChalkSpellEvent>(OnArcaneChalkSpell);
        SubscribeLocalEvent<WizardArcaneChalkComponent, AfterInteractEvent>(OnArcaneChalkAfterInteract);
    }

    private void OnArcaneChalkSpell(ArcaneChalkSpellEvent args)
    {
        if (args.Handled || !PassesSpellPrerequisites(args.Action, args.Performer) ||
            !_wizardMobState.IsAlive(args.Performer))
            return;

        var chalk = Spawn("WizardArcaneChalk", Transform(args.Performer).Coordinates);
        _wizardAudio.PlayPvs(ArcaneChalkSummonSound, args.Performer);
        if (!_doomHands.TryPickupAnyHand(args.Performer, chalk, checkActionBlocker: false))
            _doomPopup.PopupEntity(Loc.GetString("wizard-arcane-chalk-hands-full"), args.Performer, args.Performer);
        args.Handled = true;
    }

    private void OnArcaneChalkAfterInteract(EntityUid uid, WizardArcaneChalkComponent component, AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || _chalkCharges.IsEmpty(uid) ||
            !_doomHands.IsHolding(args.User, uid) ||
            !TryComp<ActorComponent>(args.User, out var actor) ||
            !CanPlaceArcaneChalk(args.User, args.ClickLocation))
            return;

        var caster = args.User;
        var target = args.ClickLocation;
        var session = actor.PlayerSession;
        _chalkDialog.OpenDialog(session, "Arcane Chalk", "-NR-Write a short message",
            (string message) =>
            {
                if (session.AttachedEntity == caster && !TerminatingOrDeleted(uid) &&
                    _doomHands.IsHolding(caster, uid) && !_chalkCharges.IsEmpty(uid) &&
                    TryPlaceArcaneChalk(caster, target, message))
                {
                    _chalkCharges.TryUseCharge(uid);
                    if (_chalkCharges.IsEmpty(uid))
                        QueueDel(uid);
                }
            });
        args.Handled = true;
    }

    private bool CanPlaceArcaneChalk(EntityUid caster, EntityCoordinates target)
    {
        if (TerminatingOrDeleted(caster) || !_wizardMobState.IsAlive(caster) ||
            !target.IsValid(EntityManager))
            return false;

        var origin = _wizardTransform.GetMapCoordinates(caster);
        var destination = _wizardTransform.ToMapCoordinates(target);
        if (origin.MapId != destination.MapId ||
            (origin.Position - destination.Position).LengthSquared() > ArcaneChalkRange * ArcaneChalkRange)
            return false;

        var tile = _redecoratingTurf.GetTileRef(target);
        return tile is { } valid && !valid.Tile.IsEmpty && !_redecoratingTurf.IsSpace(valid);
    }

    public bool TryPlaceArcaneChalk(EntityUid caster, EntityCoordinates target, string message)
    {
        message = message.Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (message.Length == 0 || !CanPlaceArcaneChalk(caster, target))
            return false;

        if (message.Length > ArcaneChalkMaxCharacters)
            message = message[..ArcaneChalkMaxCharacters];

        var tile = _redecoratingTurf.GetTileRef(target)!.Value;
        // Decals use their lower-left origin; offset the exact click like a crayon does.
        if (!_chalkDecals.TryAddDecal("WizardArcaneChalkDecal", target.Offset(new Vector2(-0.5f, -0.5f)),
                out var decalId, Color.FromHex("#E6A35499"), cleanable: true))
            return false;

        var placed = Spawn("WizardArcaneChalkMark", target);
        var markComp = Comp<WizardArcaneChalkMarkComponent>(placed);
        markComp.GridUid = tile.GridUid;
        markComp.DecalId = decalId;
        _doomMetadata.SetEntityDescription(placed,
            Loc.GetString("wizard-arcane-chalk-written", ("message", FormattedMessage.EscapeText(message))) + "\n" +
            Loc.GetString("wizard-arcane-chalk-author", ("writer", FormattedMessage.EscapeText(Name(caster)))));
        _chalkAdmin.Add(LogType.CrayonDraw, LogImpact.Low,
            $"{ToPrettyString(caster):user} wrote arcane chalk: {message}");
        return true;
    }

    private void UpdateArcaneChalk()
    {
        var query = EntityQueryEnumerator<WizardArcaneChalkMarkComponent>();
        while (query.MoveNext(out var uid, out var mark))
        {
            if (!HasComp<DecalGridComponent>(mark.GridUid))
            {
                QueueDel(uid);
                continue;
            }

            var found = false;
            // Search from the decal's origin so marks near a decal chunk boundary stay linked.
            var decalOrigin = Transform(uid).Coordinates.Position - new Vector2(0.5f, 0.5f);
            foreach (var decal in _chalkDecals.GetDecalsInRange(mark.GridUid,
                         decalOrigin, 1f))
            {
                if (decal.Index != mark.DecalId)
                    continue;

                found = true;
                break;
            }

            if (!found)
                QueueDel(uid);
        }
    }
}
