#:package Microsoft.Playwright@1.49.0
#:property JsonSerializerIsReflectionEnabledByDefault=true
// Decisive check: does the automation profile's Chrome give a HARDWARE WebGPU adapter (the 4070) or
// a SOFTWARE fallback (SwiftShader / "Microsoft Basic Render")? A software adapter is ~20-100x slower
// and its atomics don't serialize like a GPU's - which would invalidate an SD-Turbo perf A/B run here.
using Microsoft.Playwright;

// Flags are matched by NAME so they can appear in any order; the first non-flag argument is the profile.
// "headless" matters because an automated gate runs headless: if headless Chrome only ever gets a SOFTWARE
// adapter, every headless model-backed run is measuring SwiftShader - and this tool could not say so while
// it hardcoded a headed launch.
bool unsafeFlag = args.Contains("unsafe");
bool headless = args.Contains("headless");
var profileDir = args.FirstOrDefault(a => a != "unsafe" && a != "headless")
                 ?? @"C:\Users\TJ\AppData\Local\Temp\claude-imgtest-profile";

using var pw = await Playwright.CreateAsync();
await using var ctx = await pw.Chromium.LaunchPersistentContextAsync(profileDir, new()
{
    Headless = headless,
    Channel = "chrome",
    Args = unsafeFlag ? new[] { "--enable-unsafe-webgpu" } : System.Array.Empty<string>(),
});
var page = ctx.Pages.Count > 0 ? ctx.Pages[0] : await ctx.NewPageAsync();
// navigator.gpu needs a secure context; about:blank may not qualify. Navigate to a secure page.
try { await page.GotoAsync("https://example.com", new() { Timeout = 30000 }); } catch { }

// chrome://gpu is the ground truth for the WebGPU backend, but is not scriptable; use the JS API.
var info = await page.EvaluateAsync<string>(@"async () => {
  if (!navigator.gpu) return JSON.stringify({ err: 'no navigator.gpu' });
  const a = await navigator.gpu.requestAdapter({ powerPreference: 'high-performance' });
  if (!a) return JSON.stringify({ err: 'no adapter' });
  const i = a.info || (a.requestAdapterInfo ? await a.requestAdapterInfo() : {});
  return JSON.stringify({ vendor: i.vendor, architecture: i.architecture, device: i.device, description: i.description,
           isFallback: a.isFallbackAdapter, features: [...a.features].slice(0,3) });
}");
Console.WriteLine($"ADAPTER (headless={headless} unsafeFlag={unsafeFlag} profile={profileDir}): {info}");
await ctx.CloseAsync();
return 0;
