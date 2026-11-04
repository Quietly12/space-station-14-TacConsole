using System.Numerics;
using Content.Shared._Starlight.Magic.Components;
using Content.Shared._Starlight.Magic.Events;
using Content.Shared.Mobs.Components;
using Content.Shared.Projectiles;
using Content.Shared.Stunnable;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;

namespace Content.Server.Magic;

public sealed partial class MagicSystem
{
    private const string MagicMissileProjectile = "ProjectileWizardMagicMissile";

    // Balance values; the projectile prototype also has a matching four-second TimedDespawn.
    private const float MagicMissileRadius = 4f;
    private const float MagicMissileSpeed = 2.5f;
    private static readonly TimeSpan MagicMissileChaseTime = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan MagicMissileKnockdownTime = TimeSpan.FromSeconds(2);

    private void InitializeMagicMissile()
    {
        SubscribeLocalEvent<MagicMissileSpellEvent>(OnMagicMissileSpell);
        SubscribeLocalEvent<MagicMissileHomingComponent, ProjectileHitEvent>(OnMagicMissileHit);
    }

    private void OnMagicMissileSpell(MagicMissileSpellEvent args)
    {
        if (args.Handled || !PassesSpellPrerequisites(args.Action, args.Performer))
            return;

        var origin = _wizardTransform.ToMapCoordinates(Transform(args.Performer).Coordinates);
        foreach (var target in _wizardLookup.GetEntitiesInRange<MobStateComponent>(
                     Transform(args.Performer).Coordinates, MagicMissileRadius, LookupFlags.Uncontained))
        {
            if (target.Owner == args.Performer || !_wizardMobState.IsAlive(target) ||
                HasComp<KnockedDownComponent>(target))
                continue;

            var targetPosition = _wizardTransform.ToMapCoordinates(Transform(target).Coordinates);
            if (targetPosition.MapId != origin.MapId)
                continue;

            var direction = targetPosition.Position - origin.Position;
            if (direction.LengthSquared() < 0.01f)
                direction = Vector2.UnitX;

            var missile = Spawn(MagicMissileProjectile, origin);
            var homing = EnsureComp<MagicMissileHomingComponent>(missile);
            homing.Target = target;
            homing.ExpiresAt = _wizardTiming.CurTime + MagicMissileChaseTime;
            _wizardGun.ShootProjectile(missile, direction, Vector2.Zero, args.Performer, args.Performer, MagicMissileSpeed);
        }

        args.Handled = true;
    }

    private void UpdateMagicMissiles()
    {
        var query = EntityQueryEnumerator<MagicMissileHomingComponent, PhysicsComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var homing, out var physics, out var xform))
        {
            if (_wizardTiming.CurTime >= homing.ExpiresAt ||
                Deleted(homing.Target) ||
                !_wizardMobState.IsAlive(homing.Target))
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
            if (delta.LengthSquared() < 0.0001f)
                continue;

            _wizardPhysics.SetLinearVelocity(uid, Vector2.Normalize(delta) * MagicMissileSpeed, body: physics);
        }
    }

    private void OnMagicMissileHit(Entity<MagicMissileHomingComponent> ent, ref ProjectileHitEvent args)
    {
        if (args.Target == ent.Comp.Target && _wizardMobState.IsAlive(args.Target))
            _wizardStun.TryKnockdown(args.Target, MagicMissileKnockdownTime, force: true);
    }
}
