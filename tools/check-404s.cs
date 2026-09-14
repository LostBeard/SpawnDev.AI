#:package Microsoft.Playwright@1.49.0
#:property JsonSerializerIsReflectionEnabledByDefault=true
// 404 probe: name every request the demo makes that the server does not have.
//
// ⚠️ WHY. Every gate run logs "Failed to load resource: the server responded with a status of 404" and
// none of them say WHICH resource - so it has been ignored as noise. On a GitHub Pages deploy a missing
// asset is not noise: the dev server and Pages serve different trees, and a 404 that is harmless locally
// can be a broken icon, a missing model manifest or a dead _framework file in production.
//
//   dotnet run tools/check-404s.cs -- [url]
using Microsoft.Playwright;

var url = args.FirstOrDefault(a => !a.StartsWith("--")) ?? "http://localhost:5199/";

using var pw = await Playwright.CreateAsync();
await using var browser = await pw.Chromium.LaunchAsync(new() { Headless = true, Channel = "chrome" });
var page = await browser.NewPageAsync();

var misses = new List<string>();
page.Response += (_, r) => { if (r.Status >= 400) misses.Add($"{r.Status}  {r.Url}"); };
page.RequestFailed += (_, r) => misses.Add($"FAILED  {r.Url}  ({r.Failure})");

Console.WriteLine($"[404] {url}");
await page.GotoAsync(url, new() { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 60000 });
await page.WaitForTimeoutAsync(3000);

if (misses.Count == 0)
{
    Console.WriteLine("[404] clean - nothing 4xx/5xx on load.");
    return 0;
}
Console.WriteLine($"[404] {misses.Count} failing request(s):");
foreach (var m in misses.Distinct()) Console.WriteLine($"   {m}");
return 0;   // reporting probe, not a gate - the caller decides what matters
