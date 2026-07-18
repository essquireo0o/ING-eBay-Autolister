using ING_eBay_AutoLister.Models;

namespace ING_eBay_AutoLister.Services;

// Centralizes the NetProfit/ROI/Margin/break-even math the spec calls for, using a configurable
// FeeProfile instead of hardcoded assumptions. Replaces the two near-identical inline fee blocks
// that used to live in Program.cs's supplier-file-analyzer endpoint (local-data path and
// Terapeak-fallback path each hand-rolled the same formula).
public sealed class ProfitCalculator
{
    public ProfitBreakdown Calculate(
        decimal supplierUnitCost, int quantity, decimal expectedSalePrice, decimal quickSalePrice,
        decimal buyerPaidShipping, FeeProfile fees,
        decimal? actualShippingCostOverride = null, decimal? otherCosts = null)
    {
        quantity = Math.Max(1, quantity);
        var actualShipping = actualShippingCostOverride ?? fees.DefaultShippingCost;
        var other = otherCosts ?? 0m;

        var totalRevenue = expectedSalePrice + buyerPaidShipping;
        var ebayFees = Math.Round(totalRevenue * (fees.EbayFinalValueFeePercent / 100m) + fees.EbayFinalValueFeeFixed, 2);
        var promotedFees = Math.Round(totalRevenue * (fees.PromotedListingRatePercent / 100m), 2);
        // Payment-processing fees fold into "other costs" — FeeProfile.PaymentProcessingPercent
        // defaults to 0 (eBay's final value fee is normally all-inclusive), so this is inert
        // unless a caller configures it, and ProfitBreakdown doesn't need a dedicated field for it.
        var paymentFees = Math.Round(totalRevenue * (fees.PaymentProcessingPercent / 100m), 2);
        var returnReserve = Math.Round(totalRevenue * (fees.ReturnReservePercent / 100m), 2);
        var testingReserve = Math.Round(totalRevenue * (fees.TestingReservePercent / 100m), 2);
        var otherWithPayment = Math.Round(other + paymentFees, 2);

        var netProfitPerUnit = totalRevenue - supplierUnitCost - ebayFees - promotedFees
            - actualShipping - fees.DefaultPackagingCost - fees.DefaultLaborCost
            - returnReserve - testingReserve - otherWithPayment;

        var breakEven = BreakEvenPrice(
            supplierUnitCost, actualShipping, fees.DefaultPackagingCost, fees.DefaultLaborCost, otherWithPayment,
            buyerPaidShipping, fees.EbayFinalValueFeePercent, fees.EbayFinalValueFeeFixed,
            fees.PromotedListingRatePercent, fees.ReturnReservePercent, fees.TestingReservePercent);

        return new ProfitBreakdown
        {
            SupplierUnitCost = supplierUnitCost,
            Quantity = quantity,
            ExpectedSalePrice = expectedSalePrice,
            QuickSalePrice = quickSalePrice,
            BuyerPaidShipping = buyerPaidShipping,
            EbayFees = ebayFees,
            PromotedListingFees = promotedFees,
            ActualShippingCost = actualShipping,
            PackagingCost = fees.DefaultPackagingCost,
            LaborCost = fees.DefaultLaborCost,
            ReturnReserve = returnReserve,
            TestingReserve = testingReserve,
            OtherCosts = otherWithPayment,
            NetProfitPerUnit = Math.Round(netProfitPerUnit, 2),
            TotalPotentialProfit = Math.Round(netProfitPerUnit * quantity, 2),
            RoiPercent = supplierUnitCost > 0 ? Math.Round(netProfitPerUnit / supplierUnitCost * 100m, 1) : null,
            MarginPercent = totalRevenue > 0 ? Math.Round(netProfitPerUnit / totalRevenue * 100m, 1) : null,
            BreakEvenSalePrice = Math.Round(breakEven, 2),
        };
    }

    // Solves for the sale price P where NetProfit(P) == 0, given that every percentage-based fee
    // is itself a function of P (via TotalRevenue = P + shipping) — algebraic rearrangement of the
    // NetProfit formula, not a numeric search:
    //   P*(1 - totalFeePct) = fixedCosts - shipping*(1 - totalFeePct)
    //   P = fixedCosts / (1 - totalFeePct) - shipping
    private static decimal BreakEvenPrice(
        decimal supplierUnitCost, decimal actualShipping, decimal packaging, decimal labor, decimal other,
        decimal buyerPaidShipping, decimal feePercent, decimal feeFixed, decimal promotedPercent,
        decimal returnPercent, decimal testingPercent)
    {
        var totalFeeFraction = (feePercent + promotedPercent + returnPercent + testingPercent) / 100m;
        if (totalFeeFraction >= 1m) return decimal.MaxValue; // fees alone exceed revenue — no price breaks even

        var fixedCosts = supplierUnitCost + feeFixed + actualShipping + packaging + labor + other;
        return fixedCosts / (1m - totalFeeFraction) - buyerPaidShipping;
    }
}
