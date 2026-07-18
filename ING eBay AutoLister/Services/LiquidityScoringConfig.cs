namespace ING_eBay_AutoLister.Services;

// Every threshold and weight LiquidityScoringService uses, centralized in one place so the
// model can be retuned without hunting through the scoring logic — same pattern as the config
// belonging to any of this app's other scoring/calculator classes.
public class LiquidityScoringConfig
{
    // ── Time windows (days) ──────────────────────────────────────────────────────────────────
    public int RecentWindowDays = 30;   // "recent" bucket for trend comparison
    public int MidWindowDays = 60;      // 30-60 days ago bucket
    public int LongWindowDays = 90;     // 60-90 days ago bucket
    public int LookbackDaysForCap = 365; // 12 months — the window the <3-comparable cap rule checks

    // ── Minimum data requirements ────────────────────────────────────────────────────────────
    public int MinComparablesForReliableTrend = 3;
    public int CappedLiquidityScoreWhenInsufficientData = 40;

    // ── LiquidityScore component weights (should sum to ~100 at full strength) ──────────────
    public double VelocityWeight = 55;
    public double CompetitionWeight = 20;
    public double TrendWeight = 25; // applied as a +/- swing, not a 0..weight band like the others

    // Velocity scoring curve: sales per 30-day period, mapped onto 0..VelocityWeight.
    public double VelocityFullScoreThreshold = 8.0;  // >= this many sales/30d => full velocity score
    public double VelocityZeroScoreThreshold = 0.1;  // <= this many sales/30d => ~0 velocity score

    // Trend detection: recent-30d rate vs. the average of the two older 30-day buckets.
    public double RisingTrendRatio = 1.25;   // recent >= 1.25x older => Increasing
    public double FallingTrendRatio = 0.75;  // recent <= 0.75x older => Decreasing

    // Competition scoring curve: fewer active competing listings scores higher (easier to be the
    // one that sells). Only applied when ActiveCompetitionCount is actually known.
    public int CompetitionLowThreshold = 3;    // <= this many active listings => full competition score
    public int CompetitionHighThreshold = 30;  // >= this many active listings => zero competition score

    // Estimated-days-to-sell sanity bounds, so a near-zero velocity doesn't project an absurd number.
    public int MinEstimatedDaysToSell = 1;
    public int MaxEstimatedDaysToSell = 999;

    // ── LiquidityScore -> LiquidityLevel bands (0-100) ──────────────────────────────────────
    public int FastMoverThreshold = 70;  // score >= this => "Fast Mover"
    public int ModerateThreshold = 40;   // score >= this => "Moderate"
    public int SlowMoverThreshold = 15;  // score >= this => "Slow Mover"; below => "Stale/Illiquid"

    // ── Minimum liquidity gate (Opportunity Finder search) ──────────────────────────────────
    // Whether Stale/Illiquid results are excluded from search results by default. Overridable
    // per-request (see /api/opportunities/search's includeIlliquid parameter).
    public bool RejectStaleIlliquidByDefault = true;
}
