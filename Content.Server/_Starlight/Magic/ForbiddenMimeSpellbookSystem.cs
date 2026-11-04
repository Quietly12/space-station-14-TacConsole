using System.Linq;
using Content.Server.Popups;
using Content.Shared._Starlight.Magic.Mime;
using Content.Shared.Abilities.Mime;
using Content.Shared.Actions;
using Content.Shared.Actions.Components;
using Content.Shared.Interaction.Events;
using Content.Shared.Magic;
using Robust.Shared.Containers;

namespace Content.Server._Starlight.Magic;

/// <summary>
/// Restricts the forbidden book to mimes and prevents additional copies from granting duplicate actions.
/// </summary>
public sealed class ForbiddenMimeSpellbookSystem : EntitySystem
{
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private ActionContainerSystem _actionContainer = default!;
    [Dependency] private SharedContainerSystem _containers = default!;

    private static readonly HashSet<string> MimeActions =
    [
        "ActionMimeInvisibleBarrier",
        "ActionMimeFingerGun",
        "ActionMimeInvisibleHook",
    ];

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ForbiddenMimeSpellbookComponent, UseInHandEvent>(OnUse, before: [typeof(SpellbookSystem)]);
        SubscribeLocalEvent<ForbiddenMimeSpellbookComponent, SpellbookDoAfterEvent>(OnLearn, before: [typeof(SpellbookSystem)]);
    }

    private void OnUse(Entity<ForbiddenMimeSpellbookComponent> ent, ref UseInHandEvent args)
    {
        if (args.Handled)
            return;

        if (!TryComp<MimePowersComponent>(args.User, out var mime))
        {
            _popup.PopupEntity("Only a mime can learn from this book.", args.User, args.User);
            args.Handled = true;
            return;
        }

        if (mime.VowBroken)
        {
            _popup.PopupEntity("The book requires an unbroken vow of silence.", args.User, args.User);
            args.Handled = true;
            return;
        }

        var known = GetKnownActions(args.User);
        if (known.IsSupersetOf(MimeActions))
        {
            _popup.PopupEntity("There are no more secrets left to learn", args.User, args.User);
            args.Handled = true;
        }
    }

    private void OnLearn(Entity<ForbiddenMimeSpellbookComponent> ent, ref SpellbookDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled)
            return;

        var user = args.Args.User;
        if (!TryComp<MimePowersComponent>(user, out var mime) || mime.VowBroken)
        {
            args.Handled = true;
            return;
        }

        var known = GetKnownActions(user);
        if (known.IsSupersetOf(MimeActions))
        {
            args.Handled = true;
            return;
        }

        var bookActions = _containers.GetContainer(ent, ActionsContainerComponent.ContainerId);
        foreach (var action in bookActions.ContainedEntities.ToArray())
        {
            var id = Comp<MetaDataComponent>(action).EntityPrototype?.ID;
            if (id == null || !MimeActions.Contains(id))
                continue;

            if (!known.Add(id))
            {
                _actionContainer.RemoveAction(action);
                QueueDel(action);
            }
        }
    }

    private HashSet<string> GetKnownActions(EntityUid user)
    {
        var known = new HashSet<string>();
        foreach (var action in _actions.GetActions(user).ToArray())
        {
            var id = Comp<MetaDataComponent>(action).EntityPrototype?.ID;
            if (id == null || !MimeActions.Contains(id))
                continue;

            if (!known.Add(id))
            {
                _actionContainer.RemoveAction(action.Owner);
                QueueDel(action);
            }
        }

        return known;
    }
}
