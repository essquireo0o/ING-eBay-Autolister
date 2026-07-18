using ING_eBay_AutoLister.Models;

namespace ING_eBay_AutoLister.Services;

// Turns a matched-and-scored set of local comparables, plus (optionally) a Terapeak lookup, into
// the full PriceEstimate the spec calls for — median/weighted-median/trimmed-mean/P25/P75/
// quick-sale/expected-sale/recommended-listing/high-price-target — instead of a raw average.
// Reuses MarketplacePricingCalculator for outlier removal and percentile/median math (not
// reimplemented here); adds per-unit quantity normalization, buying-format-aware weighting, and
// the adaptive local-vs-Terapeak blend + disagreement detection the calculator alone doesn't do.
public sealed class MarketPriceEstimator(TerapeakMarketService terapeakMarket)
{
    private const int StrongMatchThreshold = 50; // MatchConfidence floor for a comp to count as "strong"
    private const int RecentDays = 90;

    public async Task<PriceEstimate> EstimateAsync(
        NormalizedProduct target, IReadOnlyList<MarketplaceComparableResult> localComparables,
        string rawQueryForTerapeak, string? listingType, bool allowRealTerapeakScrape,
        int? activeCompetitionCount = null, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        // Per-unit normalization: a "lot of 10" sold at $500 is a $50 comparable, not a $500 one.
        var perUnit = localComparables.Select(NormalizeToUnitPrice).ToList();
        var trimmed = MarketplacePricingCalculator.RemovePriceOutliers(perUnit);

        var strongRecent = trimmed
            .Where(c => c.MatchScore >= StrongMatchThreshold && (c.SoldDate is null || (now - c.SoldDate.Value).TotalDays <= RecentDays))
            .ToList();
        // Widen the window when recent strong comps are too sparse to trust — this is the "expand
        // the historical window" behavior the spec asks for when a product has very few recent
        // sales (confidence is lowered separately, by ConfidenceScoringService).
        var strongForStats = strongRecent.Count >= 3 ? strongRecent : trimmed.Where(c => c.MatchScore >= StrongMatchThreshold).ToList();
        if (strongForStats.Count == 0) strongForStats = trimmed;

        var prices = strongForStats.Select(c => c.SoldPrice).OrderBy(p => p).ToList();
        var weighted = strongForStats.Select(c => (c.SoldPrice, Weight: WeightFor(c, listingType, now))).ToList();

        var estimate = new PriceEstimate();
        if (prices.Count > 0)
        {
            estimate.MedianPrice = Math.Round(MarketplacePricingCalculator.Median(prices), 2);
            estimate.WeightedMedianPrice = Math.Round(MarketplacePricingCalculator.WeightedMedian(weighted), 2);
            estimate.TrimmedMeanPrice = Math.Round(prices.Average(), 2);
            estimate.Percentile25 = Math.Round(MarketplacePricingCalculator.Percentile(prices, 0.25), 2);
            estimate.Percentile75 = Math.Round(MarketplacePricingCalculator.Percentile(prices, 0.75), 2);
            estimate.MinimumRealisticPrice = prices.Min();
            estimate.MaximumRealisticPrice = prices.Max();

            estimate.QuickSalePrice = estimate.Percentile25;
            estimate.ExpectedSalePrice = estimate.WeightedMedianPrice;
            // Negotiation buffer: modest by default, scaled down as active competition rises (a
            // crowded market punishes overpricing), scaled up when competition is known to be low.
            var bufferPercent = activeCompetitionCount switch
            {
                null => 0.05m,
                <= 2 => 0.08m,
                <= 15 => 0.05m,
                _ => 0.02m,
            };
            estimate.RecommendedListingPrice = Math.Round(estimate.ExpectedSalePrice!.Value * (1 + bufferPercent), 2);
            // Raw P75 — display-gating ("only show when low competition/strong demand/superior
            // condition") is the caller's responsibility (Program.cs / OpportunityScoringService),
            // since competition/demand data lives outside this estimator.
            estimate.HighPriceTarget = estimate.Percentile75;
        }

        // ── Terapeak — validates current pricing, on-demand only (never bulk/background) ──────
        var terapeakResult = await terapeakMarket.GetAsync(target, rawQueryForTerapeak, allowRealTerapeakScrape, ct: ct);

        ApplyAdaptiveWeighting(estimate, strongForStats.Count, terapeakResult);

        return estimate;
    }

    private static MarketplaceComparableResult NormalizeToUnitPrice(MarketplaceComparableResult c)
    {
        if (c.Quantity <= 1) return c;
        return new MarketplaceComparableResult
        {
            ItemId = c.ItemId, Title = c.Title, SoldPrice = Math.Round(c.SoldPrice / c.Quantity, 2),
            Shipping = c.Shipping, TotalPrice = Math.Round((c.SoldPrice + c.Shipping) / c.Quantity, 2),
            Condition = c.Condition, SoldDate = c.SoldDate, Seller = c.Seller, ItemUrl = c.ItemUrl,
            ImageUrl = c.ImageUrl, MatchScore = c.MatchScore, Epid = c.Epid, IsFixedPrice = c.IsFixedPrice,
            SellerFeedbackCount = c.SellerFeedbackCount, SellerPositiveFeedbackPercent = c.SellerPositiveFeedbackPercent,
            Quantity = c.Quantity,
        };
    }

    // Same match-strength/recency nudges MarketplacePricingCalculator.WeightFor already applies,
    // plus buying-format: when the target will be listed Buy-It-Now, a fixed-price comp is a
    // better predictor than an auction result (which can swing on bidding dynamics).
    private static double WeightFor(MarketplaceComparableResult item, string? listingType, DateTime nowUtc)
    {
        var weight = 1.0;
        weight *= item.MatchScore switch { >= 90 => 2.0, >= 70 => 1.3, _ => 1.0 };
        if (item.SoldDate is DateTime sold)
        {
            var ageDays = (nowUtc - sold).TotalDays;
            weight *= ageDays switch { <= 30 => 1.3, <= 90 => 1.1, _ => 1.0 };
        }
        if (string.Equals(listingType, "FIXED_PRICE", StringComparison.OrdinalIgnoreCase) && item.IsFixedPrice)
            weight *= 1.25;
        return weight;
    }

    // Adaptive local-vs-Terapeak blend per the spec's table, plus a >20%-median-disagreement flag
    // so the caller shows both estimates and lowers confidence instead of silently picking one.
    private static void ApplyAdaptiveWeighting(PriceEstimate estimate, int localStrongCount, TerapeakMarketResult? terapeak)
    {
        var terapeakStrongCount = terapeak?.Data.Count ?? 0;
        estimate.TerapeakComparableCount = terapeakStrongCount;

        if (terapeak is null || terapeak.Data.Median <= 0)
        {
            estimate.LocalWeight = estimate.MedianPrice is > 0 ? 1.0m : 0m;
            estimate.TerapeakWeight = 0m;
            return;
        }

        var (localWeight, terapeakWeight) = ResolveWeights(
            estimate.MedianPrice is > 0, localStrongCount, terapeakStrongCount, terapeak.FreshnessWeight);

        estimate.LocalWeight = localWeight;
        estimate.TerapeakWeight = terapeakWeight;

        var localMedian = estimate.MedianPrice ?? 0;
        var terapeakMedian = terapeak.Data.Median > 0 ? terapeak.Data.Median : terapeak.Data.Average;
        var blendedMedian = localMedian * localWeight + terapeakMedian * terapeakWeight;
        var blendedExpected = (estimate.ExpectedSalePrice ?? localMedian) * localWeight + terapeakMedian * terapeakWeight;

        var (disagree, message) = DetectDisagreement(localMedian, terapeakMedian);
        estimate.MarketDataDisagreement = disagree;
        estimate.DisagreementMessage = message;

        estimate.MedianPrice = Math.Round(blendedMedian, 2);
        estimate.ExpectedSalePrice = Math.Round(blendedExpected, 2);
        estimate.RecommendedListingPrice = Math.Round(blendedExpected * 1.05m, 2);
        if (terapeak.Data.AvgShipping > 0 && estimate.QuickSalePrice is null)
            estimate.QuickSalePrice = Math.Round(blendedMedian * 0.85m, 2);
    }

    // Adaptive local-vs-Terapeak blend weights per the spec's table. Pure/testable: takes only
    // the counts/flags the decision depends on, not the estimate/result objects themselves.
    public static (decimal LocalWeight, decimal TerapeakWeight) ResolveWeights(
        bool hasLocalMedian, int localStrongCount, int terapeakStrongCount, double terapeakFreshnessWeight)
    {
        decimal localWeight, terapeakWeight;
        if (!hasLocalMedian)
        {
            (localWeight, terapeakWeight) = (0m, 1m);
        }
        else if (terapeakStrongCount >= 10)
        {
            (localWeight, terapeakWeight) = (0.25m, 0.75m);
        }
        else if (terapeakStrongCount < 3 && localStrongCount >= 3)
        {
            (localWeight, terapeakWeight) = (0.70m, 0.30m);
        }
        else
        {
            (localWeight, terapeakWeight) = (0.40m, 0.60m);
        }

        // Apply the Terapeak-side freshness decay to its share of the blend, redistributing the
        // discount back to local rather than just discarding it — stale Terapeak data shouldn't
        // silently vanish, it should just count for less.
        terapeakWeight *= (decimal)terapeakFreshnessWeight;
        localWeight = 1m - terapeakWeight;
        return (localWeight, terapeakWeight);
    }

    // >20% median disagreement between the two sources — flagged so the caller shows both
    // estimates and lowers confidence instead of silently picking one. Pure/testable.
    public static (bool Disagree, string? Message) DetectDisagreement(decimal localMedian, decimal terapeakMedian)
    {
        if (localMedian <= 0 || terapeakMedian <= 0) return (false, null);

        var diff = Math.Abs(localMedian - terapeakMedian) / Math.Max(localMedian, terapeakMedian);
        if (diff <= 0.20m) return (false, null);

        return (true,
            $"Local sold-history median (${localMedian:0.00}) and Terapeak median (${terapeakMedian:0.00}) " +
            $"differ by {diff:P0} — showing both, confidence lowered rather than picking one.");
    }
}
