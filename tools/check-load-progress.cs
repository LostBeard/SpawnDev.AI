#:package Microsoft.Playwright@1.49.0
#:property JsonSerializerIsReflectionEnabledByDefault=true
// LOAD-PROGRESS gate: does the page TELL the user what the first minute is doing - and does the second
// visit skip the download?
//
// 🔴 WHY IT EXISTS. Captain, on the deployed demo: "there needs to be model download and loading progress
// indicators. it takes it roughly 1 minute to respond to the first message and the user has no idea what
// is going on or how long it will take." Then, on the first version of this gate: "it always downloads...
// have you tested loading from the cache and a refresh?"
//
// So it runs BOTH visits in one go:
//   1. COLD  - empty profile, first message pays the real 1.71 GB pull. Progress must report bytes,
//              percent and an ETA, then the GPU load, with no unexplained stretch.
//   2. WARM  - the page is REFRESHED and asked again. There must be NO download: the weights come from
//              OPFS. This is the half a wipe-every-run gate can never see.
//
// ⚠️ ?worker=dedicated, on Captain's instruction, and it is what makes phase 2 mean anything. A SHARED
// worker survives a page refresh, so the model would still be resident in VRAM and the second turn would
// prove only that a warm worker is warm. A dedicated worker dies with the page, so after the refresh the
// weights genuinely have to come back off disk. (It also puts the worker's console in the page.)
//
// ⚠️ A PERSISTENT profile, wiped at the start unless --keep. An EPHEMERAL context does not get a usable
// quota: MEASURED here, it reports `quota 4.00 GiB` and then fails at ~900 MB with "would cause the
// application to exceed its storage quota" - it measures the harness, not the app.
//
// This reads the rendered caption, not the API: the numbers being right in a JSON response is not the
// complaint. The complaint is that nothing reaches the screen.
//
//   dotnet run tools/check-load-progress.cs -- [url] [--keep]
using System.Text.RegularExpressions;
using Microsoft.Playwright;

var url = args.FirstOrDefault(a => !a.StartsWith("--")) ?? "http://localhost:5199/";
var keep = args.Contains("--keep");
// The dedicated worker is the default here, not a detail - see the header. --shared runs the app's own
// default configuration instead, where the message loop stays free and every stage is reported live.
var worker = args.Contains("--shared") ? "shared" : "dedicated";
var pageUrl = url.Contains('?') ? $"{url}&worker={worker}" : $"{url}?worker={worker}";
Console.WriteLine($"[progress] worker mode: {worker}");

var profile = Path.Combine(Path.GetTempPath(), "spawndev-ai-coldstart-profile");
if (!keep && Directory.Exists(profile))
{
    Console.WriteLine($"[progress] wiping {profile} - a cold start has to be genuinely cold");
    try { Directory.Delete(profile, recursive: true); }
    catch (Exception ex) { Console.WriteLine($"[progress] could not wipe the profile: {ex.Message}"); }
}
Directory.CreateDirectory(profile);

using var pw = await Playwright.CreateAsync();
await using var browser = await pw.Chromium.LaunchPersistentContextAsync(profile, new()
{
    Headless = false,
    Channel = "chrome",   // TJ's installed Chrome (hardware WebGPU); Playwright's chromium is software
});
var page = browser.Pages.Count > 0 ? browser.Pages[0] : await browser.NewPageAsync();
page.Console += (_, m) =>
{
    // Errors always; the model-load trace because a turn that ends early ends for a reason and the
    // reason is on the console, not in the caption.
    if (m.Type == "error" || m.Text.Contains("[model-load]") || m.Text.Contains("Error")
        || m.Text.Contains("[HF-CHAT]"))
        Console.WriteLine($"[console:{m.Type}] {m.Text}");
};
page.PageError += (_, e) => Console.WriteLine($"[pageerror] {e}");

int bad = 0;
void Require(bool ok, string what)
{
    Console.WriteLine(ok ? $"[progress] OK   {what}" : $"[progress] FAIL {what}");
    if (!ok) bad++;
}

// How long one caption may stand unchanged. Every caption carries a seconds counter, so a working ticker
// changes it about once a second; anything past this is the ticker itself being stalled, not a slow step.
const double MaxStillSeconds = 4.0;

// The longest any single caption stood unchanged, and which one it was.
//
// ⚠️ THE FINAL CAPTION IS EXCLUDED, and it has to be. Its life ends when the first token arrives and the
// caption is removed - a moment this probe cannot time from outside, because the bar lingers in the DOM
// until the next delta re-renders the bubble. Scoring that interval as a stall would fail every healthy
// turn. The freeze this check exists to catch (16.6s on "1.7 GB of 1.71 GB (99%)" in a dedicated worker)
// happened mid-sequence, between two captions, which is exactly what is measured here.
static (double Seconds, string Text) Stillest(List<(double At, string Text, string Width)> caps)
{
    var worst = (Seconds: 0.0, Text: "");
    for (int i = 0; i + 1 < caps.Count; i++)
    {
        var held = caps[i + 1].At - caps[i].At;
        if (held > worst.Seconds) worst = (held, caps[i].Text);
    }
    return worst;
}

// ── One visit: load the page, start the server, ask a question, and record every caption shown. ──
async Task<(List<(double At, string Text, string Width)> Captions, double Total, double CaptionsEndedAt,
            string Transcript)>
    VisitAsync(string label, bool reload)
{
    Console.WriteLine($"[progress] ── {label} ──");
    if (reload) await page.ReloadAsync(new() { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 60000 });
    else await page.GotoAsync(pageUrl, new() { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 60000 });

    var storage = await page.EvaluateAsync<string>(
        "async () => { const e = await navigator.storage.estimate(); "
        + "return `quota ${(e.quota/1073741824).toFixed(2)} GiB, used ${(e.usage/1048576).toFixed(0)} MiB`; }");
    Console.WriteLine($"[progress] storage: {storage}");

    await page.ClickAsync("button:has-text(\"Start the AI server\")", new() { Timeout = 30000 });
    await page.WaitForSelectorAsync("textarea", new() { Timeout = 180000 });

    await page.FillAsync("textarea", "What is the capital of France? Answer in one short sentence.");
    var t0 = DateTime.UtcNow;
    await page.PressAsync("textarea", "Enter");

    var captions = new List<(double At, string Text, string Width)>();
    // ⚠️ The last moment a caption was ON SCREEN, which is NOT the end of the turn. Once tokens stream the
    // caption is removed on purpose - the text is its own indicator - so measuring the final caption's
    // life to the end of the turn would score every healthy turn as a multi-second stall.
    double captionsEndedAt = 0;
    var done = page.WaitForSelectorAsync("textarea:not([disabled])", new() { Timeout = 300000 });
    while (!done.IsCompleted)
    {
        await Task.Delay(300);
        string text = "", width = "";
        try
        {
            // ⚠️ ONLY the progress caption inside .loadbar. The looser selector also matched the FINISHED
            // message's stats line ("24.8s - 1.1 tok/s"), which appears at the end of the turn and made
            // the last real caption look as though it had stood unchanged through the whole streaming
            // phase - a stall the app never had.
            text = await page.EvaluateAsync<string>(
                "() => { const e = document.querySelector('.msg.assistant .loadbar .meta'); "
                + "return e ? e.innerText.trim() : ''; }");
            width = await page.EvaluateAsync<string>(
                "() => { const e = document.querySelector('.msg.assistant .loadbar .fill'); "
                + "return e ? e.style.width : ''; }");
        }
        catch (PlaywrightException) { break; }   // navigated / closed
        if (text.Length == 0) continue;
        captionsEndedAt = (DateTime.UtcNow - t0).TotalSeconds;
        if (captions.Count == 0 || captions[^1].Text != text)
            captions.Add((captionsEndedAt, text, width));
    }
    await done;
    var elapsed = (DateTime.UtcNow - t0).TotalSeconds;

    Console.WriteLine($"[progress] {label}: {elapsed:F1}s, {captions.Count} distinct caption(s)");
    foreach (var c in captions) Console.WriteLine($"[progress]   {c.At,6:F1}s  bar={c.Width,-5}  {c.Text}");
    return (captions, elapsed, captionsEndedAt, await page.InnerTextAsync(".transcript"));
}

// ── 1. COLD: nothing cached, the visit a first-time user pays for. ─────────────────────────────────
var (cold, coldSecs, _, coldTranscript) = await VisitAsync("COLD (empty OPFS)", reload: false);

var downloads = cold.Where(c => c.Text.StartsWith("Downloading")).ToList();
Require(downloads.Count > 0, "cold: a download caption appeared");
Require(downloads.Any(c => Regex.IsMatch(c.Text, @"\d+(\.\d+)?\s*(MB|GB)\s+of\s+\d")),
    "cold: the download caption states bytes fetched of the total");
Require(downloads.Any(c => Regex.IsMatch(c.Text, @"\(\d+%\)")),
    "cold: the download caption states a percent");
// ⚠️ ANY download caption, not the FIRST. The downloader reports a rate of 0 until a quarter second of
// the attempt has elapsed (counting bytes carried in by a resume would show a wildly inflated speed
// otherwise), so the opening caption legitimately has no ETA - and whether this sampler catches that
// caption at all depends on where a 300 ms poll lands, which is the harness, not the app. "Never invent
// an ETA without a rate" is pinned deterministically in
// ModelLoadProgressTests.AnEtaIsOnlyGivenWhenItCanBeKnown.
Require(downloads.Any(c => c.Text.Contains("left")),
    "cold: the download caption states how much longer, once a rate exists");
// It MOVED. One caption that happens to say 3% is a frozen bar with a number on it.
Require(downloads.Count >= 3, $"cold: the download caption updated more than twice (saw {downloads.Count})");
Require(cold.Any(c => c.Text.Contains("onto the GPU")), "cold: a GPU load caption appeared");

// 🔴 NO UNEXPLAINED STRETCH AFTER THE LOAD. MEASURED before this was handled: the weight upload finished
// at 60.0s and the first token arrived at 78.5s, and for those 18.5s the caption reverted to the old
// "waiting for the first token" counter - the very caption this feature exists to replace, in the middle
// of the wait it was meant to explain.
int lastCold = cold.FindLastIndex(c => c.Text.StartsWith("Downloading") || c.Text.Contains("onto the GPU"));
var stranded = lastCold < 0 ? 0
    : cold.Skip(lastCold).Count(c => c.Text.StartsWith("waiting for the first token"));
Require(stranded == 0, $"cold: nothing fell back to the bare seconds counter after the load ({stranded} did)");

var widths = cold.Where(c => c.Width.Length > 0).Select(c => c.Width).Distinct().Count();
Require(widths >= 3, $"cold: the bar width advanced (saw {widths} distinct widths)");
Require(coldTranscript.Contains("Paris", StringComparison.OrdinalIgnoreCase),
    "cold: the turn produced the answer");

// 🔴 NOTHING MAY SIT STILL. This is the user-facing property under all of the above, and the one that
// caught the real defect: with ?worker=dedicated the caption froze on "1.7 GB of 1.71 GB (99%)" for 16.6
// seconds, because OPFSStream takes createSyncAccessHandle there and ILGPU compiles shaders
// synchronously - the worker's message loop does not run, so GET /ai/progress is not answered and the
// page's ticker stalled awaiting it. A frozen "99%" is indistinguishable from a stuck download, which is
// worse than the plain counter this feature replaced.
var stillest = Stillest(cold);
Require(stillest.Seconds <= MaxStillSeconds,
    $"cold: the caption never sat still longer than {MaxStillSeconds}s "
    + $"(worst was {stillest.Seconds:F1}s on \"{stillest.Text}\")");

// ── 2. WARM: refresh and ask again. The weights are on disk; nothing may be downloaded. ────────────
// 🔴 THE QUESTION THIS ANSWERS, in Captain's words: "it always downloads... have you tested loading from
// the cache and a refresh?" A refresh kills the DEDICATED worker, so the model is gone from VRAM and has
// to be read back - from OPFS if the cache works, from the hub if it does not. The two are indis-
// tinguishable from a stopwatch alone on a fast LAN, which is exactly why the caption is the evidence.
var (warm, warmSecs, _, warmTranscript) = await VisitAsync("WARM (after refresh)", reload: true);

var warmDownloads = warm.Where(c => c.Text.StartsWith("Downloading")).ToList();
Require(warmDownloads.Count == 0,
    warmDownloads.Count == 0
        ? "warm: nothing was re-downloaded after the refresh - the OPFS cache was used"
        : $"warm: the model DOWNLOADED AGAIN after a refresh ({warmDownloads[^1].Text}) - the cache is "
          + "not being used, and every visit costs the user the full pull again");
// 🔴 CAPTAIN, ON THE REFRESHED PAGE: "it didn't show a progress bar on the refreshed page where it loaded
// from cache." A cached load in a DEDICATED worker answers no progress poll at all - it blocks its own
// message loop reading OPFS synchronously - so there are no numbers to fill a bar with. That is a reason
// for an INDETERMINATE bar, not for no bar: the second visit is exactly where the user has been told the
// model is cached and is entitled to see that something is happening.
Require(warm.Any(c => c.Width.Length > 0),
    "warm: a progress bar was on screen while the cached model loaded");
Require(warmTranscript.Contains("Paris", StringComparison.OrdinalIgnoreCase),
    "warm: the turn produced the answer");
Require(warmSecs < coldSecs,
    $"warm: the second visit was faster than the first ({warmSecs:F1}s vs {coldSecs:F1}s)");
var warmStillest = Stillest(warm);
Require(warmStillest.Seconds <= MaxStillSeconds,
    $"warm: the caption never sat still longer than {MaxStillSeconds}s "
    + $"(worst was {warmStillest.Seconds:F1}s on \"{warmStillest.Text}\")");

await browser.CloseAsync();

Console.WriteLine($"[progress] cold {coldSecs:F1}s -> warm {warmSecs:F1}s "
                + $"(saved {coldSecs - warmSecs:F1}s by not re-downloading)");
Console.WriteLine(bad == 0
    ? "[progress] PASS - the cold turn reports what it is doing, and the refresh loads from cache."
    : $"[progress] FAIL - {bad} check(s) failed.");
return bad == 0 ? 0 : 1;
