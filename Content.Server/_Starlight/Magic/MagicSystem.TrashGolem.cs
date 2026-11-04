using System.Linq;
using System.Numerics;
using Content.Shared.Actions.Components;
using Content.Shared.Cargo;
using Content.Shared.Body.Components;
using Content.Shared.Body.Part;
using Content.Server.NPC.HTN;
using Content.Server.NPC;
using Content.Server.NPC.Systems;
using Content.Server.Cargo.Systems;
using Content.Shared._Starlight.Magic.Components;
using Content.Shared._Starlight.Magic.Events;
using Content.Shared.Examine;
using Content.Shared.FixedPoint;
using Content.Shared.Item;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Pointing;
using Content.Shared.Physics;
using Content.Shared.RatKing;
using Content.Shared.Weapons.Melee;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Player;
using Robust.Shared.GameObjects;

namespace Content.Server.Magic;

[RegisterComponent]
public sealed partial class WizardTrashGolemMasterComponent : Component
{
    public readonly HashSet<EntityUid> Golems = new();
    public RatKingOrderType Order = RatKingOrderType.Follow;
    public EntityUid? StayAction;
    public EntityUid? FollowAction;
    public EntityUid? AttackAction;
}

[RegisterComponent]
public sealed partial class WizardTrashGolemComponent : Component
{
    public EntityUid Master;
    public bool ItemsDropped;
    [ViewVariables] public double MaterialValue;
}

public sealed partial class MagicSystem
{
    private const int TrashGolemTileRadius = 1;
    private const int TrashGolemMaxItems = 10;
    private const int TrashGolemBaseHealth = 40;
    private const int TrashGolemBaseDamage = 6;
    private const double TrashGolemMaxCombatValue = 2000;
    private const double TrashGolemAffixValueStep = 500;
    private const double TrashGolemHealthValueStep = 50;
    private const double TrashGolemDamageValueStep = 250;
    private const string TrashGolemContainerId = "wizard-trash-golem-items";
    private static readonly string[] TrashGolemAffixKeys =
    [
        "wizard-trash-golem-tier-0", "wizard-trash-golem-tier-1",
        "wizard-trash-golem-tier-2", "wizard-trash-golem-tier-3",
        "wizard-trash-golem-tier-4", "wizard-trash-golem-tier-5",
        "wizard-trash-golem-tier-6", "wizard-trash-golem-tier-7",
        "wizard-trash-golem-tier-8", "wizard-trash-golem-tier-9",
    ];
    private static readonly Vector2i[] TrashGolemSpawnOffsets =
    [
        new(1, 0), new(-1, 0), new(0, 1), new(0, -1),
    ];

    [Dependency] private SharedContainerSystem _trashGolemContainers = default!;
    [Dependency] private NPCSystem _trashGolemNpc = default!;
    [Dependency] private HTNSystem _trashGolemHtn = default!;
    [Dependency] private PricingSystem _trashGolemPricing = default!;

    private void InitializeTrashGolem()
    {
        SubscribeLocalEvent<TrashGolemSpellEvent>(OnTrashGolemSpell);
        SubscribeLocalEvent<WizardTrashGolemMasterComponent, TrashGolemOrderActionEvent>(OnTrashGolemOrder);
        SubscribeLocalEvent<WizardTrashGolemMasterComponent, AfterPointedAtEvent>(OnTrashGolemPointed);
        SubscribeLocalEvent<WizardTrashGolemMasterComponent, ComponentShutdown>(OnTrashGolemMasterShutdown);
        SubscribeLocalEvent<WizardTrashGolemComponent, MobStateChangedEvent>(OnTrashGolemStateChanged);
        SubscribeLocalEvent<WizardTrashGolemComponent, EntityTerminatingEvent>(OnTrashGolemTerminating);
        SubscribeLocalEvent<WizardTrashGolemComponent, ComponentShutdown>(OnTrashGolemShutdown);
        SubscribeLocalEvent<WizardTrashGolemComponent, ExaminedEvent>(OnTrashGolemExamined);
        SubscribeLocalEvent<WizardTrashGolemComponent, PriceCalculationEvent>(OnTrashGolemAppraised);
    }

    private void OnTrashGolemSpell(TrashGolemSpellEvent args)
    {
        if (args.Handled || !PassesSpellPrerequisites(args.Action, args.Performer))
            return;

        args.Handled = TryCastTrashGolem(args.Performer);
    }

    public bool TryCastTrashGolem(EntityUid caster)
    {
        if (!_wizardMobState.IsAlive(caster))
            return false;

        var center = _redecoratingTurf.GetTileRef(Transform(caster).Coordinates);
        if (center is not { } tile || tile.Tile.IsEmpty || _redecoratingTurf.IsSpace(tile))
            return false;
        if (!TryFindTrashGolemSpawn(tile, out var spawnCoordinates))
            return false;

        var items = _wizardLookup.GetEntitiesInRange<ItemComponent>(
                _redecoratingTurf.GetTileCenter(tile), 2f, LookupFlags.Uncontained)
            .Where(item => IsTrashGolemMaterial(item.Owner, caster, tile))
            .Select(item => item.Owner)
            .ToArray();

        if (items.Length == 0)
            return false;

        var golem = Spawn("MobWizardTrashGolem", spawnCoordinates);
        var holder = EnsureComp<WizardTrashGolemComponent>(golem);
        holder.Master = caster;
        var container = _trashGolemContainers.EnsureContainer<Container>(golem, TrashGolemContainerId);
        var visuals = EnsureComp<WizardTrashGolemVisualComponent>(golem);
        var materialValue = 0d;
        foreach (var item in items)
        {
            // Recheck immediately before insertion so an actor or mob can never become golem material.
            if (!IsTrashGolemMaterial(item, caster, tile))
                continue;

            var price = _trashGolemPricing.GetPrice(item);
            if (_trashGolemContainers.Insert(item, container))
            {
                visuals.DisplayItems.Add(Prototype(item)?.ID ?? string.Empty);
                if (double.IsFinite(price))
                    materialValue += Math.Max(0, price);
            }
            if (visuals.DisplayItems.Count >= TrashGolemMaxItems)
                break;
        }

        if (visuals.DisplayItems.Count == 0)
        {
            QueueDel(golem);
            return false;
        }

        holder.MaterialValue = materialValue;
        var tier = (int) Math.Floor(Math.Min(materialValue,
            (TrashGolemAffixKeys.Length - 1) * TrashGolemAffixValueStep) / TrashGolemAffixValueStep);
        _doomMetadata.SetEntityName(golem, Loc.GetString(TrashGolemAffixKeys[tier]));
        var combatValue = Math.Min(TrashGolemMaxCombatValue, materialValue);
        _doomThresholds.SetMobStateThreshold(golem,
            FixedPoint2.New(TrashGolemBaseHealth + (int) (combatValue / TrashGolemHealthValueStep)),
            MobState.Dead);
        var melee = Comp<MeleeWeaponComponent>(golem);
        melee.Damage.DamageDict["Blunt"] = FixedPoint2.New(TrashGolemBaseDamage + (int) (combatValue / TrashGolemDamageValueStep));
        Dirty(golem, melee);
        Dirty(golem, visuals);
        var master = EnsureComp<WizardTrashGolemMasterComponent>(caster);
        if (master.Golems.Count == 0)
        {
            _wizardActions.AddAction(caster, ref master.StayAction, "ActionWizardTrashGolemStay");
            _wizardActions.AddAction(caster, ref master.FollowAction, "ActionWizardTrashGolemFollow");
            _wizardActions.AddAction(caster, ref master.AttackAction, "ActionWizardTrashGolemAttack");
        }

        master.Golems.Add(golem);
        _trashGolemNpc.SetBlackboard(golem, NPCBlackboard.FollowTarget, new EntityCoordinates(caster, Vector2.Zero));
        UpdateTrashGolemOrder(golem, master.Order);
        UpdateTrashGolemActions(master);
        return true;
    }

    private void OnTrashGolemExamined(Entity<WizardTrashGolemComponent> ent, ref ExaminedEvent args)
    {
        args.PushText(Loc.GetString("wizard-trash-golem-valued-at", ("value", $"{ent.Comp.MaterialValue:F0}")));
    }

    private void OnTrashGolemAppraised(Entity<WizardTrashGolemComponent> ent, ref PriceCalculationEvent args)
    {
        args.Price = ent.Comp.MaterialValue;
        args.Handled = true;
    }

    private bool IsTrashGolemMaterial(EntityUid item, EntityUid caster, TileRef center)
    {
        // Only prototypes authored as handheld items qualify. A runtime-added Item component
        // (for example on a player) must not turn an entity into golem material.
        if (Prototype(item)?.Components.ContainsKey("Item") != true ||
            item == caster || HasComp<ActorComponent>(item) || HasComp<MindContainerComponent>(item) ||
            HasComp<MobStateComponent>(item) || HasComp<BodyComponent>(item) || HasComp<BodyPartComponent>(item) ||
            !TryComp<ItemComponent>(item, out var itemComp) || !itemComp.AllowDirectHandPickup)
            return false;

        var xform = Transform(item);
        if (xform.Anchored || xform.ParentUid != center.GridUid ||
            TryComp<PhysicsComponent>(item, out var physics) && physics.BodyType == BodyType.Static)
            return false;

        return _redecoratingTurf.GetTileRef(xform.Coordinates) is { } tile &&
            Math.Abs(tile.GridIndices.X - center.GridIndices.X) <= TrashGolemTileRadius &&
            Math.Abs(tile.GridIndices.Y - center.GridIndices.Y) <= TrashGolemTileRadius;
    }

    private bool TryFindTrashGolemSpawn(TileRef center, out EntityCoordinates coordinates)
    {
        coordinates = default;
        if (!TryComp<MapGridComponent>(center.GridUid, out var grid))
            return false;

        foreach (var offset in TrashGolemSpawnOffsets)
        {
            if (!_redecoratingMap.TryGetTileRef(center.GridUid, grid, center.GridIndices + offset, out var tile) ||
                tile.Tile.IsEmpty || _redecoratingTurf.IsSpace(tile) ||
                _redecoratingTurf.IsTileBlocked(tile, CollisionGroup.MobMask))
                continue;

            coordinates = _redecoratingTurf.GetTileCenter(tile);
            return true;
        }

        return false;
    }

    private void OnTrashGolemOrder(Entity<WizardTrashGolemMasterComponent> ent, ref TrashGolemOrderActionEvent args)
    {
        if (args.Handled)
            return;

        ent.Comp.Order = args.Type;
        foreach (var golem in ent.Comp.Golems)
            UpdateTrashGolemOrder(golem, args.Type);
        UpdateTrashGolemActions(ent.Comp);
        args.Handled = true;
    }

    private void OnTrashGolemPointed(Entity<WizardTrashGolemMasterComponent> ent, ref AfterPointedAtEvent args)
    {
        if (args.Pointed == ent.Owner || !_wizardMobState.IsAlive(args.Pointed))
            return;

        ent.Comp.Order = RatKingOrderType.CheeseEm;
        foreach (var golem in ent.Comp.Golems)
        {
            if (TerminatingOrDeleted(golem) || !_wizardMobState.IsAlive(golem))
                continue;
            _trashGolemNpc.SetBlackboard(golem, NPCBlackboard.CurrentOrderedTarget, args.Pointed);
            UpdateTrashGolemOrder(golem, RatKingOrderType.CheeseEm);
        }
        UpdateTrashGolemActions(ent.Comp);
    }

    private void UpdateTrashGolemOrder(EntityUid golem, RatKingOrderType order)
    {
        if (TerminatingOrDeleted(golem) || !_wizardMobState.IsAlive(golem) ||
            !TryComp<HTNComponent>(golem, out var htn))
            return;

        if (htn.Plan != null)
            _trashGolemHtn.ShutdownPlan(htn);
        _trashGolemNpc.SetBlackboard(golem, NPCBlackboard.CurrentOrders, order);
        _trashGolemHtn.Replan(htn);
    }

    private void UpdateTrashGolemActions(WizardTrashGolemMasterComponent master)
    {
        _wizardActions.SetToggled(master.StayAction, master.Order == RatKingOrderType.Stay);
        _wizardActions.SetToggled(master.FollowAction, master.Order == RatKingOrderType.Follow);
        _wizardActions.SetToggled(master.AttackAction, master.Order is RatKingOrderType.CheeseEm or RatKingOrderType.Loose);
    }

    private void OnTrashGolemStateChanged(Entity<WizardTrashGolemComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState == MobState.Dead)
        {
            DropTrashGolemItems(ent);
            UnregisterTrashGolem(ent);
        }
    }

    private void OnTrashGolemShutdown(Entity<WizardTrashGolemComponent> ent, ref ComponentShutdown args)
    {
        DropTrashGolemItems(ent);
        UnregisterTrashGolem(ent);
    }

    private void OnTrashGolemTerminating(Entity<WizardTrashGolemComponent> ent, ref EntityTerminatingEvent args)
    {
        DropTrashGolemItems(ent);
        UnregisterTrashGolem(ent);
    }

    private void UnregisterTrashGolem(Entity<WizardTrashGolemComponent> ent)
    {
        if (!TryComp<WizardTrashGolemMasterComponent>(ent.Comp.Master, out var master))
            return;
        master.Golems.Remove(ent.Owner);
        if (master.Golems.Count == 0)
            RemComp<WizardTrashGolemMasterComponent>(ent.Comp.Master);
    }

    private void DropTrashGolemItems(Entity<WizardTrashGolemComponent> ent)
    {
        if (ent.Comp.ItemsDropped)
            return;
        ent.Comp.ItemsDropped = true;
        if (_trashGolemContainers.TryGetContainer(ent, TrashGolemContainerId, out var container))
        {
            var destination = Transform(ent).Coordinates;
            foreach (var item in container.ContainedEntities.ToArray())
                _trashGolemContainers.Remove(item, container, destination: destination, force: true);
        }

        if (TryComp<WizardTrashGolemVisualComponent>(ent, out var visuals))
        {
            visuals.DisplayItems.Clear();
            Dirty(ent, visuals);
        }
    }

    private void OnTrashGolemMasterShutdown(Entity<WizardTrashGolemMasterComponent> ent, ref ComponentShutdown args)
    {
        foreach (var golem in ent.Comp.Golems)
            UpdateTrashGolemOrder(golem, RatKingOrderType.Stay);
        if (!TryComp<ActionsComponent>(ent, out var actions))
            return;
        _wizardActions.RemoveAction((ent.Owner, actions), ent.Comp.StayAction);
        _wizardActions.RemoveAction((ent.Owner, actions), ent.Comp.FollowAction);
        _wizardActions.RemoveAction((ent.Owner, actions), ent.Comp.AttackAction);
    }
}
