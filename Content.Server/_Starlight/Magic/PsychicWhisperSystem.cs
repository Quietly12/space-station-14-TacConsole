using Content.Server.Administration;
using Content.Server.Prayer;
using Content.Shared.Mobs.Systems;
using Content.Shared._Starlight.Magic.Events;
using Robust.Shared.Player;
using Robust.Shared.Utility;

namespace Content.Server._Starlight.Magic;

/// <summary>
/// -NR-Sends a private message after the caster finishes the spell's text dialog.
/// </summary>
public sealed partial class PsychicWhisperSystem : EntitySystem
{
    [Dependency] private QuickDialogSystem _quickDialog = default!;
    [Dependency] private PrayerSystem _prayerSystem = default!;
    [Dependency] private MobStateSystem _mobState = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PsychicWhisperEvent>(OnPsychicWhisper);
    }

    private void OnPsychicWhisper(PsychicWhisperEvent ev)
    {
        if (ev.Handled || !TryOpenWhisperDialog(ev.Performer, ev.Target, Name(ev.Action)))
            return;

        ev.Handled = true;
    }

    public bool TryOpenWhisperDialog(EntityUid performer, EntityUid target, string title = "Telepathy")
    {
        if (Deleted(performer) || Deleted(target) ||
            !TryComp<ActorComponent>(performer, out var performerActor) ||
            !TryComp<ActorComponent>(target, out var targetActor) ||
            !_mobState.IsAlive(performer))
            return false;

        var performerSession = performerActor.PlayerSession;
        var targetSession = targetActor.PlayerSession;
        _quickDialog.OpenDialog(performerSession, title, "",
            (string message) =>
            {
                if (string.IsNullOrWhiteSpace(message) || Deleted(performer) || Deleted(target) ||
                    performerSession.AttachedEntity != performer ||
                    targetSession.AttachedEntity != target ||
                    !_mobState.IsAlive(performer))
                    return;

                _prayerSystem.SendSubtleMessage(targetSession, performerSession,
                    FormattedMessage.EscapeText(message), Loc.GetString("prayer-popup-subtle-psychic-whisper"),
                    emphasizeChat: true);
            });

        return true;
    }
}
