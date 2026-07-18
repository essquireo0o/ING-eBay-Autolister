using ING_eBay_AutoLister.Services;

namespace ING_eBay_AutoLister.Tests;

public class ProfitCalculatorTests
{
    private static FeeProfile DefaultFees() => new(); // 13.25% + $0.40, everything else 0

    [Fact]
    public void Calculate_TypicalFlip_ComputesFeesNetProfitAndRoi()
    {
        var calc = new ProfitCalculator();
        var fees = DefaultFees();

        // Sell for $100 + $10 shipping, cost $50, no other costs.
        var result = calc.Calculate(supplierUnitCost: 50m, quantity: 1, expectedSalePrice: 100m,
            quickSalePrice: 85m, buyerPaidShipping: 10m, fees);

        // eBay fee = (100+10)*13.25% + 0.40 = 14.575 + 0.40 = 14.975 -> rounds to 14.98 (banker's/away rounding via MidpointRounding default)
        Assert.Equal(Math.Round(110m * 0.1325m + 0.40m, 2), result.EbayFees);
        var expectedNetProfit = 100m + 10m - 50m - result.EbayFees;
        Assert.Equal(Math.Round(expectedNetProfit, 2), result.NetProfitPerUnit);
        Assert.Equal(Math.Round(result.NetProfitPerUnit / 50m * 100m, 1), result.RoiPercent);
    }

    [Fact]
    public void Calculate_ZeroSupplierCost_RoiIsNull()
    {
        var calc = new ProfitCalculator();

        var result = calc.Calculate(0m, 1, 100m, 90m, 0m, DefaultFees());

        Assert.Null(result.RoiPercent);
    }

    [Fact]
    public void Calculate_QuantityMultipliesTotalProfitButNotPerUnit()
    {
        var calc = new ProfitCalculator();

        var result = calc.Calculate(20m, 5, 60m, 50m, 0m, DefaultFees());

        Assert.Equal(result.NetProfitPerUnit * 5, result.TotalPotentialProfit);
    }

    [Fact]
    public void Calculate_BreakEvenPrice_YieldsApproximatelyZeroProfitAtThatSalePrice()
    {
        var calc = new ProfitCalculator();
        var fees = DefaultFees();

        var initial = calc.Calculate(50m, 1, 100m, 85m, 0m, fees);
        var atBreakEven = calc.Calculate(50m, 1, initial.BreakEvenSalePrice, initial.BreakEvenSalePrice, 0m, fees);

        Assert.True(Math.Abs(atBreakEven.NetProfitPerUnit) < 0.05m,
            $"expected ~0 net profit at the break-even price, got {atBreakEven.NetProfitPerUnit}");
    }

    [Fact]
    public void Calculate_NegativeMarginScenario_ReturnsNegativeNetProfit()
    {
        var calc = new ProfitCalculator();

        // Supplier cost alone exceeds what the item resells for.
        var result = calc.Calculate(150m, 1, 100m, 85m, 0m, DefaultFees());

        Assert.True(result.NetProfitPerUnit < 0);
    }

    [Fact]
    public void Calculate_ConfiguredReserves_ReduceNetProfit()
    {
        var calc = new ProfitCalculator();
        var withoutReserves = calc.Calculate(50m, 1, 100m, 85m, 0m, DefaultFees());

        var withReserves = new FeeProfile { ReturnReservePercent = 5m, TestingReservePercent = 5m };
        var result = calc.Calculate(50m, 1, 100m, 85m, 0m, withReserves);

        Assert.True(result.NetProfitPerUnit < withoutReserves.NetProfitPerUnit);
    }
}
