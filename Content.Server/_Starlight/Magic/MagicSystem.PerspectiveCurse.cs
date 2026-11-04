using Content.Shared._Starlight.Magic.Components;
using Content.Shared._Starlight.Magic.Events;
using Content.Shared.Buckle.Components;
using Content.Shared.DoAfter;
using Content.Shared.Movement.Components;
using Content.Shared.Polymorph;

namespace Content.Server.Magic;

public sealed partial class MagicSystem
{
    private const string PerspectiveIndicator = "ActionWizardPerspectiveCurseIndicator";

    // Keep the chair timing and visual spin independent of the spell's purchase price and cooldown.
    private static readonly TimeSpan PerspectiveLockDuration = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan PerspectiveSitDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan PerspectiveChairDoAfter = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan PerspectiveSpinDuration = TimeSpan.FromSeconds(0.75);
    private static readonly TimeSpan PerspectiveSpinInterval = TimeSpan.FromSeconds(0.125);

    private void InitializePerspectiveCurse()
    {
        SubscribeLocalEvent<PerspectiveSpellEvent>(OnPerspectiveSpell);
        SubscribeLocalEvent<PerspectiveCurseComponent, PerspectiveChairDoAfterEvent>(OnPerspectiveChairDoAfter);
        SubscribeLocalEvent<PerspectiveCurseComponent, ComponentShutdown>(OnPerspectiveCurseShutdown);
        SubscribeLocalEvent<PerspectiveCurseComponent, PolymorphedEvent>(OnPerspectivePolymorphed);
    }

    private void OnPerspectiveSpell(PerspectiveSpellEvent args)
    {
        if (args.Handled ||
            !PassesSpellPrerequisites(args.Action, args.Performer) ||
            !TryApplyPerspectiveCurse(args.Target))
            return;

        args.Handled = true;
    }

    public bool TryApplyPerspectiveCurse(EntityUid target)
    {
        if (!_wizardMobState.IsAlive(target) ||
            HasComp<PerspectiveCurseComponent>(target) ||
            !TryComp<InputMoverComponent>(target, out var mover))
            return false;

        var now = _wizardTiming.CurTime;
        var curse = EnsureComp<PerspectiveCurseComponent>(target);
        curse.OriginalCameraAngle = mover.TargetRelativeRotation;
        curse.OriginalFacing = Transform(target).LocalRotation;
        curse.OriginalMap = _wizardTransform.ToMapCoordinates(Transform(target).Coordinates).MapId;
        curse.UnlockAt = now + PerspectiveLockDuration;
        curse.SpinUntil = now + PerspectiveSpinDuration;
        curse.NextSpin = now;
        curse.RotationLocked = true;
        mover.TargetRelativeRotation += Angle.FromDegrees(_random.Next(1, 4) * 90);
        Dirty(target, mover);
        Dirty(target, curse);

        SpawnAttachedTo("EffectWizardPerspectiveDizzy", Transform(target).Coordinates);
        _wizardActions.AddAction(target, ref curse.IndicatorAction, PerspectiveIndicator);
        return true;
    }

    private void UpdatePerspectiveCurse()
    {
        var now = _wizardTiming.CurTime;
        var query = EntityQueryEnumerator<PerspectiveCurseComponent>();
        while (query.MoveNext(out var uid, out var curse))
        {
            if (!_wizardMobState.IsAlive(uid) ||
                !HasComp<InputMoverComponent>(uid) ||
                _wizardTransform.ToMapCoordinates(Transform(uid).Coordinates).MapId != curse.OriginalMap)
            {
                RestorePerspective(uid, curse);
                continue;
            }

            if (curse.RotationLocked && now >= curse.UnlockAt)
            {
                curse.RotationLocked = false;
                Dirty(uid, curse);
            }

            if (curse.SpinUntil != TimeSpan.Zero)
            {
                if (now >= curse.SpinUntil)
                {
                    _wizardTransform.SetLocalRotation(uid, curse.OriginalFacing);
                    curse.SpinUntil = TimeSpan.Zero;
                }
                else if (now >= curse.NextSpin)
                {
                    curse.SpinStep++;
                    curse.NextSpin = now + PerspectiveSpinInterval;
                    _wizardTransform.SetLocalRotation(uid, curse.OriginalFacing + Angle.FromDegrees(curse.SpinStep * 90));
                }
            }

            var chair = CompOrNull<BuckleComponent>(uid)?.BuckledTo;
            if (chair is null || !HasComp<WizardCurseChairComponent>(chair.Value))
            {
                curse.Chair = null;
                curse.SeatedSince = null;
                continue;
            }

            if (curse.Chair != chair)
            {
                curse.Chair = chair;
                curse.SeatedSince = now;
                curse.DoAfterRunning = false;
            }

            if (curse.DoAfterRunning || curse.SeatedSince is not { } seated || now < seated + PerspectiveSitDelay)
                continue;

            var doAfter = new DoAfterArgs(EntityManager, uid, PerspectiveChairDoAfter,
                new PerspectiveChairDoAfterEvent(), uid, uid, chair)
            {
                BreakOnDamage = true,
                BreakOnMove = false,
                NeedHand = false,
            };
            curse.DoAfterRunning = _wizardDoAfter.TryStartDoAfter(doAfter);
        }
    }

    private void OnPerspectiveChairDoAfter(Entity<PerspectiveCurseComponent> ent, ref PerspectiveChairDoAfterEvent args)
    {
        ent.Comp.DoAfterRunning = false;
        if (args.Cancelled ||
            args.Args.Used is not { } chair ||
            CompOrNull<BuckleComponent>(ent)?.BuckledTo != chair ||
            !HasComp<WizardCurseChairComponent>(chair))
        {
            ent.Comp.SeatedSince = _wizardTiming.CurTime;
            return;
        }

        RestorePerspective(ent.Owner, ent.Comp);
    }

    private void RestorePerspective(EntityUid uid, PerspectiveCurseComponent curse, bool removeComponent = true)
    {
        if (curse.Restoring)
            return;

        curse.Restoring = true;
        curse.RotationLocked = false;
        Dirty(uid, curse);
        if (TryComp<InputMoverComponent>(uid, out var mover))
        {
            mover.TargetRelativeRotation = curse.OriginalCameraAngle;
            Dirty(uid, mover);
        }

        if (curse.SpinUntil != TimeSpan.Zero)
            _wizardTransform.SetLocalRotation(uid, curse.OriginalFacing);

        if (curse.IndicatorAction is { } action)
            _wizardActions.RemoveAction(action);

        if (removeComponent)
            RemComp<PerspectiveCurseComponent>(uid);
    }

    private void OnPerspectiveCurseShutdown(Entity<PerspectiveCurseComponent> ent, ref ComponentShutdown args)
    {
        if (!TerminatingOrDeleted(ent.Owner))
            RestorePerspective(ent.Owner, ent.Comp, removeComponent: false);
    }

    private void OnPerspectivePolymorphed(Entity<PerspectiveCurseComponent> ent, ref PolymorphedEvent args)
    {
        RestorePerspective(ent.Owner, ent.Comp);
    }
}
