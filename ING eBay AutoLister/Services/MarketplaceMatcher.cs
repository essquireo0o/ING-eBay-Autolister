using ING_eBay_AutoLister.Models;

namespace ING_eBay_AutoLister.Services;

// Text normalization + relevance scoring for matching a search identifier (part number, model,
// brand+keywords, or a bare keyword phrase) against real eBay sold-listing titles. Pure/static —
// no I/O — so it can be unit tested without a database.
public static class MarketplaceMatcher
{
    // Generic words that show up in almost every listing title regardless of what the item
    // actually is. Sharing only these with a query is not a real match — see Score().
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "new", "used", "for", "with", "and", "the", "parts", "miner", "controller", "of", "a",
        "an", "to", "in", "on", "is", "not", "working", "tested", "sale", "lot", "set", "genuine",
        "oem", "original", "free", "shipping", "fast", "authentic", "brand", "pro", "only",
    };

    // Lowercases, replaces punctuation with spaces (so "S19-Pro" and "S19 Pro" normalize the
    // same way instead of colliding into "s19pro"), and collapses repeated whitespace.
    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";

        var chars = new char[text.Length];
        for (var i = 0; i < text.Length; i++)
        {
            var c = char.ToLowerInvariant(text[i]);
            chars[i] = char.IsLetterOrDigit(c) || c == ' ' ? c : ' ';
        }

        return string.Join(' ', new string(chars).Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    public static List<string> Words(string normalizedText) =>
        string.IsNullOrEmpty(normalizedText) ? [] : [.. normalizedText.Split(' ', StringSplitOptions.RemoveEmptyEntries)];

    // The words worth matching on — everything except generic filler and single characters.
    public static List<string> ImportantWords(string normalizedText) =>
        Words(normalizedText).Where(w => w.Length > 1 && !StopWords.Contains(w)).Distinct().ToList();

    // 0-100 relevance score of a candidate title against a query, plus whether it was an exact
    // phrase/model/part-number-style match (used to weight it more heavily in pricing).
    //   100          — the whole normalized query appears verbatim in the title (exact model /
    //                  part-number match — the highest priority called for by the spec)
    //   88           — every important query word appears somewhere in the title
    //   ~57-74       — most (>=50%, at least 2) important query words appear
    //   0            — nothing meaningful matched, OR the only overlap was generic words like
    //                  "new"/"used"/"parts"/"miner"/"controller" — rejected, not a weak match
    public static (int Score, bool IsExactMatch) Score(string candidateTitle, string queryText)
    {
        var normalizedTitle = Normalize(candidateTitle);
        var normalizedQuery = Normalize(queryText);
        var importantQueryWords = ImportantWords(normalizedQuery);

        if (importantQueryWords.Count == 0 || normalizedTitle.Length == 0)
            return (0, false);

        if (normalizedQuery.Length > 0 && normalizedTitle.Contains(normalizedQuery, StringComparison.Ordinal))
            return (100, true);

        var titleWords = new HashSet<string>(Words(normalizedTitle), StringComparer.Ordinal);
        var matched = importantQueryWords.Count(w => titleWords.Contains(w));
        if (matched == 0) return (0, false);

        var coverage = (double)matched / importantQueryWords.Count;
        if (coverage >= 0.999) return (88, false);
        if (matched >= 2 && coverage >= 0.5) return (40 + (int)Math.Round(coverage * 34), false);

        // A single important word matched out of several isn't reliable enough to call a match —
        // exactly the "avoid weak matches" case, just expressed as low coverage instead of an
        // all-stopword overlap.
        return (0, false);
    }

    // Collapses repeat rows for the same listing (ItemId is UNIQUE in SoldListings, but a search
    // can still surface the same item through more than one query tier) down to the single
    // highest-scoring copy, so pricing stats never double-count one sale.
    public static List<MarketplaceComparableResult> DeduplicateByItemId(IEnumerable<MarketplaceComparableResult> items) =>
        items
            .GroupBy(i => i.ItemId, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(i => i.MatchScore).First())
            .ToList();
}
