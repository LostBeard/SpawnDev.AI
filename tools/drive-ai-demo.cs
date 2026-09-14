#:package Microsoft.Playwright@1.49.0
#:property JsonSerializerIsReflectionEnabledByDefault=true
// Live browser gate for SpawnDev.AI.Demo: does exactly what TJ would do - open the page, start the
// AI server (shared worker + WebGPU), send a chat message, verify tokens stream back.
using Microsoft.Playwright;

var url = args.FirstOrDefault(a => !a.StartsWith("--")) ?? "http://localhost:5199/";
// --persistent reuses a profile on disk, which matters for two reasons the ephemeral default hides:
//   - the cached model SURVIVES between runs, so this stops re-downloading 1.71 GiB every time;
//   - an ephemeral context gets a SMALLER storage quota (MEASURED: 3.00 GiB vs 10.00 GiB), and the
//     default model is 1.71 GiB, so the ephemeral profile is the one that hits quota limits first.
// A real visitor has a persistent profile, so this is the closer approximation of what TJ will see.
var persistent = args.Contains("--persistent");
using var pw = await Playwright.CreateAsync();
IPage page;
IBrowserContext? persistentCtx = null;
IBrowser? browser = null;
if (persistent)
{
    var dir = Path.Combine(Path.GetTempPath(), "spawndev-ai-demo-profile");
    Directory.CreateDirectory(dir);
    persistentCtx = await pw.Chromium.LaunchPersistentContextAsync(dir, new()
    {
        Headless = false,
        Channel = "chrome",
    });
    page = persistentCtx.Pages.Count > 0 ? persistentCtx.Pages[0] : await persistentCtx.NewPageAsync();
}
else
{
    browser = await pw.Chromium.LaunchAsync(new()
    {
        Headless = false,
        Channel = "chrome",   // TJ's installed Chrome build (hardware WebGPU); separate automation profile
    });
    page = await browser.NewPageAsync();
}
page.Console += (_, msg) => Console.WriteLine($"[console] {msg.Text}");

// Report the storage budget up front: a quota failure mid-load is otherwise reported as a chat error with
// no numbers, and "exceed its storage quota" does not say whether the profile was ever big enough.
try
{
    await page.GotoAsync(url, new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 60000 });
    var q = await page.EvaluateAsync<string>(
        "async () => { const e = await navigator.storage.estimate(); return `quota ${(e.quota/1073741824).toFixed(2)} GiB, used ${(e.usage/1048576).toFixed(0)} MiB`; }");
    Console.WriteLine($"[gate] storage: {q}");
}
catch (Exception ex) { Console.WriteLine($"[gate] storage probe failed: {ex.Message}"); }

Console.WriteLine($"[gate] goto {url}");
await page.GotoAsync(url, new() { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 60000 });

Console.WriteLine("[gate] clicking 'Start the AI server'");
await page.ClickAsync("button:has-text(\"Start the AI server\")", new() { Timeout = 30000 });

// Worker attach + WebGPU init: the composer (textarea) appears when ready.
await page.WaitForSelectorAsync("textarea", new() { Timeout = 180000 });
Console.WriteLine("[gate] READY: worker + WebGPU up, composer present");

Console.WriteLine("[gate] sending question");
// Composer is a <textarea> (placeholder "Message — or /model…"); Enter (no shift) submits via OnKeyDown.
await page.FillAsync("textarea", "What is the capital of France? Answer in one short sentence.");
var t0 = DateTime.UtcNow;
await page.PressAsync("textarea", "Enter");

// First request: hub download (~11s on the LAN) + GPU load + capture warmup, then streaming.
// Generation is finished when the composer re-enables (_busy = false re-renders the input enabled).
await page.WaitForSelectorAsync("textarea:not([disabled])", new() { Timeout = 300000 });
var total = (DateTime.UtcNow - t0).TotalSeconds;

var transcript = await page.InnerTextAsync(".transcript");
Console.WriteLine($"[gate] TRANSCRIPT ({total:F1}s):\n{transcript}");

bool pass = transcript.Contains("Paris", StringComparison.OrdinalIgnoreCase);
Console.WriteLine(pass ? "[gate] PASS - answer contains 'Paris'" : "[gate] FAIL - no expected answer");
if (persistentCtx != null) await persistentCtx.CloseAsync(); else if (browser != null) await browser.CloseAsync();
return pass ? 0 : 1;
