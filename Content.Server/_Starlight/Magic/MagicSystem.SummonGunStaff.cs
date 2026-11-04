using System.Collections.Generic;
using System.Numerics;
using Content.Shared.Chat;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.Audio;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;

namespace Content.Server.Magic;

[RegisterComponent]
public sealed partial class WizardSummonGunStaffComponent : Component;

public sealed partial class MagicSystem
{
    private const string SummonGunBullet = "ProjectileWizardSpectralBullet";
    private const float SummonGunBulletSpeed = 18f;
    private const float SummonGunSpawnDistance = 1.5f;
    private const float SummonGunMuzzleDistance = 0.5f;
    private static readonly Vector2 SummonGunHeightOffset = new(0f, 0.35f);
    private static readonly TimeSpan SummonGunDuration = TimeSpan.FromSeconds(10);
    // Spectral guns suppress an area rather than providing a lethal, lootable firearm.
    private static readonly SummonGunProfile[] SummonGunProfiles =
    [
        new("EffectWizardSpectralMosin", "WeaponSniperMosin", new Vector2(-1f, -1f), 2f, "/Audio/_Starlight/Weapons/Guns/Gunshots/mosin.ogg"),
        new("EffectWizardSpectralRevolver", "WeaponRevolverForged", -Vector2.UnitX, 4f, "/Audio/Weapons/Guns/Gunshots/revolver.ogg"),
        new("EffectWizardSpectralRifle", "WeaponRifleLecter", -Vector2.UnitX, 10f, "/Audio/Weapons/Guns/Gunshots/ltrifle.ogg"),
        new("EffectWizardSpectralSmg", "WeaponSubMachineGunAtreides", Vector2.UnitX, 20f, "/Audio/Weapons/Guns/Gunshots/atreides.ogg"),
    ];
    private readonly List<SummonGunVolley> _summonGunVolleys = new();

    private sealed record SummonGunProfile(string EffectPrototype, string WeaponPrototype, Vector2 SpriteBarrelDirection,
        float SpreadDegrees, string ShotSound);

    private sealed class SummonGunVolley
    {
        public EntityUid Effect;
        public EntityUid Shooter;
        public Vector2 Direction;
        public Robust.Shared.Map.MapCoordinates Position;
        public TimeSpan RecoilUntil;
        public SummonGunProfile Profile = default!;
        public TimeSpan ShotInterval;
        public TimeSpan ExpiresAt;
        public TimeSpan NextShotAt;
    }

    private void InitializeSummonGunStaff()
    {
        SubscribeLocalEvent<WizardSummonGunStaffComponent, AmmoShotEvent>(OnSummonGunStaffShot);
    }

    private void OnSummonGunStaffShot(Entity<WizardSummonGunStaffComponent> ent, ref AmmoShotEvent args)
    {
        if (args.Shooter is not { } shooter || args.FiredProjectiles.Count == 0)
            return;

        var marker = args.FiredProjectiles[0];
        if (!TryComp<PhysicsComponent>(marker, out var body) ||
            body.LinearVelocity.LengthSquared() < 0.01f)
            return;

        var direction = Vector2.Normalize(body.LinearVelocity);

        _chat.TrySendInGameICMessage(shooter, "-NR-", InGameICChatType.Speak,
            ChatTransmitRange.Normal);

        var position = _wizardTransform.GetMapCoordinates(shooter);
        var profile = SummonGunProfiles[_random.Next(SummonGunProfiles.Length)];
        var gunPrototype = _minorIllusionPrototypes.Index<EntityPrototype>(profile.WeaponPrototype);
        if (!gunPrototype.TryGetComponent<GunComponent>(out var actualGun, EntityManager.ComponentFactory))
            return;
        var effectPosition = new Robust.Shared.Map.MapCoordinates(
            position.Position + SummonGunHeightOffset + direction * SummonGunSpawnDistance,
            position.MapId);
        var effect = Spawn(profile.EffectPrototype, effectPosition);
        _wizardTransform.SetWorldRotation(effect,
            new Angle(direction) - new Angle(profile.SpriteBarrelDirection) + Angle.FromDegrees(180));
        var volley = new SummonGunVolley
        {
            Effect = effect,
            Shooter = shooter,
            Direction = direction,
            Position = effectPosition,
            Profile = profile,
            ShotInterval = TimeSpan.FromSeconds(1f / actualGun.FireRate),
            ExpiresAt = _wizardTiming.CurTime + SummonGunDuration,
            NextShotAt = _wizardTiming.CurTime,
        };
        _summonGunVolleys.Add(volley);
        FireSummonGunShot(volley);
    }

    private void UpdateSummonGunVolleys()
    {
        for (var i = _summonGunVolleys.Count - 1; i >= 0; i--)
        {
            var volley = _summonGunVolleys[i];
            if (Deleted(volley.Effect) || _wizardTiming.CurTime >= volley.ExpiresAt)
            {
                if (!Deleted(volley.Effect))
                    QueueDel(volley.Effect);
                _summonGunVolleys.RemoveAt(i);
                continue;
            }

            if (_wizardTiming.CurTime >= volley.RecoilUntil)
                _wizardTransform.SetMapCoordinates(volley.Effect, volley.Position);

            while (!Deleted(volley.Effect) && _wizardTiming.CurTime >= volley.NextShotAt &&
                   volley.NextShotAt < volley.ExpiresAt)
                FireSummonGunShot(volley);
        }
    }

    private void FireSummonGunShot(SummonGunVolley volley)
    {
        if (Deleted(volley.Effect))
            return;

        var angle = (_random.NextFloat() * 2f - 1f) * volley.Profile.SpreadDegrees * MathF.PI / 180f;
        var cos = MathF.Cos(angle);
        var sin = MathF.Sin(angle);
        var direction = new Vector2(volley.Direction.X * cos - volley.Direction.Y * sin,
            volley.Direction.X * sin + volley.Direction.Y * cos);
        var shot = Spawn(SummonGunBullet,
            new Robust.Shared.Map.MapCoordinates(volley.Position.Position + volley.Direction * SummonGunMuzzleDistance,
                volley.Position.MapId));
        _wizardGun.ShootProjectile(shot, direction, Vector2.Zero, volley.Shooter,
            volley.Shooter, SummonGunBulletSpeed);
        var flash = Spawn("EffectWizardSpectralMuzzle",
            new Robust.Shared.Map.MapCoordinates(volley.Position.Position + volley.Direction * SummonGunMuzzleDistance,
                volley.Position.MapId));
        _wizardTransform.SetWorldRotation(flash, new Angle(direction));
        _wizardTransform.SetMapCoordinates(volley.Effect,
            new Robust.Shared.Map.MapCoordinates(volley.Position.Position - volley.Direction * 0.14f,
                volley.Position.MapId));
        volley.RecoilUntil = _wizardTiming.CurTime + TimeSpan.FromSeconds(0.09);
        _wizardAudio.PlayPvs(new SoundPathSpecifier(volley.Profile.ShotSound), volley.Effect);
        volley.NextShotAt += volley.ShotInterval;
    }
}
