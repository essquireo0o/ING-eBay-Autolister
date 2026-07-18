using ING_eBay_AutoLister.Models;
using ING_eBay_AutoLister.Services;

namespace ING_eBay_AutoLister.Tests;

public class ConfidenceScoringServiceTests
{
    private static MarketAnalysisResult BaseResult() => new()
    {
        PriceEstimate = new PriceEstimate { LocalWeight = 0.6m, TerapeakWeight = 0.4m, MarketDataDisagreement = false },
        SellThrough = new SellThroughAnalysis { SoldComparableCount = 8 },
        Stability = new PriceStability { StabilityScore = 90 },
    };

    [Fact]
    public void Score_ManyStrongExactMatchesRecentAndAgreeing_IsHighConfidence()
    {
        var service = new ConfidenceScoringService();
        var result = BaseResult();

        var confidence = service.Score(result, strongComparableCount: 10, exactIdentifierMatches: 3,
            modelNumberMatches: 3, mostRecentComparableAgeDays: 5,
            conditionConsistent: true, quantityConsistent: true, categoryConsistent: true);

        Assert.True(confidence.Score >= 85, $"expected High Confidence range, got {confidence.Score}");
        Assert.Equal("High Confidence", confidence.Level);
    }

    [Fact]
    public void Score_NoComparablesAtAll_IsInsufficientEvidence()
    {
        var service = new ConfidenceScoringService();
        var result = BaseResult();
        result.SellThrough.SoldComparableCount = 0;
        result.PriceEstimate.TerapeakWeight = 0;
        result.Stability.StabilityScore = 0;

        var confidence = service.Score(result, strongComparableCount: 0, exactIdentifierMatches: 0,
            modelNumberMatches: 0, mostRecentComparableAgeDays: null,
            conditionConsistent: false, quantityConsistent: false, categoryConsistent: false);

        Assert.True(confidence.Score < 40, $"expected Insufficient Evidence range, got {confidence.Score}");
        Assert.Equal("Insufficient Evidence", confidence.Level);
    }

    [Fact]
    public void Score_MarketDataDisagreement_ScoresLowerThanAgreement()
    {
        var service = new ConfidenceScoringService();
        var agreeing = BaseResult();
        var disagreeing = BaseResult();
        disagreeing.PriceEstimate.MarketDataDisagreement = true;

        var agreeingScore = service.Score(agreeing, 6, 1, 1, 10, true, true, true);
        var disagreeingScore = service.Score(disagreeing, 6, 1, 1, 10, true, true, true);

        Assert.True(agreeingScore.Score > disagreeingScore.Score);
        Assert.Contains(disagreeingScore.Reasons, r => r.Contains("disagree", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Score_StaleData_ScoresLowerThanRecentData()
    {
        var service = new ConfidenceScoringService();
        var result = BaseResult();

        var recent = service.Score(result, 6, 1, 1, mostRecentComparableAgeDays: 10, true, true, true);
        var stale = service.Score(result, 6, 1, 1, mostRecentComparableAgeDays: 200, true, true, true);

        Assert.True(recent.Score > stale.Score);
    }
}
