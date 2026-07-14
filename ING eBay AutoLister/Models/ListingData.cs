namespace ING_eBay_AutoLister.Models;

public class ListingData
{
    // ── Title & Category ──────────────────────────────────────────
    public string Title { get; set; } = "";
    public string Subtitle { get; set; } = "";
    public string Category { get; set; } = "";          // human-readable display name
    public string CategoryId { get; set; } = "";
    public string SecondaryCategoryId { get; set; } = "";

    // ── Condition ────────────────────────────────────────────────
    // NEW | LIKE_NEW | USED_EXCELLENT | USED_VERY_GOOD | USED_GOOD | USED_ACCEPTABLE | FOR_PARTS_OR_NOT_WORKING
    public string Condition { get; set; } = "USED_EXCELLENT";
    public string ConditionDescription { get; set; } = "";

    // ── Product Identifiers ──────────────────────────────────────
    public string Brand { get; set; } = "";
    public string Mpn { get; set; } = "";               // manufacturer part number
    public string Upc { get; set; } = "";
    public string Ean { get; set; } = "";
    public string Isbn { get; set; } = "";

    // ── Description ──────────────────────────────────────────────
    public string Description { get; set; } = "";

    // ── Pricing ──────────────────────────────────────────────────
    public decimal Price { get; set; }
    public bool BestOfferEnabled { get; set; } = false;
    public decimal? AutoAcceptPrice { get; set; }
    public decimal? AutoDeclinePrice { get; set; }
    public int Quantity { get; set; } = 1;
    public int? QuantityLimitPerBuyer { get; set; }

    // ── Package / Shipping ───────────────────────────────────────
    // PackageType: LETTER | LARGE_ENVELOPE_OR_FLAT_PACK | PACKAGE_THICK_ENVELOPE | MAILING_BOX | BULKY_GOODS | VERY_LARGE_PACKAGE
    public string PackageType { get; set; } = "PACKAGE_THICK_ENVELOPE";
    public decimal WeightLbs { get; set; }
    public decimal WeightOz { get; set; }
    public decimal PackageLengthIn { get; set; }
    public decimal PackageWidthIn { get; set; }
    public decimal PackageHeightIn { get; set; }
    public int HandlingTimeBusinessDays { get; set; } = 1;
    public string ItemLocationPostalCode { get; set; } = "";
    public string ItemLocationCountry { get; set; } = "US";

    // ── Listing Options ──────────────────────────────────────────
    public bool PrivateListing { get; set; } = false;
    public int CharityDonationPercentage { get; set; } = 0;
    public string CharityId { get; set; } = "";

    // ── Images & Specifics ───────────────────────────────────────
    public List<string> ImageUrls { get; set; } = [];
    public Dictionary<string, string> ItemSpecifics { get; set; } = [];

    // Visual description Claude generates for AI photo generation
    public string VisualDescription { get; set; } = "";

    // "product_photo" = clean product image suitable for img2img
    // "webpage_screenshot" = website/listing screenshot, use txt2img only
    public string ImageType { get; set; } = "webpage_screenshot";
}

public class FetchPhotoUrlRequest { public string Url { get; set; } = ""; }

public class RemoveBgRequest
{
    public string ImageBase64 { get; set; } = "";
    public string MimeType    { get; set; } = "image/jpeg";
}

public class SaveUploadedPhotoRequest
{
    public string ImageBase64 { get; set; } = "";
    public string MimeType    { get; set; } = "image/jpeg";
}

public class AnalyzeRequest
{
    public string ImageBase64 { get; set; } = "";
    public string MimeType { get; set; } = "image/jpeg";
}

public class AnalyzeUrlRequest { public string Url { get; set; } = ""; }

public class QuickFillRequest { public string ItemName { get; set; } = ""; }

public class SoldComp
{
    public string Title { get; set; } = "";
    public decimal Price { get; set; }
    public DateTime SoldDate { get; set; }
    public string Url { get; set; } = "";
    public string ImageUrl { get; set; } = "";
}

public class SoldCompsResult
{
    public string Query { get; set; } = "";
    public List<SoldComp> Items { get; set; } = [];
    public int Count { get; set; }
    public decimal Average { get; set; }
    public decimal Median { get; set; }
    public decimal Min { get; set; }
    public decimal Max { get; set; }
    public decimal? SellThroughPercent { get; set; }
    public decimal AvgShipping { get; set; }
}

public class EbayOpportunityItem
{
    public string Title { get; set; } = "";
    public decimal Price { get; set; }
    public decimal ShippingCost { get; set; }
    public string Url { get; set; } = "";
    public string ImageUrl { get; set; } = "";
    public DateTime? EndDate { get; set; }
    public string SellerUsername { get; set; } = "";
    public int SellerFeedbackScore { get; set; }
    public string BuyingOption { get; set; } = "";
    public int BidCount { get; set; }
}

// Mutable (not anonymous) so the top-candidate Terapeak re-check can overwrite profit fields
// in place after the initial broad-estimate pass.
public class OpportunityListItem
{
    public string Title { get; set; } = "";
    public decimal Price { get; set; }
    public decimal ShippingCost { get; set; }
    public decimal TotalCost { get; set; }
    public string Url { get; set; } = "";
    public string ImageUrl { get; set; } = "";
    public DateTime? EndDate { get; set; }
    public string SellerUsername { get; set; } = "";
    public int SellerFeedbackScore { get; set; }
    public string BuyingOption { get; set; } = "";
    public int BidCount { get; set; }
    public decimal? MarketAverage { get; set; }
    public decimal? EstimatedResalePrice { get; set; }
    public decimal? EstimatedProfit { get; set; }
    public decimal? ProfitPercent { get; set; }
    public int? OpportunityScore { get; set; }
    public bool IsVerified { get; set; }
    public bool IsUnderpriced { get; set; }
    public bool IsHighProfitMargin { get; set; }
    public bool IsEndingSoon { get; set; }
    public bool IsHighDemand { get; set; }
    public bool IsNewlyListed { get; set; }
    public bool HasPoorTitle { get; set; }
    public bool HasMisspelledTitle { get; set; }
    public bool HasPoorPhoto { get; set; }
}

// Return shape of the shared opportunity-search pipeline (Program.cs FindOpportunitiesAsync),
// used by both the interactive /api/opportunities/search endpoint and the Gem Radar background
// scanner so the two don't duplicate the search/score/verify logic.
public class OpportunitySearchResult
{
    public string Query { get; set; } = "";
    public decimal MarketValue { get; set; }
    public decimal AveragePrice { get; set; }
    public string SoldSource { get; set; } = "none";
    public string ListingType { get; set; } = "AUCTION";
    public decimal? SellThroughPercent { get; set; }
    public List<OpportunityListItem> Items { get; set; } = [];
}

// A single Gem Radar find, persisted by GemRadarStore. Wraps the same OpportunityListItem the
// interactive search returns, plus the category keyword that surfaced it and when it was found,
// so the passive feed doesn't require the user to have typed anything themselves.
public class GemEntry
{
    public string Category { get; set; } = "";
    public DateTime FoundAtUtc { get; set; }
    public OpportunityListItem Item { get; set; } = new();
}

public class GeneratePhotosRequest
{
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string VisualDescription { get; set; } = "";
    public string ImageBase64 { get; set; } = "";
    public string MimeType { get; set; } = "image/jpeg";
    public string ImageType { get; set; } = "webpage_screenshot";
}

public class PostListingRequest : ListingData
{
    public string? EbayToken { get; set; }
    public string ListingFormat { get; set; } = "FIXED_PRICE";
    public int DurationDays { get; set; } = 30;
    // Per-listing policy overrides (fall back to saved credentials when blank)
    public string? FulfillmentPolicyId { get; set; }
    public string? PaymentPolicyId { get; set; }
    public string? ReturnPolicyId { get; set; }
}

public class EbayAuthRequest
{
    public string Code { get; set; } = "";
}

public class EbayOAuthRedirectRequest
{
    public string RedirectUrl { get; set; } = "";
}

public class EbayListingSummary
{
    public string OfferId { get; set; } = "";
    public string ListingId { get; set; } = "";
    public string Sku { get; set; } = "";
    public string Status { get; set; } = "";
    public string Title { get; set; } = "";
    public string Category { get; set; } = "";
    public string CategoryId { get; set; } = "";
    public string LastUpdated { get; set; } = "";
    public decimal Price { get; set; }
    public int Quantity { get; set; }
    public string Condition { get; set; } = "";
    public string ThumbnailUrl { get; set; } = "";
    public int WatchCount { get; set; }
    public string ListingUrl { get; set; } = "";
    public PostListingRequest Data { get; set; } = new();
}

public class UpdateListingRequest : PostListingRequest
{
    public string OfferId { get; set; } = "";
    public string ListingId { get; set; } = "";
    public string Sku { get; set; } = "";
    public bool ManualRevisionConfirmed { get; set; }
}

public class ImproveSeoRequest : ListingData { }

public class ModifyListingRequest : ListingData
{
    public string Instruction { get; set; } = "";
}

public class SniperBidRequest
{
    public string  ItemId  { get; set; } = "";
    public decimal MaxBid  { get; set; }
}
