using System.Text.RegularExpressions;
using ING_eBay_AutoLister.Models;

namespace ING_eBay_AutoLister.Services;

// Turns a raw product title / spreadsheet row / OCR'd text / manually typed text into a
// structured ProductIdentity — brand, model, part number, and the handful of spec fields
// (hashrate, wattage, voltage, capacity) that actually show up across the mixed
// ASIC-miner / industrial-automation / networking-gear inventory this app deals with. Regex +
// lookup tables, not NLP: every field comes back null rather than guessed when it isn't
// confidently recognized. Opportunity Finder uses this to search the local sold-history
// database by the strongest identifier actually present (see MarketplaceRepository).
//
// Extraction works by repeatedly matching the most specific/structural patterns first (specs,
// then part number, then brand) against a mutable "masked" copy of the text, blanking out each
// match as it's claimed so a later, looser pattern can never re-match text an earlier one
// already consumed. Whatever's left over after every known field has been pulled out becomes
// the Model.
public class ProductIdentityExtractor
{
    // ── Known brands ─────────────────────────────────────────────────────────────────────────
    // Each canonical brand lists every alias that should be recognized AND blanked from the
    // leftover text — e.g. a title naming both "Bitmain" and "Antminer" shouldn't leave either
    // word sitting in what becomes the Model.
    private static readonly (string Canonical, string[] Aliases)[] BrandAliases =
    [
        ("Bitmain", ["Bitmain", "Antminer"]),
        ("MicroBT", ["MicroBT", "Whatsminer"]),
        ("Canaan", ["Canaan", "Avalon"]),
        ("Innosilicon", ["Innosilicon"]),
        ("iPollo", ["iPollo"]),
        ("Goldshell", ["Goldshell"]),
        ("StrongU", ["StrongU"]),
        ("Ebang", ["Ebang"]),
        ("Fanuc", ["Fanuc"]),
        ("Siemens", ["Siemens"]),
        ("Allen-Bradley", ["Allen-Bradley", "Rockwell Automation", "Rockwell"]),
        ("ABB", ["ABB"]),
        ("Mitsubishi Electric", ["Mitsubishi Electric", "Mitsubishi"]),
        ("Yaskawa", ["Yaskawa"]),
        ("Schneider Electric", ["Schneider Electric", "Schneider"]),
        ("Omron", ["Omron"]),
        ("Bosch Rexroth", ["Bosch Rexroth"]),
        ("Kuka", ["Kuka"]),
        ("Cisco", ["Cisco"]),
        ("Juniper", ["Juniper"]),
        ("HPE", ["HPE", "Hewlett Packard Enterprise"]),
        ("HP", ["HP", "Hewlett-Packard"]),
        ("Dell", ["Dell"]),
        ("Netgear", ["Netgear"]),
        ("Ubiquiti", ["Ubiquiti"]),
        ("Aruba", ["Aruba"]),
        ("Fortinet", ["Fortinet"]),
        ("D-Link", ["D-Link"]),
        ("TP-Link", ["TP-Link"]),
        ("Maytronics", ["Maytronics"]),
        ("Astron", ["Astron"]),
        ("Mean Well", ["Mean Well"]),
        ("Delta", ["Delta"]),
    ];

    // Brand -> the product-family name that brand consistently markets under. Only worth
    // listing where a brand reliably uses one recognizable family name.
    private static readonly Dictionary<string, string> BrandFamilies = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Bitmain"] = "Antminer",
        ["MicroBT"] = "Whatsminer",
        ["Canaan"] = "Avalon",
    };

    // Literal category phrases worth recognizing verbatim when they appear in the text.
    private static readonly string[] CategoryPhrases =
    [
        "Servo Amplifier", "Servo Drive", "Servo Motor", "Power Supply", "Power Adapter",
        "Network Switch", "Router", "Controller Board", "Control Board", "Controller",
        "Circuit Board", "ASIC Miner", "Bitcoin Miner", "Crypto Miner", "Robot Cleaner",
        "Pool Cleaner", "Hard Drive", "Power Cable",
    ];

    // Best-effort brand + part-number-prefix -> category inference, for text that doesn't
    // literally state what the part is (a bare Cisco SKU is recognizably a Catalyst switch
    // model to anyone who works with this gear, even though the word "switch" never appears).
    // Not exhaustive — just enough well-known prefixes to be useful.
    private static readonly (string Brand, string Prefix, string Category)[] PartNumberCategoryHints =
    [
        ("Cisco", "WS-C", "Network Switch"),
        ("Cisco", "WS-X", "Switch Module"),
        ("Cisco", "C92", "Network Switch"),
        ("Cisco", "C93", "Network Switch"),
        ("Cisco", "C95", "Network Switch"),
        ("Cisco", "C29", "Network Switch"),
        ("Cisco", "C38", "Network Switch"),
        ("Cisco", "ASR", "Router"),
        ("Cisco", "ISR", "Router"),
        ("Cisco", "N9K", "Network Switch"),
        ("Cisco", "N5K", "Network Switch"),
        ("Fanuc", "A06B", "Servo Amplifier"),
        ("Fanuc", "A20B", "Circuit Board"),
        ("Fanuc", "A16B", "Circuit Board"),
    ];

    private static readonly string[] ConditionVocabulary =
    [
        "Brand New", "New Open Box", "Open Box", "Like New", "Pre-Owned", "Refurbished",
        "For Parts", "Not Working", "Untested", "Tested Working", "Tested", "Working",
        "Used", "New", "Broken", "Damaged", "Sealed", "Excellent", "Good", "Fair", "Poor",
    ];

    private static readonly Regex HashrateRegex = new(
        @"\b\d+(\.\d+)?\s*(TH|GH|MH|PH)\s*(/?\s*[Ss])?\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex WattageRegex = new(
        @"\b\d+(\.\d+)?\s*[Ww](?:att)?s?\b", RegexOptions.Compiled);

    private static readonly Regex VoltageRegex = new(
        @"\b\d+(\.\d+)?\s*(?:-\s*\d+(\.\d+)?)?\s*V(?:olts?)?\b", RegexOptions.Compiled);

    private static readonly Regex CapacityRegex = new(
        @"\b\d+(\.\d+)?\s*(TB|GB|MB|KB)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex RevisionRegex = new(
        @"\bRev(?:ision)?\.?\s*([A-Za-z0-9]+(?:\.[A-Za-z0-9]+)?)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // "Version 2" / "Ver. 2" / "V2" — but not a voltage like "110V" (negative lookbehind blocks
    // a digit or decimal point immediately before the V, which every voltage match would have).
    private static readonly Regex VersionRegex = new(
        @"\b(?:Version|Ver\.?)\s*(\d+(?:\.\d+)?)\b|(?<![\d.])[Vv](\d+(?:\.\d+)?)\b", RegexOptions.Compiled);

    private static readonly Regex GenerationRegex = new(
        @"\bGen(?:eration)?\.?\s*(\d+)\b|\b(\d+)(?:st|nd|rd|th)\s+Gen(?:eration)?\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex SeriesRegex = new(
        @"\bSeries\s+([A-Za-z0-9]+)\b|\b([A-Za-z0-9]+)\s+Series\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Part numbers: a hyphen-joined run of alphanumeric segments containing at least one letter
    // and one digit somewhere across the whole token — distinguishes "A06B-6079-H206" /
    // "WS-C3850-48P-S" from a plain model token like "S21" (no hyphen) or a pure numeric range
    // (no letters — and by the time this runs, voltage/wattage ranges are already blanked out).
    private static readonly Regex PartNumberRegex = new(
        @"\b(?=[A-Z0-9-]*[A-Z])(?=[A-Z0-9-]*\d)[A-Z0-9]+(?:-[A-Z0-9]+)+\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public ProductIdentity Extract(string? rawText)
    {
        var text = Normalize(rawText);
        var identity = new ProductIdentity { RawText = text };
        if (text.Length == 0) return identity;

        // Working copy whose matched spans get blanked out as each field is claimed, so a later
        // (looser) pattern never re-matches text an earlier, more specific one already consumed.
        var mask = text.ToCharArray();

        identity.ConditionKeywords = ExtractConditionKeywords(mask);
        identity.Hashrate    = ExtractAndMask(HashrateRegex, mask, NormalizeHashrate);
        identity.Wattage     = ExtractAndMask(WattageRegex, mask, NormalizeUnitValue);
        identity.Voltage     = ExtractAndMask(VoltageRegex, mask, NormalizeUnitValue);
        identity.Capacity    = ExtractAndMask(CapacityRegex, mask, NormalizeUnitValue);
        identity.Revision    = ExtractGroupAndMask(RevisionRegex, mask);
        identity.Version     = ExtractGroupAndMask(VersionRegex, mask);
        identity.Generation  = ExtractGroupAndMask(GenerationRegex, mask);
        identity.Series      = ExtractGroupAndMask(SeriesRegex, mask);
        identity.PartNumber  = ExtractAndMask(PartNumberRegex, mask, s => s.ToUpperInvariant());

        identity.Brand = ExtractBrandAndMask(text, mask);
        identity.Manufacturer = identity.Brand; // no distinct "manufactured by" signal in free-text titles

        if (identity.Brand is not null && BrandFamilies.TryGetValue(identity.Brand, out var family))
            identity.ProductFamily = family;

        identity.Category = ExtractCategoryAndMask(mask, identity.Brand, identity.PartNumber);
        identity.Model = BuildModel(mask);

        return identity;
    }

    // ── Field extraction helpers ─────────────────────────────────────────────────────────────

    private static string? ExtractAndMask(Regex regex, char[] mask, Func<string, string> normalize)
    {
        var match = regex.Match(new string(mask));
        if (!match.Success) return null;
        Blank(mask, match.Index, match.Length);
        return normalize(match.Value.Trim());
    }

    private static string? ExtractGroupAndMask(Regex regex, char[] mask)
    {
        var match = regex.Match(new string(mask));
        if (!match.Success) return null;
        Blank(mask, match.Index, match.Length);
        for (var i = 1; i < match.Groups.Count; i++)
            if (match.Groups[i].Success) return match.Groups[i].Value.Trim();
        return match.Value.Trim();
    }

    private static List<string> ExtractConditionKeywords(char[] mask)
    {
        var found = new List<string>();
        foreach (var phrase in ConditionVocabulary.OrderByDescending(p => p.Length))
        {
            var match = Regex.Match(new string(mask), $@"\b{Regex.Escape(phrase)}\b", RegexOptions.IgnoreCase);
            if (!match.Success) continue;
            found.Add(phrase);
            Blank(mask, match.Index, match.Length);
        }
        return found;
    }

    // Brand is matched against the ORIGINAL text (word matching has no risk of colliding with
    // already-consumed numeric spans), then every alias for whichever brand matched earliest is
    // blanked out of the mask — not just the one alias that fired.
    private static string? ExtractBrandAndMask(string text, char[] mask)
    {
        (int Index, string Canonical, string[] Aliases)? best = null;
        foreach (var (canonical, aliases) in BrandAliases)
        {
            foreach (var alias in aliases)
            {
                var match = Regex.Match(text, AliasPattern(alias), RegexOptions.IgnoreCase);
                if (match.Success && (best is null || match.Index < best.Value.Index))
                    best = (match.Index, canonical, aliases);
            }
        }
        if (best is null) return null;

        foreach (var alias in best.Value.Aliases)
            foreach (Match m in Regex.Matches(text, AliasPattern(alias), RegexOptions.IgnoreCase))
                Blank(mask, m.Index, m.Length);

        return best.Value.Canonical;
    }

    // A hyphenated alias like "Allen-Bradley" should also recognize "Allen Bradley" (space) and
    // "AllenBradley" (no separator) — sellers write brand names with inconsistent punctuation.
    private static string AliasPattern(string alias) =>
        $@"\b{Regex.Escape(alias).Replace("\\-", "[-\\s]?")}\b";

    private static string? ExtractCategoryAndMask(char[] mask, string? brand, string? partNumber)
    {
        foreach (var phrase in CategoryPhrases.OrderByDescending(p => p.Length))
        {
            var match = Regex.Match(new string(mask), $@"\b{Regex.Escape(phrase)}\b", RegexOptions.IgnoreCase);
            if (!match.Success) continue;
            Blank(mask, match.Index, match.Length);
            return phrase;
        }

        if (brand is not null && partNumber is not null)
        {
            foreach (var hint in PartNumberCategoryHints)
            {
                if (string.Equals(hint.Brand, brand, StringComparison.OrdinalIgnoreCase) &&
                    partNumber.StartsWith(hint.Prefix, StringComparison.OrdinalIgnoreCase))
                    return hint.Category;
            }
        }

        return null;
    }

    // Whatever's left in the mask once every other field has claimed its span IS the model —
    // e.g. "Bitmain Antminer S21 XP Hydro 473TH" has Brand/Family/Hashrate all blanked out,
    // leaving "S21 XP Hydro".
    private static string? BuildModel(char[] mask)
    {
        var leftover = Regex.Replace(new string(mask), @"[^A-Za-z0-9]+", " ").Trim();
        leftover = Regex.Replace(leftover, @"\s+", " ");
        return leftover.Length > 0 ? leftover : null;
    }

    private static void Blank(char[] mask, int index, int length)
    {
        for (var i = index; i < index + length && i < mask.Length; i++) mask[i] = ' ';
    }

    private static string Normalize(string? text) =>
        string.IsNullOrWhiteSpace(text) ? "" : Regex.Replace(text, @"\s+", " ").Trim();

    // Preserves the number and unit as written (e.g. "473TH" stays "473TH", not forced into
    // "473TH/s") rather than fabricating a suffix that wasn't actually present.
    private static string NormalizeHashrate(string raw)
    {
        var m = Regex.Match(raw, @"(\d+(?:\.\d+)?)\s*(TH|GH|MH|PH)\s*(/?\s*[Ss])?", RegexOptions.IgnoreCase);
        if (!m.Success) return raw.Trim();
        var suffix = m.Groups[3].Success ? "/s" : "";
        return $"{m.Groups[1].Value}{m.Groups[2].Value.ToUpperInvariant()}{suffix}";
    }

    private static string NormalizeUnitValue(string raw) =>
        Regex.Replace(raw.Trim(), @"\s+", "").ToUpperInvariant();
}
