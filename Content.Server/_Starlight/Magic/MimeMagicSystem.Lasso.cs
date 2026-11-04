using System.Linq;
using Content.Shared._Starlight.Magic.Mime;
using Content.Shared.Chat;
using Content.Shared.Interaction.Components;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Containers;

namespace Content.Server._Starlight.Magic;

public sealed partial class MimeMagicSystem
{
    private void OnLassoActionInit(Entity<MimeLassoActionComponent> ent, ref MapInitEvent args)
    {
        if (!_net.IsServer)
            return;

        _containers.EnsureContainer<Container>(ent, "mime-lasso");
        ent.Comp.Lasso = SpawnInContainerOrDrop("WeaponMimeInvisibleLasso", ent, "mime-lasso");
        if (ent.Comp.Lasso is { } lasso)
            Comp<MimeLassoItemComponent>(lasso).Action = ent;
        Dirty(ent);
    }

    private void OnLassoActionShutdown(Entity<MimeLassoActionComponent> ent, ref ComponentShutdown args)
    {
        if (_net.IsServer && ent.Comp.Lasso is { } lasso && !Deleted(lasso))
            QueueDel(lasso);
    }

    private void OnToggleLasso(ToggleMimeLassoActionEvent args)
    {
        if (args.Handled || !_net.IsServer || !CanMime(args.Performer) ||
            !TryComp<MimeLassoActionComponent>(args.Action, out var comp) ||
            comp.Lasso is not { } lasso || Deleted(lasso))
            return;

        if (_hands.IsHolding(args.Performer, lasso))
        {
            RemComp<UnremoveableComponent>(lasso);
            _containers.Insert(lasso, _containers.GetContainer(args.Action, "mime-lasso"));
            Comp<MimeLassoItemComponent>(lasso).Holder = null;
            _actions.SetToggled(args.Action.Owner, false);
            _chat.TrySendInGameICMessage(args.Performer, "puts away the invisible rope.",
                InGameICChatType.Emote, ChatTransmitRange.Normal);
        }
        else
        {
            if (_hands.EnumerateHeld(args.Performer).Any(item => HasComp<MimeFingerGunItemComponent>(item)))
            {
                _popup.PopupEntity("-NR-Put away your finger gun first.", args.Performer, args.Performer);
                return;
            }

            if (_hands.GetActiveItem(args.Performer) != null)
            {
                _popup.PopupEntity("Your hand must be empty first", args.Performer, args.Performer);
                return;
            }

            if (!_hands.TryPickupAnyHand(args.Performer, lasso))
                return;

            var unremoveable = EnsureComp<UnremoveableComponent>(lasso);
            unremoveable.DeleteOnDrop = false;
            Dirty(lasso, unremoveable);
            Comp<MimeLassoItemComponent>(lasso).Holder = args.Performer;
            _actions.SetToggled(args.Action.Owner, true);
            _chat.TrySendInGameICMessage(args.Performer, "unwinds an invisible rope.",
                InGameICChatType.Emote, ChatTransmitRange.Normal);
        }

        args.Handled = true;
    }

    private void OnLassoShot(Entity<MimeLassoItemComponent> ent, ref GunShotEvent args)
    {
        if (!_net.IsServer)
            return;

        _popup.PopupEntity("You throw an invisible lasso.", args.User, args.User);
        _chat.TrySendInGameICMessage(args.User, "throws an invisible lasso!",
            InGameICChatType.Emote, ChatTransmitRange.Normal);
    }

    private void ReturnLooseLassos()
    {
        var query = EntityQueryEnumerator<MimeLassoItemComponent>();
        while (query.MoveNext(out var lasso, out var item))
        {
            if (TerminatingOrDeleted(lasso))
                continue;

            if (item.Action is not { } action || TerminatingOrDeleted(action) ||
                !_containers.TryGetContainer(action, "mime-lasso", out var container))
            {
                QueueDel(lasso);
                continue;
            }

            if (container.Contains(lasso))
                continue;

            if (item.Holder is { } holder && !Deleted(holder) && _hands.IsHolding(holder, lasso))
                continue;

            RemComp<UnremoveableComponent>(lasso);
            _containers.Insert(lasso, container);
            item.Holder = null;
            _actions.SetToggled(action, false);
        }
    }
}
