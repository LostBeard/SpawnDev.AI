#:package Microsoft.Playwright@1.49.0
#:property JsonSerializerIsReflectionEnabledByDefault=true
// SD-Turbo image-gen TIMING driver for SpawnDev.AI.Demo. Opens the page on TJ's real Chrome (hardware
// WebGPU) with a PERSISTENT profile (so the SD-Turbo model OPFS-caches across runs => warm gen, not a
// cold re-download), starts the AI server, and clicks the 🔬 IMGTEST button (direct SD-Turbo, fixed
// lighthouse prompt, bypasses the LLM) TWICE: click 1 = OPFS->GPU load + gen, click 2 = gen-only
// (model resident). The page-console "IMGTEST: direct SD-Turbo load+gen = Xs" is the capturable total
// (the [NODETIME] breakdown lives in the worker console, not page-visible). Used to A/B the WebGPU f16
// read-only-weight atomicLoad->plain-load win: gen-only time (click 2) is the clean number.
// args: [url] [armLabel] [profileDir]
using Microsoft.Playwright;

var url = args.Length > 0 ? args[0] : "http://localhost:5125/";
var arm = args.Length > 1 ? args[1] : "run";
var profileDir = args.Length > 2 ? args[2] : @"C:\Users\TJ\AppData\Local\Temp\claude-imgtest-profile";

var genTimes = new List<double>();
var lastGenTcs = new TaskCompletionSource<double>();
int genSeen = 0;

using var pw = await Playwright.CreateAsync();
// Persistent context => OPFS (the model cache) survives across driver runs at the same origin.
await using var ctx = await pw.Chromium.LaunchPersistentContextAsync(profileDir, new()
{
    Headless = false,
    Channel = "chrome",
});
var page = ctx.Pages.Count > 0 ? ctx.Pages[0] : await ctx.NewPageAsync();

void OnConsole(string t)
{
    Console.WriteLine($"[console] {t}");
    var m = System.Text.RegularExpressions.Regex.Match(t, @"IMGTEST: direct SD-Turbo load\+gen = ([0-9.]+)s");
    if (m.Success)
    {
        var s = double.Parse(m.Groups[1].Value);
        genTimes.Add(s); genSeen++;
        Console.WriteLine($">>> IMGTEST_TIME ({arm}) click#{genSeen} = {s:F1}s");
        if (genSeen >= 2) lastGenTcs.TrySetResult(s);
    }
    if (t.Contains("IMGTEST FAILED")) lastGenTcs.TrySetResult(-1);
}
page.Console += (_, msg) => OnConsole(msg.Text);
// Best-effort: also surface any dedicated-worker console (may carry [NODETIME]).
ctx.Page += (_, p) => p.Console += (_, msg) => OnConsole("(pg2) " + msg.Text);

Console.WriteLine($"[img] ({arm}) goto {url}  profile={profileDir}");
await page.GotoAsync(url, new() { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 120000 });

Console.WriteLine("[img] clicking 'Start the AI server'");
await page.ClickAsync("button:has-text(\"Start the AI server\")", new() { Timeout = 30000 });
await page.WaitForSelectorAsync("textarea", new() { Timeout = 240000 });
Console.WriteLine("[img] READY: worker + WebGPU up");

var imgBtn = "button[title^=\"IMGTEST\"]";
await page.WaitForSelectorAsync(imgBtn, new() { Timeout = 30000 });

// Click 1: OPFS->GPU load + gen (cold-download the FIRST time this profile runs; warm afterwards).
Console.WriteLine("[img] IMGTEST click 1 (load+gen)");
await page.ClickAsync(imgBtn, new() { Timeout = 30000 });
// Wait for click-1 completion (button re-enables) before click 2. Generous for a cold model download.
await page.WaitForSelectorAsync(imgBtn + ":not([disabled])", new() { Timeout = 1_200_000 });

// Click 2: gen-only (model resident) — the clean A/B number.
Console.WriteLine("[img] IMGTEST click 2 (gen-only, model resident)");
await page.ClickAsync(imgBtn, new() { Timeout = 30000 });

var finished = await Task.WhenAny(lastGenTcs.Task, Task.Delay(TimeSpan.FromSeconds(600)));
bool ok = finished == lastGenTcs.Task && lastGenTcs.Task.Result > 0;

Console.WriteLine();
Console.WriteLine($"===== IMGTEST SUMMARY ({arm}) — ok={ok} =====");
for (int i = 0; i < genTimes.Count; i++)
    Console.WriteLine($"   click#{i + 1} load+gen = {genTimes[i]:F1}s{(i == 1 ? "   <-- gen-only (A/B number)" : "")}");
if (genTimes.Count >= 2) Console.WriteLine($">>> GENONLY_SECONDS ({arm}) = {genTimes[1]:F1}");
Console.WriteLine("=====================================================");

await ctx.CloseAsync();
return ok ? 0 : 2;
