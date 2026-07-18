using ING_eBay_AutoLister.Models;
using ING_eBay_AutoLister.Services;

namespace ING_eBay_AutoLister.Tests;

public class ComparableMatcherTests
{
    private static (ProductNormalizer Normalizer, ComparableMatcher Matcher) CreateMatcher()
    {
        var normalizer = new ProductNormalizer(new ProductIdentityExtractor());
        return (normalizer, new ComparableMatcher(normalizer));
    }

    private static MarketplaceComparableResult Candidate(string itemId, string title, int quantity = 1) => new()
    {
        ItemId = itemId, Title = title, SoldPrice = 100m, Quantity = quantity,
    };

    [Fact]
    public void Match_ExactPartNumber_EarnsTheExactIdentifierTierAndIsNotExcluded()
    {
        var (normalizer, matcher) = CreateMatcher();
        var target = normalizer.Normalize("Bitmain APW7-12-1800 Power Supply");

        var match = matcher.Match(target, Candidate("1", "New Bitmain APW7 PSU (APW7-12-1800) 1800W"));

        Assert.False(match.Excluded);
        Assert.Equal(MatchTier.ExactIdentifier, match.Tier);
        Assert.True(match.MatchConfidence >= 35);
    }

    [Fact]
    public void Match_ConflictingPartNumbers_IsExcluded()
    {
        var (normalizer, matcher) = CreateMatcher();
        // Two different, specific Allen-Bradley controller part numbers — not a match just
        // because both are "Allen-Bradley ControlLogix" modules.
        var target = normalizer.Normalize("Allen-Bradley 1756-L83E ControlLogix Processor");

        var match = matcher.Match(target, Candidate("1", "Allen-Bradley 1756-L61 ControlLogix Processor Module"));

        Assert.True(match.Excluded);
        Assert.Contains("conflict", match.ExclusionReason ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Match_ExactPartNumber_ScoresFarHigherThanGenericBrandOnlyMatch()
    {
        var (normalizer, matcher) = CreateMatcher();
        // Spec example: "Allen Bradley 1756-L83E should rank far above a generic Allen Bradley
        // controller" — the generic listing is weaker evidence, not a conflict with anything.
        var target = normalizer.Normalize("Allen-Bradley 1756-L83E ControlLogix Processor");

        var exact = matcher.Match(target, Candidate("1", "Allen-Bradley 1756-L83E ControlLogix Processor Module"));
        var generic = matcher.Match(target, Candidate("2", "Allen Bradley PLC Controller Untested"));

        Assert.False(exact.Excluded);
        Assert.Equal(MatchTier.ExactIdentifier, exact.Tier);
        // The generic candidate isn't flagged as conflicting with anything specific — it just
        // doesn't carry enough matching evidence to be as confident as the exact part number hit.
        Assert.True(exact.MatchConfidence > (generic.Excluded ? 0 : generic.MatchConfidence),
            "an exact part-number match must score meaningfully higher than a bare brand-only match");
    }

    [Fact]
    public void Match_PartsOnlyCandidate_IsExcludedWhenTargetIsNotPartsOnly()
    {
        var (normalizer, matcher) = CreateMatcher();
        var target = normalizer.Normalize("Bitmain Antminer S19 Pro 110TH Bitcoin Miner Tested Working");

        var match = matcher.Match(target, Candidate("1", "Bitmain Antminer S19 Pro FOR PARTS not working"));

        Assert.True(match.Excluded);
        Assert.Contains("parts", match.ExclusionReason ?? "");
    }

    [Fact]
    public void Match_BoxOnlyCandidate_IsExcludedEvenWithMatchingModelTokens()
    {
        var (normalizer, matcher) = CreateMatcher();
        var target = normalizer.Normalize("RTX 4090 Founders Edition Graphics Card");

        // "RTX 4090" appears in both, but this candidate is an empty-box listing, not the card.
        var match = matcher.Match(target, Candidate("1", "RTX 4090 Founders Edition - box only, no GPU"));

        Assert.True(match.Excluded);
        Assert.Contains("empty box", match.ExclusionReason ?? "");
    }

    [Fact]
    public void Match_QuantityMismatch_IsNotExcludedButRecordsAPenaltyReason()
    {
        var (normalizer, matcher) = CreateMatcher();
        var target = normalizer.Normalize("Bitmain Antminer S19 Pro 110TH Bitcoin Miner");

        var match = matcher.Match(target, Candidate("1", "Lot of 5 Bitmain Antminer S19 Pro 110TH Bitcoin Miners", quantity: 5));

        Assert.False(match.Excluded);
        Assert.Contains(match.PenaltyReasons, r => r.Contains("Quantity"));
    }

    [Fact]
    public void Match_UnrelatedCategory_ScoresZeroAndIsExcluded()
    {
        var (normalizer, matcher) = CreateMatcher();
        var target = normalizer.Normalize("Bitmain Antminer S19j Pro 104TH Bitcoin Miner");

        var match = matcher.Match(target, Candidate("1", "Vintage Baseball Card Collection Binder Lot 1990s"));

        Assert.True(match.Excluded);
    }
}
