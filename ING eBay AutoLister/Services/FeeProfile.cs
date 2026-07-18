namespace ING_eBay_AutoLister.Services;

// Configurable fee assumptions for ProfitCalculator — same pattern as LiquidityScoringConfig
// (plain mutable POCO singleton, sensible hardcoded defaults, no appsettings binding yet).
// Replaces the EbayFeePercent/EbayFeeFixed local consts that used to be duplicated inline at two
// call sites in Program.cs's supplier-file-analyzer endpoint.
public class FeeProfile
{
    // eBay's typical final value fee — an estimate, not account-specific (varies by category and
    // store subscription level; there's no API to fetch a seller's actual negotiated rate).
    public decimal EbayFinalValueFeePercent = 13.25m;
    public decimal EbayFinalValueFeeFixed = 0.40m;

    public decimal PromotedListingRatePercent = 0m;  // opt-in — 0 means not using Promoted Listings
    public decimal PaymentProcessingPercent = 0m;     // eBay's final value fee is normally all-inclusive

    public decimal DefaultShippingCost = 0m;          // actual cost to ship (distinct from buyer-paid shipping)
    public decimal DefaultPackagingCost = 0m;
    public decimal DefaultLaborCost = 0m;

    public decimal ReturnReservePercent = 0m;
    public decimal TestingReservePercent = 0m;
}
