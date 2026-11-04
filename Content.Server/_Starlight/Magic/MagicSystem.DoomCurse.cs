using Content.Shared._Starlight.Magic.Components;
using Content.Shared._Starlight.Magic.Events;
using Content.Shared.Buckle.Components;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.Interaction.Events;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Nutrition;
using Content.Shared.Nutrition.Components;
using Content.Shared.Stunnable;
using Content.Shared.Throwing;
using Content.Shared.Popups;
using Content.Shared.Weapons.Melee.Events;
using Robust.Shared.Audio;
using Robust.Shared.Player;
using Content.Server.Popups;

namespace Content.Server.Magic;

public sealed partial class MagicSystem
{
    private const string DoomIndicator = "ActionWizardDoomCurseIndicator";
    private static readonly TimeSpan DoomDuration = TimeSpan.FromSeconds(60);
    private static readonly SoundSpecifier DoomBassSound = new SoundPathSpecifier("/Audio/_Starlight/Magic/doom_bass_60.ogg");
    private static readonly TimeSpan DoomSitTime = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan DoomLieTime = TimeSpan.FromSeconds(5);

    [Dependency] private MetaDataSystem _doomMetadata = default!;
    [Dependency] private MobThresholdSystem _doomThresholds = default!;
    [Dependency] private DamageableSystem _doomDamage = default!;
    [Dependency] private SharedHandsSystem _doomHands = default!;
    [Dependency] private PopupSystem _doomPopup = default!;

    private void InitializeDoomCurse()
    {
        SubscribeLocalEvent<DoomCurseSpellEvent>(OnDoomCurseSpell);
        SubscribeLocalEvent<DoomCurseComponent, InteractionSuccessEvent>(OnDoomHugged);
        SubscribeLocalEvent<DoomCurseComponent, InteractionPerformedEvent>(OnDoomHug);
        SubscribeLocalEvent<DoomCurseComponent, IngestedByUserEvent>(OnDoomIngested);
        SubscribeLocalEvent<DoomCurseComponent, ThrowEvent>(OnDoomThrow);
        SubscribeLocalEvent<DoomCurseComponent, MeleeHitEvent>(OnDoomMeleeHit);
        SubscribeLocalEvent<DoomCurseComponent, AttackedEvent>(OnDoomAttacked);
        SubscribeLocalEvent<DoomCurseComponent, ComponentShutdown>(OnDoomCurseShutdown);
    }

    private void OnDoomCurseSpell(DoomCurseSpellEvent args)
    {
        if (args.Handled || !PassesSpellPrerequisites(args.Action, args.Performer))
            return;
        args.Handled = TryApplyDoomCurse(args.Target, args.Performer);
    }

    public bool TryApplyDoomCurse(EntityUid target, EntityUid? caster = null)
    {
        if (!_wizardMobState.IsAlive(target) || !HasComp<ActorComponent>(target) ||
            HasComp<DoomCurseComponent>(target))
            return false;

        var curse = EnsureComp<DoomCurseComponent>(target);
        var objectives = Enum.GetValues<DoomObjective>();
        curse.Objective = objectives[_random.Next(objectives.Length)];
        curse.Caster = caster;
        curse.ExpiresAt = _wizardTiming.CurTime + DoomDuration;
        _wizardActions.AddAction(target, ref curse.IndicatorAction, DoomIndicator);
        if (curse.IndicatorAction is { } indicator)
        {
            _wizardActions.SetCooldown(indicator, _wizardTiming.CurTime, curse.ExpiresAt);
            _doomMetadata.SetEntityDescription(indicator,
                Loc.GetString("wizard-doom-indicator-description", ("objective", Loc.GetString(DoomObjectiveLoc(curse.Objective)))));
        }

        UpdateDoomDigits(target, curse);
        curse.AudioStream = _wizardAudio.PlayGlobal(DoomBassSound, target)?.Entity;
        Dirty(target, curse);
        return true;
    }

    private static string DoomObjectiveLoc(DoomObjective objective) => objective switch
    {
        DoomObjective.Hug => "wizard-doom-hug",
        DoomObjective.GetHugged => "wizard-doom-get-hugged",
        DoomObjective.Sit => "wizard-doom-sit",
        DoomObjective.LieDown => "wizard-doom-lie",
        DoomObjective.Eat => "wizard-doom-eat",
        DoomObjective.Drink => "wizard-doom-drink",
        DoomObjective.GetPunched => "wizard-doom-get-punched",
        DoomObjective.Throw => "wizard-doom-throw",
        DoomObjective.Punch => "wizard-doom-punch",
        _ => "wizard-doom-lie",
    };

    private void OnDoomHugged(Entity<DoomCurseComponent> ent, ref InteractionSuccessEvent args)
    {
        if (ent.Comp.Objective == DoomObjective.GetHugged && args.User != ent.Owner &&
            HasComp<ActorComponent>(args.User) && _wizardMobState.IsAlive(args.User))
            CompleteDoomObjective(ent);
    }

    private void OnDoomHug(Entity<DoomCurseComponent> ent, ref InteractionPerformedEvent args)
    {
        if (ent.Comp.Objective == DoomObjective.Hug && args.Target != ent.Owner &&
            HasComp<ActorComponent>(args.Target) && _wizardMobState.IsAlive(args.Target))
            CompleteDoomObjective(ent);
    }

    private void OnDoomIngested(Entity<DoomCurseComponent> ent, ref IngestedByUserEvent args)
    {
        var type = CompOrNull<EdibleComponent>(args.Food)?.Edible.Id;
        if ((ent.Comp.Objective == DoomObjective.Eat && type == "Food") ||
            (ent.Comp.Objective == DoomObjective.Drink && type == "Drink"))
            CompleteDoomObjective(ent);
    }

    private void OnDoomThrow(Entity<DoomCurseComponent> ent, ref ThrowEvent args)
    {
        if (ent.Comp.Objective == DoomObjective.Throw && args.Thrown != ent.Owner)
            CompleteDoomObjective(ent);
    }

    private void OnDoomAttacked(Entity<DoomCurseComponent> ent, ref AttackedEvent args)
    {
        if (ent.Comp.Objective == DoomObjective.GetPunched && args.User != ent.Owner &&
            args.Used == args.User && _wizardMobState.IsAlive(args.User))
            CompleteDoomObjective(ent);
    }

    private void OnDoomMeleeHit(Entity<DoomCurseComponent> ent, ref MeleeHitEvent args)
    {
        if (ent.Comp.Objective != DoomObjective.Punch || args.User != ent.Owner ||
            args.Weapon != ent.Owner || !args.IsHit)
            return;

        foreach (var hit in args.HitEntities)
        {
            if (hit != ent.Owner && HasComp<ActorComponent>(hit) && _wizardMobState.IsAlive(hit))
            {
                CompleteDoomObjective(ent);
                return;
            }
        }
    }

    private void UpdateDoomCurse()
    {
        var query = EntityQueryEnumerator<DoomCurseComponent>();
        while (query.MoveNext(out var uid, out var curse))
        {
            if (!_wizardMobState.IsAlive(uid))
            {
                RemComp<DoomCurseComponent>(uid);
                continue;
            }

            if (_wizardTiming.CurTime >= curse.ExpiresAt)
            {
                if (_doomThresholds.TryGetThresholdForState(uid, MobState.Dead, out var threshold))
                {
                    var needed = threshold.Value - _doomDamage.GetTotalDamage(uid);
                    if (needed > 0)
                    {
                        var damage = new DamageSpecifier();
                        var type = _doomDamage.CanBeDamagedBy(uid, "Asphyxiation") ? "Asphyxiation" : "Heat";
                        damage.DamageDict.Add(type, needed);
                        _doomDamage.TryChangeDamage(uid, damage, ignoreResistances: true, ignoreGlobalModifiers: true);
                    }
                }
                RemComp<DoomCurseComponent>(uid);
                continue;
            }

            UpdateDoomDigits(uid, curse);

            if (!HasComp<DoomCurseComponent>(uid))
                continue;
            if (curse.Objective == DoomObjective.Sit)
                UpdateDoomStationaryObjective(uid, curse, CompOrNull<BuckleComponent>(uid)?.BuckledTo is { } chair &&
                    CompOrNull<StrapComponent>(chair)?.Position == StrapPosition.Stand, DoomSitTime, true);
            else if (curse.Objective == DoomObjective.LieDown)
                UpdateDoomStationaryObjective(uid, curse, HasComp<KnockedDownComponent>(uid), DoomLieTime, false);
        }
    }

    private void UpdateDoomStationaryObjective(EntityUid uid, DoomCurseComponent curse, bool active, TimeSpan duration, bool sitting)
    {
        var since = sitting ? curse.SittingSince : curse.LyingSince;
        if (!active)
            since = null;
        else if (since == null)
            since = _wizardTiming.CurTime;
        else if (_wizardTiming.CurTime - since >= duration)
        {
            CompleteDoomObjective((uid, curse));
            return;
        }
        if (sitting)
            curse.SittingSince = since;
        else
            curse.LyingSince = since;
    }

    private void CompleteDoomObjective(Entity<DoomCurseComponent> ent)
    {
        RemComp<DoomCurseComponent>(ent.Owner);
    }

    private void UpdateDoomDigits(EntityUid target, DoomCurseComponent curse)
    {
        var seconds = Math.Clamp((int) Math.Ceiling((curse.ExpiresAt - _wizardTiming.CurTime).TotalSeconds), 0, 60);
        if (seconds == curse.LastShownSeconds)
            return;

        _doomPopup.PopupEntity(seconds.ToString(), target, target, PopupType.SmallCaution);
        if (curse.Caster is { } caster && caster != target && !TerminatingOrDeleted(caster))
            _doomPopup.PopupEntity(seconds.ToString(), target, caster, PopupType.SmallCaution);
        curse.LastShownSeconds = seconds;
    }

    private void OnDoomCurseShutdown(Entity<DoomCurseComponent> ent, ref ComponentShutdown args)
    {
        _wizardAudio.Stop(ent.Comp.AudioStream);
        if (ent.Comp.IndicatorAction is { } indicator)
            _wizardActions.RemoveAction(indicator);
    }
}
