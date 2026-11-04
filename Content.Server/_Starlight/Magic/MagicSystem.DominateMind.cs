using Content.Server.Antag;
using Content.Server.Roles;
using Content.Shared._Starlight.Magic.Components;
using Content.Shared._Starlight.Magic.Events;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Content.Shared.Mindshield.Components;
using Content.Shared.Roles.Components;
using Robust.Shared.Audio;
using Robust.Shared.Player;

namespace Content.Server.Magic;

public sealed partial class MagicSystem
{
    private const string WizardDominationRole = "MindRoleWizardDominated";
    private const string WizardDominationIndicator = "ActionWizardDominationIndicator";
    private static readonly SoundSpecifier WizardAllegianceStinger = new SoundPathSpecifier("/Audio/_Starlight/Magic/wizard_stinger.ogg");

    [Dependency] private RoleSystem _wizardDominationRoles = default!;
    [Dependency] private AntagSelectionSystem _wizardDominationBriefing = default!;

    private void InitializeDominateMind()
    {
        SubscribeLocalEvent<WizardDominateMindSpellEvent>(OnDominateMindSpell);
        SubscribeLocalEvent<WizardDominationComponent, ComponentShutdown>(OnDominationRemoved);
        SubscribeLocalEvent<WizardDominationComponent, MindGotAddedEvent>(OnDominatedMindAdded);
        SubscribeLocalEvent<WizardDominationRoleComponent, GetBriefingEvent>(OnDominationBriefing);
    }

    private void OnDominateMindSpell(WizardDominateMindSpellEvent args)
    {
        if (args.Handled || !PassesSpellPrerequisites(args.Action, args.Performer))
            return;

        args.Handled = TryDominateMind(args.Performer, args.Target);
    }

    public bool TryDominateMind(EntityUid wizard, EntityUid target)
    {
        if (wizard == target || !_wizardMobState.IsAlive(target) ||
            HasComp<MindShieldComponent>(target) ||
            !HasComp<ActorComponent>(target) ||
            !_mind.TryGetMind(wizard, out var wizardMindId, out _) ||
            !_mind.TryGetMind(target, out var mindId, out var mind) ||
            HasComp<WizardDominationComponent>(mindId) ||
            _wizardDominationRoles.MindHasRole<WizardRoleComponent>(mindId))
            return false;

        _wizardDominationRoles.MindAddRole(mindId, WizardDominationRole, mind, true);

        var dominated = EnsureComp<WizardDominationComponent>(mindId);
        dominated.WizardMind = wizardMindId;
        MarkDominatedBody(target, wizardMindId);
        if (TryComp<ActorComponent>(target, out var actor))
        {
            _wizardAudio.PlayEntity(WizardAllegianceStinger, actor.PlayerSession, target);
            _wizardDominationBriefing.SendBriefing(actor.PlayerSession,
                Loc.GetString("wizard-domination-briefing", ("wizard", GetWizardMasterName(wizardMindId))), null, null);
        }
        return true;
    }

    private void OnDominationBriefing(Entity<WizardDominationRoleComponent> ent, ref GetBriefingEvent args)
    {
        if (!TryComp<WizardDominationComponent>(args.Mind.Owner, out var domination))
            return;

        args.Append(Loc.GetString("wizard-domination-briefing",
            ("wizard", GetWizardMasterName(domination.WizardMind))));
    }

    private string GetWizardMasterName(EntityUid wizardMind, string? fallback = null)
    {
        if (TryComp<MindComponent>(wizardMind, out var mind) && mind.OwnedEntity is { } wizard &&
            !Deleted(wizard))
            return Name(wizard);

        return fallback ?? Loc.GetString("wizard-unknown-master");
    }

    private void UpdateDominateMind()
    {
        var query = EntityQueryEnumerator<WizardDominationComponent, MindComponent>();
        while (query.MoveNext(out var mindId, out _, out var mind))
        {
            if (mind.OwnedEntity is { } body && HasComp<MindShieldComponent>(body))
                RemComp<WizardDominationComponent>(mindId);
        }
    }

    private void OnDominatedMindAdded(Entity<WizardDominationComponent> ent, ref MindGotAddedEvent args)
    {
        if (HasComp<MindShieldComponent>(args.Container.Owner))
            RemComp<WizardDominationComponent>(ent);
        else
            MarkDominatedBody(args.Container.Owner, ent.Comp.WizardMind);
    }

    private void MarkDominatedBody(EntityUid body, EntityUid wizardMind)
    {
        var marker = EnsureComp<WizardDominationIconComponent>(body);
        marker.WizardMind = wizardMind;
        if (marker.IndicatorAction is null)
            _wizardActions.AddAction(body, ref marker.IndicatorAction, WizardDominationIndicator);
        Dirty(body, marker);
    }

    private void OnDominationRemoved(Entity<WizardDominationComponent> ent, ref ComponentShutdown args)
    {
        if (!TerminatingOrDeleted(ent.Owner))
            _wizardDominationRoles.MindRemoveRole<WizardDominationRoleComponent>(ent.Owner);

        if (TryComp<MindComponent>(ent, out var mind) &&
            mind.OwnedEntity is { } body)
        {
            RemComp<WizardDominationIconComponent>(body);
            if (TryComp<ActorComponent>(body, out var actor))
                _wizardDominationBriefing.SendBriefing(actor.PlayerSession,
                    Loc.GetString("wizard-domination-freed"), null, null);
        }
    }
}
