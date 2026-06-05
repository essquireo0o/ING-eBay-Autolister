using System.Text;
using System.Text.Json;

namespace ING_eBay_AutoLister.Services;

public class LicenseStatus
{
    public bool   Valid    { get; set; }
    public string Tier     { get; set; } = "unlicensed"; // unlicensed | free | pro | unverified
    public string Message  { get; set; } = "";
    public bool   Checked  { get; set; }
}

public class LicenseService(CredentialsStore creds, IHttpClientFactory http)
{
    private static readonly JsonSerializerOptions _opts = new() { PropertyNameCaseInsensitive = true };

    private LicenseStatus _cached = new() { Valid = false, Tier = "unlicensed", Checked = false };

    public LicenseStatus Current => _cached;

    public async Task<LicenseStatus> CheckAsync()
    {
        var key = creds.Get().LicenseKey?.Trim() ?? "";

        if (string.IsNullOrWhiteSpace(key))
        {
            _cached = new LicenseStatus { Valid = false, Tier = "unlicensed", Checked = true, Message = "No license key entered." };
            return _cached;
        }

        // Beta / free-access keys — resolved locally, no server call needed
        if (key.Equals("ING-BETA-2025", StringComparison.OrdinalIgnoreCase))
        {
            _cached = new LicenseStatus { Valid = true, Tier = "free", Checked = true, Message = "Beta license active." };
            return _cached;
        }

        try
        {
            var client = http.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(8);

            var payload = JsonSerializer.Serialize(new
            {
                key,
                fingerprint = GetMachineFingerprint(),
                product     = "ING-eBay-AutoLister"
            });

            var res = await client.PostAsync(
                "https://ingmining.com/wp-json/ing/v1/license/check",
                new StringContent(payload, Encoding.UTF8, "application/json"));

            if (res.IsSuccessStatusCode)
            {
                var body = JsonSerializer.Deserialize<LicenseCheckResponse>(
                    await res.Content.ReadAsStringAsync(), _opts);

                _cached = new LicenseStatus
                {
                    Valid    = body?.Valid ?? false,
                    Tier     = body?.Tier ?? "unlicensed",
                    Message  = body?.Message ?? "",
                    Checked  = true
                };
            }
            else
            {
                _cached = new LicenseStatus { Valid = false, Tier = "unlicensed", Checked = true, Message = "License server rejected the key." };
            }
        }
        catch
        {
            // Server unreachable — don't block the app; treat as unverified so it keeps working
            _cached = new LicenseStatus { Valid = true, Tier = "unverified", Checked = true, Message = "License server unreachable — running in offline mode." };
        }

        return _cached;
    }

    private static string GetMachineFingerprint()
    {
        var raw  = $"{Environment.MachineName}|{Environment.UserName}";
        var hash = Math.Abs(raw.GetHashCode()).ToString("x8");
        return hash;
    }
}

public class LicenseCheckResponse
{
    public bool   Valid   { get; set; }
    public string Tier    { get; set; } = "";
    public string Message { get; set; } = "";
}
