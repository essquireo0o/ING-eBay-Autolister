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
