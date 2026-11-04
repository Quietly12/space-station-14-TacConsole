using System.Collections.Generic;
using System.Numerics;
using Content.Shared._Starlight.Magic.Events;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.Server.Magic;

public sealed partial class MagicSystem
{
    private const float MeteorSpellRange = 8f;
    private const float MeteorAreaHalfWidth = 3.5f;
    private const int MeteorMinimumCount = 2;
    private const int MeteorMaximumCount = 4;
    private const float MeteorSpellVelocity = 16f;
    private static readonly TimeSpan MeteorWarningTime = TimeSpan.FromSeconds(1.5);
    private static readonly TimeSpan MeteorSpawnInterval = TimeSpan.FromSeconds(0.18);
    private readonly List<MeteorSpellWave> _meteorSpellWaves = new();


    private sealed class MeteorSpellWave
    {
        public MapCoordinates Center;
        public int Remaining;
        public TimeSpan NextMeteorAt;
    }

    private void InitializeMeteorSwarm()
    {
        SubscribeLocalEvent<WizardMeteorSwarmSpellEvent>(OnMeteorSwarmSpell);
    }

    private void OnMeteorSwarmSpell(WizardMeteorSwarmSpellEvent args)
    {
        if (args.Handled || !PassesSpellPrerequisites(args.Action, args.Performer))
            return;

        args.Handled = TryCastMeteorSwarm(args.Performer, args.Target);
    }

    public bool TryCastMeteorSwarm(EntityUid caster, EntityCoordinates target)
    {
        if (!_wizardMobState.IsAlive(caster))
            return false;

        var center = _wizardTransform.ToMapCoordinates(target);
        var origin = _wizardTransform.GetMapCoordinates(caster);
        var tile = _redecoratingTurf.GetTileRef(target);
        if (origin.MapId != center.MapId ||
            Vector2.DistanceSquared(origin.Position, center.Position) > MeteorSpellRange * MeteorSpellRange ||
            tile is not { } floor || floor.Tile.IsEmpty || _redecoratingTurf.IsSpace(floor))
            return false;

        _meteorSpellWaves.Add(new MeteorSpellWave
        {
            Center = center,
            Remaining = _random.Next(MeteorMinimumCount, MeteorMaximumCount + 1),
            NextMeteorAt = _wizardTiming.CurTime + MeteorWarningTime,
        });
        Spawn("EffectWizardMeteorWarning", center);
        return true;
    }

    private void UpdateMeteorSwarm()
    {
        for (var i = _meteorSpellWaves.Count - 1; i >= 0; i--)
        {
            var wave = _meteorSpellWaves[i];
            if (_wizardTiming.CurTime < wave.NextMeteorAt)
                continue;

            SpawnSpellMeteor(wave.Center);
            if (--wave.Remaining == 0)
            {
                _meteorSpellWaves.RemoveAt(i);
                continue;
            }

            wave.NextMeteorAt += MeteorSpawnInterval;
        }
    }

    private void SpawnSpellMeteor(MapCoordinates center)
    {
        var angle = _random.NextFloat() * MathF.Tau;
        var edge = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * MeteorAreaHalfWidth;
        var impact = new Vector2(
            (_random.NextFloat() * 2f - 1f) * MeteorAreaHalfWidth,
            (_random.NextFloat() * 2f - 1f) * MeteorAreaHalfWidth);
        var direction = Vector2.Normalize(impact - edge);
        var meteor = Spawn(_random.NextFloat() < 0.8f ? "MeteorSmall" : "MeteorMedium",
            new MapCoordinates(center.Position + edge, center.MapId));
        if (TryComp<PhysicsComponent>(meteor, out var physics))
            _wizardPhysics.ApplyLinearImpulse(meteor, direction * MeteorSpellVelocity * physics.Mass,
                body: physics);
    }
}
