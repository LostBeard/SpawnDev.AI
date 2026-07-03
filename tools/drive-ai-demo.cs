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

// Worker attach + WebGPU init: the model dropdown appears when ready.
await page.WaitForSelectorAsync("select", new() { Timeout = 180000 });
var status = await page.InnerTextAsync("p[style*='color:#484']");
Console.WriteLine($"[gate] READY: {status}");

Console.WriteLine("[gate] sending question");
await page.FillAsync("input[placeholder='Send a message…']", "What is the capital of France? Answer in one short sentence.");
var t0 = DateTime.UtcNow;
await page.ClickAsync("button:has-text(\"Send\")");

// First request: hub download (~11s on the LAN) + GPU load + capture warmup, then streaming.
// Generation is finished when the composer re-enables (_busy = false re-renders the input enabled).
await page.WaitForSelectorAsync("input[placeholder='Send a message…']:not([disabled])", new() { Timeout = 300000 });
var total = (DateTime.UtcNow - t0).TotalSeconds;

var transcript = await page.InnerTextAsync("div[style*='overflow-y']");
Console.WriteLine($"[gate] TRANSCRIPT ({total:F1}s):\n{transcript}");

bool pass = transcript.Contains("Paris", StringComparison.OrdinalIgnoreCase);
Console.WriteLine(pass ? "[gate] PASS - answer contains 'Paris'" : "[gate] FAIL - no expected answer");
await browser.CloseAsync();
return pass ? 0 : 1;
