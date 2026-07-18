using ING_eBay_AutoLister.Models;
using ING_eBay_AutoLister.Services;

namespace ING_eBay_AutoLister.Tests;

public class OpportunityScoringServiceTests
{
    private static OpportunityScoringService CreateService() => new(new LiquidityScoringConfig());

    // A solid, unremarkable-but-valid opportunity: real identity, several strong comparables,
    // decent profit, good sell-through, low competition, stable prices, recent data.
    private static MarketAnalysisResult GoodResult(decimal netProfitPerUnit = 60m, decimal? roiPercent = 120m) => new()
    {
        Identity = new NormalizedProduct { Brand = "Bitmain", Model = "S19 Pro", Category = "Bitcoin Miner", Condition = "Used", Quantity = 1 },
        PriceEstimate = new PriceEstimate { MedianPrice = 900m, MaximumRealisticPrice = 1000m, MinimumRealisticPrice = 800m },
        SellThrough = new SellThroughAnalysis { SellThroughScore = 70, EstimatedMonthlySales = 5m, EstimatedDaysToSell = 10, SoldComparableCount = 6 },
        Competition = new CompetitionAnalysis { CloseActiveComparableCount = 2, CompetitionLevel = "Low" },
        Profit = new ProfitBreakdown
        {
            SupplierUnitCost = 500m, NetProfitPerUnit = netProfitPerUnit, TotalPotentialProfit = netProfitPerUnit,
            RoiPercent = roiPercent, BreakEvenSalePrice = 560m, BuyerPaidShipping = 20m,
        },
        Confidence = new ConfidenceBreakdown { Score = 80, Level = "Good Confidence" },
        Stability = new PriceStability { StabilityScore = 80, Trend = "Stable" },
    };

    [Fact]
    public void Score_NegativeNetProfit_IsHardRejected()
    {
        var service = CreateService();
        var result = GoodResult(netProfitPerUnit: -10m, roiPercent: -2m);

        var score = service.Score(result, strongComparableCount: 5, mostRecentComparableAgeDays: 5);

        Assert.True(score.HardRejected);
        Assert.Equal("Expected net profit is negative", score.RejectionReason);
        Assert.Equal(0, score.Score);
    }

    [Fact]
    public void Score_NoSoldComparables_IsHardRejected()
    {
        var service = CreateService();
        var result = GoodResult();
        result.SellThrough.SoldComparableCount = 0;

        var score = service.Score(result, strongComparableCount: 0, mostRecentComparableAgeDays: null);

        Assert.True(score.HardRejected);
        Assert.Equal("No reliable comparables exist", score.RejectionReason);
    }

    [Fact]
    public void Score_AccessoryListing_IsHardRejected()
    {
        var service = CreateService();
        var result = GoodResult();
        result.Identity.IsAccessoryListing = true;

        var score = service.Score(result, strongComparableCount: 5, mostRecentComparableAgeDays: 5);

        Assert.True(score.HardRejected);
        Assert.Contains("accessory", score.RejectionReason);
    }

    [Fact]
    public void Score_AmbiguousIdentity_IsHardRejected()
    {
        var service = CreateService();
        var result = GoodResult();
        result.Identity = new NormalizedProduct(); // no brand/model/part number/category at all

        var score = service.Score(result, strongComparableCount: 5, mostRecentComparableAgeDays: 5);

        Assert.True(score.HardRejected);
        Assert.Contains("ambiguous", score.RejectionReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Score_SingleSoldComparable_IsHardRejected()
    {
        var service = CreateService();
        var result = GoodResult();
        result.SellThrough.SoldComparableCount = 1;

        var score = service.Score(result, strongComparableCount: 1, mostRecentComparableAgeDays: 5);

        Assert.True(score.HardRejected);
        Assert.Contains("single sold comparable", score.RejectionReason);
    }

    [Fact]
    public void Score_StrongOpportunityMeetingRoiTarget_ScoresWellAboveWeakOne()
    {
        var service = CreateService();
        var strong = GoodResult(netProfitPerUnit: 150m, roiPercent: 150m);
        var weak = GoodResult(netProfitPerUnit: 8m, roiPercent: 5m);
        weak.SellThrough.SellThroughScore = 10;
        weak.Stability.StabilityScore = 20;

        var strongScore = service.Score(strong, strongComparableCount: 6, mostRecentComparableAgeDays: 5);
        var weakScore = service.Score(weak, strongComparableCount: 6, mostRecentComparableAgeDays: 5);

        Assert.False(strongScore.HardRejected);
        Assert.True(strongScore.Score > weakScore.Score);
    }

    [Fact]
    public void Score_FewerThanThreeStrongComparables_RecordsLowComparableCountWarning()
    {
        var service = CreateService();
        var result = GoodResult();

        var score = service.Score(result, strongComparableCount: 1, mostRecentComparableAgeDays: 5);

        Assert.Contains(score.Warnings, w => w.Contains("Fewer than three", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Score_BelowRoiTargetWithoutOffsettingFactors_IsPenalized()
    {
        var service = CreateService();
        var belowTarget = GoodResult(netProfitPerUnit: 20m, roiPercent: 40m); // under 100% ROI, modest profit, not fast, not super-confident
        belowTarget.Confidence.Score = 50;
        belowTarget.SellThrough.EstimatedDaysToSell = 60;

        var atTarget = GoodResult(netProfitPerUnit: 20m, roiPercent: 100m);
        atTarget.Confidence.Score = 50;
        atTarget.SellThrough.EstimatedDaysToSell = 60;

        var belowScore = service.Score(belowTarget, strongComparableCount: 6, mostRecentComparableAgeDays: 5);
        var atScore = service.Score(atTarget, strongComparableCount: 6, mostRecentComparableAgeDays: 5);

        Assert.Contains(belowScore.Warnings, w => w.Contains("100% ROI target"));
        Assert.True(atScore.Score > belowScore.Score);
    }

    [Fact]
    public void Score_BelowRoiTargetButHighDollarProfit_IsNotPenalizedForRoi()
    {
        var service = CreateService();
        var result = GoodResult(netProfitPerUnit: 250m, roiPercent: 40m); // big dollar profit offsets low ROI%

        var score = service.Score(result, strongComparableCount: 6, mostRecentComparableAgeDays: 5);

        Assert.DoesNotContain(score.Warnings, w => w.Contains("100% ROI target"));
    }
}
