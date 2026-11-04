using System;
using System.Collections.Generic;
using Content.Server.Ghost.Roles.Components;
using Content.Server.NPC.HTN;
using Content.Shared.Humanoid;
using Content.Shared.Mobs.Components;
using Content.Shared.Movement.Components;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Systems;
using Content.Shared.Prototypes;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;

namespace Content.Server.Magic;

[RegisterComponent]
public sealed partial class WizardRandomCreatureWandComponent : Component;

public sealed partial class MagicSystem
{
    private static readonly string[] RandomCreatureWandFamilies =
    [
        "MobAdultSlimes", "MobAngryBee", "MobBat", "MobBearSpace", "MobBee",
        "MobBoxingKangaroo", "MobButterfly", "MobCarp", "MobCat", "MobChicken",
        "MobCobraSpace", "MobCockroach", "MobCorgi", "MobCow", "MobCrab",
        "MobDuck", "MobFerret", "MobFox", "MobFrog", "MobGiantSpider",
        "MobGoat", "MobGoldBlackKoi", "MobGoose", "MobHamster", "MobKangaroo",
        "MobMoproach", "MobMothroach", "MobMouse", "MobParrot", "MobPenguin",
        "MobPig", "MobPossum", "MobPurpleSnake", "MobRaccoon", "MobRedBlackKoi",
        "MobRedCircleKoi", "MobReindeer", "MobShark", "MobSloth", "MobSlug",
        "MobSmallPurpleSnake", "MobSnail", "MobSnake", "MobSnouse", "MobSpiderSpace",
        "MobSpiderling", "MobTick", "MobTomatoKiller", "MobWorm",
    ];

    private static readonly string[] RandomCreatureWandExcludedNames =
    [
        "Admin", "Admeme", "Boss", "Dragon", "Queen", "King", "Narsie", "Ratvar",
        "Revenant", "Hellspawn", "Abomination", "Glockroach", "LaserRaptor",
        "Goliath", "Hivelord", "Basilisk", "Watcher", "Terror", "Argocyte", "Xeno",
        "Flesh", "Clown", "Syndicate", "Soviet", "Nuke", "Salvage", "Rampaging",
        "InstantDeath", "Grenade", "Cancer", "Stealth", "RGB", "Holo", "Luminous",
        "LivingLight", "Cult", "Corpse", "Dead", "Ghostrole", "Lone", "Servant",
        "Prince", "Princess", "Mother", "Gorilla", "Mimic", "K9", "Robotic",
        "Silicon", "Bot", "Advanced", "Unique", "Wizard", "Bluespace", "Elder",
        "Stargazer", "Bizarre", "TotallyNormal", "Dungeon", "Smasher", "Harvester",
        "Diona", "Lizard", "Scurret", "Monkey", "Kobold", "Paperwork", "Walter",
        "McGriff", "Alexander", "Bandito", "Bingus", "CarpMagic", "BloodSkeleton",
        "SpaceGlowKoi", "CatException", "CatFloppa", "CatRuntime", "CorgiIan",
        "CorgiLisa", "CorgiMouse", "CorgiNarsi", "CorgiSmart", "CrabAtmos",
        "FoxRenault", "PossumMorty", "PossumPoppy", "RaccoonMorticia",
        "SpiderShiva", "PollyParrot", "PenguinFlippers", "GooseGolden",
        "GoosePlasma", "GooseUranium", "Bananium", "CoalCrab", "GoldCrab",
        "IronCrab", "QuartzCrab", "SilverCrab", "UraniumCrab", "Dinosaur",
        "Remilia", "Cerberus", "Hamlet", "Willow", "Remy", "Syndy",
        "SnailSpeed", "SnailMoth", "MoproachHat",
    ];
    private readonly List<string> _randomCreatureWandPool = [];

    [Dependency] private NpcFactionSystem _creatureWandFactions = default!;

    private void InitializeRandomCreatureWand()
    {
        SubscribeLocalEvent<WizardRandomCreatureWandComponent, AmmoShotEvent>(OnRandomCreatureWandShot);
    }

    public IReadOnlyList<string> GetRandomCreatureWandPool()
    {
        if (_randomCreatureWandPool.Count > 0)
            return _randomCreatureWandPool;

        foreach (var prototype in _minorIllusionPrototypes.EnumeratePrototypes<EntityPrototype>())
        {
            if (!IsRandomCreatureWandCandidate(prototype))
                continue;

            _randomCreatureWandPool.Add(prototype.ID);
        }

        _randomCreatureWandPool.Sort(StringComparer.Ordinal);
        return _randomCreatureWandPool;
    }

    private bool IsRandomCreatureWandCandidate(EntityPrototype prototype)
    {
        var ordinaryFamily = false;
        foreach (var family in RandomCreatureWandFamilies)
        {
            if (!prototype.ID.StartsWith(family, StringComparison.Ordinal))
                continue;

            ordinaryFamily = true;
            break;
        }

        if (!ordinaryFamily || prototype.Abstract ||
            !prototype.HasComponent<MobStateComponent>(_minorIllusionComponents) ||
            !prototype.HasComponent<PhysicsComponent>(_minorIllusionComponents) ||
            !prototype.HasComponent<NpcFactionMemberComponent>(_minorIllusionComponents) ||
            !prototype.HasComponent<HTNComponent>(_minorIllusionComponents) ||
            !prototype.HasComponent<InputMoverComponent>(_minorIllusionComponents) ||
            !prototype.HasComponent<MobMoverComponent>(_minorIllusionComponents) ||
            !prototype.HasComponent<MeleeWeaponComponent>(_minorIllusionComponents) ||
            prototype.HasComponent<HumanoidAppearanceComponent>(_minorIllusionComponents) ||
            prototype.EditorSuffix?.Contains("Admeme", StringComparison.OrdinalIgnoreCase) == true)
            return false;

        foreach (var excluded in RandomCreatureWandExcludedNames)
        {
            if (prototype.ID.Contains(excluded, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    private void OnRandomCreatureWandShot(Entity<WizardRandomCreatureWandComponent> ent, ref AmmoShotEvent args)
    {
        if (args.Shooter is not { } caster || args.FiredProjectiles.Count == 0)
            return;

        var pool = GetRandomCreatureWandPool();
        if (pool.Count == 0)
            return;

        var creature = Spawn(pool[_random.Next(pool.Count)],
            Transform(caster).Coordinates);
        RemComp<GhostTakeoverAvailableComponent>(creature);
        RemComp<GhostRoleComponent>(creature);
        _creatureWandFactions.ClearFactions(creature);
        _creatureWandFactions.AddFaction(creature, "SimpleHostile");
        if (TryComp<HTNComponent>(creature, out var ai) &&
            _minorIllusionPrototypes.Index<EntityPrototype>("MobAngryBee")
                .TryGetComponent<HTNComponent>(out var hostileAi, _minorIllusionComponents))
        {
            ai.RootTask = hostileAi.RootTask;
            ai.Plan = null;
            ai.PlanAccumulator = 0;
        }
        _creatureWandFactions.IgnoreEntity(creature, caster);
    }
}
