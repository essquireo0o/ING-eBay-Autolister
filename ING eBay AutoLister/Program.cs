using ING_eBay_AutoLister.Models;
using ING_eBay_AutoLister.Services;
using Microsoft.Extensions.Hosting.WindowsServices;

// ── Crash logging ────────────────────────────────────────────────────────────
// Writes crash.log next to the exe before the process dies so the cause is
// visible even when there is no console window to read.
AppDomain.CurrentDomain.UnhandledException += (_, e) =>
{
    try
    {
        var dir = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
        File.AppendAllText(Path.Combine(dir, "crash.log"),
            $"{DateTime.Now:u}: {e.ExceptionObject}\n---\n");
    }
    catch { }
};

// ── Service mode detection ────────────────────────────────────────────────────
// When launched by the Windows SCM, run headless (no tray icon, no browser).
// Interactive launches (double-click, startup shortcut) get the full tray UI.
bool isWindowsService = WindowsServiceHelpers.IsWindowsService();

// ── Dev port override ─────────────────────────────────────────────────────────
// Set AUTOLISTER_DEV_PORT to run a second, independent instance side-by-side
// with the installed Windows service (e.g. while iterating on source without
// touching the service's port 9330).
var port    = Environment.GetEnvironmentVariable("AUTOLISTER_DEV_PORT") ?? "9330";
var baseUrl = $"http://localhost:{port}";
var isDevPort = port != "9330";

// ── Elevated helper: add inglistingengine.com → 127.0.0.1 to hosts ──────────
// The installer re-launches with this flag as admin. After adding the entry the
// process exits immediately — it is not the long-running server instance.
if (args.Contains("--add-local-dns"))
{
    try
    {
        const string hostsFile = @"C:\Windows\System32\drivers\etc\hosts";
        const string entry     = "127.0.0.1  inglistingengine.com";
        var lines = File.ReadAllLines(hostsFile);
        if (!lines.Any(l => l.Contains("inglistingengine.com", StringComparison.OrdinalIgnoreCase)))
            File.AppendAllText(hostsFile, $"\n{entry}\n");
    }
    catch { }
    return;
}

// ── Single-instance / already-running guard ───────────────────────────────────
// Service mode: SCM guarantees a single instance — skip the mutex entirely.
// Interactive mode:
//   1. Acquire mutex so only one tray instance runs at a time.
//   2. If the Windows service is already serving on 9330, show a tray icon
//      without starting a second web server (opens browser immediately).
//   3. Otherwise start the server ourselves, then show the tray icon.
System.Threading.Mutex? _mutex = null;
if (!isWindowsService)
{
    _mutex = new System.Threading.Mutex(true, $"ING-AutoLister-{port}", out var isFirstInstance);
    if (!isFirstInstance)
    {
        // Another interactive instance (tray) is already running — just open browser
        System.Diagnostics.Process.Start(
            new System.Diagnostics.ProcessStartInfo(baseUrl) { UseShellExecute = true });
        _mutex.Dispose();
        return;
    }

    // Check whether the Windows service is already hosting the web server.
    // Skipped when running on a dev override port — that's a deliberate
    // side-by-side instance, not a duplicate of the service.
    bool serverAlive = false;
    if (!isDevPort)
    {
        try
        {
            using var pingHttp = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(1) };
            serverAlive = (await pingHttp.GetAsync($"{baseUrl}/api/setup/status")).IsSuccessStatusCode;
        }
        catch { }
    }

    if (serverAlive)
    {
        // Service is running — show tray icon as a UI helper (don't start another server)
        OpenBrowser();
        System.Windows.Forms.Application.EnableVisualStyles();
        System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);
        using var trayIconSvc = new System.Windows.Forms.NotifyIcon
        {
            Icon    = CreateAppIcon(),
            Text    = $"ING AutoLister  •  localhost:{port}",
            Visible = true,
        };
        var ctxMenuSvc = new System.Windows.Forms.ContextMenuStrip();
        ctxMenuSvc.Items.Add("Open ING AutoLister", null, (_, _) => OpenBrowser());
        ctxMenuSvc.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        ctxMenuSvc.Items.Add("Close Tray Icon", null, (_, _) =>
        {
            trayIconSvc.Visible = false;
            System.Windows.Forms.Application.ExitThread();
        });
        trayIconSvc.ContextMenuStrip = ctxMenuSvc;
        trayIconSvc.DoubleClick     += (_, _) => OpenBrowser();
        System.Windows.Forms.Application.Run();
        _mutex.Dispose();
        return;
    }
}

// ── Data directory ───────────────────────────────────────────────────────────
// For portable / perUser installs the exe lives in a writable folder, so data
// stays next to the exe (original behaviour).  For perMachine / Program Files
// installs the exe directory is read-only for regular users, so user data goes
// to %LOCALAPPDATA%\ING AutoLister instead.
var exeDir = Path.GetDirectoryName(Environment.ProcessPath) ?? Directory.GetCurrentDirectory();
var pf     = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
var pf86   = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
var isSystemInstall = !string.IsNullOrEmpty(pf) &&
    (exeDir.StartsWith(pf,   StringComparison.OrdinalIgnoreCase) ||
     exeDir.StartsWith(pf86, StringComparison.OrdinalIgnoreCase));
var dataDir = isWindowsService
    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ING AutoLister")
    : isSystemInstall
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ING AutoLister")
        : exeDir;
Directory.CreateDirectory(dataDir);

// On the first run after a system install, seed the pre-configured
// credentials.json from the Program Files template into the user's data folder.
if (isSystemInstall)
{
    var credsDest = Path.Combine(dataDir, "credentials.json");
    var credsSrc  = Path.Combine(exeDir,  "credentials.json");
    if (!File.Exists(credsDest) && File.Exists(credsSrc))
        File.Copy(credsSrc, credsDest);
}

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = dataDir
});
builder.Host.UseWindowsService();
builder.Services.AddHttpClient();
builder.Services.AddSingleton<CredentialsStore>();
builder.Services.AddSingleton<ClaudeService>();
builder.Services.AddSingleton<EbayService>();
builder.Services.AddSingleton<ListingDatabase>();
builder.Services.AddSingleton<ImageGenerationService>();
builder.Services.AddSingleton<PhotoLibrary>();
builder.Services.AddSingleton<ActionLog>();
builder.Services.AddSingleton<DraftStore>();
builder.Services.AddSingleton<LicenseService>();
builder.Services.AddSingleton<StripeService>();
builder.Services.AddSingleton<AnalyticsStore>();
builder.Services.AddSingleton<TerapeakService>();
builder.Services.AddSingleton<GemRadarStore>();
builder.Services.AddSingleton<TerapeakPriceCache>();

var app = builder.Build();

// Serve UI files from embedded resources (bundled inside the exe)
{
    var asm = typeof(Program).Assembly;
    var ns  = "ING_eBay_AutoLister.wwwroot";
    var embedded = new Microsoft.Extensions.FileProviders.EmbeddedFileProvider(asm, ns);
    app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = embedded, DefaultFileNames = ["index.html"] });
    app.UseStaticFiles(new StaticFileOptions { FileProvider = embedded });
}

// Serve generated-photos from a writable folder next to the exe
{
    var photosDir = Path.Combine(app.Environment.ContentRootPath, "generated-photos");
    Directory.CreateDirectory(photosDir);
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(photosDir),
        RequestPath  = "/generated-photos"
    });
}

// Stripe keys are configured via the Settings page and stored in credentials.json

// Record install date on first run
app.Services.GetRequiredService<CredentialsStore>().EnsureInstallDate();

// Background license check — non-blocking, runs after startup
_ = Task.Run(async () =>
{
    await Task.Delay(2000);
    await app.Services.GetRequiredService<LicenseService>().CheckAsync();
});

// ── Background maintenance loop ───────────────────────────────────────────
_ = Task.Run(async () =>
{
    await Task.Delay(10_000); // wait for startup

    while (true)
    {
        try
        {
            // 1. Proactive eBay token refresh — top up 20 min before expiry
            var store = app.Services.GetRequiredService<CredentialsStore>();
            if (store.IsAccessTokenExpiringSoon(minutes: 20) && store.HasValidRefreshToken())
            {
                try
                {
                    var ebay = app.Services.GetRequiredService<EbayService>();
                    await ebay.ProactiveTokenRefreshAsync();
                }
                catch { /* non-fatal — will retry next cycle */ }
            }

            // 2. Generated-photos cleanup — keep newest 300, delete the rest
            var photosDir = Path.Combine(
                app.Services.GetRequiredService<IWebHostEnvironment>().WebRootPath,
                "generated-photos");
            if (Directory.Exists(photosDir))
            {
                var files = new DirectoryInfo(photosDir)
                    .GetFiles("*", SearchOption.TopDirectoryOnly)
                    .OrderByDescending(f => f.LastWriteTimeUtc)
                    .ToList();
                foreach (var old in files.Skip(300))
                {
                    try { old.Delete(); } catch { /* skip locked files */ }
                }
            }
        }
        catch { /* maintenance loop must never crash */ }

        await Task.Delay(TimeSpan.FromMinutes(5));
    }
});

// ── Gem Radar background scanner (Pro) ─────────────────────────────────────────
// Rotates through a fixed list of broad category keywords, one per cycle, running each through
// the exact same FindOpportunitiesAsync pipeline as a manual Opportunity Finder search — so the
// Pro "Gems" feed fills up passively without anyone having to type a keyword. Deliberately slow
// (one category every 12 minutes) and capped at a small per-category real-scrape budget: see
// TerapeakPriceCache / GetOrScrapePricingAsync for how overlapping keywords across categories
// and manual searches reuse a single scrape instead of each paying for its own.
string[] gemRadarCategories =
[
    "graphics card", "gaming laptop", "power tool", "vintage camera", "electric guitar",
    "mechanical keyboard", "smart watch", "drone", "espresso machine", "cordless drill",
    "video game console", "trading card lot", "vinyl record", "designer handbag", "cast iron skillet",
    "lego set", "action figure", "record player", "telescope", "sewing machine",
    "leather jacket", "wireless headphones", "robot vacuum", "board game",
];

_ = Task.Run(async () =>
{
    await Task.Delay(TimeSpan.FromMinutes(1)); // let startup settle first
    var idx = 0;
    while (true)
    {
        try
        {
            var terapeak = app.Services.GetRequiredService<TerapeakService>();
            if (terapeak.IsConnected)
            {
                var category = gemRadarCategories[idx % gemRadarCategories.Length];
                idx++;

                var ebay  = app.Services.GetRequiredService<EbayService>();
                var cache = app.Services.GetRequiredService<TerapeakPriceCache>();
                var log   = app.Services.GetRequiredService<ActionLog>();
                var store = app.Services.GetRequiredService<GemRadarStore>();

                var result = await FindOpportunitiesAsync(category, null, null, null, null, "AUCTION",
                    ebay, terapeak, cache, log, terapeakRecheckLimit: 3);

                // Only keep genuine, high-confidence finds in the passive feed — verified
                // pricing and a solid score, not every listing the category search returned.
                var gems = result.Items.Where(x => x.IsVerified && x.OpportunityScore >= 55).ToList();
                store.RecordScan(category, gems);
                log.Add("Info", "Gem Radar scan", $"{category}: {gems.Count} gem(s) found ({cache.Count} cached prices total)");
            }
        }
        catch { /* scanner must never crash the app; just retry next cycle */ }

        await Task.Delay(TimeSpan.FromMinutes(12));
    }
});

// ── Trial ─────────────────────────────────────────────────────────
app.MapGet("/api/trial/status", (CredentialsStore store, LicenseService license) =>
{
    store.EnsureInstallDate();
    var lic = license.Current;
    // Beta license = unlimited free access
    if (lic.Valid && lic.Tier == "free")
        return Results.Ok(new { daysRemaining = 9999, expired = false, licensed = true, tier = "beta" });
    var days    = store.TrialDaysRemaining();
    var expired = days <= 0;
    return Results.Ok(new { daysRemaining = days, expired, licensed = !expired, tier = expired ? "expired" : "trial" });
});

// ── License ───────────────────────────────────────────────────────
app.MapGet("/api/license/status", (LicenseService license) => Results.Ok(license.Current));

app.MapPost("/api/license/activate", async (LicenseService license) =>
    Results.Ok(await license.CheckAsync()));

// ── Stripe ────────────────────────────────────────────────────────
app.MapGet("/api/stripe/config", (StripeService stripe) =>
    Results.Ok(new { configured = stripe.IsConfigured, publishableKey = stripe.PublishableKey }));

app.MapPost("/api/stripe/checkout", async (StripeService stripe, HttpContext ctx) =>
{
    if (!stripe.IsConfigured)
        return Results.BadRequest(new { error = "Stripe not configured." });
    try
    {
        var successUrl = "https://ingmining.com/autolister-pro-success?session_id={CHECKOUT_SESSION_ID}";
        var cancelUrl  = $"{ctx.Request.Scheme}://{ctx.Request.Host}/";
        var url = await stripe.CreateProCheckoutSessionAsync(successUrl, cancelUrl);
        return Results.Ok(new { url });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapPost("/api/stripe/checkout/annual", async (StripeService stripe, HttpContext ctx) =>
{
    if (!stripe.IsConfigured)
        return Results.BadRequest(new { error = "Stripe not configured." });
    try
    {
        var successUrl = "https://ingmining.com/autolister-pro-success?session_id={CHECKOUT_SESSION_ID}";
        var cancelUrl  = $"{ctx.Request.Scheme}://{ctx.Request.Host}/";
        var url = await stripe.CreateProAnnualCheckoutSessionAsync(successUrl, cancelUrl);
        return Results.Ok(new { url });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

// ── Setup / credentials ───────────────────────────────────────────
app.MapGet("/api/setup/status", (CredentialsStore store) => Results.Ok(store.GetStatus()));
app.MapGet("/api/setup/fields", (CredentialsStore store) => Results.Ok(store.GetPublicFields()));

app.MapPost("/api/setup/save", (Credentials body, CredentialsStore store) =>
{
    store.Save(body);
    return Results.Ok(store.GetStatus());
});

// ── Trial guard ───────────────────────────────────────────────────
static IResult? TrialGuard(CredentialsStore store, LicenseService license)
{
    return null; // Freeware — no restrictions
}

// ── AI analysis ───────────────────────────────────────────────────
app.MapPost("/api/analyze", async (AnalyzeRequest req, ClaudeService claude, CredentialsStore store, LicenseService license) =>
{
    if (TrialGuard(store, license) is { } blocked) return blocked;
    if (string.IsNullOrEmpty(req.ImageBase64))
        return Results.BadRequest("ImageBase64 is required");
    var listing = await claude.AnalyzeImageAsync(req.ImageBase64, req.MimeType);
    return Results.Ok(listing);
});

app.MapPost("/api/analyze-url", async (AnalyzeUrlRequest req, ClaudeService claude, EbayService ebay, IHttpClientFactory httpFactory, IWebHostEnvironment env, ActionLog log, CredentialsStore store, LicenseService license) =>
{
    if (TrialGuard(store, license) is { } blocked) return blocked;
    if (string.IsNullOrWhiteSpace(req.Url))
        return Results.BadRequest(new { error = "URL is required" });

    try
    {
        // ── eBay listing URL → use eBay API directly (no scraping needed) ──
        var ebayItemId = ExtractEbayItemId(req.Url);
        if (!string.IsNullOrEmpty(ebayItemId))
        {
            log.Add("Info", "Analyze eBay item", ebayItemId);
            var item = await ebay.GetItemAsync(ebayItemId);

            // Save first image locally so the photo grid can show it
            if (item.ImageUrls.Count > 0)
            {
                try
                {
                    var http2 = httpFactory.CreateClient();
                    var imgBytes2 = await http2.GetByteArrayAsync(item.ImageUrls[0]);
                    var photosDir = System.IO.Path.Combine(env.ContentRootPath, "generated-photos");
                    System.IO.Directory.CreateDirectory(photosDir);
                    var ext2  = item.ImageUrls[0].Contains(".png") ? "png" : "jpg";
                    var file2 = $"ebay_{Guid.NewGuid():N}.{ext2}";
                    await System.IO.File.WriteAllBytesAsync(System.IO.Path.Combine(photosDir, file2), imgBytes2);
                    var ebayLocalUrl = $"/generated-photos/{file2}";
                    item.ImageUrls.Insert(0, ebayLocalUrl);
                }
                catch { /* non-fatal */ }
            }

            // Rewrite the description with Claude SEO template — the original seller's
            // description is often plain text or poorly formatted HTML
            try
            {
                log.Add("Info", "Rewriting eBay description with Claude SEO template", ebayItemId);
                var improved = await claude.ImproveSeoAsync(new ImproveSeoRequest
                {
                    Title         = item.Title ?? "",
                    Subtitle      = item.Subtitle ?? "",
                    Category      = item.Category ?? "",
                    Condition     = item.Condition ?? "",
                    Brand         = item.Brand ?? "",
                    Price         = item.Price,
                    Description   = item.Description ?? "",
                    ItemSpecifics = item.ItemSpecifics ?? [],
                    Quantity                 = item.Quantity,
                    WeightLbs                = item.WeightLbs,
                    WeightOz                 = item.WeightOz,
                    PackageLengthIn          = item.PackageLengthIn,
                    PackageWidthIn           = item.PackageWidthIn,
                    PackageHeightIn          = item.PackageHeightIn,
                    HandlingTimeBusinessDays = item.HandlingTimeBusinessDays,
                    ItemLocationPostalCode   = item.ItemLocationPostalCode ?? "",
                    ImageUrls                = item.ImageUrls,
                });
                // Keep original structured data — only take the rewritten description and title
                item.Description = improved.Description;
                if (!string.IsNullOrWhiteSpace(improved.Title)) item.Title = improved.Title;
            }
            catch (Exception ex)
            {
                // Non-fatal — return original data if Claude rewrite fails
                log.Add("Warning", "eBay description SEO rewrite failed", ex.Message);
            }

            return Results.Ok(item);
        }

        // ── General URL → headless screenshot + Claude vision ────────────────
        log.Add("Info", "Analyze URL (headless screenshot)", req.Url[..Math.Min(80, req.Url.Length)]);

        var (screenshotB64, productImageUrl) = await TakeHeadlessScreenshot(req.Url, log);
        if (string.IsNullOrEmpty(screenshotB64))
            return Results.BadRequest(new { error = "Could not load the page — try copying the product image and dropping it into the Product Photo zone instead." });

        var photosDir2 = System.IO.Path.Combine(env.ContentRootPath, "generated-photos");
        System.IO.Directory.CreateDirectory(photosDir2);

        // Save full screenshot for AI analysis
        var ssFile = $"url_{Guid.NewGuid():N}.png";
        await System.IO.File.WriteAllBytesAsync(System.IO.Path.Combine(photosDir2, ssFile), Convert.FromBase64String(screenshotB64));

        var listing2 = await claude.AnalyzeImageAsync(screenshotB64, "image/png");
        listing2.ImageUrls.Insert(0, $"/generated-photos/{ssFile}");

        // Fetch and save the clean product image (best source for BG removal — put at index 0)
        var productImageFetched = false;
        var candidateUrls = new List<string?> { productImageUrl };

        // Also try OG image
        try
        {
            var http3 = httpFactory.CreateClient();
            http3.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 Chrome/120.0");
            http3.Timeout = TimeSpan.FromSeconds(6);
            var html3 = await http3.GetStringAsync(req.Url);
            candidateUrls.Add(ExtractPrimaryImageUrl(html3));
        }
        catch { }

        foreach (var candidateUrl in candidateUrls)
        {
            if (string.IsNullOrWhiteSpace(candidateUrl) || productImageFetched) continue;
            try
            {
                var http4 = httpFactory.CreateClient();
                http4.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 Chrome/120.0");
                http4.Timeout = TimeSpan.FromSeconds(10);
                var imgBytes = await http4.GetByteArrayAsync(candidateUrl);
                var ext = candidateUrl.Contains(".png") ? "png" : "jpg";
                var prodFile = $"prod_{Guid.NewGuid():N}.{ext}";
                await System.IO.File.WriteAllBytesAsync(System.IO.Path.Combine(photosDir2, prodFile), imgBytes);
                listing2.ImageUrls.Insert(0, $"/generated-photos/{prodFile}");
                productImageFetched = true;
                log.Add("Info", "Product image saved for BG removal", prodFile);
            }
            catch { }
        }

        return Results.Ok(listing2);

    }
    catch (Exception ex)
    {
        log.Add("Warning", "Analyze URL failed", ex.Message);
        return Results.BadRequest(new { error = ex.Message });
    }
});

static async Task<(string Screenshot, string? ProductImage)> TakeHeadlessScreenshot(string url, ActionLog log)
{
    var playwrightDir = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "npm", "node_modules", "playwright");

    var escapedUrl = url.Replace("\\", "\\\\").Replace("'", "\\'");
    var pwPath = playwrightDir.Replace("\\", "\\\\");
    var script =
        $"const {{ chromium }} = require('{pwPath}');\n" +
        "(async () => {\n" +
        "  const browser = await chromium.launch({ headless: true,\n" +
        "    args: ['--disable-blink-features=AutomationControlled','--no-sandbox'] });\n" +
        "  const ctx = await browser.newContext({ viewport: { width: 1280, height: 900 },\n" +
        "    userAgent: 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36',\n" +
        "    locale: 'en-US', timezoneId: 'America/New_York' });\n" +
        "  await ctx.addInitScript(() => { Object.defineProperty(navigator,'webdriver',{get:()=>undefined}); });\n" +
        "  const page = await ctx.newPage();\n" +
        "  try {\n" +
        $"    await page.goto('{escapedUrl}', {{ waitUntil: 'domcontentloaded', timeout: 25000 }});\n" +
        "    await page.waitForTimeout(2500);\n" +
        "    for (const sel of ['button:has-text(\"Continue shopping\")','input[value*=\"Continue\"]','#continueShopping',\n" +
        "        'button[aria-label*=\"close\" i]','button[aria-label*=\"dismiss\" i]','button[aria-label*=\"accept\" i]',\n" +
        "        '[class*=\"modal-close\"]','[class*=\"popup-close\"]','[class*=\"close-modal\"]',\n" +
        "        'button:has-text(\"Accept all\")','button:has-text(\"Accept cookies\")','button:has-text(\"Got it\")','button:has-text(\"I agree\")']) {\n" +
        "      const btn = await page.$(sel).catch(()=>null);\n" +
        "      if (btn) { try { await btn.click(); await page.waitForTimeout(500); } catch(_) {} }\n" +
        "    }\n" +
        // Strip watermarks, cookie banners, consent overlays, and popups via CSS injection
        "    await page.addStyleTag({ content: [\n" +
        "      '[class*=\"watermark\" i],[id*=\"watermark\" i],[class*=\"water-mark\" i],[id*=\"water-mark\" i],',\n" +
        "      '.WatermarkContainer,[class*=\"WatermarkImage\"],[class*=\"wm-overlay\"],[class*=\"img-protection\"],',\n" +
        "      '[class*=\"cookie-banner\" i],[class*=\"cookie-bar\" i],[class*=\"cookie-notice\" i],[class*=\"cookie-wall\" i],',\n" +
        "      '[id*=\"cookie-banner\" i],[id*=\"cookie-bar\" i],[id*=\"cookie-notice\" i],',\n" +
        "      '[class*=\"consent-banner\" i],[class*=\"gdpr-banner\" i],[class*=\"gdpr-notice\" i],',\n" +
        "      '#onetrust-banner-sdk,#onetrust-consent-sdk,[class*=\"CookieBanner\"],[class*=\"cookieBanner\"],',\n" +
        "      '.cc-window,.cc-banner,[class*=\"CybotCookiebot\"],[id*=\"CybotCookiebot\"],',\n" +
        "      '.ReactModal__Overlay,.modal-backdrop,[class*=\"modal-overlay\" i],[class*=\"popup-overlay\" i],',\n" +
        "      '[class*=\"interstitial\" i],[class*=\"newsletter-popup\" i],[class*=\"email-popup\" i]',\n" +
        "      '{display:none!important;opacity:0!important;pointer-events:none!important}'\n" +
        "    ].join('') }).catch(()=>{});\n" +
        // JS pass: remove absolutely/fixed-positioned text overlays on top of product images
        // (seller brand names, company logos overlaid as HTML elements over the image)
        "    await page.evaluate(() => {\n" +
        "      try {\n" +
        "        const imgs = Array.from(document.querySelectorAll('img'))\n" +
        "          .filter(img => img.naturalWidth >= 200 && img.naturalHeight >= 200);\n" +
        "        for (const img of imgs) {\n" +
        "          const imgRect = img.getBoundingClientRect();\n" +
        "          if (!imgRect.width || !imgRect.height) continue;\n" +
        "          const container = img.closest('[class*=\"product\"],[class*=\"gallery\"],[class*=\"image-wrap\"],[class*=\"img-wrap\"],[class*=\"photo\"]') || img.parentElement;\n" +
        "          if (!container) continue;\n" +
        "          const candidates = container.querySelectorAll('*');\n" +
        "          for (const el of candidates) {\n" +
        "            if (el === img || el.tagName === 'IMG' || el.querySelector('img')) continue;\n" +
        "            const st = window.getComputedStyle(el);\n" +
        "            if (st.position !== 'absolute' && st.position !== 'fixed') continue;\n" +
        "            const text = el.textContent.trim();\n" +
        "            if (!text || text.length > 80) continue;\n" +
        "            const r = el.getBoundingClientRect();\n" +
        "            const overlaps = r.left < imgRect.right && r.right > imgRect.left &&\n" +
        "                             r.top  < imgRect.bottom && r.bottom > imgRect.top;\n" +
        "            if (overlaps) el.style.setProperty('display','none','important');\n" +
        "          }\n" +
        "        }\n" +
        "      } catch(_) {}\n" +
        "    }).catch(()=>{});\n" +
        "    await page.waitForTimeout(400);\n" +
        "    const buf = await page.screenshot({ fullPage: false });\n" +
        "    let prodUrl = null;\n" +
        "    try {\n" +
        // 1. Amazon-specific: authoritative product image selector
        "      prodUrl = await page.evaluate(() => {\n" +
        "        const el = document.querySelector('#landingImage,#imgTagWrapperId img,[data-old-hires],[data-a-hires]');\n" +
        "        if (!el) return null;\n" +
        "        return el.getAttribute('data-old-hires') || el.getAttribute('data-a-hires') || el.src || null;\n" +
        "      });\n" +
        // 2. Shopify / WooCommerce product image selectors
        "      if (!prodUrl) {\n" +
        "        prodUrl = await page.evaluate(() => {\n" +
        "          const sel = [\n" +
        "            '.product__media img', '.product-single__photo img', '.product-featured-img',\n" +
        "            '.woocommerce-product-gallery__image img', '.wp-post-image',\n" +
        "            '[data-product-featured-image]', '.product-image-main img',\n" +
        "            'img.product__image', '.featured-image img'\n" +
        "          ];\n" +
        "          for (const s of sel) {\n" +
        "            const el = document.querySelector(s);\n" +
        "            if (el) return el.getAttribute('data-src') || el.currentSrc || el.src || null;\n" +
        "          }\n" +
        "          return null;\n" +
        "        });\n" +
        "      }\n" +
        // 3. Best DOM image — skip logos, pick largest product-looking image
        "      if (!prodUrl) {\n" +
        "        const skipPat = /logo|sprite|icon|banner|no_img|placeholder|avatar|pixel|tracking|qr|wechat|barcode|scan|coupon|badge|related|similar|also|bought|header|footer|nav/i;\n" +
        "        const preferPat = /product|item|variant|main|hero|pdp|full|photo|img|cdn|media|shop|listing/i;\n" +
        "        const candidates = await page.evaluate(() =>\n" +
        "          Array.from(document.querySelectorAll('img'))\n" +
        "            .map(img => ({ src: img.currentSrc || img.src || img.getAttribute('data-src') || '', natW: img.naturalWidth, natH: img.naturalHeight }))\n" +
        "            .filter(i => i.src && i.natW >= 300 && i.natH >= 300)\n" +
        "            .sort((a,b) => (b.natW*b.natH) - (a.natW*a.natH))\n" +
        "        );\n" +
        "        const filtered = candidates.filter(c => !skipPat.test(c.src));\n" +
        "        const pool = filtered.length ? filtered : candidates;\n" +
        "        const best = pool.find(c => { const r=c.natW/c.natH; return r>0.5 && r<2.0 && preferPat.test(c.src); })\n" +
        "                  || pool.find(c => { const r=c.natW/c.natH; return r>0.5 && r<2.0; }) || pool[0];\n" +
        "        if (best) prodUrl = best.src;\n" +
        "      }\n" +
        // 4. og:image as last resort — only if it looks product-specific (not a logo)
        "      if (!prodUrl) {\n" +
        "        const ogUrl = await page.evaluate(() => { const og = document.querySelector('meta[property=\"og:image\"],meta[name=\"og:image\"]'); return og ? og.getAttribute('content') : null; });\n" +
        "        const logoLike = /logo|brand|icon|favicon|banner|header|default|placeholder/i;\n" +
        "        if (ogUrl && !logoLike.test(ogUrl)) prodUrl = ogUrl;\n" +
        "      }\n" +
        "    } catch(_) {}\n" +
        "    process.stdout.write(JSON.stringify({ ss: buf.toString('base64'), prodUrl }));\n" +
        "  } catch(e) { process.stderr.write(e.message); process.exit(1); }\n" +
        "  finally { await browser.close(); }\n" +
        "})();\n";

    var scriptFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"pwshot_{Guid.NewGuid():N}.cjs");
    await System.IO.File.WriteAllTextAsync(scriptFile, script);

    try
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName               = "node",
            ArgumentList           = { scriptFile },
            WorkingDirectory       = playwrightDir,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            CreateNoWindow         = true
        };

        using var proc = System.Diagnostics.Process.Start(psi)!;
        var stdoutTask = proc.StandardOutput.ReadToEndAsync();
        var stderrTask = proc.StandardError.ReadToEndAsync();
        // Hard 35-second timeout — kills the node process if it hangs
        using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(35));
        try { await proc.WaitForExitAsync(cts.Token); }
        catch (OperationCanceledException)
        {
            try { proc.Kill(entireProcessTree: true); } catch { /* already dead */ }
            log.Add("Warning", "Headless screenshot timed out — process killed", "35s limit");
            return (null!, null);
        }
        finally { try { System.IO.File.Delete(scriptFile); } catch { /* non-fatal */ } }
        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        if (proc.ExitCode != 0 || string.IsNullOrEmpty(stdout))
        {
            log.Add("Warning", "Headless screenshot failed", (stderr + stdout)[..Math.Min(300, (stderr + stdout).Length)]);
            return (null!, null);
        }

        // Output is JSON: { "ss": "<base64>", "prodUrl": "<url> | null" }
        string screenshotB64;
        string? productImageUrl = null;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(stdout.Trim());
            screenshotB64   = doc.RootElement.GetProperty("ss").GetString() ?? "";
            productImageUrl = doc.RootElement.TryGetProperty("prodUrl", out var p) && p.ValueKind == System.Text.Json.JsonValueKind.String
                ? p.GetString() : null;
        }
        catch
        {
            screenshotB64 = stdout.Trim();
        }

        log.Add("Info", "Headless screenshot taken",
            $"screenshot: {screenshotB64.Length} chars; product URL: {productImageUrl ?? "none"}");
        return (screenshotB64, productImageUrl);
    }
    finally
    {
        try { System.IO.File.Delete(scriptFile); } catch { }
    }
}

static string? ExtractEbayItemId(string url)
{
    // Matches: ebay.com/itm/123456789 or ebay.com/itm/title-123456789
    var m = System.Text.RegularExpressions.Regex.Match(url,
        @"ebay\.com/itm/(?:[^/]+/)?(\d{10,13})",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    return m.Success ? m.Groups[1].Value : null;
}

static string ExtractPageText(string url, string html)
{
    // Strip scripts, styles, nav
    var clean = System.Text.RegularExpressions.Regex.Replace(html,
        @"<(script|style|nav|footer|header)[^>]*>[\s\S]*?</\1>", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    // Strip all remaining tags
    var text = System.Text.RegularExpressions.Regex.Replace(clean, "<[^>]+>", " ");
    // Collapse whitespace
    text = System.Text.RegularExpressions.Regex.Replace(text, @"\s{2,}", " ").Trim();
    // Truncate
    if (text.Length > 4000) text = text[..4000];

    // Extract meta tags separately
    var og = ExtractOgTags(html);
    var jsonLd = ExtractJsonLd(html);

    return $"URL: {url}\n\nMETA/OG DATA:\n{og}\n\nPRODUCT STRUCTURED DATA:\n{jsonLd}\n\nPAGE TEXT:\n{text}";
}

static string ExtractOgTags(string html)
{
    var sb = new System.Text.StringBuilder();
    var tags = new[] { "og:title","og:description","og:image","og:price:amount","og:brand",
                       "product:price:amount","twitter:title","twitter:description" };
    foreach (var tag in tags)
    {
        var m = System.Text.RegularExpressions.Regex.Match(html,
            $@"<meta[^>]+(?:property|name)=""{System.Text.RegularExpressions.Regex.Escape(tag)}""[^>]+content=""([^""]+)""",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (m.Success) sb.AppendLine($"{tag}: {m.Groups[1].Value}");
    }
    // Also grab <title>
    var titleM = System.Text.RegularExpressions.Regex.Match(html, @"<title[^>]*>([^<]+)</title>",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    if (titleM.Success) sb.AppendLine($"title: {titleM.Groups[1].Value.Trim()}");
    return sb.ToString();
}

static string ExtractJsonLd(string html)
{
    var m = System.Text.RegularExpressions.Regex.Match(html,
        @"<script[^>]+type=""application/ld\+json""[^>]*>([\s\S]*?)</script>",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    if (!m.Success) return "";
    var raw = m.Groups[1].Value.Trim();
    return raw.Length > 1500 ? raw[..1500] : raw;
}

static string ExtractPrimaryImageUrl(string html)
{
    // Try OG image first
    var m = System.Text.RegularExpressions.Regex.Match(html,
        @"<meta[^>]+(?:property|name)=""og:image""[^>]+content=""([^""]+)""",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    if (m.Success) return m.Groups[1].Value;
    // Try Twitter image
    m = System.Text.RegularExpressions.Regex.Match(html,
        @"<meta[^>]+name=""twitter:image""[^>]+content=""([^""]+)""",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    if (m.Success) return m.Groups[1].Value;
    return "";
}

static string ExtractLocalUrl(string pageText)
{
    var m = System.Text.RegularExpressions.Regex.Match(pageText, @"PRIMARY_IMAGE_LOCAL_URL: (/generated-photos/[^\s]+)");
    return m.Success ? m.Groups[1].Value : "";
}

// Scrapes Bing's image search results page for direct ("murl") image URLs.
// Bing renders these into the static HTML (unlike Google Images, which needs a
// headless browser), so a plain HttpClient GET is enough — no Playwright needed.
static async Task<List<string>> SearchProductImagesAsync(string query, int maxResults, IHttpClientFactory httpFactory)
{
    var urls = new List<string>();
    try
    {
        var http = httpFactory.CreateClient();
        http.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
        http.Timeout = TimeSpan.FromSeconds(10);

        var searchUrl = $"https://www.bing.com/images/search?q={Uri.EscapeDataString(query)}&form=HDRSC2";
        var html = await http.GetStringAsync(searchUrl);

        var skipPat = new System.Text.RegularExpressions.Regex("logo|icon|sprite|placeholder|avatar|favicon",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        foreach (System.Text.RegularExpressions.Match m in
            System.Text.RegularExpressions.Regex.Matches(html, "murl&quot;:&quot;(.*?)&quot;"))
        {
            var url = System.Net.WebUtility.HtmlDecode(m.Groups[1].Value);
            if (skipPat.IsMatch(url)) continue;
            if (!url.Contains(".jpg", StringComparison.OrdinalIgnoreCase) &&
                !url.Contains(".jpeg", StringComparison.OrdinalIgnoreCase) &&
                !url.Contains(".png", StringComparison.OrdinalIgnoreCase) &&
                !url.Contains(".webp", StringComparison.OrdinalIgnoreCase))
                continue;
            if (urls.Contains(url)) continue;

            urls.Add(url);
            if (urls.Count >= maxResults) break;
        }
    }
    catch { /* return whatever was found, possibly empty — caller falls back gracefully */ }
    return urls;
}

// Identifies an image's real format from its magic bytes rather than trusting the
// source URL's extension (scraped image URLs frequently have a misleading extension).
// Returns null if the bytes don't match a format Claude's vision API accepts.
static string? DetectImageMime(byte[] bytes)
{
    if (bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
        return "image/png";
    if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
        return "image/jpeg";
    if (bytes.Length >= 6 && bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x38)
        return "image/gif";
    if (bytes.Length >= 12 && bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46 &&
        bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50)
        return "image/webp";
    return null;
}

app.MapPost("/api/improve-seo", async (ImproveSeoRequest req, ClaudeService claude, ActionLog log, CredentialsStore store, LicenseService license) =>
{
    if (TrialGuard(store, license) is { } blocked) return blocked;
    if (string.IsNullOrWhiteSpace(req.Title) && string.IsNullOrWhiteSpace(req.Description))
        return Results.BadRequest(new { error = "Title or description is required." });
    try
    {
        var improved = await claude.ImproveSeoAsync(req);
        log.Add("Info", "SEO improvement complete", improved.Title);
        return Results.Ok(improved);
    }
    catch (Exception ex)
    {
        log.Add("Warning", "SEO improvement failed", ex.Message);
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapPost("/api/ai-modify", async (ModifyListingRequest req, ClaudeService claude, ActionLog log, CredentialsStore store, LicenseService license) =>
{
    if (TrialGuard(store, license) is { } blocked) return blocked;
    if (string.IsNullOrWhiteSpace(req.Instruction))
        return Results.BadRequest(new { error = "Instruction is required." });
    try
    {
        var modified = await claude.ModifyListingAsync(req);
        log.Add("Info", "AI modification applied", req.Instruction);
        return Results.Ok(modified);
    }
    catch (Exception ex)
    {
        log.Add("Warning", "AI modification failed", ex.Message);
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapPost("/api/quick-fill", async (QuickFillRequest req, ClaudeService claude, IHttpClientFactory httpFactory, IWebHostEnvironment env, ActionLog log, CredentialsStore store, LicenseService license) =>
{
    if (TrialGuard(store, license) is { } blocked) return blocked;
    if (string.IsNullOrWhiteSpace(req.ItemName))
        return Results.BadRequest(new { error = "Item name is required." });

    try
    {
        log.Add("Info", "Quick-fill from item name", req.ItemName);

        // Search for product photos online and download up to 3 good ones
        var candidateUrls = await SearchProductImagesAsync(req.ItemName, 8, httpFactory);
        if (candidateUrls.Count == 0)
        {
            // Retry once with punctuation stripped — colons/dashes in the typed item
            // name occasionally return zero results from the image search.
            var simplified = System.Text.RegularExpressions.Regex.Replace(req.ItemName, @"[^\w\s]", " ");
            simplified = System.Text.RegularExpressions.Regex.Replace(simplified, @"\s+", " ").Trim();
            if (!string.IsNullOrWhiteSpace(simplified) && simplified != req.ItemName)
                candidateUrls = await SearchProductImagesAsync(simplified, 8, httpFactory);
        }

        var photosDir = System.IO.Path.Combine(env.ContentRootPath, "generated-photos");
        System.IO.Directory.CreateDirectory(photosDir);

        var savedUrls = new List<string>();
        string? firstImageBase64 = null;
        string? firstImageMime = null;

        foreach (var candidateUrl in candidateUrls)
        {
            if (savedUrls.Count >= 3) break;
            try
            {
                var http = httpFactory.CreateClient();
                http.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 Chrome/120.0");
                http.Timeout = TimeSpan.FromSeconds(8);
                var imgBytes = await http.GetByteArrayAsync(candidateUrl);
                if (imgBytes.Length < 2000) continue; // skip tiny icons/tracking pixels

                // Sniff the real format from the file's magic bytes — the URL's extension
                // often lies (e.g. a ".jpg" URL that actually serves WebP), and Claude's
                // vision API rejects images whose declared media type doesn't match the bytes.
                var mime = DetectImageMime(imgBytes);
                if (mime is null) continue; // not a recognizable image format — skip it
                var ext  = mime switch { "image/png" => "png", "image/webp" => "webp", "image/gif" => "gif", _ => "jpg" };
                var file = $"search_{Guid.NewGuid():N}.{ext}";
                await System.IO.File.WriteAllBytesAsync(System.IO.Path.Combine(photosDir, file), imgBytes);
                savedUrls.Add($"/generated-photos/{file}");

                if (firstImageBase64 is null)
                {
                    firstImageBase64 = Convert.ToBase64String(imgBytes);
                    firstImageMime   = mime;
                }
            }
            catch { /* try the next candidate */ }
        }

        if (savedUrls.Count == 0)
            log.Add("Warning", "No product photos found online", req.ItemName);

        var listing = await claude.AnalyzeProductNameAsync(req.ItemName, firstImageBase64, firstImageMime);
        listing.ImageUrls = savedUrls;

        return Results.Ok(listing);
    }
    catch (Exception ex)
    {
        log.Add("Warning", "Quick-fill failed", ex.Message);
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapGet("/api/sold-comps", async (string q, EbayService ebay, TerapeakService terapeak, ActionLog log) =>
{
    if (string.IsNullOrWhiteSpace(q))
        return Results.BadRequest(new { error = "Query is required." });

    // Always hand back links to eBay's own research tools as a fallback — Marketplace Insights
    // (the real sold-comps API) requires a special eBay approval most developer accounts don't
    // have, and eBay's search page blocks scraping outright. These deep links always work because
    // they just open in the seller's own already-logged-in browser — no API access needed.
    var terapeakUrl = "https://www.ebay.com/sh/research?marketplace=EBAY-US&tabName=SOLD&dayRange=60" +
                       "&keywords=" + Uri.EscapeDataString(q);
    var fallbackUrl = "https://www.ebay.com/sch/i.html?_nkw=" + Uri.EscapeDataString(q) + "&LH_Sold=1&LH_Complete=1&_sop=13";

    // 1) Real Terapeak data, if the seller has connected their session (Settings > Terapeak)
    if (terapeak.IsConnected)
    {
        var scrape = await terapeak.ScrapeAsync(q);
        if (scrape.Status == "ok")
        {
            var parsed = ParseTerapeakBodyText(scrape.BodyText, q);
            if (parsed is not null)
                return Results.Ok(new { parsed.Query, parsed.Items, parsed.Count, parsed.Average, parsed.Median, parsed.Min, parsed.Max, terapeakUrl, fallbackUrl, source = "terapeak" });
        }
        else if (scrape.Status == "session_expired")
        {
            log.Add("Warning", "Terapeak session expired", "Reconnect in Settings.");
        }
    }

    // 2) Marketplace Insights API (works automatically if eBay ever approves the scope)
    try
    {
        var result = await ebay.SearchSoldCompsAsync(q);
        if (result.Count > 0)
            return Results.Ok(new { result.Query, result.Items, result.Count, result.Average, result.Median, result.Min, result.Max, terapeakUrl, fallbackUrl, source = "marketplace_insights" });
    }
    catch (Exception ex)
    {
        log.Add("Warning", "Sold comps lookup failed", ex.Message);
    }

    // 3) Links only
    return Results.Ok(new { query = q, items = Array.Empty<object>(), count = 0, average = 0, median = 0, min = 0, max = 0, terapeakUrl, fallbackUrl, source = "none" });
});

// Opportunity Finder — live auctions ending soon for a keyword, ranked by estimated profit
// against recent sold comps (Terapeak if connected, Marketplace Insights as fallback), and
// filtered by a minimum seller feedback score.
app.MapGet("/api/opportunities/search", async (string q, string? category, string? condition,
    decimal? minPrice, decimal? maxPrice, string? listingType, EbayService ebay, TerapeakService terapeak,
    TerapeakPriceCache cache, ActionLog log) =>
{
    if (string.IsNullOrWhiteSpace(q))
        return Results.BadRequest(new { error = "Query is required." });

    OpportunitySearchResult result;
    try
    {
        result = await FindOpportunitiesAsync(q, category, condition, minPrice, maxPrice, listingType ?? "AUCTION", ebay, terapeak, cache, log);
    }
    catch (Exception ex)
    {
        log.Add("Warning", "Opportunity search failed", ex.Message);
        return Results.BadRequest(new { error = ex.Message });
    }

    var opportunities = result.Items;
    var lowestPrice = opportunities.Count > 0 ? opportunities.Min(x => x.TotalCost) : (decimal?)null;
    var pricedItems  = opportunities.Where(x => x.ProfitPercent.HasValue).ToList();
    var avgProfitPercent = pricedItems.Count > 0 ? Math.Round(pricedItems.Average(x => x.ProfitPercent!.Value), 1) : (decimal?)null;
    var best = pricedItems.OrderByDescending(x => x.ProfitPercent!.Value).FirstOrDefault();
    var bestOpportunity = best is null ? null : new { best.Title, best.Url, ProfitPercent = best.ProfitPercent, best.IsVerified };

    return Results.Ok(new
    {
        query = result.Query, marketValue = result.MarketValue, averagePrice = result.AveragePrice,
        soldSource = result.SoldSource, listingType = result.ListingType,
        count = opportunities.Count, lowestPrice, sellThroughPercent = result.SellThroughPercent, avgProfitPercent, bestOpportunity,
        items = opportunities
    });
});

// ── Gem Radar (Pro) ──────────────────────────────────────────────────────────
// A passive, no-keyword-required feed: the background scanner below rotates through
// GemRadarCategories on its own and runs each through the exact same pipeline as a manual
// Opportunity Finder search, so Pro users open the app to a ranked list of flips instead of
// having to already know what to search for.
app.MapGet("/api/gems/feed", (LicenseService license, GemRadarStore store, TerapeakPriceCache cache) =>
{
    var lic = license.Current;
    if (!(lic.Valid && lic.Tier == "pro"))
        return Results.Json(new { error = "Gem Radar is a Pro feature." }, statusCode: 402);

    var snap = store.GetSnapshot();
    var gems = snap.Gems.OrderByDescending(g => g.Item.OpportunityScore ?? 0).ToList();
    return Results.Ok(new
    {
        lastScanUtc      = snap.LastScanUtc,
        lastScanCategory = snap.LastScanCategory,
        totalScans       = snap.TotalScans,
        cachedQueries    = cache.Count,
        count            = gems.Count,
        gems
    });
});

// Manual trigger, ungated — lets me (the assistant) verify the scan pipeline directly without
// waiting out the real ~12-minute rotation delay or needing a live Pro license during
// development. Same pattern as /api/terapeak/debug-scrape above. Real users only ever hit the
// Pro-gated /api/gems/feed.
app.MapPost("/api/gems/debug-scan-now", async (string category, EbayService ebay, TerapeakService terapeak,
    TerapeakPriceCache cache, ActionLog log, GemRadarStore store) =>
{
    if (!terapeak.IsConnected)
        return Results.BadRequest(new { error = "Terapeak not connected." });

    var result = await FindOpportunitiesAsync(category, null, null, null, null, "AUCTION", ebay, terapeak, cache, log, terapeakRecheckLimit: 3);
    var gems = result.Items.Where(x => x.IsVerified && x.OpportunityScore >= 55).ToList();
    store.RecordScan(category, gems);
    return Results.Ok(new { category, found = gems.Count, cachedQueries = cache.Count, gems });
});

app.MapPost("/api/terapeak/connect", (TerapeakService terapeak) =>
{
    var (started, message) = terapeak.StartLogin();
    return Results.Ok(new { started, message });
});

app.MapGet("/api/terapeak/status", (TerapeakService terapeak) =>
    Results.Ok(new { connected = terapeak.IsConnected, loginInProgress = terapeak.IsLoginInProgress }));

app.MapPost("/api/terapeak/disconnect", (TerapeakService terapeak) =>
{
    terapeak.Disconnect();
    return Results.Ok(new { connected = terapeak.IsConnected });
});

// Lets me (the assistant) inspect the real rendered page + selectors once a real session
// is connected, so ParseTerapeakBodyText can be tuned against the actual DOM/text.
app.MapGet("/api/terapeak/debug-scrape", async (string q, TerapeakService terapeak) =>
{
    var scrape = await terapeak.ScrapeAsync(q);
    return Results.Ok(scrape);
});

static SoldCompsResult? ParseTerapeakBodyText(string text, string query)
{
    // Text parse of the Seller Hub Research page, tuned against a real logged-in capture
    // (see /api/terapeak/debug-scrape). innerText renders each stat tile as "<value>\n<label>",
    // e.g. "$64.31\nAvg sold price", and each result-table row as "...\n$14.90\nFixed price\n...".
    var result = new SoldCompsResult { Query = query };

    var avgMatch = System.Text.RegularExpressions.Regex.Match(text,
        @"\$\s*([\d,]+\.\d{2})\s*\n\s*Avg sold price", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    if (avgMatch.Success && decimal.TryParse(avgMatch.Groups[1].Value.Replace(",", ""), out var avg))
        result.Average = avg;

    var rangeMatch = System.Text.RegularExpressions.Regex.Match(text,
        @"\$\s*([\d,]+\.\d{2})\s*-\s*\$\s*([\d,]+\.\d{2})\s*\n\s*Sold price range", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    if (rangeMatch.Success)
    {
        if (decimal.TryParse(rangeMatch.Groups[1].Value.Replace(",", ""), out var min)) result.Min = min;
        if (decimal.TryParse(rangeMatch.Groups[2].Value.Replace(",", ""), out var max)) result.Max = max;
    }

    // "72%\nSell-through" — Terapeak shows "-" instead of a percentage when it can't compute
    // one for this query, which TryParse below just leaves as null (no data) rather than 0%.
    var sellThroughMatch = System.Text.RegularExpressions.Regex.Match(text,
        @"([\d.]+)%\s*\n\s*Sell-through", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    if (sellThroughMatch.Success && decimal.TryParse(sellThroughMatch.Groups[1].Value, out var sellThrough))
        result.SellThroughPercent = sellThrough;

    var shippingMatch = System.Text.RegularExpressions.Regex.Match(text,
        @"\$\s*([\d,]+\.\d{2})\s*\n\s*Avg shipping", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    if (shippingMatch.Success && decimal.TryParse(shippingMatch.Groups[1].Value.Replace(",", ""), out var avgShip))
        result.AvgShipping = avgShip;

    // Each matching product row shows its own avg sold price — collect them into a real
    // per-listing distribution so we get an actual median (the page-level summary above
    // only gives a single blended average, which the opportunity-profit calc doesn't want).
    var rowPrices = System.Text.RegularExpressions.Regex.Matches(text,
            @"\$\s*([\d,]+\.\d{2})\s*\n\s*(?:Fixed price|Auction|Best Offer)", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
        .Select(m => decimal.TryParse(m.Groups[1].Value.Replace(",", ""), out var p) ? p : (decimal?)null)
        .Where(p => p.HasValue)
        .Select(p => p!.Value)
        .OrderBy(p => p)
        .ToList();

    if (rowPrices.Count > 0)
    {
        result.Count  = rowPrices.Count;
        result.Median = rowPrices.Count % 2 == 1
            ? rowPrices[rowPrices.Count / 2]
            : (rowPrices[rowPrices.Count / 2 - 1] + rowPrices[rowPrices.Count / 2]) / 2m;
    }

    return result.Count > 0 || result.Average > 0 ? result : null;
}

// The whole opportunity-search-and-score pipeline, shared by the interactive
// /api/opportunities/search endpoint and the Gem Radar background scanner so a keyword search
// and a passive category scan run identically instead of drifting apart over time.
static async Task<OpportunitySearchResult> FindOpportunitiesAsync(
    string q, string? category, string? condition, decimal? minPrice, decimal? maxPrice, string listingType,
    EbayService ebay, TerapeakService terapeak, TerapeakPriceCache cache, ActionLog log, int terapeakRecheckLimit = 5)
{
    // Price the same combined keyword+category search that's actually being run below.
    var priceQuery = string.IsNullOrWhiteSpace(category) ? q : $"{q} {category}";

    // Estimate current broad market value from sold comps, same sources /api/sold-comps uses.
    // This is a rough, blended estimate across everything matching the search term — good
    // enough to rank 50 results, not precise enough to trust for any one specific item (see
    // the per-item Terapeak re-check below, which replaces it for the top few candidates).
    // Goes through the shared cache first (see GetOrScrapePricingAsync) so a query any other
    // search already paid for — manual or Gem Radar — doesn't cost a second real scrape.
    decimal marketValue = 0;
    decimal averagePrice = 0;
    decimal avgShipping = 0;
    decimal? sellThroughPercent = null;
    var soldSource = "none";
    var broadPricing = await GetOrScrapePricingAsync(priceQuery, terapeak, cache, log);
    if (broadPricing is not null)
    {
        marketValue = broadPricing.Median > 0 ? broadPricing.Median : broadPricing.Average;
        averagePrice = broadPricing.Average;
        avgShipping = broadPricing.AvgShipping;
        sellThroughPercent = broadPricing.SellThroughPercent;
        soldSource = "terapeak";
    }
    if (marketValue == 0)
    {
        try
        {
            var soldResult = await ebay.SearchSoldCompsAsync(priceQuery);
            if (soldResult.Count > 0)
            {
                marketValue = soldResult.Median > 0 ? soldResult.Median : soldResult.Average;
                averagePrice = soldResult.Average;
                soldSource = "marketplace_insights";
            }
        }
        catch (Exception ex)
        {
            log.Add("Warning", "Opportunity sold-comp lookup failed", ex.Message);
        }
    }
    // Compare against buyer's real total cost (sold price + shipping), not just sold price —
    // Terapeak reports these as separate stats.
    var totalMarketValue = marketValue > 0 ? marketValue + avgShipping : 0;

    var listings = await ebay.SearchEndingSoonAsync(q, 0, 50, category, condition, minPrice, maxPrice, listingType);

    // "Newly listed" has no real listing-start timestamp available from the Browse API's
    // item_summary — rank within the API's own newlyListed-sorted order is the only proxy,
    // so it's only meaningful when that's actually the sort in effect (i.e. not AUCTION mode).
    var sortedByRecency = listingType != "AUCTION";

    var opportunities = listings
        .Select((item, idx) =>
        {
            var totalCost = item.Price + item.ShippingCost;
            decimal? pct = totalMarketValue > 0 && totalCost > 0
                ? Math.Round((totalMarketValue - totalCost) / totalCost * 100m, 1)
                : (decimal?)null;
            return new OpportunityListItem
            {
                Title                = item.Title,
                Price                = item.Price,
                ShippingCost         = item.ShippingCost,
                TotalCost            = totalCost,
                Url                  = item.Url,
                ImageUrl             = item.ImageUrl,
                EndDate              = item.EndDate,
                SellerUsername       = item.SellerUsername,
                SellerFeedbackScore  = item.SellerFeedbackScore,
                BuyingOption         = item.BuyingOption,
                BidCount             = item.BidCount,
                // Broad, search-wide estimate — same for every item until the per-item Terapeak
                // re-check below replaces it for the top few candidates with a real per-item price.
                MarketAverage        = averagePrice > 0 ? averagePrice : (decimal?)null,
                EstimatedResalePrice = totalMarketValue > 0 ? totalMarketValue : (decimal?)null,
                EstimatedProfit      = totalMarketValue > 0 && totalCost > 0 ? Math.Round(totalMarketValue - totalCost, 2) : (decimal?)null,
                ProfitPercent        = pct,
                // Cheap heuristics, not AI/vision analysis — catch obvious cases only.
                IsUnderpriced      = pct is > 15,
                IsHighProfitMargin = pct is > 50,
                IsEndingSoon       = item.BuyingOption == "AUCTION" && item.EndDate.HasValue && item.EndDate.Value <= DateTime.UtcNow.AddHours(6),
                IsHighDemand       = item.BuyingOption == "AUCTION" && item.BidCount >= 5,
                IsNewlyListed      = sortedByRecency && idx < 10,
                HasPoorTitle       = HasPoorTitle(item.Title),
                HasMisspelledTitle = HasMisspelledTitle(item.Title),
                HasPoorPhoto       = string.IsNullOrWhiteSpace(item.ImageUrl)
            };
        })
        .ToList();

    // Re-check candidates against comps for THAT specific item (keywords pulled from its own
    // title), not the broad search-wide estimate — a single search term like "postcard" or
    // "graphics card" can span a $0.01–$8,000 range, so the blended market value above is too
    // noisy to trust for any one listing. Real Terapeak scrapes are the scarce resource here,
    // so they're rationed to terapeakRecheckLimit per search — but a cache hit is free and
    // doesn't touch that budget, so EVERY priced listing gets checked against the cache first,
    // not just the top few. The longer this app runs (manual searches + the Gem Radar scanner
    // both feed the same cache), the more listings get verified pricing for zero extra scrapes.
    if (terapeakRecheckLimit > 0)
    {
        var realScrapesUsed = 0;
        var candidates = opportunities
            .Where(x => x.ProfitPercent.HasValue)
            .OrderByDescending(x => x.ProfitPercent!.Value);

        foreach (var candidate in candidates)
        {
            var keywords = ExtractKeywords(candidate.Title);
            if (string.IsNullOrWhiteSpace(keywords)) continue;

            var isCached = cache.TryGet(keywords, TimeSpan.FromHours(48)) is not null;
            if (!isCached)
            {
                if (realScrapesUsed >= terapeakRecheckLimit) continue;
                realScrapesUsed++;
            }

            var itemParsed = await GetOrScrapePricingAsync(keywords, terapeak, cache, log);
            if (itemParsed is null) continue;

            var itemMarketValue = (itemParsed.Median > 0 ? itemParsed.Median : itemParsed.Average) + itemParsed.AvgShipping;
            if (itemMarketValue <= 0 || candidate.TotalCost <= 0) continue;

            candidate.MarketAverage        = itemParsed.Average > 0 ? itemParsed.Average : candidate.MarketAverage;
            candidate.EstimatedResalePrice = itemMarketValue;
            candidate.EstimatedProfit      = Math.Round(itemMarketValue - candidate.TotalCost, 2);
            candidate.ProfitPercent        = Math.Round((itemMarketValue - candidate.TotalCost) / candidate.TotalCost * 100m, 1);
            candidate.IsVerified           = true;
            candidate.IsUnderpriced        = candidate.ProfitPercent is > 15;
            candidate.IsHighProfitMargin   = candidate.ProfitPercent is > 50;
        }
    }

    foreach (var item in opportunities)
        item.OpportunityScore = ComputeOpportunityScore(item);

    opportunities = opportunities.OrderByDescending(x => x.ProfitPercent ?? -999m).ToList();

    return new OpportunitySearchResult
    {
        Query = q, MarketValue = marketValue, AveragePrice = averagePrice, SoldSource = soldSource,
        ListingType = listingType, SellThroughPercent = sellThroughPercent, Items = opportunities
    };
}

// Single entry point for "get me sold-comp pricing for this query" — checks the persistent
// cache first (free, no eBay traffic) and only falls through to a real Terapeak scrape on a
// miss, caching whatever it finds for the next caller. This is what lets the interactive search
// and the Gem Radar background scanner share one scrape budget instead of each burning its own.
static async Task<SoldCompsResult?> GetOrScrapePricingAsync(
    string query, TerapeakService terapeak, TerapeakPriceCache cache, ActionLog log, TimeSpan? maxAge = null)
{
    var age = maxAge ?? TimeSpan.FromHours(48);
    var cached = cache.TryGet(query, age);
    if (cached is not null)
        return new SoldCompsResult
        {
            Query = query, Average = cached.Average, Median = cached.Median,
            AvgShipping = cached.AvgShipping, SellThroughPercent = cached.SellThroughPercent
        };

    if (!terapeak.IsConnected) return null;

    try
    {
        var scrape = await terapeak.ScrapeAsync(query);
        if (scrape.Status != "ok") return null;

        var parsed = ParseTerapeakBodyText(scrape.BodyText, query);
        if (parsed is not null)
            cache.Set(query, parsed.Average, parsed.Median, parsed.AvgShipping, parsed.SellThroughPercent);
        return parsed;
    }
    catch (Exception ex)
    {
        log.Add("Warning", "Terapeak scrape failed", ex.Message);
        return null;
    }
}

// Single 0-100 number blending everything the Opportunity Finder knows about a listing, so
// results can be ranked/skimmed at a glance instead of mentally juggling five separate signals.
// Profit dominates (it's the whole point), verified Terapeak pricing counts more than the
// broad estimate, and demand/trust/quality act as smaller nudges up or down.
static int? ComputeOpportunityScore(OpportunityListItem item)
{
    if (item.ProfitPercent is not decimal pct) return null;

    var profitScore     = Math.Min(60.0, Math.Max(0.0, (double)pct * 0.6));
    var confidenceScore = item.IsVerified ? 15.0 : 5.0;

    var demandScore = 0.0;
    if (item.IsHighDemand) demandScore += 8;
    if (item.IsEndingSoon) demandScore += 7;

    // Log-scaled so a seller with 5,000 feedback isn't scored 100x higher than one with 50.
    var trustScore = Math.Min(10.0, Math.Log10(item.SellerFeedbackScore + 1) * 3);

    var score = profitScore + confidenceScore + demandScore + trustScore;
    if (item.HasPoorTitle) score -= 5;
    if (item.HasPoorPhoto) score -= 5;

    return (int)Math.Round(Math.Clamp(score, 0, 100));
}

// Strips generic filler words from a listing title so it can be used as a Terapeak search term
// specific to that one item, instead of the broad (often single-word) original search query.
// Cheap heuristic, not NLP — keeps title word order (brand/model usually comes first) and just
// drops connectors/marketing fluff, capped to a handful of words so the query isn't over-narrow.
static string ExtractKeywords(string title, int maxWords = 3)
{
    string[] stopwords =
    [
        "and", "or", "for", "with", "of", "in", "on", "to", "from", "by", "the", "a", "an",
        "free", "shipping", "fast", "genuine", "authentic", "official", "brand", "nib", "nwt", "oem"
    ];
    var words = System.Text.RegularExpressions.Regex.Matches(title, @"[A-Za-z0-9]+")
        .Select(m => m.Value)
        .Where(w => w.Length > 1 && !stopwords.Contains(w.ToLowerInvariant()))
        .ToList();

    // A model/part number (e.g. "660" in "EVGA GeForce GTX 660") is the strongest signal for
    // narrowing a Terapeak search to the right item. Truncating at a fixed word count can cut
    // it off, silently turning "EVGA GeForce GTX 660" into "EVGA GeForce" — which prices
    // against every EVGA GeForce card ever sold instead of this specific low-end model.
    // Extend (capped) far enough to include the first digit-bearing token if one exists.
    var take = maxWords;
    var firstDigitIdx = words.FindIndex(w => w.Any(char.IsDigit));
    if (firstDigitIdx >= 0)
        take = Math.Min(Math.Max(take, firstDigitIdx + 1), 8);

    return string.Join(' ', words.Take(Math.Min(take, words.Count)));
}

// Cheap heuristics for the Opportunity Finder's "Poor titles" filter — not AI/NLP, just the
// obvious cases: too short, too few words, or shouty all-caps spam.
static bool HasPoorTitle(string title)
{
    var t = title.Trim();
    if (t.Length < 20) return true;
    if (t.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length < 4) return true;
    if (t.Length > 10 && t == t.ToUpperInvariant() && t.Any(char.IsLetter)) return true;
    return false;
}

// Cheap heuristic for the "Misspelled titles" filter — a small common-typo word list plus a
// repeated-letter-run check ("Xboxxx"), not a real dictionary/spellcheck.
static bool HasMisspelledTitle(string title)
{
    string[] commonMisspellings =
    [
        "recieve", "seperate", "definately", "occured", "untill", "wich", "beleive",
        "acessories", "accesories", "orignal", "genuion", "authentc", "excelent",
        "perfet", "conditon", "brandnew", "guarenteed", "warrenty", "shiping",
        "wireles", "controler", "consol", "protable", "compatable"
    ];
    var words = System.Text.RegularExpressions.Regex.Split(title.ToLowerInvariant(), @"[^a-z]+");
    if (words.Any(w => commonMisspellings.Contains(w))) return true;
    // Repeated-letter run (e.g. "Xboxxx") rarely happens in real words — but exclude i/v/x/l,
    // since "III", "XXL", "XXXL" etc. (Roman numerals, size labels) are common and legitimate.
    return System.Text.RegularExpressions.Regex.IsMatch(title, @"([^ivxlIVXL\s])\1{2,}");
}

app.MapPost("/api/generate-photos", async (GeneratePhotosRequest req, ImageGenerationService imgGen, ActionLog log, CredentialsStore store, LicenseService license) =>
{
    if (TrialGuard(store, license) is { } blocked) return blocked;
    if (string.IsNullOrWhiteSpace(req.Title))
        return Results.BadRequest(new { error = "Title is required to generate product photos." });
    try
    {
        // Use the product photo as img2img reference only when Claude confirms it's a clean product shot
        var refImage = req.ImageType == "product_photo" && !string.IsNullOrWhiteSpace(req.ImageBase64)
            ? req.ImageBase64 : null;
        var urls = await imgGen.GenerateProductPhotosAsync(req.Title, req.Description,
            req.VisualDescription, refImage, string.IsNullOrEmpty(refImage) ? null : req.MimeType);
        return Results.Ok(new { urls });
    }
    catch (Exception ex)
    {
        log.Add("Warning", "Image generation failed", ex.Message);
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapGet("/api/image-gen/test", async (ImageGenerationService imgGen) =>
{
    var (online, message) = await imgGen.TestLocalServerAsync();
    return Results.Ok(new { online, message });
});

app.MapGet("/api/image-gen/test-endpoint", async (string endpoint, string backend, ImageGenerationService imgGen) =>
{
    var (online, message) = await imgGen.TestEndpointAsync(endpoint, backend);
    return Results.Ok(new { online, message });
});

app.MapGet("/api/image-gen/detect", async (ImageGenerationService imgGen) =>
{
    var result = await imgGen.DetectLocalServersAsync();
    return Results.Ok(result);
});

app.MapGet("/api/image-gen/comfyui-models", async (string endpoint, ImageGenerationService imgGen) =>
{
    var models = await imgGen.GetComfyUiModelsAsync(endpoint);
    return Results.Ok(new { models });
});

app.MapGet("/api/image-gen/mode", (CredentialsStore store) =>
{
    var c = store.Get();
    return Results.Ok(new { mode = c.ImageGenMode ?? "disabled" });
});

// ── eBay OAuth ────────────────────────────────────────────────────
app.MapGet("/api/ebay/auth-url", (EbayService ebay) =>
    Results.Ok(new { url = ebay.GetAuthorizationUrl() }));

// Legacy direct callback (sandbox / local dev only)
app.MapGet("/api/ebay/callback", async (string code, EbayService ebay, CredentialsStore store, HttpContext ctx) =>
{
    var token = await ebay.ExchangeCodeForTokenResultAsync(code);
    store.SaveOAuthTokensFull(token.AccessToken, token.RefreshToken, token.ExpiresIn, token.RefreshTokenExpiresIn, token.TokenType);
    ctx.Response.Redirect("/");
});

// Server-relay finish: eBay → inglisting.com/api/ebay/callback (PHP) → here
// PHP already exchanged the code; this endpoint fetches the tokens from the server pickup endpoint.
app.MapGet("/api/ebay/finish", async (string session, string pickup, EbayService ebay, CredentialsStore store, ActionLog log, IHttpClientFactory httpFactory, HttpContext ctx) =>
{
    // Validate session matches what we generated (CSRF check)
    if (ebay.PendingOAuthSession != session)
    {
        log.Add("Warning", "OAuth finish: state mismatch", $"Expected {ebay.PendingOAuthSession}, got {session}");
        ctx.Response.Redirect("/?ebay_error=state_mismatch");
        return;
    }

    try
    {
        var client = httpFactory.CreateClient();
        var pickupUrl = $"https://inglisting.com/api/ebay/pickup/?session={Uri.EscapeDataString(session)}&pickup={Uri.EscapeDataString(pickup)}";
        var res = await client.GetAsync(pickupUrl);
        var body = await res.Content.ReadAsStringAsync();

        if (!res.IsSuccessStatusCode)
        {
            log.Add("Warning", "OAuth pickup failed", body);
            ctx.Response.Redirect("/?ebay_error=pickup_failed");
            return;
        }

        var tokens = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, System.Text.Json.JsonElement>>(body)!;
        var accessToken   = tokens.GetValueOrDefault("access_token").GetString()   ?? "";
        var refreshToken  = tokens.GetValueOrDefault("refresh_token").GetString()  ?? "";
        var expiresIn     = tokens.GetValueOrDefault("expires_in").TryGetInt32(out var ei) ? ei : 7200;
        var refreshExpiry = tokens.GetValueOrDefault("refresh_token_expires_in").TryGetInt32(out var ri) ? ri : 47304000;
        var tokenType     = tokens.GetValueOrDefault("token_type").GetString()     ?? "User Access Token";

        store.SaveOAuthTokensFull(accessToken, refreshToken, expiresIn, refreshExpiry, tokenType);
        log.Add("Info", "eBay OAuth connected via server relay", "Tokens saved successfully.");
        ctx.Response.Redirect("/?ebay_connected=1");
    }
    catch (Exception ex)
    {
        log.Add("Error", "OAuth finish exception", ex.Message);
        ctx.Response.Redirect("/?ebay_error=exception");
    }
});

app.MapPost("/api/ebay/token", async (EbayAuthRequest req, EbayService ebay, CredentialsStore store) =>
{
    var token = await ebay.ExchangeCodeForTokenResultAsync(req.Code);
    store.SaveOAuthTokensFull(token.AccessToken, token.RefreshToken, token.ExpiresIn, token.RefreshTokenExpiresIn, token.TokenType);
    return Results.Ok(new { hasToken = !string.IsNullOrWhiteSpace(token.AccessToken), hasRefreshToken = !string.IsNullOrWhiteSpace(token.RefreshToken) });
});

app.MapPost("/api/ebay/exchange-redirect-url", async (EbayOAuthRedirectRequest req, EbayService ebay, CredentialsStore store, ActionLog log) =>
{
    if (string.IsNullOrWhiteSpace(req.RedirectUrl))
        return Results.BadRequest("Paste the full eBay OAuth redirect URL.");

    try
    {
        var result = await ebay.ExchangeProductionRedirectUrlAsync(req.RedirectUrl);
        store.SaveOAuthTokensFull(result.Token, result.RefreshToken, result.ExpiresIn, result.RefreshTokenExpiresIn, result.TokenType);
        log.Add("Info", "Production eBay OAuth connected", $"Accepted URL: {result.AcceptedUrl}; Redirect URI: {result.RedirectUri}; State: {result.State}");

        return Results.Ok(new
        {
            hasToken = true,
            hasRefreshToken = !string.IsNullOrWhiteSpace(result.RefreshToken),
            acceptedUrl = result.AcceptedUrl,
            redirectUri = result.RedirectUri,
            state = result.State,
            message = "Production eBay OAuth token saved locally."
        });
    }
    catch (Exception ex)
    {
        log.Add("Warning", "Production OAuth exchange failed", ex.Message);
        return Results.BadRequest(ex.Message);
    }
});

app.MapGet("/api/ebay/policies", async (EbayService ebay, ActionLog log) =>
{
    try
    {
        var result = await ebay.GetBusinessPoliciesAsync();
        return Results.Ok(result);
    }
    catch (Exception ex)
    {
        log.Add("Warning", "Business policy load failed", ex.Message);
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapGet("/api/ebay/token-status", (CredentialsStore store) =>
    Results.Ok(new
    {
        hasToken = !string.IsNullOrWhiteSpace(store.GetUserToken()),
        hasRefreshToken = !string.IsNullOrWhiteSpace(store.GetRefreshToken())
    }));

app.MapPost("/api/ebay/disconnect", (CredentialsStore store) =>
{
    store.ClearEbayTokens();
    return Results.Ok();
});

// ── Listings ──────────────────────────────────────────────────────
app.MapGet("/api/ebay/listings", async (EbayService ebay, ActionLog log) =>
{
    try
    {
        var listings = await ebay.GetListingsAsync();
        log.Add("Info", $"Import complete: {listings.Count} listing(s)", listings.Count == 0
            ? "Zero active listings found via Inventory API. See earlier log entries for details."
            : $"First: {listings.FirstOrDefault()?.Title ?? "(no title)"}");
        return Results.Ok(listings);
    }
    catch (Exception ex)
    {
        log.Add("Warning", "Import listings endpoint failed", ex.Message);
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapGet("/api/local-db/status", (ListingDatabase db) => Results.Ok(db.GetStatus()));

app.MapGet("/api/local-listings/placeholder", () => Results.Ok(PlaceholderListings.Get()));

app.MapGet("/api/photos/default-folders", (PhotoLibrary photos) => Results.Ok(photos.GetDefaultFolders()));

app.MapPost("/api/photos/fetch-url", async (FetchPhotoUrlRequest req, IHttpClientFactory http, IWebHostEnvironment env, ActionLog log) =>
{
    if (string.IsNullOrWhiteSpace(req.Url)) return Results.BadRequest("URL required");
    try
    {
        using var client = http.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(15);
        client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0");
        var bytes = await client.GetByteArrayAsync(req.Url);
        var ext = req.Url.Contains(".png", StringComparison.OrdinalIgnoreCase) ? "png" : "jpg";
        var photosDir = Path.Combine(env.ContentRootPath, "generated-photos");
        Directory.CreateDirectory(photosDir);
        var filename = $"fetched_{Guid.NewGuid():N}.{ext}";
        await File.WriteAllBytesAsync(Path.Combine(photosDir, filename), bytes);
        var url = $"/generated-photos/{filename}";
        log.Add("Info", "Product photo fetched", req.Url[..Math.Min(80, req.Url.Length)]);
        return Results.Ok(new { url });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapPost("/api/photos/save-uploaded", async (SaveUploadedPhotoRequest req, IWebHostEnvironment env, ActionLog log) =>
{
    if (string.IsNullOrWhiteSpace(req.ImageBase64))
        return Results.BadRequest("ImageBase64 is required");

    var photosDir = Path.Combine(env.ContentRootPath, "generated-photos");
    Directory.CreateDirectory(photosDir);

    var ext = (req.MimeType ?? "").Contains("png") ? "png" : "jpg";
    var filename = $"upload_{Guid.NewGuid():N}.{ext}";
    await File.WriteAllBytesAsync(Path.Combine(photosDir, filename), Convert.FromBase64String(req.ImageBase64));

    var url = $"/generated-photos/{filename}";
    log.Add("Info", "Uploaded product photo saved", filename);
    return Results.Ok(new { url });
});

app.MapPost("/api/photos/remove-bg", async (RemoveBgRequest req, IWebHostEnvironment env, ActionLog log) =>
{
    if (string.IsNullOrWhiteSpace(req.ImageBase64))
        return Results.BadRequest("ImageBase64 is required");

    var photosDir = Path.Combine(env.ContentRootPath, "generated-photos");
    Directory.CreateDirectory(photosDir);

    var ext        = (req.MimeType ?? "").Contains("png") ? "png" : "jpg";
    var inputFile  = Path.Combine(Path.GetTempPath(), $"rembg_in_{Guid.NewGuid():N}.{ext}");
    var outputFile = Path.Combine(photosDir, $"rembg_{Guid.NewGuid():N}.png");
    var scriptFile = Path.Combine(Path.GetTempPath(), $"rembg_script_{Guid.NewGuid():N}.py");

    try
    {
        await File.WriteAllBytesAsync(inputFile, Convert.FromBase64String(req.ImageBase64));
        await File.WriteAllTextAsync(scriptFile, """
import sys
from rembg import remove
from PIL import Image
import numpy as np
from scipy import ndimage

img = Image.open(sys.argv[1]).convert('RGBA')
cutout = remove(img).convert('RGBA')

# Drop only tiny stray artifacts — keep all components above 0.5% of total pixels
arr = np.array(cutout)
alpha = arr[:, :, 3]
mask = alpha > 10
total_px = mask.size
min_keep = total_px * 0.005  # anything smaller than 0.5% of image is an artifact

labeled, num_features = ndimage.label(mask)
if num_features > 1:
    sizes = np.array([(labeled == i + 1).sum() for i in range(num_features)])
    keep_mask = np.zeros_like(mask)
    for i, sz in enumerate(sizes):
        if sz >= min_keep:
            keep_mask |= (labeled == i + 1)
    rows_idx, cols_idx = np.where(keep_mask)
else:
    rows_idx, cols_idx = np.where(mask)

if len(rows_idx) > 0:
    pad = 5
    ih, iw = mask.shape
    rmin = max(0, int(rows_idx.min()) - pad)
    rmax = min(ih - 1, int(rows_idx.max()) + pad)
    cmin = max(0, int(cols_idx.min()) - pad)
    cmax = min(iw - 1, int(cols_idx.max()) + pad)
    cutout = cutout.crop((cmin, rmin, cmax + 1, rmax + 1))
else:
    bbox = cutout.getbbox()
    if bbox:
        cutout = cutout.crop(bbox)

# Scale product to fill 88% of a 1000x1000 white canvas
canvas = 1000
product_size = int(canvas * 0.88)
w, h = cutout.size
scale = product_size / max(w, h)
new_w = max(1, int(w * scale))
new_h = max(1, int(h * scale))
cutout = cutout.resize((new_w, new_h), Image.LANCZOS)

# Paste centered on white background
result = Image.new('RGB', (canvas, canvas), (255, 255, 255))
x = (canvas - new_w) // 2
y = (canvas - new_h) // 2
result.paste(cutout, (x, y), cutout)
result.save(sys.argv[2], 'PNG')
""");

        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName               = "python",
            ArgumentList           = { scriptFile, inputFile, outputFile },
            RedirectStandardError  = true,
            UseShellExecute        = false,
            CreateNoWindow         = true
        };

        using var proc   = System.Diagnostics.Process.Start(psi)!;
        var stderr       = await proc.StandardError.ReadToEndAsync();
        await proc.WaitForExitAsync();

        if (proc.ExitCode != 0 || !File.Exists(outputFile))
            throw new Exception($"rembg failed (exit {proc.ExitCode}): {stderr[..Math.Min(300, stderr.Length)]}");

        var url = $"/generated-photos/{Path.GetFileName(outputFile)}";
        log.Add("Info", "Background removed", Path.GetFileName(outputFile));
        return Results.Ok(new { url });
    }
    catch (Exception ex)
    {
        log.Add("Warning", "Background removal failed", ex.Message);
        return Results.BadRequest(new { error = ex.Message });
    }
    finally
    {
        if (File.Exists(inputFile))  File.Delete(inputFile);
        if (File.Exists(scriptFile)) File.Delete(scriptFile);
    }
});

app.MapPost("/api/bulk-import/extract-links", async (AnalyzeUrlRequest req, IHttpClientFactory http) =>
{
    if (string.IsNullOrWhiteSpace(req.Url)) return Results.BadRequest("URL required");
    try
    {
        var client = http.CreateClient();
        client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/120 Safari/537.36");
        var html = await client.GetStringAsync(req.Url);

        // Extract product links — works for Shopify (/products/), eBay, and generic /product/ patterns
        var matches = System.Text.RegularExpressions.Regex.Matches(
            html, @"href=""(/(?:products|product|listing|item|p)/[^""?#]+)""",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        var baseUri = new Uri(req.Url);
        var links = matches
            .Select(m => new Uri(baseUri, m.Groups[1].Value).ToString())
            .Distinct()
            .Where(u => !u.Contains("/collections/") && !u.Contains("/categories/"))
            .Take(50)
            .ToList();

        return Results.Ok(new { links });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapGet("/api/ebay/category-children", async (string? id, EbayService ebay) =>
{
    var children = await ebay.GetCategoryChildrenAsync(id ?? "0");
    return Results.Ok(children);
});

app.MapGet("/api/ebay/category-suggestions", async (string q, EbayService ebay, ActionLog log) =>
{
    if (string.IsNullOrWhiteSpace(q) || q.Length < 2)
        return Results.Ok(Array.Empty<object>());
    try
    {
        var suggestions = await ebay.GetCategorySuggestionsAsync(q);
        return Results.Ok(suggestions);
    }
    catch (Exception ex)
    {
        log.Add("Warning", "Category suggestions failed", ex.Message);
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapPost("/api/ebay/upload-picture", async (RemoveBgRequest req, EbayService ebay, ActionLog log) =>
{
    if (string.IsNullOrWhiteSpace(req.ImageBase64))
        return Results.BadRequest(new { error = "ImageBase64 is required" });
    try
    {
        var url = await ebay.UploadPictureToEpsAsync(req.ImageBase64, req.MimeType ?? "image/jpeg");
        log.Add("Info", "Picture uploaded to eBay EPS", url[..Math.Min(80, url.Length)]);
        return Results.Ok(new { url });
    }
    catch (Exception ex)
    {
        log.Add("Warning", "eBay picture upload failed", ex.Message);
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapGet("/api/logs/recent", (ActionLog log) => Results.Ok(log.Recent()));


// ── Local Drafts ──────────────────────────────────────────────────
app.MapGet("/api/local-drafts/ensure-folder", (DraftStore drafts) =>
    Results.Ok(new { path = drafts.EnsureFolder() }));

app.MapGet("/api/local-drafts/list", (DraftStore drafts) => Results.Ok(drafts.ListDrafts()));

app.MapPost("/api/local-drafts/save", (DraftFile draft, DraftStore drafts, ActionLog log) =>
{
    var filename = drafts.SaveDraft(draft);
    log.Add("Info", "Draft saved locally", $"{filename}");
    return Results.Ok(new { filename });
});

app.MapGet("/api/local-drafts/load/{filename}", (string filename, DraftStore drafts) =>
{
    var draft = drafts.LoadDraft(filename);
    return draft != null ? Results.Ok(draft) : Results.NotFound();
});

app.MapDelete("/api/local-drafts/delete/{filename}", (string filename, DraftStore drafts, ActionLog log) =>
{
    drafts.DeleteDraft(filename);
    log.Add("Info", "Draft deleted", filename);
    return Results.Ok();
});

app.MapPost("/api/listing/seller-hub-draft", async (PostListingRequest req, EbayService ebay, ActionLog log) =>
{
    var titlePreview = (req.Title ?? "").Trim();
    if (titlePreview.Length > 60) titlePreview = titlePreview[..60] + "…";
    log.Add("Info", "Seller Hub Draft: endpoint called", $"Title: {titlePreview}");

    try
    {
        var result = await ebay.CreateSellerHubDraftAsync(req);
        log.Add("Info", "Seller Hub Draft: succeeded", $"DraftId: {result.DraftId}; URL: {result.SellerHubUrl}");
        return Results.Ok(new { ok = true, draftId = result.DraftId, sellerHubUrl = result.SellerHubUrl });
    }
    catch (Exception ex)
    {
        var shortError = ex.Message.Length > 300 ? ex.Message[..300] + "…" : ex.Message;
        log.Add("Warning", "Seller Hub Draft: failed", shortError);
        return Results.Json(new { ok = false, error = shortError, details = ex.Message }, statusCode: 400);
    }
});

app.MapPost("/api/listing/post", async (PostListingRequest req, EbayService ebay, ActionLog log) =>
{
    var titlePreview = (req.Title ?? "").Trim();
    if (titlePreview.Length > 60) titlePreview = titlePreview[..60] + "…";
    log.Add("Info", "Create Draft: endpoint called", $"Title: {titlePreview}; CategoryId: {req.CategoryId}; Price: {req.Price:F2}");

    try
    {
        var offerId = await ebay.CreateListingAsync(req, req.EbayToken);
        log.Add("Info", "Create Draft: succeeded", $"OfferId: {offerId}");
        return Results.Ok(new
        {
            ok = true,
            offerId,
            listingId = "",
            status = "Draft",
            message = "Draft offer created. It has not been published."
        });
    }
    catch (Exception ex)
    {
        var shortError = ex.Message.Length > 300 ? ex.Message[..300] + "…" : ex.Message;
        log.Add("Warning", "Create Draft: failed", shortError);
        return Results.Json(
            new { ok = false, error = shortError, details = ex.Message, where = "CreateDraft" },
            statusCode: 400);
    }
});

app.MapPost("/api/listing/publish", async (PostListingRequest req, EbayService ebay, ActionLog log, CredentialsStore store, LicenseService license) =>
{
    if (TrialGuard(store, license) is { } blocked) return blocked;
    try
    {
        var result = await ebay.PublishListingAsync(req);
        var listingUrl = !string.IsNullOrEmpty(result.ListingId)
            ? $"https://www.ebay.com/itm/{result.ListingId}"
            : "";
        log.Add("Info", "eBay listing published live", $"Listing ID: {result.ListingId}; Offer ID: {result.OfferId}; SKU: {result.Sku}");
        return Results.Ok(new { result.OfferId, result.ListingId, result.Sku, listingUrl });
    }
    catch (Exception ex)
    {
        log.Add("Warning", "eBay publish failed", ex.Message);
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapPost("/api/local-listings/save-edit", (UpdateListingRequest req, ListingDatabase db, ActionLog log) =>
{
    var result = db.SaveEdit(req);
    log.Add("Info", "Local edit saved", string.IsNullOrWhiteSpace(req.Sku) ? req.Title : req.Sku);
    return Results.Ok(result);
});

app.MapPost("/api/listing/update", async (UpdateListingRequest req, EbayService ebay, ActionLog log) =>
{
    if (!req.ManualRevisionConfirmed)
    {
        log.Add("Warning", "eBay revise blocked", "Manual revision confirmation was missing.");
        return Results.BadRequest("Manual revision confirmation is required before revising eBay.");
    }

    await ebay.UpdateListingAsync(req);
    log.Add("Info", "eBay listing revised", string.IsNullOrWhiteSpace(req.Sku) ? req.OfferId : req.Sku);
    return Results.Ok();
});

// ── Owner dashboard ───────────────────────────────────────────────
app.MapGet("/api/owner/stats", (string? k, CredentialsStore store, AnalyticsStore analytics, ActionLog log, StripeService stripe) =>
{
    var adminKey = store.EnsureAdminKey();
    if (string.IsNullOrWhiteSpace(k) || k != adminKey)
        return Results.Unauthorized();
    var snap = analytics.GetSnapshot();
    return Results.Ok(new
    {
        analytics       = snap,
        recentLogs      = log.Recent(),
        stripeConfigured = stripe.IsConfigured,
        dashboardUrl    = $"{baseUrl}/owner?k={adminKey}"
    });
});

app.MapGet("/owner", (string? k, CredentialsStore store, StripeService stripe) =>
{
    var adminKey = store.EnsureAdminKey();
    if (string.IsNullOrWhiteSpace(k) || k != adminKey)
        return Results.Content("<html><body><h2>401 Unauthorized</h2></body></html>", "text/html", statusCode: 401);
    var stripeConfigured = stripe.IsConfigured;
    var stripePubKey     = stripe.PublishableKey ?? "";

    var html = $$"""
<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="UTF-8"/>
<meta name="viewport" content="width=device-width,initial-scale=1"/>
<title>Owner Dashboard — ING Listing Engine</title>
<style>
  *{box-sizing:border-box;margin:0;padding:0}
  body{font-family:system-ui,sans-serif;background:#0f1117;color:#e2e8f0;min-height:100vh;padding:2rem}
  h1{font-size:1.5rem;font-weight:700;margin-bottom:1.5rem;color:#f8fafc}
  h2{font-size:1rem;font-weight:600;margin-bottom:.75rem;color:#94a3b8;text-transform:uppercase;letter-spacing:.05em}
  .grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(160px,1fr));gap:1rem;margin-bottom:2rem}
  .card{background:#1e2330;border:1px solid #2d3748;border-radius:10px;padding:1.25rem}
  .card-val{font-size:2rem;font-weight:700;color:#60a5fa}
  .card-lbl{font-size:.75rem;color:#64748b;margin-top:.25rem}
  table{width:100%;border-collapse:collapse;background:#1e2330;border-radius:10px;overflow:hidden;margin-bottom:2rem}
  th{background:#2d3748;padding:.6rem 1rem;text-align:left;font-size:.75rem;color:#94a3b8;text-transform:uppercase}
  td{padding:.6rem 1rem;border-top:1px solid #2d3748;font-size:.85rem}
  .log-warn{color:#f59e0b}.log-info{color:#94a3b8}
  .badge{display:inline-block;padding:.2rem .5rem;border-radius:4px;font-size:.7rem;font-weight:600}
  .badge-warn{background:#78350f;color:#fcd34d}.badge-info{background:#1e3a5f;color:#93c5fd}
  #status{color:#94a3b8;font-size:.85rem;margin-bottom:1rem}
  .refresh-btn{background:#2563eb;color:#fff;border:none;border-radius:6px;padding:.5rem 1rem;cursor:pointer;font-size:.85rem}
  .refresh-btn:hover{background:#1d4ed8}
</style>
</head>
<body>
<h1>ING Listing Engine™ — Owner Dashboard</h1>
<div id="status">Loading…</div>
<button class="refresh-btn" onclick="load()">Refresh</button>

<div style="background:#1e2330;border:1px solid #2d3748;border-radius:10px;padding:1.25rem;margin-bottom:2rem">
  <h2 style="margin-bottom:.75rem">Stripe / Monetization</h2>
  <div style="display:flex;gap:2rem;flex-wrap:wrap;font-size:.9rem">
    <div><span style="color:#64748b">Status:</span> <strong style="color:{{(stripeConfigured ? "#4ade80" : "#f87171")}}">{{(stripeConfigured ? "✓ Configured" : "✗ Not configured")}}</strong></div>
    <div><span style="color:#64748b">Monthly:</span> <strong style="color:#60a5fa">$29.99/mo</strong></div>
    <div><span style="color:#64748b">Annual:</span> <strong style="color:#60a5fa">$249.99/yr</strong></div>
    <div><span style="color:#64748b">Publishable key:</span> <code style="font-size:.75rem;color:#94a3b8">{{(stripePubKey.Length > 16 ? stripePubKey[..16] + "…" : "(none)")}}</code></div>
  </div>
  <div style="margin-top:.75rem;font-size:.8rem;color:#64748b">
    Checkout endpoints: <code style="color:#93c5fd">POST /api/stripe/checkout</code> (monthly) &nbsp;|&nbsp; <code style="color:#93c5fd">POST /api/stripe/checkout/annual</code>
  </div>
</div>

<div id="root"></div>
<script>
const KEY = new URLSearchParams(location.search).get('k');
async function load() {
  document.getElementById('status').textContent = 'Loading…';
  const res = await fetch('/api/owner/stats?k=' + encodeURIComponent(KEY));
  if (!res.ok) { document.getElementById('status').textContent = 'Error ' + res.status; return; }
  const d = await res.json();
  const a = d.analytics;
  document.getElementById('status').textContent = 'Last updated: ' + new Date().toLocaleTimeString();
  const stats = [
    { val: a.totalPageLoads, lbl: 'Page Loads' },
    { val: (a.uniqueIps||[]).length, lbl: 'Unique IPs' },
    { val: a.aiAnalyses, lbl: 'AI Analyses' },
    { val: a.bulkImports, lbl: 'Bulk Imports' },
    { val: a.listingsPublished, lbl: 'Published' },
    { val: a.draftsSaved, lbl: 'Drafts Saved' },
  ];
  let html = '<div class="grid">' + stats.map(s =>
    `<div class="card"><div class="card-val">${s.val??0}</div><div class="card-lbl">${s.lbl}</div></div>`
  ).join('') + '</div>';
  if (a.firstSeen) html += `<p style="color:#64748b;font-size:.8rem;margin-bottom:2rem">First seen: ${new Date(a.firstSeen).toLocaleString()} &nbsp;|&nbsp; Last seen: ${new Date(a.lastSeen).toLocaleString()}</p>`;
  if ((a.daily||[]).length) {
    html += '<h2>Daily (last 30 days)</h2><table><thead><tr><th>Date</th><th>Page Loads</th><th>Unique IPs</th><th>AI Analyses</th><th>Bulk Imports</th><th>Published</th></tr></thead><tbody>';
    [...a.daily].reverse().forEach(r => {
      html += `<tr><td>${r.date}</td><td>${r.pageLoads}</td><td>${r.uniqueIps}</td><td>${r.aiAnalyses}</td><td>${r.bulkImports}</td><td>${r.listingsPublished}</td></tr>`;
    });
    html += '</tbody></table>';
  }
  if ((d.recentLogs||[]).length) {
    html += '<h2>Recent Logs</h2><table><thead><tr><th>Time</th><th>Level</th><th>Action</th><th>Detail</th></tr></thead><tbody>';
    d.recentLogs.forEach(l => {
      const cls = l.level==='Warning'?'badge-warn':'badge-info';
      html += `<tr><td>${new Date(l.timestamp).toLocaleTimeString()}</td><td><span class="badge ${cls}">${l.level}</span></td><td>${esc(l.title)}</td><td style="color:#64748b">${esc(l.detail)}</td></tr>`;
    });
    html += '</tbody></table>';
  }
  document.getElementById('root').innerHTML = html;
}
function esc(s){const d=document.createElement('div');d.textContent=s??'';return d.innerHTML;}
load();
</script>
</body>
</html>
""";
    return Results.Content(html, "text/html");
});

// ── eBay Sniper ───────────────────────────────────────────────────────────────
app.MapGet("/api/sniper/lookup", async (string itemId, EbayService ebay, ActionLog log) =>
{
    try
    {
        var item = await ebay.GetItemAsync(itemId);
        return Results.Ok(new
        {
            itemId,
            title      = item.Title ?? "",
            endsAt     = (string?)null,   // Trading API GetItem can return EndTime — wired below
            currentBid = item.Price,
        });
    }
    catch (Exception ex)
    {
        log.Add("Warning", "Sniper lookup failed", ex.Message);
        return Results.Ok(new { itemId, title = "", endsAt = (string?)null, currentBid = (decimal?)null });
    }
});

app.MapPost("/api/sniper/bid", async (SniperBidRequest req, EbayService ebay, ActionLog log) =>
{
    try
    {
        await ebay.PlaceMaxBidAsync(req.ItemId, req.MaxBid);
        log.Add("Info", "Sniper bid placed", $"Item {req.ItemId} @ ${req.MaxBid:F2}");
        return Results.Ok(new { ok = true });
    }
    catch (Exception ex)
    {
        log.Add("Warning", "Sniper bid failed", ex.Message);
        return Results.Ok(new { ok = false, error = ex.Message });
    }
});

// ── Service mode: headless web server, lifecycle managed by Windows SCM ──────
if (isWindowsService)
{
    await app.RunAsync(baseUrl);
    return;
}

// ── Interactive mode: background web server + system tray icon ───────────────
var webTask = app.RunAsync(baseUrl);

// Open the browser automatically once Kestrel has bound the port
_ = Task.Run(async () =>
{
    await Task.Delay(1200);
    OpenBrowser();
});

// One-time check: add the local DNS entry for inglistingengine.com if missing
_ = Task.Run(async () =>
{
    await Task.Delay(3000);
    EnsureLocalDns(exeDir);
});

System.Windows.Forms.Application.EnableVisualStyles();
System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);

using var trayIcon = new System.Windows.Forms.NotifyIcon
{
    Icon    = CreateAppIcon(),
    Text    = $"ING AutoLister  •  localhost:{port}",
    Visible = true,
};
trayIcon.ShowBalloonTip(
    3000, "ING AutoLister",
    "Running in background. Right-click this icon to open or quit.",
    System.Windows.Forms.ToolTipIcon.Info);

var ctxMenu = new System.Windows.Forms.ContextMenuStrip();
ctxMenu.Items.Add("Open ING AutoLister", null, (_, _) => OpenBrowser());
ctxMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
ctxMenu.Items.Add("Quit ING AutoLister", null, (_, _) =>
{
    trayIcon.Visible = false;
    System.Windows.Forms.Application.ExitThread();
});
trayIcon.ContextMenuStrip  = ctxMenu;
trayIcon.DoubleClick      += (_, _) => OpenBrowser();

System.Windows.Forms.Application.Run(); // blocks until ExitThread()
await app.StopAsync(TimeSpan.FromSeconds(3));
_mutex?.Dispose();

static System.Drawing.Icon CreateAppIcon()
{
    var bmp = new System.Drawing.Bitmap(32, 32, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
    using var g = System.Drawing.Graphics.FromImage(bmp);
    g.SmoothingMode     = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
    g.Clear(System.Drawing.Color.Transparent);

    // Dark teal rounded background
    var teal = System.Drawing.Color.FromArgb(8, 37, 41);   // #082529
    var gold = System.Drawing.Color.FromArgb(199, 154, 54); // #c79a36

    using var bgBrush = new System.Drawing.SolidBrush(teal);
    using var path = new System.Drawing.Drawing2D.GraphicsPath();
    int r = 5;
    path.AddArc(0, 0, r * 2, r * 2, 180, 90);
    path.AddArc(32 - r * 2, 0, r * 2, r * 2, 270, 90);
    path.AddArc(32 - r * 2, 32 - r * 2, r * 2, r * 2, 0, 90);
    path.AddArc(0, 32 - r * 2, r * 2, r * 2, 90, 90);
    path.CloseFigure();
    g.FillPath(bgBrush, path);

    // Gold price-tag arrow: H5 10  H19 L27 16 L19 22 H5 Z  (scaled from 32px viewBox)
    var tagPts = new System.Drawing.PointF[]
    {
        new(4,  9), new(18, 9), new(26, 16),
        new(18, 23), new(4, 23)
    };
    using var tagBrush = new System.Drawing.SolidBrush(gold);
    g.FillPolygon(tagBrush, tagPts);

    // Punch hole
    using var holeBrush = new System.Drawing.SolidBrush(teal);
    g.FillEllipse(holeBrush, 5.5f, 14f, 4f, 4f);

    var handle = bmp.GetHicon();
    return System.Drawing.Icon.FromHandle(handle);
}

void OpenBrowser() =>
    System.Diagnostics.Process.Start(
        new System.Diagnostics.ProcessStartInfo(baseUrl) { UseShellExecute = true });

// Adds inglistingengine.com → 127.0.0.1 to the hosts file by re-launching the
// exe with --add-local-dns under a UAC elevation prompt (one-time, non-fatal).
static void EnsureLocalDns(string exeDir)
{
    const string hostsFile = @"C:\Windows\System32\drivers\etc\hosts";
    const string hostname  = "inglistingengine.com";
    var flagFile = Path.Combine(exeDir, ".dns-configured");
    if (File.Exists(flagFile)) return;
    try
    {
        var text = File.ReadAllText(hostsFile);
        if (text.Contains(hostname, StringComparison.OrdinalIgnoreCase))
        {
            File.WriteAllText(flagFile, DateTime.Now.ToString("u"));
            return;
        }
    }
    catch { return; }
    // Entry missing — elevate to add it
    try
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName         = Environment.ProcessPath!,
            Arguments        = "--add-local-dns",
            Verb             = "runas",
            UseShellExecute  = true,
        };
        using var proc = System.Diagnostics.Process.Start(psi);
        proc?.WaitForExit(8000);
        File.WriteAllText(flagFile, DateTime.Now.ToString("u"));
    }
    catch { /* user declined UAC — no problem, app still works on localhost */ }
}
