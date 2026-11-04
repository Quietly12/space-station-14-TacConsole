using Content.Shared.FixedPoint;
using Content.Shared.Store;
using Content.Shared.Store.Components;
using Robust.Shared.Prototypes;

namespace Content.Server.Store.Systems;

public sealed partial class StoreSystem
{
    private static readonly ProtoId<StoreCategoryPrototype> WizardCantripCategory = "SpellbookCantrips";
    private static readonly ProtoId<StoreCategoryPrototype> ApprenticeCantripCategory = "SpellbookApprenticeCantrips";
    private static readonly ProtoId<CurrencyPrototype> WizCoinCurrency = "WizCoin";
    private static readonly ProtoId<CurrencyPrototype> ApprenticeWizCoinCurrency = "ApprenticeWizCoin";
    private const string WizardCantripDiscountId = "wizard-cantrip-free-picks";
    private const string ApprenticeCantripDiscountId = "apprentice-cantrip-free-pick";
    private const int FreeWizardCantripPicks = 2;

    private void RefreshWizardCantripDiscounts(StoreComponent store)
    {
        if (store.Categories.Contains(WizardCantripCategory))
            RefreshCantripDiscounts(store, WizardCantripCategory, WizCoinCurrency, WizardCantripDiscountId, FreeWizardCantripPicks);

        if (store.Categories.Contains(ApprenticeCantripCategory))
            RefreshCantripDiscounts(store, ApprenticeCantripCategory, ApprenticeWizCoinCurrency, ApprenticeCantripDiscountId, 1);
    }

    private static void RefreshCantripDiscounts(StoreComponent store,
        ProtoId<StoreCategoryPrototype> category,
        ProtoId<CurrencyPrototype> currency,
        string discountId,
        int freePicks)
    {
        if (!store.Categories.Contains(category))
            return;

        var purchased = 0;
        foreach (var listing in store.FullListingsCatalog)
        {
            if (listing.Categories.Contains(category))
                purchased += listing.PurchaseAmount;
        }

        foreach (var listing in store.FullListingsCatalog)
        {
            if (!listing.Categories.Contains(category))
                continue;

            listing.RemoveCostModifier(discountId);

            if (purchased >= freePicks
                || !listing.OriginalCost.TryGetValue(currency, out var cost)
                || cost != FixedPoint2.New(1))
                continue;

            listing.AddCostModifier(discountId,
                new Dictionary<ProtoId<CurrencyPrototype>, FixedPoint2>
                {
                    { currency, FixedPoint2.New(-1) },
                });
        }
    }
}
