namespace ING_eBay_AutoLister.Models;

// ── Local sold-history lookup (read-only, against the externally-maintained Marketplace.db) ──

// What the caller is looking for, strongest identifier first. All fields are optional; the
// repository tries PartNumber, then Model, then Brand+Model, then Brand+Category, then Keywords
// alone, stopping at the first tier that returns a reliable match (see
// MarketplaceRepository.FindComparablesAsync). Normally populated from a ProductIdentity — see
// ProductIdentityExtractor.
public class MarketplaceLookupRequest
{
    public string? PartNumber { get; set; }
    public string? Model { get; set; }
    public string? Brand { get; set; }
    public string? Category { get; set; }
    public string? Keywords { get; set; }     // fallback broad search text (e.g. product name)
    public string? Condition { get; set; }    // optional hint used to weight comparables, not a hard filter
    public int MaxComparables { get; set; } = 12;
    // How many active listings currently exist for this product, if the caller already knows
    // (e.g. from a live listing count it already fetched elsewhere) — feeds
    // LiquidityAssessment.ActiveCompetitionCount. Null = Unknown; never fetched by this request itself.
    public int? ActiveCompetitionCount { get; set; }
}

// Optional narrowing filters, usable on any of the repository's search methods.
public class MarketplaceSearchFilters
{
    public string? Condition { get; set; }
    public DateTime? SoldAfter { get; set; }
    public DateTime? SoldBefore { get; set; }
    public decimal? MinPrice { get; set; }
    public decimal? MaxPrice { get; set; }
}

// One sold listing matched against a query, scored for relevance.
public class MarketplaceComparableResult
{
    public string ItemId { get; set; } = "";
    public string Title { get; set; } = "";
    public decimal SoldPrice { get; set; }
    public decimal Shipping { get; set; }
    public decimal TotalPrice { get; set; }
    public string? Condition { get; set; }
    public DateTime? SoldDate { get; set; }
    public string? Seller { get; set; }
    public string? ItemUrl { get; set; }
    public string? ImageUrl { get; set; }
    public int MatchScore { get; set; } // 0-100; see MarketplaceMatcher

    // ── Fields pulled from SoldListings.RawJson (a SerpApi eBay-scrape blob) ────────────────
    // The flat Brand/Model/Category columns on SoldListings are unpopulated in production —
    // these RawJson-derived fields are the real per-row signal available for matching/scoring.
    public string? Epid { get; set; }                       // eBay catalog/product ID (~28% of rows have one)
    public bool IsFixedPrice { get; set; }                    // RawJson.buying_format == "buy_it_now"
    public int? SellerFeedbackCount { get; set; }
    public double? SellerPositiveFeedbackPercent { get; set; }
    public int Quantity { get; set; } = 1;                    // lot/quantity detected from this row's own title
}

// Aggregate pricing statistics + a defensible suggested resale price for one lookup.
public class MarketplacePricingSummary
{
    public string Query { get; set; } = "";
    public int MatchCount { get; set; }
    public decimal? AveragePrice { get; set; }
    public decimal? MedianPrice { get; set; }
    public decimal? MinimumPrice { get; set; }
    public decimal? MaximumPrice { get; set; }
    public decimal? AverageShipping { get; set; }
    public decimal? SuggestedResalePrice { get; set; }
    public int ConfidenceScore { get; set; } // 0-100
    public List<MarketplaceComparableResult> ComparableListings { get; set; } = [];

    // Headline sell-through/liquidity fields (see LiquidityScoringService) — duplicated here at
    // the top level for convenient access alongside price/confidence; Liquidity below carries
    // every other computed field (velocity, days between sales, trend, competition).
    public int LiquidityScore { get; set; } // 0-100
    public string LiquidityLevel { get; set; } = "Stale/Illiquid"; // Fast Mover | Moderate | Slow Mover | Stale/Illiquid
    public LiquidityAssessment? Liquidity { get; set; }
}

// ── Sell-through / liquidity scoring (see LiquidityScoringService) ──────────────────────────

// How quickly an item is likely to sell, estimated entirely from SoldDate density in the local
// sold-history database — no external API calls. Every field is best-effort; HasSufficientData
// and InsufficientDataMessage tell the caller when there wasn't enough history to trust the
// numbers (see LiquidityScoringConfig.MinComparablesForReliableTrend).
public class LiquidityAssessment
{
    public decimal SalesVelocity { get; set; }           // matching sold listings per 30-day period
    public double? DaysBetweenSales { get; set; }         // average gap between consecutive sold dates
    public int? EstimatedDaysToSell { get; set; }          // projected days until the next sale
    public string DemandTrend { get; set; } = "Unknown";   // Increasing | Stable | Decreasing | Unknown
    public int? ActiveCompetitionCount { get; set; }       // null = Unknown (not available without new scraping)
    public int LiquidityScore { get; set; }                // 0-100
    public string LiquidityLevel { get; set; } = "Stale/Illiquid"; // Fast Mover | Moderate | Slow Mover | Stale/Illiquid
    public bool HasSufficientData { get; set; }
    public string? InsufficientDataMessage { get; set; }
}

// ── Product identity extraction (see ProductIdentityExtractor) ──────────────────────────────

// Structured product identity pulled from a raw title, supplier spreadsheet row, OCR'd image
// text, or manually typed text. Every field is best-effort — regex + lookup tables, not true
// NLP/ML — so a field comes back null rather than guessed whenever it isn't confidently
// recognized. Feeds MarketplaceLookupRequest so Opportunity Finder can search the sold-history
// database by the strongest identifier actually present instead of just raw keywords.
public class ProductIdentity
{
    public string? Brand { get; set; }
    public string? Manufacturer { get; set; }
    public string? ProductFamily { get; set; }
    public string? Model { get; set; }
    public string? PartNumber { get; set; }
    public string? Hashrate { get; set; }
    public string? Voltage { get; set; }
    public string? Wattage { get; set; }
    public string? Capacity { get; set; }
    public string? Revision { get; set; }
    public List<string> ConditionKeywords { get; set; } = [];
    public string? Version { get; set; }
    public string? Series { get; set; }
    public string? Generation { get; set; }
    public string? Category { get; set; }
    public string RawText { get; set; } = "";
}
