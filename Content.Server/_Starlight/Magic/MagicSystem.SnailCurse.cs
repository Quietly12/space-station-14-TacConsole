using System.Numerics;
using Content.Shared._Starlight.Magic.Components;
using Content.Shared._Starlight.Magic.Events;
using Content.Shared.Chemistry.Components;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs.Components;
using Content.Shared.Popups;
using Content.Shared.Stunnable;

namespace Content.Server.Magic;

public sealed partial class MagicSystem
{
    private const string SnailCurseIndicator = "ActionWizardSnailCurseIndicator";

    // Balance values for the curse and its trail.
    private static readonly TimeSpan SnailCurseDuration = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan SnailTrailInterval = TimeSpan.FromSeconds(0.4);
    private const float SnailTrailDistance = 0.5f;
    private const float SnailCrawlSpeedMultiplier = 0.5f;
    private static readonly FixedPoint2 SnailTrailVolume = FixedPoint2.New(30);

    private void InitializeSnailCurse()
    {
        SubscribeLocalEvent<SnailCurseSpellEvent>(OnSnailCurseSpell);
        SubscribeLocalEvent<SnailCurseComponent, StandUpAttemptEvent>(OnSnailStandUpAttempt);
        SubscribeLocalEvent<SnailCurseComponent, KnockedDownRefreshEvent>(OnSnailKnockdownRefresh);
        SubscribeLocalEvent<SnailCurseComponent, ComponentShutdown>(OnSnailCurseShutdown);
    }

    private void OnSnailCurseSpell(SnailCurseSpellEvent args)
    {
        if (args.Handled || !PassesSpellPrerequisites(args.Action, args.Performer))
            return;

        args.Handled = TryApplySnailCurse(args.Target);
    }

    public bool TryApplySnailCurse(EntityUid target)
    {
        if (!_wizardMobState.IsAlive(target) ||
            !HasComp<MobStateComponent>(target) ||
            HasComp<SnailCurseComponent>(target))
            return false;

        var curse = EnsureComp<SnailCurseComponent>(target);
        if (!_wizardStun.TryCrawling(target, SnailCurseDuration, autoStand: true, drop: false, force: true))
        {
            RemComp<SnailCurseComponent>(target);
            return false;
        }

        var mapPosition = _wizardTransform.ToMapCoordinates(Transform(target).Coordinates);
        curse.ExpiresAt = _wizardTiming.CurTime + SnailCurseDuration;
        curse.NextTrailAt = _wizardTiming.CurTime;
        curse.LastTrailPosition = mapPosition.Position;
        curse.LastTrailMap = mapPosition.MapId;
        _wizardActions.AddAction(target, ref curse.IndicatorAction, SnailCurseIndicator);
        if (curse.IndicatorAction is { } indicator)
            _wizardActions.SetCooldown(indicator, _wizardTiming.CurTime, curse.ExpiresAt);
        return true;
    }

    private void UpdateSnailCurse()
    {
        var query = EntityQueryEnumerator<SnailCurseComponent>();
        while (query.MoveNext(out var uid, out var curse))
        {
            if (_wizardTiming.CurTime >= curse.ExpiresAt || !_wizardMobState.IsAlive(uid))
            {
                RemComp<SnailCurseComponent>(uid);
                continue;
            }

            if (_wizardTiming.CurTime < curse.NextTrailAt)
                continue;

            var position = _wizardTransform.ToMapCoordinates(Transform(uid).Coordinates);
            if (position.MapId != curse.LastTrailMap)
            {
                curse.LastTrailMap = position.MapId;
                curse.LastTrailPosition = position.Position;
                continue;
            }

            if (Vector2.DistanceSquared(position.Position, curse.LastTrailPosition) < SnailTrailDistance * SnailTrailDistance)
                continue;

            curse.LastTrailPosition = position.Position;
            curse.NextTrailAt = _wizardTiming.CurTime + SnailTrailInterval;
            _wizardPuddles.TrySpillAt(Transform(uid).Coordinates,
                new Solution("SpaceLube", SnailTrailVolume), out _, sound: false);
        }
    }

    private void OnSnailStandUpAttempt(Entity<SnailCurseComponent> ent, ref StandUpAttemptEvent args)
    {
        args.Cancelled = true;
        if (!args.Autostand)
            args.Message = ("-NR- The snail curse keeps you crawling.", PopupType.SmallCaution);
    }

    private void OnSnailKnockdownRefresh(Entity<SnailCurseComponent> ent, ref KnockedDownRefreshEvent args)
    {
        args.SpeedModifier *= SnailCrawlSpeedMultiplier;
    }

    private void OnSnailCurseShutdown(Entity<SnailCurseComponent> ent, ref ComponentShutdown args)
    {
        if (ent.Comp.IndicatorAction is { } action && !TerminatingOrDeleted(ent.Owner))
            _wizardActions.RemoveAction(action);
    }
}
