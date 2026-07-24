using ING_eBay_AutoLister.Services;

namespace ING_eBay_AutoLister.Tests;

public class MarketPriceEstimatorTests
{
    // ── Weight resolution ────────────────────────────────────────────────────────

    [Fact]
    public void ResolveWeights_NoLocalMedian_TrustsTerapeakEntirely()
    {
        var (local, terapeak) = MarketPriceEstimator.ResolveWeights(
            hasLocalMedian: false, localStrongCount: 0, terapeakStrongCount: 5, terapeakFreshnessWeight: 1.0);

        Assert.Equal(0m, local);
        Assert.Equal(1m, terapeak);
    }

    [Fact]
    public void ResolveWeights_NoTerapeakComps_TrustsLocalEntirely()
    {
        var (local, terapeak) = MarketPriceEstimator.ResolveWeights(
            hasLocalMedian: true, localStrongCount: 6, terapeakStrongCount: 0, terapeakFreshnessWeight: 1.0);

        Assert.Equal(1m, local);
        Assert.Equal(0m, terapeak);
    }

    [Fact]
    public void ResolveWeights_EqualEvidence_SplitsEvenly()
    {
        // Equal counts, no spread info -> pure sample-size ratio = 50/50 (no built-in bias).
        var (local, terapeak) = MarketPriceEstimator.ResolveWeights(true, 5, 5, 1.0);

        Assert.Equal(0.50m, terapeak, 2);
        Assert.Equal(0.50m, local, 2);
    }

    [Fact]
    public void ResolveWeights_MoreTerapeakComps_FavorTerapeakSmoothlyAndMonotonically()
    {
        var few  = MarketPriceEstimator.ResolveWeights(true, localStrongCount: 5, terapeakStrongCount: 5,  terapeakFreshnessWeight: 1.0);
        var some = MarketPriceEstimator.ResolveWeights(true, localStrongCount: 5, terapeakStrongCount: 10, terapeakFreshnessWeight: 1.0);
        var many = MarketPriceEstimator.ResolveWeights(true, localStrongCount: 5, terapeakStrongCount: 20, terapeakFreshnessWeight: 1.0);

        // Strictly increasing Terapeak weight as its comp count grows — no discontinuous jumps.
        Assert.True(some.TerapeakWeight > few.TerapeakWeight);
        Assert.True(many.TerapeakWeight > some.TerapeakWeight);
        Assert.Equal(0.667m, some.TerapeakWeight, 2); // 10 / (5 + 10)
    }

    [Fact]
    public void ResolveWeights_ThinTerapeakStrongLocal_FavorsLocal()
    {
        var (local, terapeak) = MarketPriceEstimator.ResolveWeights(true, localStrongCount: 5, terapeakStrongCount: 1, terapeakFreshnessWeight: 1.0);

        Assert.True(local > terapeak);
        Assert.Equal(0.167m, terapeak, 2); // 1 / (5 + 1)
    }

    [Fact]
    public void ResolveWeights_WiderSpreadLowersThatSourcesWeight()
    {
        // Same counts; give Terapeak a much wider spread relative to its median -> less reliable.
        var tight = MarketPriceEstimator.ResolveWeights(true, 5, 5, 1.0,
            localMedian: 100m, localSpread: 10m, terapeakMedian: 100m, terapeakSpread: 10m);
        var wide  = MarketPriceEstimator.ResolveWeights(true, 5, 5, 1.0,
            localMedian: 100m, localSpread: 10m, terapeakMedian: 100m, terapeakSpread: 120m);

        Assert.Equal(0.50m, tight.TerapeakWeight, 2);       // equal spread -> even split
        Assert.True(wide.TerapeakWeight < tight.TerapeakWeight); // noisy Terapeak counts for less
    }

    [Fact]
    public void ResolveWeights_StaleTerapeakData_LosesShareOfTheBlendToLocal()
    {
        var fresh = MarketPriceEstimator.ResolveWeights(true, localStrongCount: 5, terapeakStrongCount: 10, terapeakFreshnessWeight: 1.0);
        var stale = MarketPriceEstimator.ResolveWeights(true, localStrongCount: 5, terapeakStrongCount: 10, terapeakFreshnessWeight: 0.2);

        Assert.True(stale.TerapeakWeight < fresh.TerapeakWeight);
        Assert.True(stale.LocalWeight   > fresh.LocalWeight);
    }

    [Fact]
    public void ResolveWeights_ExtremeMismatch_NeverFullyErasesEitherRealSource()
    {
        var (local, terapeak) = MarketPriceEstimator.ResolveWeights(true, localStrongCount: 100, terapeakStrongCount: 1, terapeakFreshnessWeight: 1.0);

        // Clamped so a real second source keeps at least a 15% voice.
        Assert.Equal(0.15m, terapeak, 2);
        Assert.Equal(0.85m, local, 2);
    }

    // ── Disagreement detection (unchanged behavior) ──────────────────────────────

    [Fact]
    public void DetectDisagreement_MediansWithinTwentyPercent_NoDisagreementFlagged()
    {
        var (disagree, message) = MarketPriceEstimator.DetectDisagreement(localMedian: 100m, terapeakMedian: 115m);

        Assert.False(disagree);
        Assert.Null(message);
    }

    [Fact]
    public void DetectDisagreement_MediansMoreThanTwentyPercentApart_FlagsDisagreementWithBothValues()
    {
        var (disagree, message) = MarketPriceEstimator.DetectDisagreement(localMedian: 100m, terapeakMedian: 160m);

        Assert.True(disagree);
        Assert.Contains("100.00", message);
        Assert.Contains("160.00", message);
    }

    [Fact]
    public void DetectDisagreement_OneSourceHasNoData_NeverFlagsDisagreement()
    {
        var (disagree, _) = MarketPriceEstimator.DetectDisagreement(localMedian: 0m, terapeakMedian: 160m);

        Assert.False(disagree);
    }
}
