using System.Diagnostics;
using System.Text.Json;

namespace ING_eBay_AutoLister.Services;

/// <summary>
/// Terapeak (eBay Seller Hub's sold-comps research tool) has no public API — it's a regular
/// logged-in website, authenticated by browser cookies, not the OAuth tokens used elsewhere in
/// this app. This service pops a real (visible) browser window once for the seller to log into
/// eBay normally, saves that session to disk, then reuses it headlessly to read real sold-comp
/// data straight off the rendered Seller Hub page.
/// </summary>
public class TerapeakService(IWebHostEnvironment env, ActionLog log)
{
    private readonly string _sessionPath = Path.Combine(env.ContentRootPath, "terapeak-session.json");
    private volatile bool _loginInProgress;

    private static string PlaywrightDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "npm", "node_modules", "playwright");

    public bool IsConnected => File.Exists(_sessionPath);
    public bool IsLoginInProgress => _loginInProgress;

    // ── One-time interactive login ────────────────────────────────────────────

    public (bool Started, string Message) StartLogin()
    {
        if (_loginInProgress)
            return (false, "A login window is already open — finish logging in there.");

        _loginInProgress = true;
        _ = Task.Run(RunLoginProcessAsync);
        return (true, "A browser window just opened — log into eBay there. It closes itself once you're in.");
    }

    private async Task RunLoginProcessAsync()
    {
        var pwPath = PlaywrightDir.Replace("\\", "\\\\");
        var sessionPathEscaped = _sessionPath.Replace("\\", "\\\\");
        var script =
            $"const {{ chromium }} = require('{pwPath}');\n" +
            "(async () => {\n" +
            // The real installed Chrome (not Playwright's bundled "Chrome for Testing" build)
            // reports a normal, self-consistent fingerprint — eBay's bot detection flags the
            // bundled test browser much more readily, especially after repeated automated hits.
            "  const browser = await chromium.launch({ channel: 'chrome', headless: false, args: ['--disable-blink-features=AutomationControlled'] });\n" +
            "  const ctx = await browser.newContext({ viewport: null });\n" +
            "  await ctx.addInitScript(() => { Object.defineProperty(navigator,'webdriver',{get:()=>undefined}); });\n" +
            "  const page = await ctx.newPage();\n" +
            "  try {\n" +
            "    await page.goto('https://www.ebay.com/sh/research?marketplace=EBAY-US&tabName=SOLD', { waitUntil: 'domcontentloaded', timeout: 30000 });\n" +
            "  } catch (_) {}\n" +
            "  const deadline = Date.now() + 6 * 60 * 1000;\n" +
            "  while (Date.now() < deadline) {\n" +
            "    if (!browser.isConnected()) break;\n" + // user closed the window manually
            "    if (page.url().includes('/sh/research')) break;\n" +
            "    await page.waitForTimeout(1000).catch(() => {});\n" +
            "  }\n" +
            "  if (browser.isConnected() && page.url().includes('/sh/research')) {\n" +
            "    await page.waitForTimeout(1500);\n" +
            "    const state = await ctx.storageState();\n" +
            $"    require('fs').writeFileSync('{sessionPathEscaped}', JSON.stringify(state));\n" +
            "    process.stdout.write('SAVED');\n" +
            "  } else {\n" +
            "    process.stdout.write('CANCELLED');\n" +
            "  }\n" +
            "  try { await browser.close(); } catch (_) {}\n" +
            "})();\n";

        var scriptFile = Path.Combine(Path.GetTempPath(), $"terapeak_login_{Guid.NewGuid():N}.cjs");
        await File.WriteAllTextAsync(scriptFile, script);

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName               = "node",
                ArgumentList           = { scriptFile },
                WorkingDirectory       = PlaywrightDir,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
                CreateNoWindow         = true
            };

            using var proc = Process.Start(psi)!;
            var stdoutTask = proc.StandardOutput.ReadToEndAsync();
            var stderrTask = proc.StandardError.ReadToEndAsync();

            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(7));
            try { await proc.WaitForExitAsync(cts.Token); }
            catch (OperationCanceledException)
            {
                try { proc.Kill(entireProcessTree: true); } catch { }
                log.Add("Warning", "Terapeak login timed out", "No login completed within 7 minutes.");
                return;
            }

            var stdout = (await stdoutTask).Trim();
            var stderr = await stderrTask;

            if (stdout == "SAVED")
                log.Add("Info", "Terapeak connected", "Session saved — sold comps will now use real Terapeak data.");
            else
                log.Add("Warning", "Terapeak login not completed", string.IsNullOrWhiteSpace(stderr) ? stdout : stderr);
        }
        finally
        {
            try { File.Delete(scriptFile); } catch { }
            _loginInProgress = false;
        }
    }

    public void Disconnect()
    {
        try { File.Delete(_sessionPath); } catch { }
        log.Add("Info", "Terapeak disconnected", "Saved session removed.");
    }

    // ── Headless scrape using the saved session ────────────────────────────────

    public async Task<TerapeakScrapeResult> ScrapeAsync(string query)
    {
        if (!IsConnected)
            return new TerapeakScrapeResult { Status = "not_connected" };

        var pwPath = PlaywrightDir.Replace("\\", "\\\\");
        var sessionPathEscaped = _sessionPath.Replace("\\", "\\\\");
        var debugShotPath = Path.Combine(env.ContentRootPath, "generated-photos", $"terapeak_debug_{Guid.NewGuid():N}.png");
        var debugShotEscaped = debugShotPath.Replace("\\", "\\\\");
        var url = "https://www.ebay.com/sh/research?marketplace=EBAY-US&tabName=SOLD&dayRange=60&keywords=" + Uri.EscapeDataString(query);

        var script =
            $"const {{ chromium }} = require('{pwPath}');\n" +
            "(async () => {\n" +
            "  const browser = await chromium.launch({ channel: 'chrome', headless: true });\n" +
            $"  const ctx = await browser.newContext({{ storageState: '{sessionPathEscaped}', viewport: {{ width: 1400, height: 1000 }} }});\n" +
            "  const page = await ctx.newPage();\n" +
            "  let loggedOut = false;\n" +
            "  try {\n" +
            $"    await page.goto('{url}', {{ waitUntil: 'domcontentloaded', timeout: 25000 }});\n" +
            "    await page.waitForTimeout(3500);\n" +
            "    loggedOut = /signin\\.ebay\\.com|\\/signin/.test(page.url());\n" +
            "  } catch (_) {}\n" +
            $"  await page.screenshot({{ path: '{debugShotEscaped}', fullPage: true }}).catch(()=>{{}});\n" +
            "  const bodyText = await page.evaluate(() => document.body.innerText).catch(() => '');\n" +
            "  process.stdout.write(JSON.stringify({ loggedOut, url: page.url(), bodyText: bodyText.slice(0, 15000) }));\n" +
            "  await browser.close();\n" +
            "})();\n";

        var scriptFile = Path.Combine(Path.GetTempPath(), $"terapeak_scrape_{Guid.NewGuid():N}.cjs");
        await File.WriteAllTextAsync(scriptFile, script);

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName               = "node",
                ArgumentList           = { scriptFile },
                WorkingDirectory       = PlaywrightDir,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
                CreateNoWindow         = true
            };

            using var proc = Process.Start(psi)!;
            var stdoutTask = proc.StandardOutput.ReadToEndAsync();
            var stderrTask = proc.StandardError.ReadToEndAsync();

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(40));
            try { await proc.WaitForExitAsync(cts.Token); }
            catch (OperationCanceledException)
            {
                try { proc.Kill(entireProcessTree: true); } catch { }
                return new TerapeakScrapeResult { Status = "error", Error = "Scrape timed out." };
            }

            var stdout = (await stdoutTask).Trim();
            var stderr = await stderrTask;

            if (string.IsNullOrWhiteSpace(stdout))
                return new TerapeakScrapeResult { Status = "error", Error = string.IsNullOrWhiteSpace(stderr) ? "No output from scrape." : stderr };

            using var doc = JsonDocument.Parse(stdout);
            var loggedOut = doc.RootElement.TryGetProperty("loggedOut", out var lo) && lo.GetBoolean();
            var bodyText  = doc.RootElement.TryGetProperty("bodyText", out var bt) ? bt.GetString() ?? "" : "";

            if (loggedOut)
            {
                Disconnect(); // session expired — clear it so the UI prompts to reconnect
                return new TerapeakScrapeResult { Status = "session_expired" };
            }

            return new TerapeakScrapeResult { Status = "ok", BodyText = bodyText, DebugScreenshotPath = debugShotPath };
        }
        finally
        {
            try { File.Delete(scriptFile); } catch { }
        }
    }
}

public class TerapeakScrapeResult
{
    public string Status { get; set; } = ""; // ok | not_connected | session_expired | error
    public string BodyText { get; set; } = "";
    public string? DebugScreenshotPath { get; set; }
    public string? Error { get; set; }
}
