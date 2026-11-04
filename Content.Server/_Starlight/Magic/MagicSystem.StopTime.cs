using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Shared._Starlight.Actions.Components;
using Content.Shared._Starlight.Magic.Components;
using Content.Shared._Starlight.Magic.Events;
using Content.Shared.Actions;
using Content.Shared.Mobs.Components;
using Content.Shared.IgnitionSource;
using Content.Shared.Popups;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Hitscan.Events;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics;
using Robust.Shared.Spawners;

namespace Content.Server.Magic;

public sealed partial class MagicSystem
{
    private static readonly TimeSpan StopTimeDuration = TimeSpan.FromSeconds(10);
    private const float StopTimeHalfWidth = 2.5f;
    private const string StopTimeAction = "ActionWizardStopTime";
    private readonly List<StopTimeField> _stopTimeFields = new();
    private readonly List<SuspendedHitscan> _suspendedHitscans = new();
    private readonly List<SuspendedProjectileSpell> _suspendedProjectileSpells = new();
    private readonly List<SuspendedGunProjectile> _suspendedGunProjectiles = new();
    private readonly Dictionary<EntityUid, SuspendedMovingProjectile> _suspendedMovingProjectiles = new();
    [Dependency] private SharedIgnitionSourceSystem _stopTimeIgnition = default!;

    private sealed record StopTimeField(MapCoordinates Center, EntityUid Caster, EntityUid? CasterMind,
        HashSet<EntityUid> ExemptMinds, TimeSpan ExpiresAt, List<EntityUid> Visuals);
    private sealed record SuspendedHitscan(EntityUid Ammo, HitscanTraceEvent Shot, MapCoordinates Origin, EntityUid Visual);
    private sealed record SuspendedProjectileSpell(EntityUid Caster, string Prototype, MapCoordinates Origin,
        Vector2 Direction, float Speed, EntityUid Visual);
    private sealed record SuspendedGunProjectile(EntityUid Projectile, EntityUid Gun, EntityUid? Shooter,
        MapCoordinates Origin, Vector2 Direction, Vector2 GunVelocity, float Speed,
        BodyType BodyType, bool CanCollide, bool WasIgnited, float? Lifetime);
    private sealed record SuspendedMovingProjectile(Vector2 Velocity, BodyType BodyType, bool CanCollide,
        float? Lifetime, TimeSpan? MissileTime, TimeSpan? BarrageTime);

    private void InitializeStopTime()
    {
        SubscribeLocalEvent<WizardStopTimeSpellEvent>(OnStopTimeSpell);
        SubscribeLocalEvent<TimeStopProjectileSpellAttemptEvent>(OnTimeStopProjectileSpellAttempt);
    }

    private void OnStopTimeSpell(WizardStopTimeSpellEvent args)
    {
        if (args.Handled || !PassesSpellPrerequisites(args.Action, args.Performer))
            return;

        args.Handled = TryCastStopTime(args.Performer);
    }

    public bool TryCastStopTime(EntityUid caster)
    {
        if (!_wizardMobState.IsAlive(caster))
            return false;

        var center = _wizardTransform.GetMapCoordinates(caster);
        if (center.MapId == MapId.Nullspace || _stopTimeFields.Exists(field =>
                field.Caster == caster || IsInsideStopTime(center, field.Center)))
            return false;

        var exemptMinds = new HashSet<EntityUid>();
        var holders = EntityQueryEnumerator<MobStateComponent>();
        while (holders.MoveNext(out var uid, out _))
        {
            if (HasStopTimeAction(uid) && _mind.TryGetMind(uid, out var mindId, out _))
                exemptMinds.Add(mindId);
        }

        EntityUid? casterMind = null;
        if (_mind.TryGetMind(caster, out var ownMind, out _))
        {
            casterMind = ownMind;
            exemptMinds.Add(ownMind);
        }

        var visuals = new List<EntityUid>();
        for (var x = -2; x <= 2; x++)
        for (var y = -2; y <= 2; y++)
            visuals.Add(Spawn("EffectWizardStopTimeField",
                new MapCoordinates(center.Position + new Vector2(x, y), center.MapId)));
        _stopTimeFields.Add(new StopTimeField(center, caster, casterMind, exemptMinds,
            _wizardTiming.CurTime + StopTimeDuration, visuals));
        UpdateStopTime();
        return true;
    }

    private bool IsInsideStopTime(MapCoordinates point, MapCoordinates center) =>
        point.MapId == center.MapId &&
        MathF.Abs(point.Position.X - center.Position.X) < StopTimeHalfWidth &&
        MathF.Abs(point.Position.Y - center.Position.Y) < StopTimeHalfWidth;

    private bool HasActiveStopTimeAt(MapCoordinates point)
    {
        foreach (var field in _stopTimeFields)
        {
            if (IsInsideStopTime(point, field.Center))
                return true;
        }

        return false;
    }

    private bool HasStopTimeAction(EntityUid uid)
    {
        foreach (var action in _wizardActions.GetActions(uid))
        {
            if (MetaData(action).EntityPrototype?.ID == StopTimeAction)
                return true;
        }

        return false;
    }

    private bool KnowsStopTime(EntityUid uid)
    {
        if (_mind.TryGetMind(uid, out var mindId, out _))
        {
            foreach (var field in _stopTimeFields)
            {
                if (field.ExemptMinds.Contains(mindId))
                    return true;
            }

            return false;
        }

        foreach (var field in _stopTimeFields)
        {
            if ((field.CasterMind == null && field.Caster == uid) || HasStopTimeAction(uid))
                return true;
        }

        return false;
    }

    public bool TrySuspendHitscanShot(EntityUid ammo, ref HitscanTraceEvent shot)
    {
        if (shot.Shooter is not { } shooter || !KnowsStopTime(shooter))
            return false;

        var origin = _wizardTransform.ToMapCoordinates(shot.FromCoordinates);
        if (!HasActiveStopTimeAt(origin))
            return false;

        var visual = Spawn("EffectWizardStoppedBullet",
            new MapCoordinates(origin.Position + shot.ShotDirection * 0.6f, origin.MapId));
        _suspendedHitscans.Add(new SuspendedHitscan(ammo, shot, origin, visual));
        return true;
    }

    public bool TrySuspendGunProjectile(EntityUid projectile, EntityUid gun, EntityUid? shooter,
        Vector2 direction, Vector2 gunVelocity, float speed)
    {
        if (shooter is not { } uid || !KnowsStopTime(uid))
            return false;

        var origin = _wizardTransform.GetMapCoordinates(projectile);
        if (!HasActiveStopTimeAt(origin) || !TryComp<PhysicsComponent>(projectile, out var physics))
            return false;

        var ignited = TryComp<IgnitionSourceComponent>(projectile, out var source) && source.Ignited;
        if (ignited)
            _stopTimeIgnition.SetIgnited(projectile, false);
        var bodyType = physics.BodyType;
        var canCollide = physics.CanCollide;
        var lifetime = SuspendProjectileLifetime(projectile);
        _wizardPhysics.SetLinearVelocity(projectile, Vector2.Zero, body: physics);
        _wizardPhysics.SetBodyType(projectile, BodyType.Static, body: physics);
        _wizardPhysics.SetCanCollide(projectile, false, body: physics);
        _suspendedGunProjectiles.Add(new SuspendedGunProjectile(projectile, gun, shooter,
            origin, direction, gunVelocity, speed, bodyType, canCollide, ignited, lifetime));
        return true;
    }

    private float? SuspendProjectileLifetime(EntityUid projectile)
    {
        if (!TryComp<TimedDespawnComponent>(projectile, out var timed))
            return null;

        var remaining = timed.Lifetime;
        RemComp<TimedDespawnComponent>(projectile);
        return remaining;
    }

    private void ResumeProjectileLifetime(EntityUid projectile, float? lifetime)
    {
        if (lifetime is { } remaining)
            EnsureComp<TimedDespawnComponent>(projectile).Lifetime = remaining;
    }

    private void OnTimeStopProjectileSpellAttempt(ref TimeStopProjectileSpellAttemptEvent args)
    {
        if (!KnowsStopTime(args.Caster))
            return;

        var origin = _wizardTransform.ToMapCoordinates(args.From);
        if (!HasActiveStopTimeAt(origin))
            return;

        var destination = _wizardTransform.ToMapCoordinates(args.To);
        var direction = destination.Position - origin.Position;
        if (direction == Vector2.Zero)
            return;

        var visual = Spawn(args.Prototype == "ProjectileFireball"
                ? "EffectWizardStoppedFireball"
                : "EffectWizardStoppedBullet",
            new MapCoordinates(origin.Position + Vector2.Normalize(direction) * 0.6f, origin.MapId));
        _suspendedProjectileSpells.Add(new SuspendedProjectileSpell(args.Caster, args.Prototype,
            origin, direction, args.Speed, visual));
        args.Suspended = true;
    }

    private void UpdateStopTime()
    {
        if (_stopTimeFields.Count == 0 && _suspendedHitscans.Count == 0 &&
            _suspendedProjectileSpells.Count == 0 && _suspendedGunProjectiles.Count == 0 &&
            _suspendedMovingProjectiles.Count == 0)
            return;

        for (var i = _stopTimeFields.Count - 1; i >= 0; i--)
        {
            var field = _stopTimeFields[i];
            if (_wizardTiming.CurTime < field.ExpiresAt)
                continue;

            _stopTimeFields.RemoveAt(i);
            foreach (var visual in field.Visuals)
            {
                if (!TerminatingOrDeleted(visual))
                    QueueDel(visual);
            }
        }

        var projectileQuery = EntityQueryEnumerator<ProjectileComponent, PhysicsComponent>();
        while (projectileQuery.MoveNext(out var uid, out _, out var physics))
        {
            if (TerminatingOrDeleted(uid) || _suspendedMovingProjectiles.ContainsKey(uid) ||
                _suspendedGunProjectiles.Exists(shot => shot.Projectile == uid) ||
                !HasActiveStopTimeAt(_wizardTransform.GetMapCoordinates(uid)))
                continue;

            TimeSpan? missileTime = null;
            if (TryComp<MagicMissileHomingComponent>(uid, out var missile))
            {
                missileTime = missile.ExpiresAt - _wizardTiming.CurTime;
                missile.ExpiresAt = TimeSpan.MaxValue;
            }

            TimeSpan? barrageTime = null;
            if (TryComp<WizardBarrageOrbComponent>(uid, out var barrage))
            {
                barrageTime = barrage.ExpiresAt - _wizardTiming.CurTime;
                barrage.ExpiresAt = TimeSpan.MaxValue;
            }

            _suspendedMovingProjectiles.Add(uid,
                new SuspendedMovingProjectile(physics.LinearVelocity, physics.BodyType, physics.CanCollide,
                    SuspendProjectileLifetime(uid), missileTime, barrageTime));
            _wizardPhysics.SetLinearVelocity(uid, Vector2.Zero, body: physics);
            _wizardPhysics.SetBodyType(uid, BodyType.Static, body: physics);
            _wizardPhysics.SetCanCollide(uid, false, body: physics);
        }

        foreach (var (uid, stopped) in _suspendedMovingProjectiles.ToArray())
        {
            if (TerminatingOrDeleted(uid))
            {
                _suspendedMovingProjectiles.Remove(uid);
                continue;
            }

            if (HasActiveStopTimeAt(_wizardTransform.GetMapCoordinates(uid)))
                continue;

            _suspendedMovingProjectiles.Remove(uid);
            if (!TryComp<PhysicsComponent>(uid, out var physics))
                continue;

            _wizardPhysics.SetBodyType(uid, stopped.BodyType, body: physics);
            _wizardPhysics.SetCanCollide(uid, stopped.CanCollide, body: physics);
            _wizardPhysics.SetLinearVelocity(uid, stopped.Velocity, body: physics);
            ResumeProjectileLifetime(uid, stopped.Lifetime);
            if (stopped.MissileTime is { } missileTime &&
                TryComp<MagicMissileHomingComponent>(uid, out var missile))
                missile.ExpiresAt = _wizardTiming.CurTime + missileTime;
            if (stopped.BarrageTime is { } barrageTime &&
                TryComp<WizardBarrageOrbComponent>(uid, out var barrage))
                barrage.ExpiresAt = _wizardTiming.CurTime + barrageTime;
        }

        var affected = new HashSet<EntityUid>();
        var aware = new HashSet<EntityUid>();
        var query = EntityQueryEnumerator<MobStateComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            if (TerminatingOrDeleted(uid) || !HasActiveStopTimeAt(_wizardTransform.GetMapCoordinates(uid)))
                continue;

            if (KnowsStopTime(uid))
            {
                aware.Add(uid);
                continue;
            }

            affected.Add(uid);
            if (HasComp<WizardStopTimeAffectedComponent>(uid))
                continue;

            var marker = EnsureComp<WizardStopTimeAffectedComponent>(uid);
            marker.AddedStasis = !HasComp<StasisFrozenComponent>(uid);
            if (marker.AddedStasis)
                EnsureComp<StasisFrozenComponent>(uid);
            _doomPopup.PopupEntity(Loc.GetString("wizard-stop-time-frozen"), uid, uid,
                PopupType.LargeCaution);
            if (TryComp<PhysicsComponent>(uid, out var physics))
                _wizardPhysics.SetLinearVelocity(uid, Vector2.Zero, body: physics);
        }

        var affectedQuery = EntityQueryEnumerator<WizardStopTimeAffectedComponent>();
        while (affectedQuery.MoveNext(out var uid, out var marker))
        {
            if (affected.Contains(uid))
                continue;

            if (marker.AddedStasis)
                RemComp<StasisFrozenComponent>(uid);
            RemComp<WizardStopTimeAffectedComponent>(uid);
            _doomPopup.PopupEntity(Loc.GetString("wizard-stop-time-resumed"), uid, uid,
                PopupType.Large);
        }

        foreach (var uid in aware)
            EnsureComp<WizardStopTimeAwareComponent>(uid);

        var awareQuery = EntityQueryEnumerator<WizardStopTimeAwareComponent>();
        while (awareQuery.MoveNext(out var uid, out _))
        {
            if (!aware.Contains(uid))
                RemComp<WizardStopTimeAwareComponent>(uid);
        }

        for (var i = 0; i < _suspendedHitscans.Count;)
        {
            var suspended = _suspendedHitscans[i];
            if (HasActiveStopTimeAt(suspended.Origin))
            {
                i++;
                continue;
            }

            _suspendedHitscans.RemoveAt(i);
            if (!TerminatingOrDeleted(suspended.Visual))
                QueueDel(suspended.Visual);
            if (TerminatingOrDeleted(suspended.Ammo))
                continue;

            var shot = suspended.Shot;
            RaiseLocalEvent(suspended.Ammo, ref shot);
            QueueDel(suspended.Ammo);
        }

        for (var i = 0; i < _suspendedProjectileSpells.Count;)
        {
            var suspended = _suspendedProjectileSpells[i];
            if (HasActiveStopTimeAt(suspended.Origin))
            {
                i++;
                continue;
            }

            _suspendedProjectileSpells.RemoveAt(i);
            if (!TerminatingOrDeleted(suspended.Visual))
                QueueDel(suspended.Visual);
            var projectile = Spawn(suspended.Prototype, suspended.Origin);
            var caster = TerminatingOrDeleted(suspended.Caster) ? (EntityUid?) null : suspended.Caster;
            _wizardGun.ShootProjectile(projectile, suspended.Direction, Vector2.Zero, caster, caster,
                suspended.Speed);
        }

        for (var i = 0; i < _suspendedGunProjectiles.Count;)
        {
            var suspended = _suspendedGunProjectiles[i];
            if (HasActiveStopTimeAt(suspended.Origin))
            {
                i++;
                continue;
            }

            _suspendedGunProjectiles.RemoveAt(i);
            if (TerminatingOrDeleted(suspended.Projectile) ||
                !TryComp<PhysicsComponent>(suspended.Projectile, out var physics))
                continue;

            _wizardPhysics.SetBodyType(suspended.Projectile, suspended.BodyType, body: physics);
            _wizardPhysics.SetCanCollide(suspended.Projectile, suspended.CanCollide, body: physics);
            ResumeProjectileLifetime(suspended.Projectile, suspended.Lifetime);
            if (suspended.WasIgnited)
                _stopTimeIgnition.SetIgnited(suspended.Projectile);
            var gun = TerminatingOrDeleted(suspended.Gun) ? (EntityUid?) null : suspended.Gun;
            var shooter = suspended.Shooter is { } user && !TerminatingOrDeleted(user)
                ? user
                : (EntityUid?) null;
            _wizardGun.ShootProjectile(suspended.Projectile, suspended.Direction, suspended.GunVelocity,
                gun, shooter, suspended.Speed);
        }
    }
}
