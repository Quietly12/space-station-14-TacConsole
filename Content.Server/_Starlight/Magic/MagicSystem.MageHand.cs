using Content.Shared._Starlight.Computers.RemoteControl;
using Content.Shared._Starlight.Magic.Components;
using Content.Shared._Starlight.Magic.Events;
using Content.Shared.CombatMode;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Mobs;
using Robust.Server.GameObjects;
using Robust.Shared.Player;
using Robust.Shared.Physics.Components;

namespace Content.Server.Magic;

public sealed partial class MagicSystem
{
    private static readonly TimeSpan MageHandDuration = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan MageHandCooldown = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MageHandControlGrace = TimeSpan.FromSeconds(2);

    [Dependency] private UserInterfaceSystem _mageHandUi = default!;
    [Dependency] private SharedCombatModeSystem _mageHandCombat = default!;

    private void InitializeMageHand()
    {
        SubscribeLocalEvent<ToggleMageHandSpellEvent>(OnToggleMageHand);
        SubscribeLocalEvent<ReturnMageHandSpellEvent>(OnReturnMageHand);
        SubscribeLocalEvent<WizardMageHandBodyComponent, MobStateChangedEvent>(OnMageHandStateChanged);
    }

    private void OnToggleMageHand(ToggleMageHandSpellEvent args)
    {
        if (args.Handled)
            return;

        if (HasComp<WizardMageHandComponent>(args.Performer))
        {
            StopMageHand(args.Performer);
            args.Handled = true;
            return;
        }

        if (!PassesSpellPrerequisites(args.Action, args.Performer) ||
            !_wizardMobState.IsAlive(args.Performer) ||
            !TryComp<ActorComponent>(args.Performer, out var actor))
            return;

        var hand = Spawn("WizardMageHandBody", Transform(args.Performer).Coordinates);
        var console = Spawn("WizardMageHandConsole", Transform(args.Performer).Coordinates);
        Comp<WizardMageHandBodyComponent>(hand).Caster = args.Performer;
        var remote = Comp<RemoteControlConsoleComponent>(console);
        remote.RemoteBrain = hand;
        Dirty(console, remote);

        _mageHandCombat.SetInCombatMode(hand, true);
        _wizardActions.AddAction(hand, "ActionWizardMageHandReturn");
        var caster = EnsureComp<WizardMageHandComponent>(args.Performer);
        caster.Hand = hand;
        caster.Console = console;
        caster.Action = args.Action;
        caster.ExpiresAt = _wizardTiming.CurTime + MageHandDuration;
        caster.ControlGraceEndsAt = _wizardTiming.CurTime + MageHandControlGrace;
        _wizardActions.SetToggled(args.Action.Owner, true);
        _mageHandUi.OpenUi((console, null), RemoteControlUIKey.Key, actor.PlayerSession);
        args.Handled = true;
    }

    private void OnReturnMageHand(ReturnMageHandSpellEvent args)
    {
        if (args.Handled || !TryComp<WizardMageHandBodyComponent>(args.Performer, out var body) ||
            !HasComp<WizardMageHandComponent>(body.Caster))
            return;

        StopMageHand(body.Caster);
        args.Handled = true;
    }

    private void OnMageHandStateChanged(Entity<WizardMageHandBodyComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState == MobState.Dead)
            StopMageHand(ent.Comp.Caster);
    }

    private void StopMageHand(EntityUid caster)
    {
        if (!TryComp<WizardMageHandComponent>(caster, out var spell))
            return;

        var hand = spell.Hand;
        var console = spell.Console;
        var action = spell.Action;
        RemComp<WizardMageHandComponent>(caster);

        if (!TerminatingOrDeleted(console))
        {
            _mageHandUi.CloseUi((console, null), RemoteControlUIKey.Key, caster);
            QueueDel(console);
        }

        if (!TerminatingOrDeleted(hand))
        {
            _doomHands.TryDrop(hand, Transform(hand).Coordinates);
            QueueDel(hand);
        }

        if (!TerminatingOrDeleted(action))
        {
            _wizardActions.SetToggled(action, false);
            _wizardActions.SetCooldown(action, MageHandCooldown);
        }
    }

    private void UpdateMageHands()
    {
        var hands = EntityQueryEnumerator<WizardMageHandBodyComponent, PhysicsComponent>();
        while (hands.MoveNext(out var handUid, out _, out var physics))
        {
            var velocity = physics.LinearVelocity;
            if (velocity.LengthSquared() > 0.01f)
                _wizardTransform.SetLocalRotation(handUid,
                    new Angle(MathF.Atan2(velocity.Y, velocity.X)));
        }

        var query = EntityQueryEnumerator<WizardMageHandComponent>();
        while (query.MoveNext(out var caster, out var spell))
        {
            if (TerminatingOrDeleted(spell.Hand) || TerminatingOrDeleted(spell.Console) ||
                !_wizardMobState.IsAlive(caster) || spell.ExpiresAt <= _wizardTiming.CurTime ||
                !TryComp<RemoteControlConsoleComponent>(spell.Console, out var remote))
            {
                StopMageHand(caster);
                continue;
            }

            if (remote.Controller == caster)
            {
                spell.HasControlled = true;
                continue;
            }

            if (spell.HasControlled || spell.ControlGraceEndsAt <= _wizardTiming.CurTime)
                StopMageHand(caster);
        }
    }
}
