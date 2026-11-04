using Content.Server.Chat.Managers;
using Content.Shared._Starlight.Magic.Components;
using Content.Shared._Starlight.Magic.Events;
using Content.Shared.Chat;
using Robust.Shared.Player;
using Robust.Shared.Utility;

namespace Content.Server.Magic;

public sealed class WizardGhostSpeechEvent(EntityUid speaker, string message) : EntityEventArgs
{
    public readonly EntityUid Speaker = speaker;
    public readonly string Message = message;
}

public sealed partial class MagicSystem
{
    private static readonly TimeSpan SpeakWithDeadDuration = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan SpeakWithDeadCooldown = TimeSpan.FromSeconds(1);
    private const float SpeakWithDeadRange = 7f;

    [Dependency] private IChatManager _deadChatManager = default!;

    private void InitializeSpeakWithDead()
    {
        SubscribeLocalEvent<SpeakWithDeadSpellEvent>(OnSpeakWithDeadSpell);
        SubscribeLocalEvent<WizardGhostSpeechEvent>(OnWizardGhostSpeech);
    }

    private void OnSpeakWithDeadSpell(SpeakWithDeadSpellEvent args)
    {
        if (args.Handled)
            return;

        if (HasComp<WizardDeadListeningComponent>(args.Performer))
        {
            StopSpeakWithDead(args.Performer);
            args.Handled = true;
            return;
        }

        if (!PassesSpellPrerequisites(args.Action, args.Performer))
            return;

        if (!_wizardMobState.IsAlive(args.Performer) || !HasComp<ActorComponent>(args.Performer))
            return;

        var listener = EnsureComp<WizardDeadListeningComponent>(args.Performer);
        listener.ExpiresAt = _wizardTiming.CurTime + SpeakWithDeadDuration;
        listener.Action = args.Action;
        _wizardActions.SetToggled(args.Action.Owner, true);
        _chat.TrySendInGameICMessage(args.Performer, "GHOSTS, TELL ME YOUR SECRETS",
            InGameICChatType.Speak, ChatTransmitRange.Normal);
        args.Handled = true;
    }

    private void StopSpeakWithDead(EntityUid caster)
    {
        if (!TryComp<WizardDeadListeningComponent>(caster, out var listener))
            return;

        var action = listener.Action;
        RemComp<WizardDeadListeningComponent>(caster);
        if (!TerminatingOrDeleted(action))
        {
            _wizardActions.SetToggled(action, false);
            _wizardActions.SetCooldown(action, SpeakWithDeadCooldown);
        }
    }

    private void UpdateSpeakWithDead()
    {
        var query = EntityQueryEnumerator<WizardDeadListeningComponent>();
        while (query.MoveNext(out var caster, out var listener))
        {
            if (listener.ExpiresAt <= _wizardTiming.CurTime || !_wizardMobState.IsAlive(caster))
                StopSpeakWithDead(caster);
        }
    }

    private void OnWizardGhostSpeech(WizardGhostSpeechEvent args)
    {
        if (TerminatingOrDeleted(args.Speaker))
            return;

        var ghostPosition = _wizardTransform.GetMapCoordinates(args.Speaker);
        var query = EntityQueryEnumerator<WizardDeadListeningComponent, ActorComponent>();
        while (query.MoveNext(out var caster, out var listener, out var actor))
        {
            if (listener.ExpiresAt <= _wizardTiming.CurTime || !_wizardMobState.IsAlive(caster))
                continue;

            var casterPosition = _wizardTransform.GetMapCoordinates(caster);
            if (casterPosition.MapId != ghostPosition.MapId ||
                (casterPosition.Position - ghostPosition.Position).LengthSquared() >
                SpeakWithDeadRange * SpeakWithDeadRange)
                continue;

            var wrapped = Loc.GetString("wizard-speak-with-dead-heard-message",
                ("message", FormattedMessage.EscapeText(args.Message)));
            _deadChatManager.ChatMessageToOne(ChatChannel.Local, args.Message, wrapped,
                default, false, actor.PlayerSession.Channel);
        }
    }
}
