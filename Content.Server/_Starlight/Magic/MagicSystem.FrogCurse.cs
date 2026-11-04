using Content.Server.Polymorph.Components;
using Content.Shared._Starlight.Magic.Components;
using Content.Shared._Starlight.Magic.Events;
using Content.Shared.Damage.Systems;
using Content.Shared.Interaction.Events;
using Content.Shared.Mobs.Components;
using Robust.Shared.Audio;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server.Magic;

public sealed partial class MagicSystem
{
    private const string FrogCursePolymorph = "WizardFrogCurse";
    private const string FrogCurseIndicator = "ActionWizardFrogCurseIndicator";
    private static readonly SoundSpecifier FrogCroakSound = new SoundPathSpecifier("/Audio/_Starlight/Magic/frog_croak.ogg");
    private static readonly SoundSpecifier FrogCroak2Sound = new SoundPathSpecifier("/Audio/_Starlight/Magic/frog_croak2.ogg");

    private void InitializeFrogCurse()
    {
        SubscribeLocalEvent<FrogCurseSpellEvent>(OnFrogCurseSpell);
        SubscribeLocalEvent<FrogCurseComponent, InteractionSuccessEvent>(OnFrogCurseHugged);
        SubscribeLocalEvent<FrogCurseComponent, DamageChangedEvent>(OnFrogCurseDamaged);
        SubscribeLocalEvent<FrogCurseComponent, ComponentShutdown>(OnFrogCurseShutdown);
    }

    private void OnFrogCurseSpell(FrogCurseSpellEvent args)
    {
        if (args.Handled ||
            !PassesSpellPrerequisites(args.Action, args.Performer) ||
            !_wizardMobState.IsAlive(args.Target) ||
            HasComp<PolymorphedEntityComponent>(args.Target))
            return;

        if (_wizardPolymorph.PolymorphEntity(args.Target, FrogCursePolymorph) is not { } frog)
            return;

        var curse = EnsureComp<FrogCurseComponent>(frog);
        _wizardActions.AddAction(frog, ref curse.IndicatorAction, FrogCurseIndicator);
        PlayFrogCurseEffects(frog);
        args.Handled = true;
    }

    private void PlayFrogCurseEffects(EntityUid frog)
    {
        Spawn("WizardMinorIllusionPuff", Transform(frog).Coordinates);
        _wizardAudio.PlayPvs(FrogCroakSound, frog);
        Timer.Spawn(TimeSpan.FromSeconds(1), () =>
        {
            if (TerminatingOrDeleted(frog) || !HasComp<FrogCurseComponent>(frog))
                return;

            _wizardAudio.PlayPvs(FrogCroak2Sound, frog);
        });
    }

    private void OnFrogCurseHugged(Entity<FrogCurseComponent> ent, ref InteractionSuccessEvent args)
    {
        if (args.User == ent.Owner ||
            !HasComp<ActorComponent>(args.User) ||
            !HasComp<MobStateComponent>(args.User) ||
            !_wizardMobState.IsAlive(args.User))
            return;

        RevertFrogCurse(ent);
    }

    private void OnFrogCurseDamaged(Entity<FrogCurseComponent> ent, ref DamageChangedEvent args)
    {
        if (args.DamageIncreased)
            RevertFrogCurse(ent);
    }

    private void RevertFrogCurse(Entity<FrogCurseComponent> ent)
    {
        if (TryComp<PolymorphedEntityComponent>(ent, out var polymorph) && !polymorph.Reverted)
            _wizardPolymorph.Revert((ent.Owner, polymorph));
    }

    private void OnFrogCurseShutdown(Entity<FrogCurseComponent> ent, ref ComponentShutdown args)
    {
        if (ent.Comp.IndicatorAction is { } action)
            _wizardActions.RemoveAction(action);
    }
}
