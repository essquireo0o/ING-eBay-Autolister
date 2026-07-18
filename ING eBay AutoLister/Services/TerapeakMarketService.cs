using System.Text.RegularExpressions;
using ING_eBay_AutoLister.Models;

namespace ING_eBay_AutoLister.Services;

// One result from either a cache hit or a real Terapeak scrape, plus how much weight it should
// carry given its age — full weight inside 30 days, reduced 31-90 days, progressively less beyond.
public class TerapeakMarketResult
{
    public SoldCompsResult Data { get; set; } = new();
    public DateTime ScrapedAtUtc { get; set; }
    public bool FromCache { get; set; }
    public double FreshnessWeight { get; set; }
}

// Thin orchestration over the existing TerapeakService (browser scrape) + TerapeakPriceCache
// (SQLite persistence) pair — centralizes what used to be duplicated inline in Program.cs
// (GetOrScrapePricingAsync) so both the Opportunity Finder search and the Supplier File Analyzer
// share one implementation, and adds a normalized cache-key signature plus freshness weighting.
//
// SAFETY: Terapeak is a real logged-in browser scrape, not an API — this class NEVER initiates a
// scrape on its own. GetAsync only scrapes when the caller explicitly passes allowRealScrape=true,
// and even then only on a cache miss. Callers are responsible for rationing how many times per
// search they set allowRealScrape=true (see Program.cs's terapeakRecheckLimit) — this class must
// never be changed to pre-fetch, bulk-scan, or background-refresh Terapeak data.
public sealed class TerapeakMarketService(TerapeakService terapeak, TerapeakPriceCache cache, ActionLog log)
{
    // A normalized search signature (brand+model+major-spec+condition+category), not the raw
    // query string — two searches that mean the same product but differ in wording/word order
    // hit the same cache entry instead of each paying for their own scrape.
    public static string BuildCacheKey(NormalizedProduct product)
    {
        var parts = new[] { product.Brand, product.Model, product.Capacity, product.Generation, product.Condition, product.Category }
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s!.Trim().ToLowerInvariant())
            .ToList();

        return parts.Count > 0 ? string.Join('|', parts) : MarketplaceMatcher.Normalize(product.RawText);
    }

    public async Task<TerapeakMarketResult?> GetAsync(
        NormalizedProduct product, string rawQueryForScrape, bool allowRealScrape,
        TimeSpan? maxAge = null, CancellationToken ct = default)
    {
        var key = BuildCacheKey(product);
        var age = maxAge ?? TimeSpan.FromHours(48);
        var now = DateTime.UtcNow;

        var cached = cache.TryGet(key, age);
        if (cached is not null)
        {
            return new TerapeakMarketResult
            {
                Data = new SoldCompsResult
                {
                    Query = rawQueryForScrape, Average = cached.Average, Median = cached.Median,
                    AvgShipping = cached.AvgShipping, SellThroughPercent = cached.SellThroughPercent,
                },
                ScrapedAtUtc = cached.ScrapedAtUtc,
                FromCache = true,
                FreshnessWeight = FreshnessWeight(cached.ScrapedAtUtc, now),
            };
        }

        if (!allowRealScrape || !terapeak.IsConnected) return null;

        // Randomized delay so request timing doesn't look like a metronome — preserved exactly
        // from the prior GetOrScrapePricingAsync anti-bot-detection behavior. A real scrape only
        // ever happens here, once, for a product actually being analyzed right now.
        await Task.Delay(TimeSpan.FromSeconds(Random.Shared.Next(2, 10)), ct);

        TerapeakScrapeResult scrape;
        try
        {
            scrape = await terapeak.ScrapeAsync(rawQueryForScrape);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            log.Add("Warning", "Terapeak scrape failed", ex.Message);
            return null;
        }

        if (scrape.Status != "ok") return null;

        var parsed = ParseTerapeakBodyText(scrape.BodyText, rawQueryForScrape);
        if (parsed is null) return null;

        cache.Set(key, parsed.Average, parsed.Median, parsed.AvgShipping, parsed.SellThroughPercent);
        return new TerapeakMarketResult { Data = parsed, ScrapedAtUtc = now, FromCache = false, FreshnessWeight = 1.0 };
    }

    private static double FreshnessWeight(DateTime scrapedAtUtc, DateTime nowUtc)
    {
        var ageDays = (nowUtc - scrapedAtUtc).TotalDays;
        return ageDays switch
        {
            <= 30 => 1.0,
            <= 90 => 0.7,
            <= 180 => 0.4,
            _ => 0.2,
        };
    }

    // Text parse of the Seller Hub Research page, tuned against a real logged-in capture (see
    // /api/terapeak/debug-scrape). innerText renders each stat tile as "<value>\n<label>", e.g.
    // "$64.31\nAvg sold price", and each result-table row as "...\n$14.90\nFixed price\n...".
    // Moved here (was Program.cs's ParseTerapeakBodyText local function) so both the Opportunity
    // Finder search and the Supplier File Analyzer share one parser instead of each having a copy.
    // Public so the standalone /api/sold-comps and /api/terapeak/debug-scrape endpoints (outside
    // the Opportunity Finder pipeline) can still parse a raw scrape without duplicating this.
    public static SoldCompsResult? ParseTerapeakBodyText(string text, string query)
    {
        var result = new SoldCompsResult { Query = query };

        var avgMatch = Regex.Match(text, @"\$\s*([\d,]+\.\d{2})\s*\n\s*Avg sold price", RegexOptions.IgnoreCase);
        if (avgMatch.Success && decimal.TryParse(avgMatch.Groups[1].Value.Replace(",", ""), out var avg))
            result.Average = avg;

        var rangeMatch = Regex.Match(text, @"\$\s*([\d,]+\.\d{2})\s*-\s*\$\s*([\d,]+\.\d{2})\s*\n\s*Sold price range", RegexOptions.IgnoreCase);
        if (rangeMatch.Success)
        {
            if (decimal.TryParse(rangeMatch.Groups[1].Value.Replace(",", ""), out var min)) result.Min = min;
            if (decimal.TryParse(rangeMatch.Groups[2].Value.Replace(",", ""), out var max)) result.Max = max;
        }

        // "72%\nSell-through" — Terapeak shows "-" instead of a percentage when it can't compute
        // one for this query, which TryParse below just leaves as null (no data) rather than 0%.
        var sellThroughMatch = Regex.Match(text, @"([\d.]+)%\s*\n\s*Sell-through", RegexOptions.IgnoreCase);
        if (sellThroughMatch.Success && decimal.TryParse(sellThroughMatch.Groups[1].Value, out var sellThrough))
            result.SellThroughPercent = sellThrough;

        var shippingMatch = Regex.Match(text, @"\$\s*([\d,]+\.\d{2})\s*\n\s*Avg shipping", RegexOptions.IgnoreCase);
        if (shippingMatch.Success && decimal.TryParse(shippingMatch.Groups[1].Value.Replace(",", ""), out var avgShip))
            result.AvgShipping = avgShip;

        // Each matching product row shows its own avg sold price — collect them into a real
        // per-listing distribution so we get an actual median (the page-level summary above only
        // gives a single blended average, which the profit calc doesn't want).
        var rowPrices = Regex.Matches(text, @"\$\s*([\d,]+\.\d{2})\s*\n\s*(?:Fixed price|Auction|Best Offer)", RegexOptions.IgnoreCase)
            .Select(m => decimal.TryParse(m.Groups[1].Value.Replace(",", ""), out var p) ? p : (decimal?)null)
            .Where(p => p.HasValue)
            .Select(p => p!.Value)
            .OrderBy(p => p)
            .ToList();

        if (rowPrices.Count > 0)
        {
            result.Count = rowPrices.Count;
            result.Median = rowPrices.Count % 2 == 1
                ? rowPrices[rowPrices.Count / 2]
                : (rowPrices[rowPrices.Count / 2 - 1] + rowPrices[rowPrices.Count / 2]) / 2m;
        }

        return result.Count > 0 || result.Average > 0 ? result : null;
    }
}
