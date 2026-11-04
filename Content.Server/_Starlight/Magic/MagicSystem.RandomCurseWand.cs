using System.Collections.Generic;
using Content.Shared._Starlight.Magic.Components;
using Content.Shared.Cluwne;
using Content.Shared.Slippery;
using Content.Shared.StepTrigger.Components;
using Content.Shared.StepTrigger.Systems;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Projectiles;

namespace Content.Server.Magic;

public sealed partial class MagicSystem
{
    [Dependency] private StepTriggerSystem _randomCurseStepTrigger = default!;
    // WIP balance: relative weights follow the spellbook's Wizcoin prices. Cluwne is
    // intentionally a single slot in a 103-slot pool (0.97%).
    private static readonly (RandomCurseKind Kind, int Weight)[] RandomCurseWeights =
    [
        (RandomCurseKind.Noir, 20),              // 1 Wizcoin
        (RandomCurseKind.Perspective, 20),       // 1 Wizcoin
        (RandomCurseKind.Slippery, 10),          // 2 Wizcoins
        (RandomCurseKind.Snail, 10),             // 2 Wizcoins
        (RandomCurseKind.Ancient, 10),           // 2 Wizcoins
        (RandomCurseKind.Moods, 10),             // 2 Wizcoins
        (RandomCurseKind.Static, 10),            // 2 Wizcoins
        (RandomCurseKind.Frog, 6),               // 3 Wizcoins
        (RandomCurseKind.Doom, 6),               // 3 Wizcoins
        (RandomCurseKind.Cluwne, 1),             // 5 Wizcoins
    ];

    private enum RandomCurseKind
    {
        Noir, Perspective, Slippery, Snail, Ancient, Moods, Static, Frog, Doom, Cluwne,
    }

    private void InitializeRandomCurseWand()
    {
        SubscribeLocalEvent<WizardRandomCurseWandComponent, AmmoShotEvent>(OnRandomCurseWandShot);
        SubscribeLocalEvent<WizardRandomCurseProjectileComponent, ProjectileHitEvent>(OnRandomCurseProjectileHit);
    }

    private void OnRandomCurseWandShot(Entity<WizardRandomCurseWandComponent> ent, ref AmmoShotEvent args)
    {
        foreach (var projectile in args.FiredProjectiles)
            EnsureComp<WizardRandomCurseProjectileComponent>(projectile).Caster = args.Shooter;
    }

    private void OnRandomCurseProjectileHit(Entity<WizardRandomCurseProjectileComponent> ent, ref ProjectileHitEvent args)
    {
        if (!_wizardMobState.IsAlive(args.Target))
            return;

        var remaining = new List<(RandomCurseKind Kind, int Weight)>(RandomCurseWeights);
        while (remaining.Count > 0)
        {
            var total = 0;
            foreach (var (_, weight) in remaining)
                total += weight;

            var roll = _random.Next(total);
            for (var i = 0; i < remaining.Count; i++)
            {
                var (kind, weight) = remaining[i];
                if (roll >= weight)
                {
                    roll -= weight;
                    continue;
                }

                if (ApplyRandomCurse(kind, args.Target, ent.Comp.Caster))
                    return;

                remaining.RemoveAt(i);
                break;
            }
        }
    }

    private bool ApplyRandomCurse(RandomCurseKind kind, EntityUid target, EntityUid? caster)
    {
        switch (kind)
        {
            case RandomCurseKind.Noir: return TryApplyNoirCurse(target);
            case RandomCurseKind.Perspective: return TryApplyPerspectiveCurse(target);
            case RandomCurseKind.Snail: return TryApplySnailCurse(target);
            case RandomCurseKind.Ancient: return TryApplyAncientCurse(target);
            case RandomCurseKind.Moods: return TryApplyMoodsCurse(target);
            case RandomCurseKind.Static: return TryApplyStaticElectricityCurse(target);
            case RandomCurseKind.Doom: return TryApplyDoomCurse(target, caster);
            case RandomCurseKind.Frog:
                if (_wizardPolymorph.PolymorphEntity(target, "WizardFrogCurse") is not { } frog)
                    return false;
                var curse = EnsureComp<FrogCurseComponent>(frog);
                _wizardActions.AddAction(frog, ref curse.IndicatorAction, "ActionWizardFrogCurseIndicator");
                PlayFrogCurseEffects(frog);
                return true;
            case RandomCurseKind.Slippery:
                if (HasComp<SlipperyComponent>(target)) return false;
                EnsureComp<SlipperyComponent>(target);
                var trigger = EnsureComp<StepTriggerComponent>(target);
                _randomCurseStepTrigger.SetRequiredTriggerSpeed(target, -1, trigger);
                EnsureComp<WizardSlipperyCurseComponent>(target);
                return true;
            case RandomCurseKind.Cluwne:
                if (HasComp<CluwneComponent>(target)) return false;
                EnsureComp<CluwneComponent>(target);
                EnsureComp<WizardCluwneCurseComponent>(target);
                return true;
            default: return false;
        }
    }
}
