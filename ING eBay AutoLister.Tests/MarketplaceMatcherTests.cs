using ING_eBay_AutoLister.Models;
using ING_eBay_AutoLister.Services;

namespace ING_eBay_AutoLister.Tests;

public class MarketplaceMatcherTests
{
    [Fact]
    public void Normalize_LowercasesAndCollapsesPunctuationAndSpaces()
    {
        var result = MarketplaceMatcher.Normalize("  Bitmain  Antminer-S19,  PRO!!  ");
        Assert.Equal("bitmain antminer s19 pro", result);
    }

    [Fact]
    public void Score_ExactModelPhrase_ReturnsHighestScore()
    {
        var (score, isExact) = MarketplaceMatcher.Score(
            "Bitmain Antminer S19j Pro 104TH/s Bitcoin Miner ASIC Tested",
            "Antminer S19j Pro");

        Assert.True(isExact);
        Assert.Equal(100, score);
    }

    [Fact]
    public void Score_ExactPartNumberToken_ReturnsHighestScore()
    {
        var (score, isExact) = MarketplaceMatcher.Score(
            "New Bitmain APW7 Power Supply PSU Antminer 100-264V 1800W (APW7-12-1800)",
            "APW7-12-1800");

        Assert.True(isExact);
        Assert.Equal(100, score);
    }

    [Fact]
    public void Score_BroadTitleMatch_AllImportantWordsPresent_ScoresHighButNotExact()
    {
        var (score, isExact) = MarketplaceMatcher.Score(
            "Bitmain Antminer S19 95TH/s SHA-256 ASIC Bitcoin Miner - Tested Working",
            "Antminer S19 Bitcoin Miner");

        Assert.False(isExact);
        Assert.True(score >= 80, $"expected a strong broad match, got {score}");
    }

    [Fact]
    public void Score_OnlyGenericWordsShared_IsRejectedAsWeakMatch()
    {
        // "new" / "used" / "miner" / "controller" are exactly the generic words the spec calls
        // out — sharing only these with the title must not count as a match.
        var (score, isExact) = MarketplaceMatcher.Score(
            "Brand New Controller Board For Generic ASIC Miner",
            "used miner controller");

        Assert.False(isExact);
        Assert.Equal(0, score);
    }

    [Fact]
    public void Score_NoOverlapAtAll_ReturnsZero()
    {
        var (score, isExact) = MarketplaceMatcher.Score(
            "Vintage Baseball Card Collection Lot",
            "Antminer S19 Pro");

        Assert.False(isExact);
        Assert.Equal(0, score);
    }

    [Fact]
    public void DeduplicateByItemId_KeepsHighestScoringCopy()
    {
        var items = new List<MarketplaceComparableResult>
        {
            new() { ItemId = "1", Title = "A", MatchScore = 40 },
            new() { ItemId = "1", Title = "A", MatchScore = 95 },
            new() { ItemId = "2", Title = "B", MatchScore = 60 },
        };

        var result = MarketplaceMatcher.DeduplicateByItemId(items);

        Assert.Equal(2, result.Count);
        Assert.Equal(95, result.Single(r => r.ItemId == "1").MatchScore);
    }
}
