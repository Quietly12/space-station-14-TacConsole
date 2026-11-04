using System.Collections.Generic;
using System.Numerics;
using Content.Shared._Starlight.Magic.Events;
using Content.Shared.Atmos.Components;
using Content.Shared.Physics;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;

namespace Content.Server.Magic;

public sealed partial class MagicSystem
{
    private const float TornadoCastRange = 8f;
    private const float TornadoMoveSpeed = 1.6f;
    private const float TornadoPullRadius = 2f;
    // Match the singularity grenade's distance-scaled gravity-well pulse, while keeping walls and the caster immune.
    private const float TornadoRadialAcceleration = 100f;
    private const float TornadoTangentialAcceleration = 30f;
    private const float TornadoMinimumPullDistanceSquared = 0.25f;
    private static readonly TimeSpan TornadoLifetime = TimeSpan.FromSeconds(7);
    private readonly List<TornadoState> _tornadoes = new();

    private sealed class TornadoState
    {
        public EntityUid Effect;
        public EntityUid Caster;
        public Vector2 Direction;
        public TimeSpan ExpiresAt;
    }

    private void InitializeTornado()
    {
        SubscribeLocalEvent<TornadoSpellEvent>(OnTornadoSpell);
    }

    private void OnTornadoSpell(TornadoSpellEvent args)
    {
        if (args.Handled || !PassesSpellPrerequisites(args.Action, args.Performer))
            return;

        args.Handled = TryCastTornado(args.Performer, args.Target);
    }

    public bool TryCastTornado(EntityUid caster, EntityCoordinates target)
    {
        if (!_wizardMobState.IsAlive(caster))
            return false;

        var origin = _wizardTransform.GetMapCoordinates(caster);
        var aim = _wizardTransform.ToMapCoordinates(target);
        if (origin.MapId != aim.MapId ||
            (aim.Position - origin.Position).LengthSquared() > TornadoCastRange * TornadoCastRange)
            return false;

        var direction = aim.Position - origin.Position;
        if (direction.LengthSquared() < 0.01f)
            return false;

        var effect = Spawn("EffectWizardTornado", origin);
        _tornadoes.Add(new TornadoState
        {
            Effect = effect,
            Caster = caster,
            Direction = Vector2.Normalize(direction),
            ExpiresAt = _wizardTiming.CurTime + TornadoLifetime,
        });
        return true;
    }

    private void UpdateTornadoes(float frameTime)
    {
        for (var i = _tornadoes.Count - 1; i >= 0; i--)
        {
            var tornado = _tornadoes[i];
            if (Deleted(tornado.Effect) || _wizardTiming.CurTime >= tornado.ExpiresAt)
            {
                if (!Deleted(tornado.Effect))
                    QueueDel(tornado.Effect);
                _tornadoes.RemoveAt(i);
                continue;
            }

            var current = _wizardTransform.GetMapCoordinates(tornado.Effect);
            var next = new MapCoordinates(current.Position + tornado.Direction * TornadoMoveSpeed * frameTime,
                current.MapId);
            if (!_wizardInteraction.InRangeUnobstructed(current, next, 1f,
                    CollisionGroup.Impassable) ||
                !_redecoratingMap.TryFindGridAt(next, out _, out _))
            {
                QueueDel(tornado.Effect);
                _tornadoes.RemoveAt(i);
                continue;
            }

            _wizardTransform.SetMapCoordinates(tornado.Effect, next);
            foreach (var other in _wizardLookup.GetEntitiesInRange<PhysicsComponent>(
                         Transform(tornado.Effect).Coordinates, TornadoPullRadius,
                         LookupFlags.Dynamic | LookupFlags.Sundries))
            {
                if (other.Owner == tornado.Caster || other.Owner == tornado.Effect ||
                    (other.Comp.BodyType & (BodyType.Dynamic | BodyType.KinematicController)) == 0)
                    continue;

                if (TryComp<MovedByPressureComponent>(other.Owner, out var moved) && !moved.Enabled)
                    continue;

                var position = _wizardTransform.GetMapCoordinates(other.Owner);
                if (position.MapId != next.MapId ||
                    !_wizardInteraction.InRangeUnobstructed(next, position, TornadoPullRadius,
                        CollisionGroup.Impassable))
                    continue;

                var displacement = next.Position - position.Position;
                var distanceSquared = displacement.LengthSquared();
                if (distanceSquared < 0.00001f)
                    continue;

                var tangent = new Vector2(-displacement.Y, displacement.X);
                var scale = other.Comp.Mass * frameTime /
                            MathF.Max(distanceSquared, TornadoMinimumPullDistanceSquared);
                var impulse = (displacement * TornadoRadialAcceleration +
                               tangent * TornadoTangentialAcceleration) * scale;
                _wizardPhysics.ApplyLinearImpulse(other.Owner, impulse, body: other.Comp);
            }
        }
    }
}
