using ING_eBay_AutoLister.Models;

namespace ING_eBay_AutoLister.Services;

// Estimates how quickly an item is likely to sell, using only SoldDate density in comparables
// already pulled from the local sold-history database — no external API calls happen here.
// ActiveCompetitionCount is the one field that can reflect data from elsewhere in the app (e.g.
// an already-fetched live-listing count); it's simply Unknown (null) unless the caller supplies
// it, since this service itself never goes and fetches it.
//
// Constructible with or without DI (ActionLog is optional) so it's usable both as a registered
// singleton and directly from pure/static callers like MarketplacePricingCalculator.
public class LiquidityScoringService(LiquidityScoringConfig? config = null, ActionLog? log = null)
{
    private readonly LiquidityScoringConfig _config = config ?? new();

    public LiquidityAssessment Assess(
        string productQuery, IReadOnlyList<MarketplaceComparableResult> comparables,
        int? activeCompetitionCount = null, DateTime? nowUtc = null)
    {
        var now = nowUtc ?? DateTime.UtcNow;
        var c = _config;

        // Malformed/missing SoldDate values are simply excluded from every date-based
        // calculation below — MarketplaceComparableResult.SoldDate is already null for anything
        // that failed to parse (see MarketplaceRepository), and a future-dated or default value
        // is defensively excluded here too rather than trusted.
        var dated = comparables
            .Where(cmp => cmp.SoldDate is DateTime d && d > DateTime.MinValue && d <= now.AddDays(1))
            .Select(cmp => cmp.SoldDate!.Value)
            .OrderBy(d => d)
            .ToList();

        var lookbackCutoff = now.AddDays(-c.LookbackDaysForCap);
        var withinLookback = dated.Where(d => d >= lookbackCutoff).ToList();

        var assessment = new LiquidityAssessment
        {
            ActiveCompetitionCount = activeCompetitionCount,
        };

        if (dated.Count == 0)
        {
            assessment.SalesVelocity = 0;
            assessment.DemandTrend = "Unknown";
            assessment.LiquidityScore = 0;
            assessment.LiquidityLevel = "Stale/Illiquid";
            assessment.HasSufficientData = false;
            assessment.InsufficientDataMessage = "Not enough recent sales to estimate how fast this sells.";

            log?.Add("Info", "Liquidity assessment", $"Query: \"{productQuery}\"; Comparables: {comparables.Count} (0 with usable sold dates); Velocity: 0; Score: 0");
            return assessment;
        }

        // ── Velocity: sales per 30-day period, anchored to "how far back does the data go from
        // now" rather than the raw min/max span of the sample — a single old sale correctly
        // reads as very low velocity instead of a degenerate near-zero span inflating the rate.
        var oldest = withinLookback.Count > 0 ? withinLookback.Min() : dated.Min();
        var spanDays = Math.Max(1.0, (now - oldest).TotalDays);
        var salesCountForVelocity = withinLookback.Count > 0 ? withinLookback.Count : dated.Count;
        assessment.SalesVelocity = Math.Round((decimal)(salesCountForVelocity / spanDays * 30.0), 2);

        // ── Days between sales: average gap between consecutive sold dates.
        var sortedForGaps = withinLookback.Count >= 2 ? withinLookback : dated;
        if (sortedForGaps.Count >= 2)
        {
            var gaps = new List<double>();
            for (var i = 1; i < sortedForGaps.Count; i++)
                gaps.Add((sortedForGaps[i] - sortedForGaps[i - 1]).TotalDays);
            assessment.DaysBetweenSales = Math.Round(gaps.Average(), 1);
        }

        // ── Estimated days to sell: the inverse of the 30-day velocity rate, clamped to a sane range.
        if (assessment.SalesVelocity > 0)
        {
            var days = 30.0 / (double)assessment.SalesVelocity;
            assessment.EstimatedDaysToSell = Math.Clamp((int)Math.Round(days), c.MinEstimatedDaysToSell, c.MaxEstimatedDaysToSell);
        }

        var hasEnoughForTrend = withinLookback.Count >= c.MinComparablesForReliableTrend;

        // ── Demand trend: recent 30-day count vs. the average of the 30-60 and 60-90 day-ago buckets.
        double trendRatio;
        if (hasEnoughForTrend)
        {
            var recentCount = withinLookback.Count(d => d >= now.AddDays(-c.RecentWindowDays));
            var midCount = withinLookback.Count(d => d >= now.AddDays(-c.MidWindowDays) && d < now.AddDays(-c.RecentWindowDays));
            var oldCount = withinLookback.Count(d => d >= now.AddDays(-c.LongWindowDays) && d < now.AddDays(-c.MidWindowDays));
            var olderRate = (midCount + oldCount) / 2.0;

            if (recentCount == 0 && olderRate == 0)
            {
                assessment.DemandTrend = "Stable";
                trendRatio = 1.0;
            }
            else if (olderRate == 0)
            {
                assessment.DemandTrend = recentCount > 0 ? "Increasing" : "Stable";
                trendRatio = recentCount > 0 ? c.RisingTrendRatio : 1.0;
            }
            else
            {
                trendRatio = recentCount / olderRate;
                assessment.DemandTrend = trendRatio >= c.RisingTrendRatio ? "Increasing"
                    : trendRatio <= c.FallingTrendRatio ? "Decreasing"
                    : "Stable";
            }
        }
        else
        {
            // Fewer than 3 comparables in the last 12 months — trend can't be trusted.
            assessment.DemandTrend = "Unknown";
            trendRatio = 1.0;
        }

        // ── LiquidityScore: velocity (0..VelocityWeight) + trend swing (+/-TrendWeight) +
        // competition (0..CompetitionWeight, neutral half-credit when competition is Unknown).
        var velocityFraction = Clamp01(
            ((double)assessment.SalesVelocity - c.VelocityZeroScoreThreshold) /
            (c.VelocityFullScoreThreshold - c.VelocityZeroScoreThreshold));
        var velocityScore = velocityFraction * c.VelocityWeight;

        var trendAdjustment = assessment.DemandTrend switch
        {
            "Increasing" => c.TrendWeight,
            "Decreasing" => -c.TrendWeight,
            _ => 0.0, // Stable or Unknown — no adjustment either way
        };

        double competitionScore;
        if (activeCompetitionCount is int comp)
        {
            var competitionFraction = 1.0 - Clamp01(
                (comp - c.CompetitionLowThreshold) / (double)(c.CompetitionHighThreshold - c.CompetitionLowThreshold));
            competitionScore = competitionFraction * c.CompetitionWeight;
        }
        else
        {
            competitionScore = c.CompetitionWeight / 2.0; // Unknown — neutral, neither rewarded nor penalized
        }

        var rawScore = velocityScore + trendAdjustment + competitionScore;
        var score = (int)Math.Round(Math.Clamp(rawScore, 0, 100));

        if (!hasEnoughForTrend)
        {
            // Fewer than 3 comparables in the last 12 months: cap regardless of what the
            // velocity/competition math produced, and the caller should treat this as "not
            // enough data" even though a (capped) score is still returned.
            score = Math.Min(score, c.CappedLiquidityScoreWhenInsufficientData);
            assessment.HasSufficientData = false;
            assessment.InsufficientDataMessage = "Not enough recent sales to estimate how fast this sells.";
        }
        else
        {
            assessment.HasSufficientData = true;
        }

        assessment.LiquidityScore = score;
        assessment.LiquidityLevel = score switch
        {
            _ when score >= c.FastMoverThreshold => "Fast Mover",
            _ when score >= c.ModerateThreshold => "Moderate",
            _ when score >= c.SlowMoverThreshold => "Slow Mover",
            _ => "Stale/Illiquid",
        };

        log?.Add("Info", "Liquidity assessment",
            $"Query: \"{productQuery}\"; Comparables used: {salesCountForVelocity}; " +
            $"Velocity: {assessment.SalesVelocity}/30d; Trend: {assessment.DemandTrend}; Score: {assessment.LiquidityScore} ({assessment.LiquidityLevel})");

        return assessment;
    }

    private static double Clamp01(double value) => Math.Clamp(value, 0.0, 1.0);
}
