using System.Collections.Generic;
using Content.Shared._Starlight.Magic.Components;
using Content.Shared._Starlight.Magic.Events;
using Content.Shared.Gravity;
using Content.Shared.Mobs.Components;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;
using Robust.Shared.Audio;

namespace Content.Server.Magic;

public sealed partial class MagicSystem
{
    private static readonly TimeSpan ZeroGravityDuration = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ZeroGravityScanInterval = TimeSpan.FromSeconds(0.25);
    private static readonly TimeSpan ZeroGravityHardLandingKnockdown = TimeSpan.FromSeconds(1.5);
    private const float ZeroGravityHalfWidth = 5f;
    private const float ZeroGravityHardLandingSpeed = 1.9f;
    private static readonly SoundSpecifier ZeroGravityLandingSound = new SoundPathSpecifier("/Audio/Effects/slip.ogg");

    private readonly List<ZeroGravityZone> _zeroGravityZones = new();
    private TimeSpan _nextZeroGravityScan;

    private readonly record struct ZeroGravityZone(MapCoordinates Center, TimeSpan ExpiresAt);

    private void InitializeLocalizedZeroGravity()
    {
        SubscribeLocalEvent<LocalizedZeroGravitySpellEvent>(OnLocalizedZeroGravitySpell);
    }

    private void OnLocalizedZeroGravitySpell(LocalizedZeroGravitySpellEvent args)
    {
        if (args.Handled || !PassesSpellPrerequisites(args.Action, args.Performer))
            return;

        args.Handled = TryLocalizedZeroGravity(args.Performer);
    }

    public bool TryLocalizedZeroGravity(EntityUid caster)
    {
        if (!_wizardMobState.IsAlive(caster))
            return false;

        var center = _wizardTransform.GetMapCoordinates(caster);
        _zeroGravityZones.Add(new ZeroGravityZone(
            center,
            _wizardTiming.CurTime + ZeroGravityDuration));
        Spawn("EffectGravityPulse", Transform(caster).Coordinates);
        _nextZeroGravityScan = TimeSpan.Zero;
        UpdateLocalizedZeroGravity();
        return true;
    }

    private void UpdateLocalizedZeroGravity()
    {
        if (_zeroGravityZones.Count == 0)
            return;

        var now = _wizardTiming.CurTime;
        if (now < _nextZeroGravityScan)
            return;

        _nextZeroGravityScan = now + ZeroGravityScanInterval;
        _zeroGravityZones.RemoveAll(zone => zone.ExpiresAt <= now);

        if (_zeroGravityZones.Count == 0)
        {
            var affected = EntityQueryEnumerator<WizardZeroGravityComponent>();
            while (affected.MoveNext(out var uid, out _))
                RemComp<WizardZeroGravityComponent>(uid);
            return;
        }

        var query = EntityQueryEnumerator<MobStateComponent, GravityAffectedComponent>();
        while (query.MoveNext(out var uid, out _, out _))
        {
            var inside = false;
            MapCoordinates? location = null;
            if (_wizardMobState.IsAlive(uid))
            {
                location = _wizardTransform.GetMapCoordinates(uid);
                foreach (var zone in _zeroGravityZones)
                {
                    if (location.Value.MapId != zone.Center.MapId)
                        continue;

                    var offset = location.Value.Position - zone.Center.Position;
                    if (MathF.Abs(offset.X) <= ZeroGravityHalfWidth &&
                        MathF.Abs(offset.Y) <= ZeroGravityHalfWidth)
                    {
                        inside = true;
                        break;
                    }
                }
            }

            if (inside)
            {
                var affected = EnsureComp<WizardZeroGravityComponent>(uid);
                affected.LastPosition = Transform(uid).Coordinates;
                affected.LastMapPosition = location;
                affected.LastPositionAt = now;
            }
            else if (TryComp<WizardZeroGravityComponent>(uid, out var affected))
            {
                var speed = TryComp<PhysicsComponent>(uid, out var body)
                    ? body.LinearVelocity.Length()
                    : 0f;

                var current = Transform(uid).Coordinates;
                if (affected.LastPosition is { } previous)
                {
                    var seconds = (now - affected.LastPositionAt).TotalSeconds;
                    var displacement = current.EntityId == previous.EntityId
                        ? (current.Position - previous.Position).Length()
                        : location is { } mapLocation &&
                          affected.LastMapPosition is { } mapPrevious &&
                          mapLocation.MapId == mapPrevious.MapId
                            ? (mapLocation.Position - mapPrevious.Position).Length()
                            : 0f;
                    if (seconds > 0 && displacement <= 3f)
                        speed = MathF.Max(speed, (float) (displacement / seconds));
                }

                RemComp<WizardZeroGravityComponent>(uid);
                if (speed > ZeroGravityHardLandingSpeed)
                {
                    _wizardStun.TryKnockdown(uid, ZeroGravityHardLandingKnockdown, drop: false);
                    _wizardAudio.PlayPvs(ZeroGravityLandingSound, uid);
                }
            }
        }
    }
}
