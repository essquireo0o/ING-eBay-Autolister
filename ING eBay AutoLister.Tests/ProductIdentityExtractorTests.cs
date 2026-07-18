using ING_eBay_AutoLister.Services;

namespace ING_eBay_AutoLister.Tests;

public class ProductIdentityExtractorTests
{
    private readonly ProductIdentityExtractor _extractor = new();

    [Fact]
    public void Extract_BitmainAntminerTitle_ParsesBrandFamilyModelHashrate()
    {
        var identity = _extractor.Extract("Bitmain Antminer S21 XP Hydro 473TH");

        Assert.Equal("Bitmain", identity.Brand);
        Assert.Equal("Antminer", identity.ProductFamily);
        Assert.Equal("S21 XP Hydro", identity.Model);
        Assert.Equal("473TH", identity.Hashrate);
    }

    [Fact]
    public void Extract_FanucServoAmplifierTitle_ParsesBrandPartNumberCategory()
    {
        var identity = _extractor.Extract("Fanuc A06B-6079-H206 Servo Amplifier");

        Assert.Equal("Fanuc", identity.Brand);
        Assert.Equal("A06B-6079-H206", identity.PartNumber);
        Assert.Equal("Servo Amplifier", identity.Category);
    }

    [Fact]
    public void Extract_CiscoSwitchSku_ParsesBrandPartNumberAndInfersCategory()
    {
        var identity = _extractor.Extract("Cisco WS-C3850-48P-S");

        Assert.Equal("Cisco", identity.Brand);
        Assert.Equal("WS-C3850-48P-S", identity.PartNumber);
        Assert.Equal("Network Switch", identity.Category); // inferred from the WS-C prefix, not literal text
    }

    [Fact]
    public void Extract_ConditionKeywords_AreRecognizedAndDoNotPolluteModel()
    {
        var identity = _extractor.Extract("Bitmain Antminer S19 Pro Tested Working");

        // "Tested Working" is in the condition vocabulary as one compound phrase, matched
        // before the shorter standalone "Tested"/"Working" entries.
        Assert.Contains("Tested Working", identity.ConditionKeywords);
        Assert.DoesNotContain("Tested", identity.Model ?? "");
        Assert.DoesNotContain("Working", identity.Model ?? "");
    }

    [Fact]
    public void Extract_EmptyText_ReturnsEmptyIdentityWithoutThrowing()
    {
        var identity = _extractor.Extract("");

        Assert.Null(identity.Brand);
        Assert.Null(identity.PartNumber);
        Assert.Empty(identity.ConditionKeywords);
    }

    [Fact]
    public void Extract_VoltageIsNotMisreadAsAPartNumber()
    {
        var identity = _extractor.Extract("New Bitmain APW7 Power Supply 100-264V 1800W");

        Assert.NotNull(identity.Voltage);
        Assert.NotNull(identity.Wattage);
    }
}
