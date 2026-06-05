using ING_eBay_AutoLister.Models;

namespace ING_eBay_AutoLister.Services;

public static class PlaceholderListings
{
    public static List<EbayListingSummary> Get()
    {
        var updated = DateTimeOffset.UtcNow.AddDays(-1).ToString("O");

        return
        [
            CreateMiner(
                "SAMPLE-S19-95TH",
                "ING-SAMPLE-S19-95",
                "Bitmain Antminer S19 95TH Bitcoin Miner SHA-256 ASIC",
                "Antminer S19",
                "95 TH/s",
                "3250 W",
                799,
                4,
                updated),
            CreateMiner(
                "SAMPLE-S19-110TH",
                "ING-SAMPLE-S19-110",
                "Bitmain Antminer S19 110TH Bitcoin Miner SHA-256 ASIC",
                "Antminer S19",
                "110 TH/s",
                "3250 W",
                1099,
                2,
                updated),
            CreateMiner(
                "SAMPLE-S19J-PRO",
                "ING-SAMPLE-S19J-PRO",
                "Bitmain Antminer S19j Pro 104TH Bitcoin Miner",
                "Antminer S19j Pro",
                "104 TH/s",
                "3068 W",
                949,
                3,
                updated),
            CreateMiner(
                "SAMPLE-L7",
                "ING-SAMPLE-L7",
                "Bitmain Antminer L7 9050MH Litecoin Dogecoin ASIC Miner",
                "Antminer L7",
                "9050 MH/s",
                "3425 W",
                4299,
                1,
                updated)
        ];
    }

    private static EbayListingSummary CreateMiner(
        string listingId,
        string sku,
        string title,
        string model,
        string hashrate,
        string powerUsage,
        decimal price,
        int quantity,
        string updated)
    {
        var data = new PostListingRequest
        {
            Title = title,
            Category = "ASIC Miners",
            CategoryId = "111418",
            Condition = "USED_EXCELLENT",
            ConditionDescription = "Sample listing for dashboard testing. Confirm final condition before publishing.",
            Brand = "Bitmain",
            Mpn = model,
            Description = $"{title}. Tested ASIC miner placeholder data for local dashboard review.",
            Price = price,
            Quantity = quantity,
            PackageType = "MAILING_BOX",
            WeightLbs = 35,
            PackageLengthIn = 24,
            PackageWidthIn = 18,
            PackageHeightIn = 18,
            HandlingTimeBusinessDays = 1,
            ItemLocationCountry = "US",
            ItemSpecifics = new Dictionary<string, string>
            {
                ["Brand"] = "Bitmain",
                ["Model"] = model,
                ["Hashrate"] = hashrate,
                ["Power Usage"] = powerUsage,
                ["Algorithm"] = model.Contains("L7", StringComparison.OrdinalIgnoreCase) ? "Scrypt" : "SHA-256"
            }
        };

        return new EbayListingSummary
        {
            OfferId = "",
            ListingId = listingId,
            Sku = sku,
            Status = "SAMPLE",
            Title = title,
            Category = data.Category,
            CategoryId = data.CategoryId,
            LastUpdated = updated,
            Price = price,
            Quantity = quantity,
            Condition = data.Condition,
            ThumbnailUrl = "",
            Data = data
        };
    }
}
