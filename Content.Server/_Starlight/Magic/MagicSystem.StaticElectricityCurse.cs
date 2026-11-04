using Content.Server.Electrocution;
using Content.Shared._Starlight.Magic.Components;
using Content.Shared._Starlight.Magic.Events;
using Content.Shared.Electrocution;
using Content.Shared.DoAfter;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Mobs.Components;
using Content.Shared.Nutrition;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;
using Content.Shared.Verbs;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Physics.Events;

namespace Content.Server.Magic;

public sealed partial class MagicSystem
{
    private const string StaticElectricityIndicator = "ActionWizardStaticElectricityIndicator";
    private static readonly TimeSpan StaticElectricityShockTime = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan StaticElectricityTouchCooldown = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan StaticElectricityEatTime = TimeSpan.FromSeconds(2);
    private const int StaticElectricityShockDamage = 1;

    [Dependency] private ElectrocutionSystem _wizardElectrocution = default!;
    [Dependency] private IngestionSystem _wizardIngestion = default!;
    [Dependency] private SharedAudioSystem _wizardAudio = default!;

    private static readonly SoundSpecifier StaticShockSound = new SoundPathSpecifier("/Audio/_Starlight/Magic/lightningshock.ogg");
    private static readonly SoundSpecifier GloveEatingSound = new SoundCollectionSpecifier("eating");

    private void InitializeStaticElectricityCurse()
    {
        SubscribeLocalEvent<StaticElectricitySpellEvent>(OnStaticElectricitySpell);
        SubscribeLocalEvent<StaticElectricityCurseComponent, ContactInteractionEvent>(OnStaticElectricityContact);
        SubscribeLocalEvent<StaticElectricityCurseComponent, StartCollideEvent>(OnStaticElectricityCollide);
        SubscribeLocalEvent<StaticElectricityCurseComponent, InteractUsingEvent>(OnStaticElectricityForceFeed);
        SubscribeLocalEvent<StaticElectricityCurseComponent, StaticElectricityGloveDoAfterEvent>(OnStaticElectricityGloveEaten);
        SubscribeLocalEvent<StaticElectricityCurseComponent, StaticElectricityForceFeedDoAfterEvent>(OnStaticElectricityForceFed);
        SubscribeLocalEvent<StaticElectricityCurseComponent, ComponentShutdown>(OnStaticElectricityShutdown);
        SubscribeLocalEvent<StaticElectricityCurativeGloveComponent, GetVerbsEvent<AlternativeVerb>>(OnStaticElectricityGloveVerbs);
        SubscribeLocalEvent<StaticElectricityCurativeGloveComponent, IngestedEvent>(OnStaticElectricityGloveIngested);
    }

    private void OnStaticElectricitySpell(StaticElectricitySpellEvent args)
    {
        if (args.Handled || !PassesSpellPrerequisites(args.Action, args.Performer))
            return;

        args.Handled = TryApplyStaticElectricityCurse(args.Target);
    }

    public bool TryApplyStaticElectricityCurse(EntityUid target)
    {
        if (!_wizardMobState.IsAlive(target) || HasComp<StaticElectricityCurseComponent>(target))
            return false;

        var curse = EnsureComp<StaticElectricityCurseComponent>(target);
        _wizardActions.AddAction(target, ref curse.IndicatorAction, StaticElectricityIndicator);
        return true;
    }

    private void OnStaticElectricityContact(Entity<StaticElectricityCurseComponent> ent, ref ContactInteractionEvent args)
    {
        TryStaticElectricityShock(ent, args.Other);
    }

    private void OnStaticElectricityCollide(Entity<StaticElectricityCurseComponent> ent, ref StartCollideEvent args)
    {
        TryStaticElectricityShock(ent, args.OtherEntity);
    }

    private void TryStaticElectricityShock(Entity<StaticElectricityCurseComponent> ent, EntityUid toucher)
    {
        if (toucher == ent.Owner || TerminatingOrDeleted(toucher))
            return;

        var now = _wizardTiming.CurTime;
        if (ent.Comp.NextShockByToucher.TryGetValue(toucher, out var nextShock) && nextShock > now)
            return;

        if (!HasComp<MobStateComponent>(toucher))
        {
            PlayStaticElectricityEffects(toucher);
            ent.Comp.NextShockByToucher[toucher] = now + StaticElectricityTouchCooldown;
            return;
        }

        if (!_wizardMobState.IsAlive(toucher))
            return;

        if (_wizardElectrocution.TryDoElectrocution(toucher, ent.Owner,
                StaticElectricityShockDamage, StaticElectricityShockTime, refresh: false))
        {
            PlayStaticElectricityEffects(toucher);
            ent.Comp.NextShockByToucher[toucher] = now + StaticElectricityTouchCooldown;
        }
    }

    private void PlayStaticElectricityEffects(EntityUid touched)
    {
        Spawn("EffectSparks", Transform(touched).Coordinates);
        _wizardAudio.PlayPvs(StaticShockSound, touched);
    }

    private void OnStaticElectricityGloveVerbs(Entity<StaticElectricityCurativeGloveComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanInteract || !args.CanAccess ||
            !HasComp<StaticElectricityCurseComponent>(args.User) ||
            !IsCurativeGlove(ent.Owner) ||
            Transform(ent.Owner).ParentUid != args.User)
            return;

        var user = args.User;
        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("wizard-static-electricity-eat-gloves"),
            Act = () => TryStartEatingInsulatedGloves(user, ent.Owner),
            Priority = 3,
        });
    }

    private bool IsCurativeGlove(EntityUid gloves)
    {
        if (!HasComp<EdibleComponent>(gloves))
            return false;

        // The curse will accept most insulated gloves as a worthy sacrifice, even chameleon insulated if you really want to eat those
        return MetaData(gloves).EntityPrototype?.ID is
            "ClothingHandsGlovesColorYellow" or
            "ClothingHandsGlovesColorYellowBudget" or
            "ClothingHandsGlovesFingerlessInsulated" or
            "ClothingHandsGlovesFingerlessBudget" or
            "ClothingHandsGlovesInsulatedChameleon";
    }

    private void TryStartEatingInsulatedGloves(EntityUid user, EntityUid gloves)
    {
        if (!HasComp<StaticElectricityCurseComponent>(user) ||
            !IsCurativeGlove(gloves) ||
            Transform(gloves).ParentUid != user ||
            !_wizardIngestion.HasMouthAvailable(user, user))
            return;

        var doAfter = new DoAfterArgs(EntityManager, user, StaticElectricityEatTime,
            new StaticElectricityGloveDoAfterEvent(), user, gloves, gloves)
        {
            BreakOnDamage = true,
            BreakOnMove = true,
            NeedHand = true,
        };
        _wizardDoAfter.TryStartDoAfter(doAfter);
    }

    private void OnStaticElectricityForceFeed(Entity<StaticElectricityCurseComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || args.User == ent.Owner || !IsCurativeGlove(args.Used) ||
            Transform(args.Used).ParentUid != args.User ||
            !_wizardIngestion.HasMouthAvailable(args.User, ent.Owner))
            return;

        var doAfter = new DoAfterArgs(EntityManager, args.User, StaticElectricityEatTime,
            new StaticElectricityForceFeedDoAfterEvent(), ent.Owner, ent.Owner, args.Used)
        {
            BreakOnMove = true,
            NeedHand = true,
        };
        args.Handled = _wizardDoAfter.TryStartDoAfter(doAfter);
    }

    private void OnStaticElectricityGloveEaten(Entity<StaticElectricityCurseComponent> ent,
        ref StaticElectricityGloveDoAfterEvent args)
    {
        if (args.Cancelled || args.Args.Used is not { } gloves ||
            !IsCurativeGlove(gloves) ||
            Transform(gloves).ParentUid != ent.Owner ||
            !_wizardIngestion.HasMouthAvailable(ent.Owner, ent.Owner))
            return;

        _wizardAudio.PlayPvs(GloveEatingSound, ent.Owner);
        QueueDel(gloves);
        RemComp<StaticElectricityCurseComponent>(ent.Owner);
    }

    private void OnStaticElectricityForceFed(Entity<StaticElectricityCurseComponent> ent,
        ref StaticElectricityForceFeedDoAfterEvent args)
    {
        if (args.Cancelled || args.User == ent.Owner || args.Args.Used is not { } gloves ||
            !IsCurativeGlove(gloves) || Transform(gloves).ParentUid != args.User ||
            !_wizardIngestion.HasMouthAvailable(args.User, ent.Owner))
            return;

        _wizardAudio.PlayPvs(GloveEatingSound, ent.Owner);
        QueueDel(gloves);
        RemComp<StaticElectricityCurseComponent>(ent.Owner);
    }

    private void OnStaticElectricityGloveIngested(Entity<StaticElectricityCurativeGloveComponent> ent, ref IngestedEvent args)
    {
        if (HasComp<StaticElectricityCurseComponent>(args.Target) &&
            IsCurativeGlove(ent.Owner) &&
            TryComp<EdibleComponent>(ent.Owner, out var edible) && _wizardIngestion.IsEmpty((ent.Owner, edible)))
            RemComp<StaticElectricityCurseComponent>(args.Target);
    }

    private void OnStaticElectricityShutdown(Entity<StaticElectricityCurseComponent> ent, ref ComponentShutdown args)
    {
        if (ent.Comp.IndicatorAction is { } action)
            _wizardActions.RemoveAction(action);
    }
}
