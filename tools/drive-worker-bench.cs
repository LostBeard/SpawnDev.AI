#:package Microsoft.Playwright@1.49.0
#:property JsonSerializerIsReflectionEnabledByDefault=true
// Window-vs-worker cost triangulation for the SpawnDev.AI demo.
//
//   dotnet run tools/drive-worker-bench.cs -- [url]
//
// ⚠️ WHY THIS EXISTS. MEASURED 2026-09-03: the SAME compiled graph (Whisper decode, enc 227 / dec 374
// nodes) costs 972 ms/step with the engine hosted in the demo's worker and 357 ms/step with it hosted in
// PlaywrightMultiTest's page. Model, node counts and WebGPU flags are ruled out by measurement. Since
// per-node cost in this engine is .NET-side bookkeeping plus one JS crossing per dispatch, exactly three
// things can carry a 2.67x: managed execution, the JS crossing, or the scheduler between awaits.
// AiWorkerClient.InitAsync now times all three IN THE SAME PAGE LOAD - once window-side, once worker-side -
// so the comparison shares a browser, a machine and a moment. This driver just opens the page, presses the
// button that attaches the worker, and prints the three verdict lines.
//
// It deliberately does NOT run a turn: the answer arrives in the first seconds, and a full hands-free turn
// costs minutes of GPU on TJ's desktop.
using Microsoft.Playwright;

var url = (args.FirstOrDefault(a => a.StartsWith("http")) ?? "http://localhost:5199/?worker=dedicated&bench=1").TrimEnd('/');
var profileDir = Path.Combine(Path.GetTempPath(), "spawndev-ai-handsfree-profile");
Directory.CreateDirectory(profileDir);

using var pw = await Playwright.CreateAsync();
await using var ctx = await pw.Chromium.LaunchPersistentContextAsync(profileDir, new()
{
    Headless = false,
    Channel = "chrome",
    Args = new[]
    {
        "--use-fake-ui-for-media-stream",
        "--autoplay-policy=no-user-gesture-required",
        "--enable-unsafe-webgpu",
        "--enable-features=WebGPUService,SkiaGraphite,FileSystemAccessPersistentPermission",
        "--ignore-gpu-blocklist",
        "--disable-software-rasterizer",
    },
});

var page = await ctx.NewPageAsync();
var cdp = await ctx.NewCDPSessionAsync(page);
await cdp.SendAsync("Network.enable");
await cdp.SendAsync("Network.setCacheDisabled", new Dictionary<string, object> { ["cacheDisabled"] = true });

var seen = new List<string>();
var done = new TaskCompletionSource();
page.Console += (_, m) =>
{
    var t = m.Text;
    if (t.Contains("-bench]") || t.StartsWith("[worker]"))
    {
        Console.WriteLine(t);
        seen.Add(t);
        // Three benchmarks: managed, interop, and two yield modes.
        if (seen.Count(x => x.Contains("yield-bench")) >= 2) done.TrySetResult();
    }
};

await page.GotoAsync(url, new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 60_000 });

// The worker attaches on the first request, so something has to ask for one. The hands-free button is the
// shortest route to that and is already how the rest of the gates start a session.
// ⚠️ THE APP DOES NOT START ITSELF - see tools/README.md.
await page.ClickAsync("button:has-text(\"Start the AI server\")", new() { Timeout = 120_000 });
await page.Locator(".composer textarea").WaitForAsync(new() { Timeout = 300_000 });
var button = page.Locator(".composer button[title*='Hands-free']");
await button.WaitForAsync(new() { Timeout = 60_000 });
await button.ClickAsync(new() { Timeout = 60_000 });

var finished = await Task.WhenAny(done.Task, Task.Delay(TimeSpan.FromMinutes(4)));
Console.WriteLine(finished == done.Task ? "BENCH COMPLETE" : "TIMED OUT waiting for the benchmark lines");
return finished == done.Task ? 0 : 1;
