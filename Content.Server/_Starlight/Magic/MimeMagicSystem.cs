using System.Numerics;
using System.Linq;
using Content.Server.Chat.Systems;
using Content.Server.Popups;
using Content.Shared._Starlight.Magic.Mime;
using Content.Shared.Abilities.Mime;
using Content.Shared.Actions;
using Content.Shared.ActionBlocker;
using Content.Shared.Chat;
using Content.Shared.Coordinates.Helpers;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction.Components;
using Content.Shared.Maps;
using Content.Shared.Mobs.Components;
using Content.Shared.Movement.Events;
using Content.Shared.Movement.Pulling.Systems;
using Content.Shared.Physics;
using Content.Shared.Projectiles;
using Content.Shared.Stunnable;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Map;
using Robust.Shared.Containers;
using Robust.Shared.Network;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;

namespace Content.Server._Starlight.Magic;

/// <summary>
/// The world effects of the forbidden mime book have no sprites. Its action icons remain visible to the caster.
/// </summary>
public sealed partial class MimeMagicSystem : EntitySystem
{
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private ActionBlockerSystem _actionBlocker = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private PullingSystem _pulling = default!;
    [Dependency] private SharedStunSystem _stun = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private TurfSystem _turf = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private INetManager _net = default!;

    private readonly Dictionary<EntityUid, HookPull> _pulls = new();
    private const int HookPullCycles = 4;
    private static readonly TimeSpan HookPullTime = TimeSpan.FromSeconds(0.3);
    private static readonly TimeSpan HookPauseTime = TimeSpan.FromSeconds(0.3);

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<InvisibleBarrierActionEvent>(OnBarrier);
        SubscribeLocalEvent<MimeFingerGunActionComponent, MapInitEvent>(OnFingerGunActionInit);
        SubscribeLocalEvent<MimeFingerGunActionComponent, ComponentShutdown>(OnFingerGunActionShutdown);
        SubscribeLocalEvent<ToggleFingerGunActionEvent>(OnToggleFingerGun);
        SubscribeLocalEvent<MimeFingerGunItemComponent, GunShotEvent>(OnFingerGunShot);
        SubscribeLocalEvent<MimeLassoActionComponent, MapInitEvent>(OnLassoActionInit);
        SubscribeLocalEvent<MimeLassoActionComponent, ComponentShutdown>(OnLassoActionShutdown);
        SubscribeLocalEvent<ToggleMimeLassoActionEvent>(OnToggleLasso);
        SubscribeLocalEvent<MimeLassoItemComponent, GunShotEvent>(OnLassoShot);
        SubscribeLocalEvent<InvisibleHookProjectileComponent, ProjectileHitEvent>(OnHookHit);
        SubscribeLocalEvent<MimeHookPullingComponent, UpdateCanMoveEvent>(OnHookPullingCanMove);
    }

    private bool CanMime(EntityUid performer) =>
        TryComp<MimePowersComponent>(performer, out var mime) && mime.Enabled && !mime.VowBroken;

    private void OnBarrier(InvisibleBarrierActionEvent args)
    {
        if (args.Handled || !_net.IsServer || !CanMime(args.Performer))
            return;

        var xform = Transform(args.Performer);
        var forward = xform.LocalRotation.ToWorldVec();
        var side = new Vector2(-forward.Y, forward.X);
        var tiles = new List<TileRef>(3);
        for (var i = -1; i <= 1; i++)
        {
            var coords = xform.Coordinates.Offset(-forward + side * i).SnapToGrid(EntityManager);
            var tile = _turf.GetTileRef(coords);
            if (tile == null || _turf.IsTileBlocked(tile.Value, CollisionGroup.Impassable | CollisionGroup.Opaque))
                return;
            tiles.Add(tile.Value);
        }

        foreach (var tile in tiles)
            Spawn("MimeInvisibleBarrier", _turf.GetTileCenter(tile));
        args.Handled = true;
    }

    private void OnFingerGunActionInit(Entity<MimeFingerGunActionComponent> ent, ref MapInitEvent args)
    {
        if (!_net.IsServer)
            return;

        _containers.EnsureContainer<Container>(ent, "mime-finger-gun");
        ent.Comp.Gun = SpawnInContainerOrDrop("WeaponMimeFingerGun", ent, "mime-finger-gun");
        if (ent.Comp.Gun is { } gun)
            Comp<MimeFingerGunItemComponent>(gun).Action = ent;
        Dirty(ent);
    }

    private void OnFingerGunActionShutdown(Entity<MimeFingerGunActionComponent> ent, ref ComponentShutdown args)
    {
        if (_net.IsServer && ent.Comp.Gun is { } gun && !Deleted(gun))
            QueueDel(gun);
    }

    private void OnToggleFingerGun(ToggleFingerGunActionEvent args)
    {
        if (args.Handled || !_net.IsServer || !CanMime(args.Performer) ||
            !TryComp<MimeFingerGunActionComponent>(args.Action, out var comp) ||
            comp.Gun is not { } gun || Deleted(gun))
            return;

        if (_hands.IsHolding(args.Performer, gun))
        {
            RemComp<UnremoveableComponent>(gun);
            _containers.Insert(gun, _containers.GetContainer(args.Action, "mime-finger-gun"));
            Comp<MimeFingerGunItemComponent>(gun).Holder = null;
            _actions.SetToggled(args.Action.Owner, false);
            _chat.TrySendInGameICMessage(args.Performer, "Holsters their finger gun", InGameICChatType.Emote, ChatTransmitRange.Normal);
        }
        else
        {
            if (_hands.EnumerateHeld(args.Performer).Any(HasComp<MimeLassoItemComponent>))
            {
                _popup.PopupEntity("-NR-Put away your invisible lasso first.", args.Performer, args.Performer);
                return;
            }

            if (_hands.GetActiveItem(args.Performer) != null)
            {
                _popup.PopupEntity("Your hand must be empty first", args.Performer, args.Performer);
                return;
            }

            if (!_hands.TryPickupAnyHand(args.Performer, gun))
                return;

            var unremoveable = EnsureComp<UnremoveableComponent>(gun);
            unremoveable.DeleteOnDrop = false;
            Dirty(gun, unremoveable);
            Comp<MimeFingerGunItemComponent>(gun).Holder = args.Performer;
            _actions.SetToggled(args.Action.Owner, true);
            _chat.TrySendInGameICMessage(args.Performer, "Raises a finger in the shape of a gun", InGameICChatType.Emote, ChatTransmitRange.Normal);
        }

        args.Handled = true;
    }

    private void OnFingerGunShot(Entity<MimeFingerGunItemComponent> ent, ref GunShotEvent args)
    {
        if (_net.IsServer)
            _chat.TrySendInGameICMessage(args.User, "Fires the finger gun!", InGameICChatType.Emote, ChatTransmitRange.Normal);
    }

    private void OnHookHit(Entity<InvisibleHookProjectileComponent> ent, ref ProjectileHitEvent args)
    {
        if (!_net.IsServer || args.Shooter is not { } shooter || !CanMime(shooter) ||
            args.Target == shooter || !HasComp<MobStateComponent>(args.Target) ||
            !TryComp<PhysicsComponent>(args.Target, out _))
            return;

        _chat.TrySendInGameICMessage(shooter, "pulls on an invisible rope!", InGameICChatType.Emote, ChatTransmitRange.Normal);
        if (_stun.TryKnockdown(args.Target, TimeSpan.FromSeconds(2), force: true))
            _chat.TrySendInGameICMessage(args.Target, "Is caught by an invisible lasso and falls over!",
                InGameICChatType.Emote, ChatTransmitRange.Normal, ignoreActionBlocker: true);
        if (IsHeldByOther(args.Target, shooter))
            return;

        EndHookPull(args.Target);
        _pulls[args.Target] = new HookPull(shooter, HookPullCycles, _timing.CurTime, false);
        EnsureComp<MimeHookPullingComponent>(shooter);
        _actionBlocker.UpdateCanMove(shooter);
        if (TryComp<PhysicsComponent>(shooter, out var shooterBody))
            _physics.SetLinearVelocity(shooter, Vector2.Zero, body: shooterBody);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (!_net.IsServer)
            return;

        ReturnLooseFingerGuns();
        ReturnLooseLassos();

        if (_pulls.Count == 0)
            return;

        foreach (var (target, pull) in _pulls.ToArray())
        {
            if (Deleted(target) || Deleted(pull.Mime) || !TryComp<PhysicsComponent>(target, out var body))
            {
                EndHookPull(target);
                continue;
            }

            if (TryComp<PhysicsComponent>(pull.Mime, out var mimeBody))
                _physics.SetLinearVelocity(pull.Mime, Vector2.Zero, body: mimeBody);

            if (IsHeldByOther(target, pull.Mime))
            {
                _physics.SetLinearVelocity(target, Vector2.Zero, body: body);
                EndHookPull(target);
                continue;
            }

            if (_timing.CurTime < pull.NextStep)
                continue;

            if (pull.Stopping)
            {
                _physics.SetLinearVelocity(target, Vector2.Zero, body: body);
                if (pull.Remaining <= 0)
                    EndHookPull(target);
                else
                    _pulls[target] = pull with { NextStep = _timing.CurTime + HookPauseTime, Stopping = false };
                continue;
            }

            var targetMap = _transform.ToMapCoordinates(Transform(target).Coordinates);
            var mimeMap = _transform.ToMapCoordinates(Transform(pull.Mime).Coordinates);
            if (targetMap.MapId != mimeMap.MapId)
            {
                EndHookPull(target);
                continue;
            }

            var delta = mimeMap.Position - targetMap.Position;
            if (delta.LengthSquared() < 1f)
            {
                EndHookPull(target);
                continue;
            }

            _physics.SetLinearVelocity(target, Vector2.Zero, body: body);
            _physics.ApplyLinearImpulse(target, Vector2.Normalize(delta) * body.Mass * 9.5f, body: body);
            _pulls[target] = pull with { Remaining = pull.Remaining - 1, NextStep = _timing.CurTime + HookPullTime, Stopping = true };
        }
    }

    private void OnHookPullingCanMove(Entity<MimeHookPullingComponent> ent, ref UpdateCanMoveEvent args)
    {
        args.Cancel();
    }

    private void EndHookPull(EntityUid target)
    {
        if (!_pulls.Remove(target, out var pull) || Deleted(pull.Mime) ||
            _pulls.Values.Any(active => active.Mime == pull.Mime))
            return;

        RemComp<MimeHookPullingComponent>(pull.Mime);
        _actionBlocker.UpdateCanMove(pull.Mime);
    }

    private bool IsHeldByOther(EntityUid target, EntityUid mime) =>
        _pulling.GetPuller(target) is { } puller && puller != mime;

    private void ReturnLooseFingerGuns()
    {
        var query = EntityQueryEnumerator<MimeFingerGunItemComponent>();
        while (query.MoveNext(out var gun, out var fingerGun))
        {
            if (TerminatingOrDeleted(gun))
                continue;

            if (fingerGun.Action is not { } action || TerminatingOrDeleted(action) ||
                !_containers.TryGetContainer(action, "mime-finger-gun", out var container))
            {
                QueueDel(gun);
                continue;
            }

            if (container.Contains(gun))
                continue;

            if (fingerGun.Holder is { } holder && !Deleted(holder) && _hands.IsHolding(holder, gun))
                continue;

            RemComp<UnremoveableComponent>(gun);
            _containers.Insert(gun, container);
            fingerGun.Holder = null;
            _actions.SetToggled(action, false);
        }
    }

    private sealed record HookPull(EntityUid Mime, int Remaining, TimeSpan NextStep, bool Stopping);
}
