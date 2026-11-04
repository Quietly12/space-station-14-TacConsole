using Content.Server.Actions;
using Content.Shared._Starlight.Roles;
using Content.Shared._Starlight.Magic.Components;
using Content.Shared.Antag;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Content.Shared.Roles;
using Content.Shared.Roles.Components;
using Content.Shared.Tag;
using Robust.Shared.GameStates;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._Starlight.Roles;

/// <summary>
/// Ensures the wizard mob entity has the Wizard tag whenever a WizardRoleComponent or
/// WizardDuelistRoleComponent mind role is assigned, and removes it when both are gone.
/// This guarantees that RestrictByUserTag checks on wizard items always work correctly,
/// regardless of the spawn path (roundstart, midround ghost role, admin force-make-antag, etc.).
/// </summary>
public sealed partial class WizardRoleSystem : EntitySystem
{
    [Dependency] private SharedRoleSystem _roles = default!;
    [Dependency] private ActionsSystem _actions = default!;
    [Dependency] private SharedMindSystem _minds = default!;
    [Dependency] private TagSystem _tag = default!;

    private static readonly ProtoId<TagPrototype> _wizardTag = "Wizard";

    public override void Initialize()
    {
        base.Initialize();
        // Primary path: role is added after the mind already owns an entity.
        SubscribeLocalEvent<RoleAddedEvent>(OnRoleAdded);
        // Fallback path: the mind is transferred into a body after the role was already added
        // (e.g. ghost-role takeover where RoleAddedEvent fires before OwnedEntity is set).
        SubscribeLocalEvent<MindAddedMessage>(OnMindAdded);
        SubscribeLocalEvent<WizardAllyComponent, MindRemovedMessage>(OnMindRemoved);
        SubscribeLocalEvent<WizardAllyComponent, ComponentGetStateAttemptEvent>(OnAllyGetStateAttempt);
        SubscribeLocalEvent<WizardAllyComponent, ComponentStartup>(DirtyWizardAllies);
        SubscribeLocalEvent<WizardApprenticeIconComponent, ComponentGetStateAttemptEvent>(OnApprenticeIconGetStateAttempt);
        SubscribeLocalEvent<WizardDominationIconComponent, MindRemovedMessage>(OnDominatedMindRemoved);
        SubscribeLocalEvent<WizardDominationIconComponent, ComponentGetStateAttemptEvent>(OnDominationIconGetStateAttempt);
        SubscribeLocalEvent<WizardDominationIconComponent, ComponentStartup>(OnDominationIconStartup);
        SubscribeLocalEvent<WizardDominationIconComponent, ComponentShutdown>(OnDominationIconShutdown);
        // RevolutionarySystem already owns ComponentStartup for this component.
        SubscribeLocalEvent<ShowAntagIconsComponent, ComponentInit>(OnShowAntagIconsInit);
        // Removal path: strip the tag when the wizard role is removed, if no wizard role remains.
        SubscribeLocalEvent<RoleRemovedEvent>(OnRoleRemoved);
    }

    private bool IsWizardMind(EntityUid mindId, MindComponent _)
    => _roles.MindHasRole<WizardRoleComponent>(mindId)
        || _roles.MindHasRole<WizardDuelistRoleComponent>(mindId)
        || _roles.MindHasRole<WizardApprenticeRoleComponent>(mindId);

    private void OnRoleAdded(RoleAddedEvent args)
    {
        if (!IsWizardMind(args.MindId, args.Mind))
            return;

        var ownedEntity = args.Mind.OwnedEntity;
        if (ownedEntity == null)
            return;

        _tag.AddTag(ownedEntity.Value, _wizardTag);
        EnsureComp<WizardAllyComponent>(ownedEntity.Value);
        UpdateApprenticeIcon(args.MindId, ownedEntity.Value);
    }

    /// <summary>
    /// Fallback: fires on the mob entity when a mind is transferred into it.
    /// Covers cases where RoleAddedEvent fired before OwnedEntity was assigned.
    /// </summary>
    private void OnMindAdded(MindAddedMessage args)
    {
        if (!IsWizardMind(args.Mind.Owner, args.Mind.Comp))
            return;

        _tag.AddTag(args.Container.Owner, _wizardTag);
        EnsureComp<WizardAllyComponent>(args.Container.Owner);
        UpdateApprenticeIcon(args.Mind.Owner, args.Container.Owner);
    }

    private void UpdateApprenticeIcon(EntityUid mindId, EntityUid body)
    {
        if (_roles.MindHasRole<WizardApprenticeRoleComponent>(mindId))
            EnsureComp<WizardApprenticeIconComponent>(body);
        else
            RemComp<WizardApprenticeIconComponent>(body);
        DirtyWizardAllies();
    }

    private void OnMindRemoved(Entity<WizardAllyComponent> ent, ref MindRemovedMessage args)
    {
        RemComp<WizardAllyComponent>(ent);
        RemComp<WizardApprenticeIconComponent>(ent.Owner);
        _tag.RemoveTag(ent.Owner, _wizardTag);
    }

    private void OnApprenticeIconGetStateAttempt(Entity<WizardApprenticeIconComponent> ent,
        ref ComponentGetStateAttemptEvent args)
    {
        if (args.Player?.AttachedEntity is not { } viewer)
            return;

        args.Cancelled = !HasComp<WizardAllyComponent>(viewer) && !HasComp<ShowAntagIconsComponent>(viewer);
    }

    private void OnAllyGetStateAttempt(Entity<WizardAllyComponent> ent, ref ComponentGetStateAttemptEvent args)
    {
        if (args.Player?.AttachedEntity is not { } viewer)
            return;

        if (HasComp<WizardAllyComponent>(viewer) || HasComp<ShowAntagIconsComponent>(viewer))
            return;

        // A dominated player sees their controlling wizard, but not unrelated wizards or apprentices.
        args.Cancelled = !_minds.TryGetMind(ent.Owner, out var targetMind, out _) ||
            !_roles.MindHasRole<WizardRoleComponent>(targetMind) ||
            !IsInDominationGroup(viewer, targetMind);
    }

    private void OnDominatedMindRemoved(Entity<WizardDominationIconComponent> ent, ref MindRemovedMessage args)
    {
        RemComp<WizardDominationIconComponent>(ent);
    }

    private void OnDominationIconGetStateAttempt(Entity<WizardDominationIconComponent> ent,
        ref ComponentGetStateAttemptEvent args)
    {
        if (args.Player?.AttachedEntity is not { } viewer)
            return;

        args.Cancelled = !HasComp<ShowAntagIconsComponent>(viewer) &&
            !IsInDominationGroup(viewer, ent.Comp.WizardMind);
    }

    private bool IsInDominationGroup(EntityUid viewer, EntityUid wizardMind)
    {
        if (!_minds.TryGetMind(viewer, out var viewerMind, out _))
            return false;

        return viewerMind == wizardMind ||
            TryComp<WizardDominationComponent>(viewerMind, out var domination) &&
            domination.WizardMind == wizardMind;
    }

    private void OnDominationIconStartup(Entity<WizardDominationIconComponent> ent, ref ComponentStartup args)
    {
        DirtyWizardAllies();
        DirtyDominationIcons();
    }

    private void OnDominationIconShutdown(Entity<WizardDominationIconComponent> ent, ref ComponentShutdown args)
    {
        if (ent.Comp.IndicatorAction is { } indicator && !TerminatingOrDeleted(ent.Owner))
            _actions.RemoveAction(indicator);
        DirtyWizardAllies();
        DirtyDominationIcons();
    }

    private void DirtyWizardAllies<T>(EntityUid uid, T comp, ComponentStartup args)
    {
        DirtyWizardAllies();
        DirtyDominationIcons();
        DirtyApprenticeIcons();
    }

    private void OnShowAntagIconsInit(EntityUid uid, ShowAntagIconsComponent comp, ComponentInit args)
    {
        DirtyWizardAllies();
    }

    private void DirtyWizardAllies()
    {
        var query = AllEntityQuery<WizardAllyComponent>();
        while (query.MoveNext(out var ally, out var marker))
            Dirty(ally, marker);
    }

    private void DirtyDominationIcons()
    {
        var query = AllEntityQuery<WizardDominationIconComponent>();
        while (query.MoveNext(out var dominated, out var marker))
            Dirty(dominated, marker);
    }

    private void DirtyApprenticeIcons()
    {
        var query = AllEntityQuery<WizardApprenticeIconComponent>();
        while (query.MoveNext(out var apprentice, out var marker))
            Dirty(apprentice, marker);
    }

    /// <summary>
    /// Fires after a role is removed from the mind. Only strip the tag if
    /// no wizard role of any kind remains on this mind.
    /// </summary>
    private void OnRoleRemoved(RoleRemovedEvent args)
    {
        if (args.Mind.OwnedEntity is { } currentBody &&
            (HasComp<WizardAllyComponent>(currentBody) || HasComp<WizardApprenticeIconComponent>(currentBody)))
            UpdateApprenticeIcon(args.MindId, currentBody);

        if (IsWizardMind(args.MindId, args.Mind))
            return;

        var ownedEntity = args.Mind.OwnedEntity;
        if (ownedEntity == null)
            return;

        _tag.RemoveTag(ownedEntity.Value, _wizardTag);
        RemComp<WizardAllyComponent>(ownedEntity.Value);
        RemComp<WizardApprenticeIconComponent>(ownedEntity.Value);
    }
}
