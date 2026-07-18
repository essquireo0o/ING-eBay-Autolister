using ING_eBay_AutoLister.Models;
using ING_eBay_AutoLister.Services;

namespace ING_eBay_AutoLister.Tests;

public class LiquidityScoringServiceTests
{
    private static readonly DateTime Now = new(2026, 7, 16, 0, 0, 0, DateTimeKind.Utc);
    private readonly LiquidityScoringService _service = new();

    private static MarketplaceComparableResult Comp(int daysAgo) => new()
    {
        ItemId = Guid.NewGuid().ToString(),
        Title = "Test item",
        SoldPrice = 100m,
        SoldDate = Now.AddDays(-daysAgo),
    };

    [Fact]
    public void Assess_ManyRecentSales_IsFastMover()
    {
        // 10 sales clustered in the last 29 days -> high density, all recent -> Increasing trend.
        var comparables = Enumerable.Range(1, 10).Select(Comp).ToList();

        var result = _service.Assess("fast mover item", comparables, nowUtc: Now);

        Assert.True(result.HasSufficientData);
        Assert.Equal("Fast Mover", result.LiquidityLevel);
        Assert.True(result.LiquidityScore >= 70, $"expected >=70, got {result.LiquidityScore}");
        Assert.True(result.SalesVelocity > 5, $"expected high velocity, got {result.SalesVelocity}");
    }

    [Fact]
    public void Assess_SparseOldSales_IsSlowMover()
    {
        // 4 sales spread across ~100 days, none in the last 30 -> low velocity, stable/no trend.
        var comparables = new List<MarketplaceComparableResult> { Comp(10), Comp(40), Comp(70), Comp(95) };

        var result = _service.Assess("slow mover item", comparables, nowUtc: Now);

        Assert.True(result.HasSufficientData);
        Assert.Equal("Slow Mover", result.LiquidityLevel);
        Assert.InRange(result.LiquidityScore, 15, 39);
    }

    [Fact]
    public void Assess_MoreSalesRecentlyThanBefore_IsIncreasingTrend()
    {
        // 5 sales in the last 20 days vs. 1 sale 45 days ago and 1 sale 75 days ago.
        var comparables = new List<MarketplaceComparableResult>
        {
            Comp(2), Comp(6), Comp(11), Comp(16), Comp(20), Comp(45), Comp(75),
        };

        var result = _service.Assess("rising item", comparables, nowUtc: Now);

        Assert.Equal("Increasing", result.DemandTrend);
    }

    [Fact]
    public void Assess_FewerSalesRecentlyThanBefore_IsDecreasingTrend()
    {
        // 1 sale in the last 20 days vs. 4 sales 45 days ago and 4 sales 75 days ago.
        var comparables = new List<MarketplaceComparableResult>
        {
            Comp(15),
            Comp(42), Comp(44), Comp(46), Comp(48),
            Comp(72), Comp(74), Comp(76), Comp(78),
        };

        var result = _service.Assess("falling item", comparables, nowUtc: Now);

        Assert.Equal("Decreasing", result.DemandTrend);
    }

    [Fact]
    public void Assess_FewerThanThreeComparables_CapsScoreAndMarksTrendUnknown()
    {
        // Only 2 comparables, both very recent (would otherwise score very high on velocity alone).
        var comparables = new List<MarketplaceComparableResult> { Comp(1), Comp(2) };

        var result = _service.Assess("sparse item", comparables, nowUtc: Now);

        Assert.False(result.HasSufficientData);
        Assert.Equal("Unknown", result.DemandTrend);
        Assert.True(result.LiquidityScore <= 40, $"expected score capped at 40, got {result.LiquidityScore}");
        Assert.NotNull(result.InsufficientDataMessage);
    }

    [Fact]
    public void Assess_MalformedOrMissingSoldDates_AreExcludedWithoutCrashing()
    {
        var comparables = new List<MarketplaceComparableResult>
        {
            Comp(5), Comp(10), Comp(15), // valid, recent
            new() { ItemId = "bad1", Title = "no date", SoldPrice = 50m, SoldDate = null },
            new() { ItemId = "bad2", Title = "default date", SoldPrice = 50m, SoldDate = default },
            new() { ItemId = "bad3", Title = "far future date", SoldPrice = 50m, SoldDate = Now.AddYears(50) },
        };

        var result = _service.Assess("mixed dates item", comparables, nowUtc: Now);

        // Only the 3 valid, recent dates (5/10/15 days ago) should have been used — proven by a
        // real, non-crashing result consistent with exactly those 3, not the 6 total rows.
        Assert.True(result.HasSufficientData);
        Assert.True(result.SalesVelocity > 0);
        Assert.Equal("Increasing", result.DemandTrend); // all 3 usable dates fall in the last 30 days
    }

    [Fact]
    public void Assess_ZeroComparables_ReturnsInsufficientDataWithoutCrashing()
    {
        var result = _service.Assess("nothing", [], nowUtc: Now);

        Assert.False(result.HasSufficientData);
        Assert.Equal(0, result.LiquidityScore);
        Assert.Equal("Stale/Illiquid", result.LiquidityLevel);
        Assert.NotNull(result.InsufficientDataMessage);
    }

    [Fact]
    public void Assess_VeryOldComparablesOnly_ReducesLiquidityWithoutError()
    {
        // 5 sales all ~300 days ago, well within the 365-day lookback but nowhere near "recent".
        var comparables = new List<MarketplaceComparableResult>
        {
            Comp(290), Comp(295), Comp(300), Comp(305), Comp(310),
        };

        var result = _service.Assess("old item", comparables, nowUtc: Now);

        Assert.True(result.HasSufficientData); // 5 >= MinComparablesForReliableTrend
        Assert.True(result.LiquidityScore < 40, $"expected low score for stale sales, got {result.LiquidityScore}");
        Assert.NotEqual("Fast Mover", result.LiquidityLevel);
    }
}
