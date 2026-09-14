#:package Microsoft.Playwright@1.49.0
#:property JsonSerializerIsReflectionEnabledByDefault=true
// ENDPOINTER PRELOAD probe: does the VAD model actually load, and from where?
//
// 🔴 WHY IT EXISTS. Captain, off the running demo: "vad (HttpRequestException: TypeError: network error)
// did not preload; it will be loaded when first needed." The model is 643 KB served from this app's own
// wwwroot, and `curl` gets it fine - so the failure is about the URL the WORKER resolves, not the file.
// A browser reports every cross-origin refusal, blocked request and unreachable host as the same
// "TypeError: network error", so the one fact that separates them is the absolute URL, which
// AiVadEngine now names in the message.
//
// Short on purpose: it toggles hands-free (the only thing that warms the endpointer), reads what the page
// says, and stops. It does NOT drive a conversation - tools/drive-hands-free.cs owns that.
//
//   dotnet run tools/check-vad-preload.cs -- [url] [--dedicated]
using Microsoft.Playwright;

var url = (args.FirstOrDefault(a => a.StartsWith("http")) ?? "http://localhost:5199/").TrimEnd('/');
// ⚠️ SHARED by default, because that is the configuration the report came from and the one whose console
// cannot be read - the system BUBBLE is the only channel out of it.
var worker = args.Contains("--dedicated") ? "dedicated" : "shared";
var pageUrl = $"{url}/?worker={worker}";

var profile = Path.Combine(Path.GetTempPath(), "spawndev-ai-vad-probe-profile");
Directory.CreateDirectory(profile);

using var pw = await Playwright.CreateAsync();
await using var browser = await pw.Chromium.LaunchPersistentContextAsync(profile, new()
{
    Headless = false,
    Channel = "chrome",
    Permissions = new[] { "microphone" },   // hands-free opens the mic; without this the toggle stalls
});
var page = browser.Pages.Count > 0 ? browser.Pages[0] : await browser.NewPageAsync();
page.Console += (_, m) =>
{
    // ⚠️ EXCLUDE the per-batch trace. Hands-free logs [HF-VAD]/[HF-MIC] several times a second, which
    // buried this probe's own verdict under 20 KB of scrolling in the first run.
    if (m.Text.StartsWith("[HF-VAD]") || m.Text.StartsWith("[HF-MIC]")) return;
    if (m.Type == "error" || m.Text.Contains("AiVadEngine", StringComparison.Ordinal)
        || m.Text.Contains("endpointer", StringComparison.OrdinalIgnoreCase))
        Console.WriteLine($"[vad] [console:{m.Type}] {m.Text}");
};

Console.WriteLine($"[vad] worker mode: {worker}");
Console.WriteLine($"[vad] goto {pageUrl}");
await page.GotoAsync(pageUrl, new() { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 60000 });
await page.ClickAsync("button:has-text(\"Start the AI server\")", new() { Timeout = 30000 });
await page.WaitForSelectorAsync("textarea", new() { Timeout = 180000 });
Console.WriteLine("[vad] worker + WebGPU up");

// The file itself, from the PAGE, so "the server serves it" is established before blaming the worker.
var direct = await page.EvaluateAsync<string>(@"async () => {
  try {
    const r = await fetch('references/vad/silero_vad.onnx');
    const b = await r.arrayBuffer();
    return `page fetch: ${r.status}, ${b.byteLength} bytes, from ${r.url}`;
  } catch (e) { return `page fetch FAILED: ${e}`; }
}");
Console.WriteLine($"[vad] {direct}");

Console.WriteLine("[vad] toggling hands-free (this is what warms the endpointer)");
await page.ClickAsync("button[title*='Hands-free']", new() { Timeout = 30000 });

// The warm is awaited before the status settles, so poll both the status line and the system bubbles.
string status = "", bubbles = "";
var deadline = DateTime.UtcNow.AddSeconds(180);
while (DateTime.UtcNow < deadline)
{
    await Task.Delay(500);
    status = await page.EvaluateAsync<string>(
        "() => { const e = document.querySelector('.sdai-ftr span'); return e ? e.innerText.trim() : ''; }");
    bubbles = string.Join(" | ", await page.Locator(".msg.system").AllInnerTextsAsync());
    if (status.Contains("Hands-free on", StringComparison.OrdinalIgnoreCase)) break;
    if (bubbles.Contains("endpointer", StringComparison.OrdinalIgnoreCase)) break;
}

Console.WriteLine($"[vad] status: \"{status}\"");
if (bubbles.Length > 0) Console.WriteLine($"[vad] system messages: {bubbles}");

// Stop the microphone rather than leaving it open on the way out.
try { await page.ClickAsync("button[title*='Stop hands-free']", new() { Timeout = 5000 }); } catch { }
await browser.CloseAsync();

var failed = status.Contains("did not preload", StringComparison.OrdinalIgnoreCase)
          || status.Contains("Could not preload", StringComparison.OrdinalIgnoreCase)
          || bubbles.Contains("did not preload", StringComparison.OrdinalIgnoreCase);
Console.WriteLine(failed
    ? "[vad] FAIL - the endpointer did not preload. The message above names the absolute URL it tried."
    : "[vad] PASS - the endpointer preloaded.");
return failed ? 1 : 0;
