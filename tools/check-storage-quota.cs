#:package Microsoft.Playwright@1.49.0
#:property JsonSerializerIsReflectionEnabledByDefault=true
// STORAGE QUOTA probe: what does the browser actually grant this origin, and how much is already used?
//
// 🔴 WHY IT EXISTS. drive-ai-demo failed with "The operation failed because it would cause the
// application to exceed its storage quota." That message names the condition but not the numbers, and the
// two candidate causes need opposite fixes:
//   - an EPHEMERAL automation profile gets a small quota, so the gate is measuring its own harness; or
//   - the real profile is genuinely near its limit, and the app must handle it.
// A quota figure tells them apart in one run. It also reports persistence, because a non-persisted origin
// can be evicted under pressure - which looks like "the model re-downloads for no reason".
//
//   dotnet run tools/check-storage-quota.cs -- [url] [--persistent]
using Microsoft.Playwright;

var url = args.FirstOrDefault(a => !a.StartsWith("--")) ?? "http://localhost:5199/";
var persistent = args.Contains("--persistent");

using var pw = await Playwright.CreateAsync();
IPage page;
IBrowserContext? ctx = null;
IBrowser? browser = null;

if (persistent)
{
    var dir = Path.Combine(Path.GetTempPath(), "spawndev-ai-avatar-check-profile");
    Directory.CreateDirectory(dir);
    ctx = await pw.Chromium.LaunchPersistentContextAsync(dir, new() { Headless = true, Channel = "chrome" });
    page = ctx.Pages.Count > 0 ? ctx.Pages[0] : await ctx.NewPageAsync();
}
else
{
    browser = await pw.Chromium.LaunchAsync(new() { Headless = true, Channel = "chrome" });
    page = await browser.NewPageAsync();
}

Console.WriteLine($"[quota] profile: {(persistent ? "PERSISTENT (same one the avatar gate uses)" : "EPHEMERAL (fresh, what drive-ai-demo uses)")}");
await page.GotoAsync(url, new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 60000 });

var json = await page.EvaluateAsync<string>(@"async () => {
  const e = await navigator.storage.estimate();
  let persisted = null;
  try { persisted = await navigator.storage.persisted(); } catch {}
  return JSON.stringify({ quota: e.quota ?? -1, usage: e.usage ?? -1, persisted });
}");
Console.WriteLine($"[quota] {json}");

var doc = System.Text.Json.JsonDocument.Parse(json).RootElement;
double quota = doc.GetProperty("quota").GetDouble();
double usage = doc.GetProperty("usage").GetDouble();
Console.WriteLine($"[quota] quota {quota / 1073741824.0:F2} GiB, used {usage / 1048576.0:F0} MiB, "
                + $"free {(quota - usage) / 1073741824.0:F2} GiB, persisted={doc.GetProperty("persisted")}");

// The demo's smallest catalogue entry is ~386 MB and the recommended one is ~1.83 GB. If free space
// cannot hold the recommended model, the gate is measuring the harness, not the app.
const double Recommended = 1_834_426_016;
Console.WriteLine((quota - usage) < Recommended
    ? $"[quota] TOO SMALL for the recommended model ({Recommended / 1073741824.0:F2} GiB) - this profile cannot run it."
    : $"[quota] room for the recommended model ({Recommended / 1073741824.0:F2} GiB).");

if (ctx != null) await ctx.CloseAsync();
if (browser != null) await browser.CloseAsync();
