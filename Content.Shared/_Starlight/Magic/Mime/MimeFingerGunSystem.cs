using Content.Shared.Throwing;
using Content.Shared.Abilities.Mime;
using Content.Shared.Weapons.Ranged.Events;

namespace Content.Shared._Starlight.Magic.Mime;

/// <summary>
/// -NR-Mime pantomime hand items cannot be launched by the normal throw input.
/// -NR-This runs on both client and server to prevent a predicted throw.
/// </summary>
public sealed class MimeFingerGunSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<MimeFingerGunItemComponent, ThrowItemAttemptEvent>(OnThrowAttempt);
        SubscribeLocalEvent<MimeLassoItemComponent, ThrowItemAttemptEvent>(OnLassoThrowAttempt);
        SubscribeLocalEvent<MimeFingerGunItemComponent, ShotAttemptedEvent>(OnFingerGunShotAttempt);
        SubscribeLocalEvent<MimeLassoItemComponent, ShotAttemptedEvent>(OnLassoShotAttempt);
    }

    private void OnThrowAttempt(Entity<MimeFingerGunItemComponent> ent, ref ThrowItemAttemptEvent args)
    {
        args.Cancelled = true;
    }

    private void OnLassoThrowAttempt(Entity<MimeLassoItemComponent> ent, ref ThrowItemAttemptEvent args)
    {
        args.Cancelled = true;
    }

    private void OnFingerGunShotAttempt(Entity<MimeFingerGunItemComponent> ent, ref ShotAttemptedEvent args)
    {
        if (!CanUseForbiddenMimeMagic(args.User))
            args.Cancel();
    }

    private void OnLassoShotAttempt(Entity<MimeLassoItemComponent> ent, ref ShotAttemptedEvent args)
    {
        if (!CanUseForbiddenMimeMagic(args.User))
            args.Cancel();
    }

    private bool CanUseForbiddenMimeMagic(EntityUid user) =>
        TryComp<MimePowersComponent>(user, out var mime) && mime.Enabled && !mime.VowBroken;
}
