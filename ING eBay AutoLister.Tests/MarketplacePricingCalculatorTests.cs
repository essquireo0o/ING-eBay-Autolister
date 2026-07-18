using ING_eBay_AutoLister.Models;
using ING_eBay_AutoLister.Services;

namespace ING_eBay_AutoLister.Tests;

public class MarketplacePricingCalculatorTests
{
    private static MarketplaceComparableResult Comp(decimal price, int matchScore = 90, decimal shipping = 0, DateTime? soldDate = null) => new()
    {
        ItemId = Guid.NewGuid().ToString(),
        Title = "Test item",
        SoldPrice = price,
        Shipping = shipping,
        TotalPrice = price + shipping,
        MatchScore = matchScore,
        SoldDate = soldDate,
    };

    [Theory]
    [InlineData(new double[] { 1, 2, 3 }, 2)]
    [InlineData(new double[] { 1, 2, 3, 4 }, 2.5)]
    [InlineData(new double[] { 10 }, 10)]
    public void Median_ComputesCorrectly(double[] values, double expected)
    {
        var decimals = values.Select(v => (decimal)v).ToList();
        var result = MarketplacePricingCalculator.Median(decimals);
        Assert.Equal((decimal)expected, result);
    }

    [Fact]
    public void RemovePriceOutliers_DropsExtremeOutlierFarFromTheCluster()
    {
        var items = new List<MarketplaceComparableResult>
        {
            Comp(95), Comp(100), Comp(105), Comp(110), Comp(5000), // 5000 is an obvious outlier
        };

        var trimmed = MarketplacePricingCalculator.RemovePriceOutliers(items);

        Assert.DoesNotContain(trimmed, i => i.SoldPrice == 5000);
        Assert.Equal(4, trimmed.Count);
    }

    [Fact]
    public void RemovePriceOutliers_SkipsTrimmingWhenTooFewSamples()
    {
        // Fewer than 4 samples — quartiles aren't meaningful, so nothing should be dropped even
        // though 5000 looks extreme next to the other two.
        var items = new List<MarketplaceComparableResult> { Comp(100), Comp(110), Comp(5000) };

        var trimmed = MarketplacePricingCalculator.RemovePriceOutliers(items);

        Assert.Equal(3, trimmed.Count);
    }

    [Fact]
    public void WeightedMedian_HeavierItemsPullTheResultTowardThem()
    {
        var weighted = new List<(decimal Price, double Weight)> { (100m, 1.0), (200m, 1.0), (100m, 5.0) };

        var result = MarketplacePricingCalculator.WeightedMedian(weighted);

        Assert.Equal(100m, result); // the heavily-weighted 100 should dominate the 50th percentile
    }

    [Fact]
    public void Summarize_SuggestedPrice_IsNotSimplyTheHighestListing()
    {
        var comparables = new List<MarketplaceComparableResult>
        {
            Comp(90), Comp(95), Comp(100), Comp(105), Comp(400), // 400 is the highest, and an outlier
        };

        var summary = MarketplacePricingCalculator.Summarize("test query", comparables, preferredCondition: null);

        Assert.True(summary.SuggestedResalePrice < 400, "suggested price should not just be the highest listing");
        Assert.True(summary.SuggestedResalePrice is >= 85 and <= 115, $"expected a price near the cluster, got {summary.SuggestedResalePrice}");
    }

    [Fact]
    public void Summarize_NoComparables_ReturnsZeroMatchCountAndZeroConfidence()
    {
        var summary = MarketplacePricingCalculator.Summarize("nothing found", [], preferredCondition: null);

        Assert.Equal(0, summary.MatchCount);
        Assert.Equal(0, summary.ConfidenceScore);
        Assert.Null(summary.SuggestedResalePrice);
    }

    [Fact]
    public void Summarize_MoreAndTighterComparables_YieldsHigherConfidence()
    {
        var tight = Enumerable.Range(0, 10).Select(_ => Comp(100, matchScore: 100)).ToList();
        var loose = new List<MarketplaceComparableResult> { Comp(50, matchScore: 30), Comp(500, matchScore: 30) };

        var tightSummary = MarketplacePricingCalculator.Summarize("q", tight, null);
        var looseSummary = MarketplacePricingCalculator.Summarize("q", loose, null);

        Assert.True(tightSummary.ConfidenceScore > looseSummary.ConfidenceScore);
    }
}
