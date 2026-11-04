using Content.Server.Storage.EntitySystems;
using Content.Server.Roles;
using Content.Shared.Inventory;
using Content.Shared._Starlight.Magic.Components;
using Content.Shared.Paper;
using Content.Shared.UserInterface;
using Content.Shared.Mobs.Components;
using Content.Shared.Roles.Components;
using Content.Shared.Store.Components;
using Content.Shared._Starlight.Store.Events;
using Content.Shared.Mind.Components;
using Content.Shared.Mind;
using Robust.Shared.Audio;
using Robust.Shared.Player;

namespace Content.Server.Magic;

public sealed partial class MagicSystem
{
    private const string WizardApprenticeRole = "MindRoleWizardApprentice";
    private static readonly string[] ApprenticeWands =
    [
        "WeaponWandRandomCreature",
        "WeaponWandPolymorphMonkey",
        "WeaponWandPolymorphDoor",
        "WeaponWandPolymorphCarp",
    ];
    private static readonly (string Robes, string Hat)[] ApprenticeOutfits =
    [
        ("ClothingOuterWizard", "ClothingHeadHatWizard"),
        ("ClothingOuterWizardRed", "ClothingHeadHatRedwizard"),
        ("ClothingOuterWizardPurple", "ClothingHeadHatPurplewizard"),
    ];
    private static readonly SoundSpecifier ContractFlameSound = new SoundPathSpecifier("/Audio/Magic/fireball.ogg");
    [Dependency] private StorageSystem _apprenticeStorage = default!;
    [Dependency] private PaperSystem _apprenticePaper = default!;

    private void InitializeApprenticeshipContract()
    {
        SubscribeLocalEvent<WizardApprenticeshipContractComponent, BeforeActivatableUIOpenEvent>(OnApprenticeshipContractOpened);
        SubscribeLocalEvent<WizardApprenticeshipContractComponent, PaperSignedEvent>(OnApprenticeshipContractSigned);
        SubscribeLocalEvent<WizardApprenticeshipContractComponent, StorePurchasedListingEvent>(OnApprenticeshipContractPurchased);
        SubscribeLocalEvent<WizardApprenticeRoleComponent, GetBriefingEvent>(OnApprenticeBriefing);
    }

    private void OnApprenticeBriefing(Entity<WizardApprenticeRoleComponent> ent, ref GetBriefingEvent args)
    {
        var wizardName = ent.Comp.WizardMind is { } wizardMind
            ? GetWizardMasterName(wizardMind, ent.Comp.WizardName)
            : ent.Comp.WizardName;
        args.Append(Loc.GetString("wizard-apprentice-briefing", ("wizard", wizardName)));
    }

    private void OnApprenticeshipContractPurchased(Entity<WizardApprenticeshipContractComponent> ent, ref StorePurchasedListingEvent args)
    {
        if (!_mind.TryGetMind(args.Purchaser, out var mindId, out _) ||
            !_wizardDominationRoles.MindHasRole<WizardRoleComponent>(mindId))
            return;

        BindApprenticeshipContract(ent, args.Purchaser, mindId);
        ShowApprenticeshipContractBound(args.Purchaser);
    }

    private void BindApprenticeshipContract(Entity<WizardApprenticeshipContractComponent> ent, EntityUid wizard, EntityUid mindId)
    {
        ent.Comp.Wizard = wizard;
        ent.Comp.WizardMind = mindId;
        ent.Comp.WizardName = Name(wizard);
        _apprenticePaper.SetContent(ent.Owner,
            Loc.GetString("wizard-apprentice-contract-paper-bound", ("wizard", ent.Comp.WizardName)));
    }

    private void ShowApprenticeshipContractBound(EntityUid wizard)
    {
        var boundMessage = Loc.GetString("wizard-apprentice-contract-bound");
        _doomPopup.PopupEntity(boundMessage, wizard, wizard);
        _wizardDominationBriefing.SendBriefing(wizard, boundMessage, null, null);
    }

    private void OnApprenticeshipContractOpened(Entity<WizardApprenticeshipContractComponent> ent,
        ref BeforeActivatableUIOpenEvent args)
    {
        if (ent.Comp.WizardMind != null ||
            !_mind.TryGetMind(args.User, out var mindId, out _) ||
            !_wizardDominationRoles.MindHasRole<WizardRoleComponent>(mindId))
            return;

        BindApprenticeshipContract(ent, args.User, mindId);
        ShowApprenticeshipContractBound(args.User);
    }

    private void OnApprenticeshipContractSigned(Entity<WizardApprenticeshipContractComponent> ent,
        ref PaperSignedEvent args)
    {
        if (args.Cancelled)
            return;

        var signer = args.Signer;
        if (!_wizardMobState.IsAlive(signer) ||
            !_mind.TryGetMind(signer, out var mindId, out var mind) ||
            ent.Comp.WizardMind == null || mindId == ent.Comp.WizardMind ||
            _wizardDominationRoles.MindHasRole<WizardRoleComponent>(mindId) ||
            _wizardDominationRoles.MindHasRole<WizardApprenticeRoleComponent>(mindId) ||
            !HasComp<MobStateComponent>(signer) ||
            !HasComp<InventoryComponent>(signer))
        {
            args.Cancelled = true;
            args.FailReason = Loc.GetString("wizard-apprentice-contract-invalid-signer");
            return;
        }

        var location = Transform(signer).Coordinates;
        var backpack = Spawn("ClothingBackpackWizard", location);
        var grimoire = Spawn("WizardsApprenticeGrimoire", location);
        var wand = Spawn(ApprenticeWands[_random.Next(ApprenticeWands.Length)], location);
        var outfit = ApprenticeOutfits[_random.Next(ApprenticeOutfits.Length)];
        var robes = Spawn(outfit.Robes, location);
        var hat = Spawn(outfit.Hat, location);
        var contents = new[] { grimoire, wand };
        var packed = true;
        foreach (var item in contents)
        {
            if (!_apprenticeStorage.Insert(backpack, item, out _, playSound: false))
            {
                packed = false;
                break;
            }
        }

        if (!packed)
        {
            foreach (var item in contents)
                QueueDel(item);
            QueueDel(robes);
            QueueDel(hat);
            QueueDel(backpack);
            args.Cancelled = true;
            args.FailReason = Loc.GetString("wizard-apprentice-contract-pack-failed");
            return;
        }

        Comp<StoreComponent>(grimoire).AccountOwner = mindId;
        _wizardDominationRoles.MindAddRole(mindId, WizardApprenticeRole, mind, true);
        if (_wizardDominationRoles.MindHasRole<WizardApprenticeRoleComponent>(mindId, out var apprenticeRole))
        {
            apprenticeRole.Value.Comp2.WizardMind = ent.Comp.WizardMind;
            apprenticeRole.Value.Comp2.WizardName = ent.Comp.WizardName ?? string.Empty;
        }
        _chainLightningInventory.TryUnequip(signer, "outerClothing", silent: true, force: true);
        _chainLightningInventory.TryUnequip(signer, "head", silent: true, force: true);
        if (!_chainLightningInventory.TryEquip(signer, robes, "outerClothing", force: true))
            _apprenticeStorage.Insert(backpack, robes, out _, playSound: false);
        if (!_chainLightningInventory.TryEquip(signer, hat, "head", force: true))
            _apprenticeStorage.Insert(backpack, hat, out _, playSound: false);
        _doomHands.TryDrop(signer, ent.Owner, location, checkActionBlocker: false, doDropInteraction: false);
        if (!_doomHands.TryPickupAnyHand(signer, backpack, checkActionBlocker: false))
            _doomPopup.PopupEntity(Loc.GetString("wizard-apprentice-backpack-failed"), signer, signer);
        var wizardName = ent.Comp.WizardName ?? string.Empty;
        if (TryComp<MindComponent>(ent.Comp.WizardMind, out var wizardMind) &&
            wizardMind.OwnedEntity is { } currentWizard)
            wizardName = Name(currentWizard);
        var signedMessage = Loc.GetString("wizard-apprentice-contract-signed", ("wizard", wizardName));
        _doomPopup.PopupEntity(signedMessage, signer, signer);
        _wizardDominationBriefing.SendBriefing(signer, signedMessage, null, null);
        _wizardDominationBriefing.SendBriefing(signer,
            Loc.GetString("wizard-apprentice-briefing", ("wizard", wizardName)), null, null);
        Spawn("WizardApprenticeshipContractFlame", location);
        _wizardAudio.PlayPvs(ContractFlameSound, signer);
        if (TryComp<ActorComponent>(signer, out var signingActor))
            _wizardAudio.PlayEntity(WizardAllegianceStinger, signingActor.PlayerSession, signer);
        QueueDel(ent);
    }
}
