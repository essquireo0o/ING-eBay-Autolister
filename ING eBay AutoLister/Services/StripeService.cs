using Stripe;
using Stripe.Checkout;

namespace ING_eBay_AutoLister.Services;

public class StripeService(CredentialsStore creds)
{
    public bool IsConfigured => !string.IsNullOrWhiteSpace(creds.Get().StripeSecretKey);

    public string PublishableKey => creds.Get().StripePublishableKey;

    public async Task<string> CreateProCheckoutSessionAsync(string successUrl, string cancelUrl)
    {
        StripeConfiguration.ApiKey = creds.Get().StripeSecretKey;

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
                        UnitAmount = 4999, // $49.99
                        Recurring  = new SessionLineItemPriceDataRecurringOptions
                        {
                            Interval = "month"
                        },
                        ProductData = new SessionLineItemPriceDataProductDataOptions
                        {
                            Name        = "ING Listing Engine™ — Pro",
                            Description = "Monthly Pro subscription. Unlimited listings, bulk import, AI image generation, priority support.",
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
