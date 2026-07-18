using ING_eBay_AutoLister.Services;

namespace ING_eBay_AutoLister.Tests;

public class MarketPriceEstimatorTests
{
    [Fact]
    public void ResolveWeights_NoLocalMedian_TrustsTerapeakEntirely()
    {
        var (local, terapeak) = MarketPriceEstimator.ResolveWeights(hasLocalMedian: false, localStrongCount: 0, terapeakStrongCount: 5, terapeakFreshnessWeight: 1.0);

        Assert.Equal(0m, local);
        Assert.Equal(1m, terapeak);
    }

    [Fact]
    public void ResolveWeights_TenOrMoreStrongTerapeakComps_FavorsTerapeak()
    {
        var (local, terapeak) = MarketPriceEstimator.ResolveWeights(true, localStrongCount: 2, terapeakStrongCount: 10, terapeakFreshnessWeight: 1.0);

        Assert.Equal(0.75m, terapeak);
        Assert.Equal(0.25m, local);
    }

    [Fact]
    public void ResolveWeights_FewTerapeakButStrongLocal_FavorsLocal()
    {
        var (local, terapeak) = MarketPriceEstimator.ResolveWeights(true, localStrongCount: 5, terapeakStrongCount: 1, terapeakFreshnessWeight: 1.0);

        Assert.Equal(0.70m, local);
        Assert.Equal(0.30m, terapeak);
    }

    [Fact]
    public void ResolveWeights_StaleTerapeakData_LosesShareOfTheBlendToLocal()
    {
        var fresh = MarketPriceEstimator.ResolveWeights(true, localStrongCount: 2, terapeakStrongCount: 10, terapeakFreshnessWeight: 1.0);
        var stale = MarketPriceEstimator.ResolveWeights(true, localStrongCount: 2, terapeakStrongCount: 10, terapeakFreshnessWeight: 0.2);

        Assert.True(stale.TerapeakWeight < fresh.TerapeakWeight);
        Assert.True(stale.LocalWeight > fresh.LocalWeight);
    }

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
