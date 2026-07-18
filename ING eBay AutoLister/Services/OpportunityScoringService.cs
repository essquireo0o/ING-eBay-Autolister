using ING_eBay_AutoLister.Models;

namespace ING_eBay_AutoLister.Services;

// The final 0-100 Opportunity Score — how good the deal looks, assuming the numbers are trusted
// (that trust is ConfidenceScoringService's separate job, consumed here only as one 10%-weighted
// input). Implements the spec's weighted-component formula, replacing Program.cs's old
// ComputeOpportunityScore local function (simpler profit/confidence/throughput/demand/trust/
// liquidity formula) entirely — not kept running alongside it.
public sealed class OpportunityScoringService(LiquidityScoringConfig liquidityConfig)
{
    // Return-risk categories get a small penalty — historically higher return/dispute rates on
    // eBay for electronics/wearables versus, say, industrial parts or collectibles.
    private static readonly string[] ReturnRiskCategories = ["Phone", "Tablet", "Laptop", "Electronics", "Clothing", "Shoes"];

    public ScoreBreakdown Score(MarketAnalysisResult result, int strongComparableCount, int? mostRecentComparableAgeDays)
    {
        var reasons = new List<string>();
        var warnings = new List<string>();

        // ── Hard rejects — capped low regardless of everything else below ──────────────────
        var profit = result.Profit;
        var maxRealistic = result.PriceEstimate.MaximumRealisticPrice;

        string? rejection = profit is null ? "No profit data available"
            : profit.NetProfitPerUnit < 0 ? "Expected net profit is negative"
            : maxRealistic is decimal maxP && profit.BreakEvenSalePrice > maxP ? "Break-even price is above the realistic market price"
            : result.SellThrough.SoldComparableCount == 0 ? "No reliable comparables exist"
            : result.Identity.IsAccessoryListing ? "Product appears to be an accessory, not the main product"
            : IsIdentityAmbiguous(result.Identity) ? "Product identity is ambiguous (no brand/model/part number/category identified)"
            : result.SellThrough.SoldComparableCount == 1 ? "Projected numbers rest on a single sold comparable"
            : null;

        if (rejection is not null)
        {
            warnings.Add(rejection);
            return new ScoreBreakdown
            {
                Score = 0, HardRejected = true, RejectionReason = rejection,
                Warnings = warnings, Reasons = reasons,
            };
        }

        // ── Weighted components (spec: profit 25, ROI 20, sell-through 20, velocity 10,
        // confidence 10, competition 5, stability 5, recency 5) ────────────────────────────
        var netProfitScore = NetProfitScore(profit!.NetProfitPerUnit);
        var roiScore = RoiScore(profit.RoiPercent);
        var sellThroughScore = result.SellThrough.SellThroughScore;
        var velocityScore = VelocityScore(result.SellThrough.EstimatedMonthlySales);
        var confidenceScore = result.Confidence.Score;
        var competitionScore = CompetitionScore(result.Competition.CloseActiveComparableCount);
        var stabilityScore = result.Stability.StabilityScore;
        var recencyScore = RecencyScore(mostRecentComparableAgeDays);

        var components = new Dictionary<string, double>
        {
            ["NetProfit"] = netProfitScore, ["ROI"] = roiScore, ["SellThrough"] = sellThroughScore,
            ["Velocity"] = velocityScore, ["Confidence"] = confidenceScore, ["Competition"] = competitionScore,
            ["Stability"] = stabilityScore, ["Recency"] = recencyScore,
        };

        var weighted =
            netProfitScore * 0.25 + roiScore * 0.20 + sellThroughScore * 0.20 + velocityScore * 0.10 +
            confidenceScore * 0.10 + competitionScore * 0.05 + stabilityScore * 0.05 + recencyScore * 0.05;

        // ── Penalties ────────────────────────────────────────────────────────────────────
        double penalty = 0;
        if (strongComparableCount < 3) { penalty += 15; warnings.Add("Fewer than three strong comparables"); }
        if (result.Confidence.Score < 40) { penalty += 10; warnings.Add("Mostly low-confidence matches"); }
        if (result.Stability.StabilityScore < 30) { penalty += 10; warnings.Add("Large price variance among comparables"); }
        if (result.Stability.Trend == "Falling") { penalty += 8; warnings.Add("Recent prices are falling"); }
        if (result.Competition.CompetitionLevel == "High") { penalty += 8; warnings.Add("High active competition"); }
        if (result.SellThrough.SellThroughScore < 15) { penalty += 10; warnings.Add("Low sell-through"); }
        if (!string.IsNullOrWhiteSpace(result.Identity.Category) &&
            ReturnRiskCategories.Any(c => result.Identity.Category.Contains(c, StringComparison.OrdinalIgnoreCase)))
        { penalty += 5; warnings.Add("Return-risk category"); }
        if (string.IsNullOrWhiteSpace(result.Identity.Condition)) { penalty += 5; warnings.Add("Unknown condition"); }
        if (string.IsNullOrWhiteSpace(result.Identity.Model) && string.IsNullOrWhiteSpace(result.Identity.PartNumber))
        { penalty += 5; warnings.Add("Missing model number"); }
        if (profit.BuyerPaidShipping <= 0) { penalty += 3; warnings.Add("Missing shipping estimate"); }
        if (profit.NetProfitPerUnit is > 0 and < 10) { penalty += 10; warnings.Add("Very small net profit"); }
        if (mostRecentComparableAgeDays is > 90) { penalty += 8; warnings.Add("Comparable data is older than 90 days"); }
        if (profit.BreakEvenSalePrice > 0 && Math.Abs(profit.BreakEvenSalePrice - profit.SupplierUnitCost) / profit.BreakEvenSalePrice < 0.10m)
        { penalty += 10; warnings.Add("Supplier cost is close to the break-even price"); }

        // User preference: target >=100% ROI. Below that is still scored, but only avoids a
        // penalty when it's offset by unusually high dollar profit, very fast sales, or high
        // confidence (low risk) — matching "should not receive a top recommendation unless..."
        var roi = profit.RoiPercent ?? -1;
        var highDollarProfit = profit.NetProfitPerUnit >= 100;
        var fastSale = result.SellThrough.EstimatedDaysToSell is <= 7;
        var highConfidence = result.Confidence.Score >= 85;
        if (roi < 100 && !(highDollarProfit || fastSale || highConfidence))
        {
            penalty += 10;
            warnings.Add("Below the 100% ROI target without offsetting profit, speed, or confidence");
        }
        else if (roi >= 100)
        {
            reasons.Add($"Meets the {roi:0}% ROI target");
        }

        if (profit.NetProfitPerUnit >= 50) reasons.Add($"${profit.NetProfitPerUnit:0.00} net profit per unit");
        if (result.SellThrough.SellThroughScore >= 60) reasons.Add($"{result.SellThrough.Interpretation} sell-through");
        if (result.Confidence.Score >= 65) reasons.Add(result.Confidence.Level);

        var finalScore = (int)Math.Round(Math.Clamp(weighted - penalty, 0, 100));

        return new ScoreBreakdown
        {
            Score = finalScore, ComponentScores = components, Reasons = reasons, Warnings = warnings,
            HardRejected = false,
        };
    }

    private static bool IsIdentityAmbiguous(NormalizedProduct identity) =>
        string.IsNullOrWhiteSpace(identity.Brand) && string.IsNullOrWhiteSpace(identity.Model) &&
        string.IsNullOrWhiteSpace(identity.PartNumber) && string.IsNullOrWhiteSpace(identity.Category);

    private static double NetProfitScore(decimal netProfitPerUnit) => netProfitPerUnit switch
    {
        < 10 => 0, < 25 => 20, < 50 => 40, < 100 => 65, < 250 => 85, _ => 100,
    };

    private static double RoiScore(decimal? roiPercent) => roiPercent switch
    {
        null or < 0 => 0, < 25 => 10, < 50 => 25, < 75 => 45, < 100 => 65, < 150 => 85, _ => 100,
    };

    // Reuses LiquidityScoringConfig's velocity threshold instead of a second hardcoded scale.
    private double VelocityScore(decimal monthlySales) =>
        Math.Clamp((double)monthlySales / liquidityConfig.VelocityFullScoreThreshold, 0, 1) * 100.0;

    // Reuses LiquidityScoringConfig's competition thresholds (low=3, high=30) — inverted, since
    // fewer active competitors is better for the opportunity score.
    private double CompetitionScore(int closeActiveComparableCount)
    {
        var fraction = 1.0 - Math.Clamp(
            (closeActiveComparableCount - liquidityConfig.CompetitionLowThreshold) /
            (double)(liquidityConfig.CompetitionHighThreshold - liquidityConfig.CompetitionLowThreshold), 0, 1);
        return fraction * 100.0;
    }

    private static double RecencyScore(int? ageDays) => ageDays switch
    {
        null => 50.0, <= 30 => 100.0, <= 90 => 60.0, <= 180 => 30.0, _ => 10.0,
    };
}
