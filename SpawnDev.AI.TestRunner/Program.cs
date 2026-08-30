using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

// SpawnDev.AI browser test harness - a port of the SpawnDev.SpawnJS.WebWorkers runner, which is why the
// console contract (READY: / TEST: / RESULTS:) is identical.
//
//   dotnet run --project SpawnDev.AI.TestRunner                     the fast suite (no model download)
//   dotnet run --project SpawnDev.AI.TestRunner -- --heavy          include the model-downloading tests
//   dotnet run --project SpawnDev.AI.TestRunner -- MultiTurn        only tests whose name contains that
//   dotnet run --project SpawnDev.AI.TestRunner -- --headed         watch it in a real browser window
//   dotnet run --project SpawnDev.AI.TestRunner -- --url http://... use an already running dev server
//   dotnet run --project SpawnDev.AI.TestRunner -- --cold          wipe the browser profile first
//   dotnet run --project SpawnDev.AI.TestRunner -- --dedicated     dedicated worker, so its console reaches
//                                                                 the window and model-load progress is
//                                                                 VISIBLE (a shared worker's is not)
//
// Exit code is the number of failed tests, so it is usable as a gate.
//
// ⚠️ The browser profile PERSISTS between runs (see LaunchAsync). Model weights are OPFS-cached, and OPFS
// lives in the profile, so a fresh context means every heavy run re-downloads every model from cold. That
// is what made the first interleaved images+chat run sit for 24 minutes with nothing to show. Use --cold
// deliberately when a cold load is the thing under test.

var filter = "";
var headed = false;
var heavy = false;
var cold = false;
var dedicated = false;
var verbose = false;
var externalUrl = "";
for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--headed": headed = true; break;
        case "--heavy": heavy = true; break;
        case "--cold": cold = true; break;
        case "--dedicated": dedicated = true; break;
        case "--verbose": verbose = true; break;
        case "--url": externalUrl = ++i < args.Length ? args[i] : ""; break;
        case "--filter": filter = ++i < args.Length ? args[i] : ""; break;
        case "-h":
        case "--help":
            Console.WriteLine("usage: [filter] [--filter <text>] [--heavy] [--headed] [--verbose] "
                            + "[--cold] [--dedicated] [--url <url>]");
            return 0;
        default:
            if (!args[i].StartsWith("-")) filter = args[i];
            break;
    }
}

var repoRoot = FindRepoRoot();
var demoProject = Path.Combine(repoRoot, "SpawnDev.AI.Demo", "SpawnDev.AI.Demo.csproj");
if (!File.Exists(demoProject))
{
    Console.Error.WriteLine($"Could not find SpawnDev.AI.Demo.csproj (looked in {demoProject})");
    return 1;
}

Process? server = null;
try
{
    var url = externalUrl;
    if (string.IsNullOrEmpty(url))
    {
        (server, url) = await StartServerAsync(demoProject);
        if (string.IsNullOrEmpty(url))
        {
            Console.Error.WriteLine("Dev server did not report an app url");
            return 1;
        }
    }

    // ?tests=1 is what makes the demo run the suite at all - without it a normal visitor just gets the app.
    var query = "?tests=1";
    if (heavy) query += "&heavy=1";
    // A DEDICATED worker shares its console with the window, so Playwright can see model-load progress.
    // A shared worker's console is invisible to page.Console, which makes a slow load look like a hang.
    if (dedicated) query += "&worker=dedicated";
    if (!string.IsNullOrEmpty(filter)) query += $"&filter={Uri.EscapeDataString(filter)}";
    return await RunAsync(url.TrimEnd('/') + "/" + query, headed, verbose, heavy, cold);
}
finally
{
    if (server is { HasExited: false })
    {
        try { server.Kill(entireProcessTree: true); } catch { /* best effort */ }
    }
}

// walks up from the assembly location to the folder holding the solution
static string FindRepoRoot()
{
    var dir = AppContext.BaseDirectory;
    while (!string.IsNullOrEmpty(dir))
    {
        if (Directory.GetFiles(dir, "*.slnx").Length > 0 || Directory.GetFiles(dir, "*.sln").Length > 0) return dir;
        dir = Path.GetDirectoryName(dir) ?? "";
    }
    return Directory.GetCurrentDirectory();
}

static async Task<(Process?, string)> StartServerAsync(string demoProject)
{
    Console.WriteLine("building and starting SpawnDev.AI.Demo...");
    var psi = new ProcessStartInfo("dotnet", $"run -c Release --project \"{demoProject}\"")
    {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
    };
    var process = Process.Start(psi);
    if (process == null) return (null, "");

    var urlFound = new TaskCompletionSource<string>();
    var appUrl = new Regex(@"(?:App url|Now listening on):\s*(http://\S+)", RegexOptions.IgnoreCase);
    process.OutputDataReceived += (_, e) =>
    {
        if (e.Data == null) return;
        var match = appUrl.Match(e.Data);
        if (match.Success) urlFound.TrySetResult(match.Groups[1].Value);
    };
    process.BeginOutputReadLine();
    process.BeginErrorReadLine();

    var completed = await Task.WhenAny(urlFound.Task, Task.Delay(TimeSpan.FromMinutes(5)));
    return (process, completed == urlFound.Task ? urlFound.Task.Result : "");
}

static async Task<int> RunAsync(string url, bool headed, bool verbose, bool heavy, bool cold)
{
    using var playwright = await Playwright.CreateAsync();
    await using var context = await LaunchAsync(playwright, headed, cold);
    var page = context.Pages.Count > 0 ? context.Pages[0] : await context.NewPageAsync();

    var finished = new TaskCompletionSource<string>();
    var results = new List<string>();
    page.Console += (_, msg) =>
    {
        var text = msg.Text;
        if (text.StartsWith("TEST: ")) results.Add(text[6..]);
        else if (text.StartsWith("RESULTS: ")) finished.TrySetResult(text[9..]);
        else if (text.StartsWith("READY: ")) Console.WriteLine($"  {text}");
        else if (verbose || msg.Type == "error") Console.WriteLine($"  [{msg.Type}] {text}");
    };
    page.PageError += (_, err) => Console.WriteLine($"  [pageerror] {err}");

    Console.WriteLine($"running {url}");
    // DOMContentLoaded, NOT network-idle: this app holds a SharedWorker open, so the network never goes
    // idle and a network-idle wait would time out while the suite is running perfectly well. The real
    // completion signal is the page's own "RESULTS:" line.
    await page.GotoAsync(url, new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 60_000 });

    // A heavy run downloads models (an LLM, and for the image tests an image model too) before the first
    // answer, so it gets a much longer ceiling than the fast suite.
    // ⚠️ This MUST exceed the largest per-test Timeout in the suite, or the runner reports TIMED OUT while
    // the test it is waiting on is still legitimately running - a harness that lies about its own subject.
    // InterleavedImagesAndChatSurviveRepeatedEviction currently sits at 90 minutes.
    var budget = heavy ? TimeSpan.FromMinutes(120) : TimeSpan.FromMinutes(10);
    var completed = await Task.WhenAny(finished.Task, Task.Delay(budget));

    Console.WriteLine();
    var failed = 0;
    foreach (var line in results)
    {
        // Name|Result|DurationMs|Detail
        var parts = line.Split('|', 4);
        if (parts.Length < 3) { Console.WriteLine(line); continue; }
        if (parts[1] == "FAIL") failed++;
        Console.WriteLine($"  {parts[1],-4}  {parts[0]} ({parts[2]}ms)");
        if (parts.Length > 3 && !string.IsNullOrWhiteSpace(parts[3])) Console.WriteLine($"        {parts[3]}");
    }
    Console.WriteLine();
    if (completed != finished.Task)
    {
        Console.WriteLine($"TIMED OUT after {budget.TotalMinutes:F0} min - the suite never reported a summary");
        return Math.Max(1, failed);
    }
    Console.WriteLine(finished.Task.Result);
    return failed;
}

static async Task<IBrowserContext> LaunchAsync(IPlaywright playwright, bool headed, bool cold)
{
    // PERSISTENT profile, on purpose. Model weights are OPFS-cached and OPFS lives in the browser profile,
    // so a fresh context re-downloads every model on every run - which turns a heavy run into a
    // multi-gigabyte download rather than a test of the code. tools/drive-ai-imgtest.cs pins a persistent
    // profile for exactly this reason ("so the SD-Turbo model OPFS-caches across runs => warm gen, not a
    // cold re-download"). It also makes the tests more representative: a returning user has a warm cache.
    var profileDir = Path.Combine(Path.GetTempPath(), "spawndev-ai-testrunner-profile");
    if (cold && Directory.Exists(profileDir))
    {
        Console.WriteLine($"  --cold: wiping {profileDir}");
        try { Directory.Delete(profileDir, recursive: true); } catch (Exception ex)
        { Console.WriteLine($"  [warn] could not wipe the profile: {ex.Message}"); }
    }
    Directory.CreateDirectory(profileDir);
    Console.WriteLine($"  profile: {profileDir}");

    // ⚠️ Prefer INSTALLED Chrome. Playwright's bundled chromium exposes a SOFTWARE WebGPU adapter, so a
    // GGUF decode either refuses to run or runs orders of magnitude slower - which reads as a hang rather
    // than a configuration problem. tools/drive-ai-demo.cs pins Channel="chrome" for the same reason.
    try
    {
        return await playwright.Chromium.LaunchPersistentContextAsync(profileDir, new()
        {
            Headless = !headed,
            Channel = "chrome",
        });
    }
    catch
    {
        Console.WriteLine("  [warn] installed Chrome not found - falling back to bundled chromium, whose "
                        + "WebGPU adapter is SOFTWARE; model-backed tests may be unusably slow");
        return await playwright.Chromium.LaunchPersistentContextAsync(profileDir, new() { Headless = !headed });
    }
}
