using Stripe;
using Stripe.Checkout;

namespace ING_eBay_AutoLister.Services;

public class StripeService(CredentialsStore creds)
{
    public bool IsConfigured => !string.IsNullOrWhiteSpace(creds.Get().StripeSecretKey);

    public string PublishableKey => creds.Get().StripePublishableKey;

    public Task<string> CreateProCheckoutSessionAsync(string successUrl, string cancelUrl)
        => CreateCheckoutSessionAsync("month", 2999, successUrl, cancelUrl);

    public Task<string> CreateProAnnualCheckoutSessionAsync(string successUrl, string cancelUrl)
        => CreateCheckoutSessionAsync("year", 24999, successUrl, cancelUrl);

    private async Task<string> CreateCheckoutSessionAsync(string interval, long unitAmount, string successUrl, string cancelUrl)
    {
        StripeConfiguration.ApiKey = creds.Get().StripeSecretKey;

        var intervalLabel = interval == "year" ? "Annual" : "Monthly";
        var options = new SessionCreateOptions
        {
            PaymentMethodTypes = ["card"],
            LineItems =
            [
                new SessionLineItemOptions
                {
                    PriceData = new SessionLineItemPriceDataOptions
                    {
                        Currency   = "usd",
                        UnitAmount = unitAmount,
                        Recurring  = new SessionLineItemPriceDataRecurringOptions
                        {
                            Interval = interval
                        },
                        ProductData = new SessionLineItemPriceDataProductDataOptions
                        {
                            Name        = $"ING Listing Engine™ — Pro ({intervalLabel})",
                            Description = $"{intervalLabel} Pro subscription. Unlimited listings, bulk import, AI image generation, priority support.",
                            Images      = ["https://ingmining.com/wp-content/uploads/ing-autolister-logo.png"],
                        }
                    },
                    Quantity = 1,
                }
            ],
            Mode                = "subscription",
            SuccessUrl          = successUrl,
            CancelUrl           = cancelUrl,
            SubscriptionData    = new SessionSubscriptionDataOptions
            {
                Metadata = new Dictionary<string, string>
                {
                    ["product"] = "ING-eBay-AutoLister-Pro"
                }
            },
            Metadata = new Dictionary<string, string>
            {
                ["product"] = "ING-eBay-AutoLister-Pro"
            }
        };

        var service = new SessionService();
        var session = await service.CreateAsync(options);
        return session.Url;
    }
}
