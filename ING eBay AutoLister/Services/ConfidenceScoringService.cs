using ING_eBay_AutoLister.Models;

namespace ING_eBay_AutoLister.Services;

// A genuinely separate 0-100 confidence score — how much to trust the price/profit/opportunity
// numbers, as distinct from how good the opportunity itself looks. Never folded into
// OpportunityScoringService's output; when local and Terapeak disagree, this is what surfaces
// "both estimates shown, confidence lowered" instead of the caller silently picking one.
public sealed class ConfidenceScoringService
{
    public ConfidenceBreakdown Score(
        MarketAnalysisResult result, int strongComparableCount, int exactIdentifierMatches,
        int modelNumberMatches, int? mostRecentComparableAgeDays,
        bool conditionConsistent, bool quantityConsistent, bool categoryConsistent)
    {
        var reasons = new List<string>();
        double points = 0;

        var comparablePoints = Math.Min(strongComparableCount, 10) / 10.0 * 25.0;
        points += comparablePoints;
        reasons.Add(strongComparableCount >= 5
            ? $"{strongComparableCount} strong comparables"
            : $"Only {strongComparableCount} strong comparable(s)");

        var identifierPoints = Math.Min(exactIdentifierMatches, 3) / 3.0 * 20.0;
        points += identifierPoints;
        if (exactIdentifierMatches > 0) reasons.Add($"{exactIdentifierMatches} exact-identifier match(es)");

        var modelPoints = Math.Min(modelNumberMatches, 3) / 3.0 * 15.0;
        points += modelPoints;
        if (modelNumberMatches > 0) reasons.Add($"{modelNumberMatches} exact model-number match(es)");

        double agreementPoints;
        var hasLocal = result.SellThrough.SoldComparableCount > 0;
        var hasTerapeak = result.PriceEstimate.TerapeakWeight > 0;
        if (result.PriceEstimate.MarketDataDisagreement)
        {
            agreementPoints = 0;
            reasons.Add("Local sold-history and Terapeak medians disagree by more than 20%");
        }
        else if (hasLocal && hasTerapeak)
        {
            agreementPoints = 15;
            reasons.Add("Local sold-history and Terapeak data agree");
        }
        else
        {
            agreementPoints = 7;
        }
        points += agreementPoints;

        var recencyPoints = mostRecentComparableAgeDays switch
        {
            null => 0.0,
            <= 30 => 10.0,
            <= 90 => 6.0,
            <= 180 => 3.0,
            _ => 1.0,
        };
        points += recencyPoints;
        if (mostRecentComparableAgeDays is > 90) reasons.Add("Most recent comparable is over 90 days old");

        var variancePoints = result.Stability.StabilityScore / 100.0 * 10.0;
        points += variancePoints;
        if (result.Stability.StabilityScore < 40) reasons.Add("Wide price spread among comparables");

        var consistencyCount = (conditionConsistent ? 1 : 0) + (quantityConsistent ? 1 : 0) + (categoryConsistent ? 1 : 0);
        points += consistencyCount / 3.0 * 5.0;
        if (consistencyCount < 3) reasons.Add("Condition, quantity, or category inconsistency among comparables");

        var score = (int)Math.Round(Math.Clamp(points, 0, 100));
        return new ConfidenceBreakdown
        {
            Score = score,
            Level = score switch
            {
                >= 85 => "High Confidence",
                >= 65 => "Good Confidence",
                >= 40 => "Limited Confidence",
                _ => "Insufficient Evidence",
            },
            Reasons = reasons,
        };
    }
}
