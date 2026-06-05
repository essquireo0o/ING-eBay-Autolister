using ING_eBay_AutoLister.Models;

namespace ING_eBay_AutoLister.Services;

public static class AsicMinerPresets
{
    private sealed record MinerPreset(
        string Match,
        string Model,
        string Hashrate,
        string PowerUsage,
        string Algorithm,
        string CompatibleCurrency);

    private static readonly MinerPreset[] Presets =
    [
        new("s19j pro", "Antminer S19j Pro", "104 TH/s", "3068 W", "SHA-256", "Bitcoin"),
        new("s19 110", "Antminer S19", "110 TH/s", "3250 W", "SHA-256", "Bitcoin"),
        new("s19 95", "Antminer S19", "95 TH/s", "3250 W", "SHA-256", "Bitcoin"),
        new("l7", "Antminer L7", "9050 MH/s", "3425 W", "Scrypt", "Litecoin, Dogecoin")
    ];

    public static ListingData Apply(ListingData listing)
    {
        var preset = MatchPreset(listing);
        if (preset is null) return listing;

        listing.Brand = Fill(listing.Brand, "Bitmain");
        listing.Mpn = Fill(listing.Mpn, preset.Model);
        listing.Category = Fill(listing.Category, "ASIC Miners");
        listing.Condition = Fill(listing.Condition, "USED_EXCELLENT");
        listing.ConditionDescription = Fill(
            listing.ConditionDescription,
            "Used ASIC miner. Verify hashrate, firmware, cosmetic condition, and included power cabling before publishing.");
        listing.PackageType = Fill(listing.PackageType, "MAILING_BOX");
        listing.WeightLbs = listing.WeightLbs <= 0 ? 35 : listing.WeightLbs;
        listing.PackageLengthIn = listing.PackageLengthIn <= 0 ? 24 : listing.PackageLengthIn;
        listing.PackageWidthIn = listing.PackageWidthIn <= 0 ? 18 : listing.PackageWidthIn;
        listing.PackageHeightIn = listing.PackageHeightIn <= 0 ? 18 : listing.PackageHeightIn;
        listing.HandlingTimeBusinessDays = listing.HandlingTimeBusinessDays <= 0 ? 1 : listing.HandlingTimeBusinessDays;
        listing.ItemLocationCountry = Fill(listing.ItemLocationCountry, "US");
        listing.ItemSpecifics ??= [];

        AddSpecific(listing, "Brand", "Bitmain");
        AddSpecific(listing, "Manufacturer", "Bitmain");
        AddSpecific(listing, "Model", preset.Model);
        AddSpecific(listing, "Hashrate", preset.Hashrate);
        AddSpecific(listing, "Power Usage", preset.PowerUsage);
        AddSpecific(listing, "Algorithm", preset.Algorithm);
        AddSpecific(listing, "Compatible Currency", preset.CompatibleCurrency);
        AddSpecific(listing, "Cooling", "Air cooled");
        AddSpecific(listing, "Firmware Compatibility", "Stock and compatible aftermarket ASIC firmware");

        if (string.IsNullOrWhiteSpace(listing.Description))
        {
            listing.Description = $"""
                <b>{preset.Model} ASIC Miner</b>
                <p>AI-generated draft for an ING Mining listing. Review all details before publishing.</p>
                <ul>
                  <li>Hashrate: {preset.Hashrate}</li>
                  <li>Algorithm: {preset.Algorithm}</li>
                  <li>Typical power usage: {preset.PowerUsage}</li>
                  <li>Condition and included accessories must be verified before listing.</li>
                </ul>
                """;
        }

        return listing;
    }

    private static MinerPreset? MatchPreset(ListingData listing)
    {
        var text = $"{listing.Title} {listing.Subtitle} {listing.Brand} {listing.Mpn} {listing.Description}".ToLowerInvariant();
        return Presets.FirstOrDefault(p => text.Contains(p.Match, StringComparison.OrdinalIgnoreCase));
    }

    private static string Fill(string current, string fallback) =>
        string.IsNullOrWhiteSpace(current) ? fallback : current;

    private static void AddSpecific(ListingData listing, string name, string value)
    {
        if (!listing.ItemSpecifics.ContainsKey(name) || string.IsNullOrWhiteSpace(listing.ItemSpecifics[name]))
            listing.ItemSpecifics[name] = value;
    }
}
