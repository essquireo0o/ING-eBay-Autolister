using ING_eBay_AutoLister.Models;

namespace ING_eBay_AutoLister.Services;

// Turns a set of matched comparable sold listings into descriptive stats and a defensible
// suggested resale price. Pure/static — no I/O — so it can be unit tested without a database.
public static class MarketplacePricingCalculator
{
    // Interquartile-range outlier rejection: drops anything more than 1.5x the IQR outside the
    // 25th/75th percentile of sold price. Skipped below 4 samples — quartiles aren't meaningful
    // on that few points, and trimming a 3-item set can easily throw away the only real signal.
    public static List<MarketplaceComparableResult> RemovePriceOutliers(IReadOnlyList<MarketplaceComparableResult> items)
    {
        if (items.Count < 4) return [.. items];

        var prices = items.Select(i => i.SoldPrice).OrderBy(p => p).ToList();
        var q1 = Percentile(prices, 0.25);
        var q3 = Percentile(prices, 0.75);
        var iqr = q3 - q1;
        var lower = q1 - 1.5m * iqr;
        var upper = q3 + 1.5m * iqr;

        var kept = items.Where(i => i.SoldPrice >= lower && i.SoldPrice <= upper).ToList();
        // If every point technically falls "outside" a degenerate zero-IQR band (e.g. almost all
        // prices identical except one), don't return an empty set — fall back to the original.
        return kept.Count > 0 ? kept : [.. items];
    }

    // Public so MarketPriceEstimator can reuse the same interpolation for P25/P75/quick-sale/
    // high-price-target instead of re-implementing percentile math.
    public static decimal Percentile(List<decimal> sortedAscending, double percentile)
    {
        if (sortedAscending.Count == 1) return sortedAscending[0];
        var rank = percentile * (sortedAscending.Count - 1);
        var lowerIndex = (int)Math.Floor(rank);
        var upperIndex = (int)Math.Ceiling(rank);
        if (lowerIndex == upperIndex) return sortedAscending[lowerIndex];
        var fraction = (decimal)(rank - lowerIndex);
        return sortedAscending[lowerIndex] + (sortedAscending[upperIndex] - sortedAscending[lowerIndex]) * fraction;
    }

    public static decimal Median(IReadOnlyList<decimal> values)
    {
        if (values.Count == 0) return 0;
        var sorted = values.OrderBy(v => v).ToList();
        var mid = sorted.Count / 2;
        return sorted.Count % 2 == 0 ? (sorted[mid - 1] + sorted[mid]) / 2m : sorted[mid];
    }

    // The 50th-percentile price when every item counts `Weight` times instead of once — heavier
    // items pull the median toward them without needing to physically duplicate rows.
    public static decimal WeightedMedian(IReadOnlyList<(decimal Price, double Weight)> weighted)
    {
        var positiveWeighted = weighted.Where(w => w.Weight > 0).OrderBy(w => w.Price).ToList();
        if (positiveWeighted.Count == 0) return 0;

        var totalWeight = positiveWeighted.Sum(w => w.Weight);
        var half = totalWeight / 2.0;
        var cumulative = 0.0;
        foreach (var (price, weight) in positiveWeighted)
        {
            cumulative += weight;
            if (cumulative >= half) return price;
        }
        return positiveWeighted[^1].Price;
    }

    // Exact model/part-number matches count for more than a broad keyword match, and recent
    // sales count for more than old ones — both nudges are deliberately modest (never more than
    // ~2x) so a handful of matches can't swamp the rest of the comparable set.
    private static double WeightFor(MarketplaceComparableResult item, string? preferredCondition, DateTime nowUtc)
    {
        var weight = 1.0;

        weight *= item.MatchScore switch
        {
            >= 95 => 2.0,  // exact model / part-number match
            >= 80 => 1.3,  // all important words matched
            _     => 1.0
        };

        if (item.SoldDate is DateTime sold)
        {
            var ageDays = (nowUtc - sold).TotalDays;
            weight *= ageDays switch
            {
                <= 30 => 1.3,
                <= 90 => 1.1,
                _     => 1.0
            };
        }

        if (!string.IsNullOrWhiteSpace(preferredCondition) && !string.IsNullOrWhiteSpace(item.Condition) &&
            item.Condition.Contains(preferredCondition, StringComparison.OrdinalIgnoreCase))
        {
            weight *= 1.2;
        }

        return weight;
    }

    // Count, average/median/min/max sold price, average shipping, a defensible suggested resale
    // price, a 0-100 confidence score, and sell-through/liquidity — everything the Opportunity
    // Finder needs to show for one lookup, from a single already-matched, already-scored set of
    // comparables. liquidityService defaults to an unconfigured instance so existing callers
    // (including tests) that don't pass one keep compiling and behaving sensibly.
    public static MarketplacePricingSummary Summarize(
        string query, IReadOnlyList<MarketplaceComparableResult> comparables, string? preferredCondition,
        DateTime? nowUtc = null, LiquidityScoringService? liquidityService = null, int? activeCompetitionCount = null)
    {
        var now = nowUtc ?? DateTime.UtcNow;
        var liquidity = (liquidityService ?? new LiquidityScoringService()).Assess(query, comparables, activeCompetitionCount, now);

        if (comparables.Count == 0)
        {
            return new MarketplacePricingSummary
            {
                Query = query, MatchCount = 0, ConfidenceScore = 0,
                Liquidity = liquidity, LiquidityScore = liquidity.LiquidityScore, LiquidityLevel = liquidity.LiquidityLevel
            };
        }

        var prices = comparables.Select(c => c.SoldPrice).ToList();
        var summary = new MarketplacePricingSummary
        {
            Query           = query,
            MatchCount      = comparables.Count,
            AveragePrice    = Math.Round(prices.Average(), 2),
            MedianPrice     = Math.Round(Median(prices), 2),
            MinimumPrice    = prices.Min(),
            MaximumPrice    = prices.Max(),
            AverageShipping = Math.Round(comparables.Average(c => c.Shipping), 2),
            ComparableListings = [.. comparables],
            Liquidity       = liquidity,
            LiquidityScore  = liquidity.LiquidityScore,
            LiquidityLevel  = liquidity.LiquidityLevel,
        };

        // Suggested price: reject outliers, weight exact matches + recent sales more heavily,
        // then take the weighted median — never just the highest listing in the set.
        var trimmed = RemovePriceOutliers(comparables);
        var weighted = trimmed.Select(c => (c.SoldPrice, WeightFor(c, preferredCondition, now))).ToList();
        summary.SuggestedResalePrice = Math.Round(WeightedMedian(weighted), 2);

        // Confidence blends sample size, how strong the matches were, and how tightly the prices
        // cluster (a wide spread means "comparable" is doing a lot of work and the number is less
        // trustworthy even with plenty of samples).
        var countScore = Math.Min(comparables.Count, 10) / 10.0 * 40.0;
        var matchScore = comparables.Average(c => c.MatchScore) / 100.0 * 35.0;
        var median = summary.MedianPrice ?? 0;
        var spread = median > 0 ? (double)((summary.MaximumPrice - summary.MinimumPrice) / median) : 1.0;
        var consistencyScore = Math.Clamp(1.0 - spread / 2.0, 0.0, 1.0) * 25.0;
        summary.ConfidenceScore = (int)Math.Round(Math.Clamp(countScore + matchScore + consistencyScore, 0, 100));

        return summary;
    }
}
