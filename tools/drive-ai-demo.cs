#:package Microsoft.Playwright@1.49.0
#:property JsonSerializerIsReflectionEnabledByDefault=true
// Live browser gate for SpawnDev.AI.Demo: does exactly what TJ would do - open the page, start the
// AI server (shared worker + WebGPU), send a chat message, verify tokens stream back.
using Microsoft.Playwright;

var url = args.Length > 0 ? args[0] : "http://localhost:5199/";
using var pw = await Playwright.CreateAsync();
await using var browser = await pw.Chromium.LaunchAsync(new()
{
    Headless = false,
    Channel = "chrome",   // TJ's installed Chrome build (hardware WebGPU); separate automation profile
});
var page = await browser.NewPageAsync();
page.Console += (_, msg) => Console.WriteLine($"[console] {msg.Text}");

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
await browser.CloseAsync();
return pass ? 0 : 1;
