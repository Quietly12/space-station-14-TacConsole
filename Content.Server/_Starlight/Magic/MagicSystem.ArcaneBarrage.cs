using System;
using System.Collections.Generic;
using System.Numerics;
using Content.Shared._Starlight.Magic.Events;
using Content.Shared.Actions.Components;
using Content.Shared.Mobs.Components;
using Content.Shared.Projectiles;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;
using Robust.Shared.Audio;

namespace Content.Server.Magic;

[RegisterComponent]
public sealed partial class WizardBarrageOrbComponent : Component
{
    public EntityUid Target;
    public EntityUid Caster;
    public TimeSpan ExpiresAt;
    public bool Lightning;
    public int BounceCount;
    public int VolleyId;
    public HashSet<EntityUid> HitTargets = new();
}

[RegisterComponent]
public sealed partial class WizardLightningBarrageChargeComponent : Component
{
    public Dictionary<int, int> BouncesByVolley = new();
    public TimeSpan ExpiresAt;
}

public sealed partial class MagicSystem
{
    private const string ArcaneBarrageOrbPrototype = "ProjectileWizardArcaneBarrage";
    private const string LightningBarrageOrbPrototype = "ProjectileWizardLightningBarrage";
    private const string ArcaneBarrageRisePrototype = "EffectWizardArcaneBarrageRise";
    private const string LightningBarrageRisePrototype = "EffectWizardLightningBarrageRise";
    private const int BarrageOrbsPerLevel = 15;
    private const int BarrageOrbsPerBatch = 3;
    private const int BarrageMaxLevel = 3;
    private const float BarrageTargetRadius = 6f;
    private const float BarrageOrbSpeed = 3.375f;
    private const float BarrageRiseRadius = 0.42f;
    private const float BarrageRiseHeight = 1.1f;
    private const float LightningBounceRadius = 2f;
    private const int LightningMaxBounces = 2;
    private const int LightningElectrocutionBounceThreshold = 15;
    private static readonly TimeSpan BarrageRiseTime = TimeSpan.FromSeconds(0.5);
    private static readonly TimeSpan BarrageBatchInterval = TimeSpan.FromSeconds(0.05);
    private static readonly TimeSpan BarrageChaseTime = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan LightningChargeMemoryTime = TimeSpan.FromSeconds(12);
    private static readonly TimeSpan LightningElectrocutionTime = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan LightningBounceDelay = TimeSpan.FromSeconds(0.2);
    private static readonly SoundSpecifier LightningHitSound =
        new SoundPathSpecifier("/Audio/_Starlight/Magic/lightningshock.ogg");

    private readonly List<PendingBarrageOrb> _pendingBarrageOrbs = new();
    private readonly List<PendingBarrageVolley> _pendingBarrageVolleys = new();
    private readonly List<PendingLightningBounce> _pendingLightningBounces = new();
    private int _nextBarrageVolleyId;

    private sealed class PendingBarrageOrb
    {
        public EntityUid Effect;
        public EntityUid Caster;
        public EntityUid? Target;
        public MapCoordinates Start;
        public Vector2 End;
        public TimeSpan CreatedAt;
        public bool Lightning;
        public int VolleyId;
    }

    private sealed class PendingBarrageVolley
    {
        public EntityUid Caster;
        public MapCoordinates Origin;
        public EntityUid?[] Assignments = [];
        public int Spawned;
        public TimeSpan NextSpawnAt;
        public bool Lightning;
        public int VolleyId;
    }

    private sealed class PendingLightningBounce
    {
        public EntityUid Effect;
        public EntityUid Caster;
        public EntityUid Target;
        public MapCoordinates Origin;
        public TimeSpan LaunchAt;
        public int BounceCount;
        public int VolleyId;
        public HashSet<EntityUid> HitTargets = new();
    }

    private void InitializeArcaneBarrage()
    {
        SubscribeLocalEvent<ArcaneBarrageSpellEvent>(OnArcaneBarrageSpell);
        SubscribeLocalEvent<LightningBarrageSpellEvent>(OnLightningBarrageSpell);
        SubscribeLocalEvent<WizardBarrageOrbComponent, ProjectileHitEvent>(OnBarrageOrbHit);
    }

    private void OnArcaneBarrageSpell(ArcaneBarrageSpellEvent args)
    {
        if (args.Handled || !PassesSpellPrerequisites(args.Action, args.Performer))
            return;

        CastBarrage(args.Performer, args.Action, lightning: false);
        args.Handled = true;
    }

    private void OnLightningBarrageSpell(LightningBarrageSpellEvent args)
    {
        if (args.Handled || !PassesSpellPrerequisites(args.Action, args.Performer))
            return;

        CastBarrage(args.Performer, args.Action, lightning: true);
        args.Handled = true;
    }

    private void CastBarrage(EntityUid caster, EntityUid action, bool lightning)
    {
        var casterCoordinates = Transform(caster).Coordinates;
        var origin = _wizardTransform.ToMapCoordinates(casterCoordinates);
        var targets = new List<EntityUid>();
        foreach (var candidate in _wizardLookup.GetEntitiesInRange<MobStateComponent>(
                     casterCoordinates, BarrageTargetRadius, LookupFlags.Uncontained))
        {
            if (candidate.Owner == caster || !_wizardMobState.IsAlive(candidate))
                continue;

            var targetCoordinates = _wizardTransform.ToMapCoordinates(Transform(candidate).Coordinates);
            if (targetCoordinates.MapId == origin.MapId)
                targets.Add(candidate.Owner);
        }

        var level = TryComp<ActionUpgradeComponent>(action, out var upgrade)
            ? Math.Clamp(upgrade.Level, 1, BarrageMaxLevel)
            : 1;
        var count = BarrageOrbsPerLevel * level;
        var volleyId = ++_nextBarrageVolleyId;
        var assignments = new EntityUid?[count];
        if (targets.Count > 0)
        {
            var cycle = new List<EntityUid>(targets);
            EntityUid? lastTarget = null;
            for (var i = 0; i < count;)
            {
                _random.Shuffle(cycle);
                if (cycle.Count > 1 && cycle[0] == lastTarget)
                    (cycle[0], cycle[1]) = (cycle[1], cycle[0]);

                foreach (var target in cycle)
                {
                    assignments[i++] = target;
                    lastTarget = target;
                    if (i == count)
                        break;
                }
            }
        }

        var volley = new PendingBarrageVolley
        {
            Caster = caster,
            Origin = origin,
            Assignments = assignments,
            NextSpawnAt = _wizardTiming.CurTime,
            Lightning = lightning,
            VolleyId = volleyId,
        };
        _pendingBarrageVolleys.Add(volley);
        SpawnBarrageBatch(volley, _wizardTiming.CurTime);
        volley.NextSpawnAt += BarrageBatchInterval;
    }

    private void SpawnBarrageBatch(PendingBarrageVolley volley, TimeSpan now)
    {
        var count = volley.Assignments.Length;
        var endIndex = Math.Min(volley.Spawned + BarrageOrbsPerBatch, count);
        for (var i = volley.Spawned; i < endIndex; i++)
        {
            var angle = MathF.Tau * i / count;
            var radial = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            var start = new MapCoordinates(volley.Origin.Position + Vector2.UnitY * 0.55f + radial * 0.08f, volley.Origin.MapId);
            var end = volley.Origin.Position + radial * BarrageRiseRadius + Vector2.UnitY * BarrageRiseHeight;
            var effect = Spawn(volley.Lightning ? LightningBarrageRisePrototype : ArcaneBarrageRisePrototype, start);
            _pendingBarrageOrbs.Add(new PendingBarrageOrb
            {
                Effect = effect,
                Caster = volley.Caster,
                Target = volley.Assignments[i],
                Start = start,
                End = end,
                CreatedAt = now,
                Lightning = volley.Lightning,
                VolleyId = volley.VolleyId,
            });
        }
        volley.Spawned = endIndex;
    }

    private void UpdateArcaneBarrage()
    {
        var now = _wizardTiming.CurTime;
        for (var i = _pendingBarrageVolleys.Count - 1; i >= 0; i--)
        {
            var volley = _pendingBarrageVolleys[i];
            while (volley.Spawned < volley.Assignments.Length && now >= volley.NextSpawnAt)
            {
                SpawnBarrageBatch(volley, volley.NextSpawnAt);
                volley.NextSpawnAt += BarrageBatchInterval;
            }

            if (volley.Spawned == volley.Assignments.Length)
                _pendingBarrageVolleys.RemoveAt(i);
        }

        for (var i = _pendingLightningBounces.Count - 1; i >= 0; i--)
        {
            var bounce = _pendingLightningBounces[i];
            if (now < bounce.LaunchAt)
                continue;

            if (!Deleted(bounce.Caster) && !Deleted(bounce.Target) &&
                _wizardMobState.IsAlive(bounce.Target))
                LaunchBarrageOrb(bounce.Origin, bounce.Target, bounce.Caster, true,
                    bounce.BounceCount, bounce.VolleyId, bounce.HitTargets);

            if (!Deleted(bounce.Effect))
                QueueDel(bounce.Effect);
            _pendingLightningBounces.RemoveAt(i);
        }

        for (var i = _pendingBarrageOrbs.Count - 1; i >= 0; i--)
        {
            var pending = _pendingBarrageOrbs[i];
            if (Deleted(pending.Effect))
            {
                _pendingBarrageOrbs.RemoveAt(i);
                continue;
            }

            var progress = Math.Clamp((float) ((now - pending.CreatedAt).TotalSeconds / BarrageRiseTime.TotalSeconds), 0f, 1f);
            var position = Vector2.Lerp(pending.Start.Position, pending.End, progress);
            _wizardTransform.SetMapCoordinates(pending.Effect, new MapCoordinates(position, pending.Start.MapId));
            if (progress < 1f)
                continue;

            if (pending.Target is { } target &&
                !Deleted(target) &&
                _wizardMobState.IsAlive(target) &&
                !Deleted(pending.Caster))
            {
                LaunchBarrageOrb(new MapCoordinates(position, pending.Start.MapId), target,
                    pending.Caster, pending.Lightning, 0, pending.VolleyId, new HashSet<EntityUid>());
            }

            QueueDel(pending.Effect);
            _pendingBarrageOrbs.RemoveAt(i);
        }

        var query = EntityQueryEnumerator<WizardBarrageOrbComponent, PhysicsComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var homing, out var physics, out var xform))
        {
            if (now >= homing.ExpiresAt || Deleted(homing.Target) || !_wizardMobState.IsAlive(homing.Target))
            {
                QueueDel(uid);
                continue;
            }

            var current = _wizardTransform.ToMapCoordinates(xform.Coordinates);
            var target = _wizardTransform.ToMapCoordinates(Transform(homing.Target).Coordinates);
            if (current.MapId != target.MapId)
            {
                QueueDel(uid);
                continue;
            }

            var delta = target.Position - current.Position;
            if (delta.LengthSquared() > 0.0001f)
                _wizardPhysics.SetLinearVelocity(uid, Vector2.Normalize(delta) * BarrageOrbSpeed,
                    body: physics);
        }

        var charges = EntityQueryEnumerator<WizardLightningBarrageChargeComponent>();
        while (charges.MoveNext(out var uid, out var charge))
        {
            if (now >= charge.ExpiresAt)
                RemComp<WizardLightningBarrageChargeComponent>(uid);
        }
    }

    private void LaunchBarrageOrb(MapCoordinates origin, EntityUid target, EntityUid caster,
        bool lightning, int bounceCount, int volleyId, HashSet<EntityUid> hitTargets)
    {
        var targetPosition = _wizardTransform.ToMapCoordinates(Transform(target).Coordinates);
        if (origin.MapId != targetPosition.MapId)
            return;

        var direction = targetPosition.Position - origin.Position;
        if (direction.LengthSquared() < 0.0001f)
            direction = Vector2.UnitX;

        var orb = Spawn(lightning ? LightningBarrageOrbPrototype : ArcaneBarrageOrbPrototype, origin);
        var homing = EnsureComp<WizardBarrageOrbComponent>(orb);
        homing.Target = target;
        homing.Caster = caster;
        homing.ExpiresAt = _wizardTiming.CurTime + BarrageChaseTime;
        homing.Lightning = lightning;
        homing.BounceCount = bounceCount;
        homing.VolleyId = volleyId;
        homing.HitTargets = hitTargets;
        _wizardGun.ShootProjectile(orb, direction, Vector2.Zero, caster, caster, BarrageOrbSpeed);
    }

    private void OnBarrageOrbHit(Entity<WizardBarrageOrbComponent> ent, ref ProjectileHitEvent args)
    {
        var orb = ent.Comp;
        if (!orb.Lightning || !HasComp<MobStateComponent>(args.Target) || !_wizardMobState.IsAlive(args.Target))
            return;

        orb.HitTargets.Add(args.Target);
        _wizardAudio.PlayPvs(LightningHitSound, args.Target);
        if (orb.BounceCount > 0)
            CountLightningBounce(args.Target, orb, ref args);

        if (orb.BounceCount >= LightningMaxBounces || Deleted(orb.Caster))
            return;

        EntityUid? next = null;
        var closestDistance = LightningBounceRadius * LightningBounceRadius;
        var hitCoordinates = Transform(args.Target).Coordinates;
        var hitPosition = _wizardTransform.ToMapCoordinates(hitCoordinates);
        foreach (var candidate in _wizardLookup.GetEntitiesInRange<MobStateComponent>(
                     hitCoordinates, LightningBounceRadius, LookupFlags.Uncontained))
        {
            if (candidate.Owner == orb.Caster || orb.HitTargets.Contains(candidate.Owner) ||
                !_wizardMobState.IsAlive(candidate))
                continue;

            var position = _wizardTransform.ToMapCoordinates(Transform(candidate).Coordinates);
            if (position.MapId != hitPosition.MapId)
                continue;

            var distance = Vector2.DistanceSquared(position.Position, hitPosition.Position);
            if (distance >= closestDistance)
                continue;

            closestDistance = distance;
            next = candidate.Owner;
        }

        if (next is not { } nextTarget)
            return;

        var nextPosition = _wizardTransform.ToMapCoordinates(Transform(nextTarget).Coordinates);
        var direction = nextPosition.Position - hitPosition.Position;
        if (direction.LengthSquared() < 0.0001f)
            direction = Vector2.UnitX;
        var bounceOrigin = new MapCoordinates(hitPosition.Position + Vector2.Normalize(direction) * 0.45f,
            hitPosition.MapId);
        _pendingLightningBounces.Add(new PendingLightningBounce
        {
            Effect = Spawn(LightningBarrageRisePrototype, bounceOrigin),
            Caster = orb.Caster,
            Target = nextTarget,
            Origin = bounceOrigin,
            LaunchAt = _wizardTiming.CurTime + LightningBounceDelay,
            BounceCount = orb.BounceCount + 1,
            VolleyId = orb.VolleyId,
            HitTargets = new HashSet<EntityUid>(orb.HitTargets),
        });
    }

    private void CountLightningBounce(EntityUid target, WizardBarrageOrbComponent orb, ref ProjectileHitEvent args)
    {
        var charge = EnsureComp<WizardLightningBarrageChargeComponent>(target);
        charge.ExpiresAt = _wizardTiming.CurTime + LightningChargeMemoryTime;
        charge.BouncesByVolley.TryGetValue(orb.VolleyId, out var hits);
        hits++;
        charge.BouncesByVolley[orb.VolleyId] = hits;
        if (hits < LightningElectrocutionBounceThreshold)
            return;

        if (_wizardElectrocution.TryDoElectrocution(target, orb.Caster, 1,
                LightningElectrocutionTime, refresh: false))
        {
            args.Damage.DamageDict["Shock"] = 3;
            charge.BouncesByVolley.Remove(orb.VolleyId);
        }
    }
}
