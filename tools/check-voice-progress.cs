#:package Microsoft.Playwright@1.49.0
#:property JsonSerializerIsReflectionEnabledByDefault=true
// VOICE gate: does preparing a voice and speaking a reply SAY what they are doing - and how long is the
// pause between spoken chunks?
//
// 🔴 WHY IT EXISTS. Captain, on the deployed demo: "progress bars for the voice model(s) and 'Preparing a
// voice' and there is still a long pause between tts streamed 'chunks'".
//
// Two separate questions, one run:
//   1. REPORTING - choosing a voice loads the ZipVoice models (two int8 graphs plus a 54 MB vocoder out of
//      a remote archive, MEASURED 88.7 s cold) and used to show one static sentence for the whole time.
//      A bar has to be on screen for both the preparation and the first synthesis.
//   2. THE GAP - the reply is synthesised a chunk ahead and played one at a time, so a pause between
//      chunks has three possible causes needing opposite fixes: the next chunk not being rendered yet,
//      the wait for the previous clip overshooting, or the cost of starting a clip. The page logs
//      `[HF-SPEAK] chunk n/m: silence X ms (waited Y ms for synthesis...)` and this reads those numbers
//      out rather than guessing at them.
//
// ⚠️ ?worker=dedicated, because a SHARED worker's console does not reach the page and the chunk timings
// are console lines. This measures the gap, not the progress reporting, which is weakest here (see
// check-load-progress.cs).
//
//   dotnet run tools/check-voice-progress.cs -- [url] [--fresh]
using System.Text.RegularExpressions;
using Microsoft.Playwright;

var url = args.FirstOrDefault(a => a.StartsWith("http")) ?? "http://localhost:5199/";
var pageUrl = url.Contains('?') ? $"{url}&worker=dedicated" : $"{url}?worker=dedicated";

// Kept between runs by default: this gate is about the VOICE, and re-downloading the 1.71 GB chat model
// every time buys nothing. --fresh wipes it when the cold voice load is what you want to watch.
var profile = Path.Combine(Path.GetTempPath(), "spawndev-ai-voice-progress-profile");
if (args.Contains("--fresh") && Directory.Exists(profile))
{
    Console.WriteLine($"[voice] wiping {profile}");
    try { Directory.Delete(profile, recursive: true); } catch (Exception ex) { Console.WriteLine($"[voice] {ex.Message}"); }
}
Directory.CreateDirectory(profile);

using var pw = await Playwright.CreateAsync();
await using var browser = await pw.Chromium.LaunchPersistentContextAsync(profile, new()
{
    Headless = false,
    Channel = "chrome",
});
var page = browser.Pages.Count > 0 ? browser.Pages[0] : await browser.NewPageAsync();

var chunkLines = new List<string>();
page.Console += (_, m) =>
{
    if (m.Text.Contains("[HF-SPEAK]")) { chunkLines.Add(m.Text); Console.WriteLine($"[voice] {m.Text}"); }
    else if (m.Type == "error") Console.WriteLine($"[voice] [console error] {m.Text}");
};

int bad = 0;
void Require(bool ok, string what)
{
    Console.WriteLine(ok ? $"[voice] OK   {what}" : $"[voice] FAIL {what}");
    if (!ok) bad++;
}

// Every distinct progress caption currently on screen, with its bar width.
async Task<(string Text, string Width)> SampleAsync()
{
    try
    {
        var text = await page.EvaluateAsync<string>(
            "() => { const e = document.querySelector('.loadbar .meta, .loadbar .sethint'); "
            + "return e ? e.innerText.trim() : ''; }");
        var width = await page.EvaluateAsync<string>(
            "() => { const e = document.querySelector('.loadbar .fill'); return e ? e.style.width : ''; }");
        return (text, width);
    }
    catch (PlaywrightException) { return ("", ""); }
}

Console.WriteLine($"[voice] goto {pageUrl}");
await page.GotoAsync(pageUrl, new() { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 60000 });
await page.ClickAsync("button:has-text(\"Start the AI server\")", new() { Timeout = 30000 });
await page.WaitForSelectorAsync("textarea", new() { Timeout = 180000 });
Console.WriteLine("[voice] worker + WebGPU up");

// ── 1. PREPARING A VOICE ───────────────────────────────────────────────────────────────────────────
// A bundled voice needs nothing recorded first, so this works on a fresh profile.
var options = await page.EvalOnSelectorAllAsync<string[]>(
    ".voice-picker option", "els => els.map(e => e.value).filter(v => v.length > 0)");
if (options.Length == 0) { Console.WriteLine("[voice] FAIL no selectable voice in the picker"); return 1; }
var voice = options[^1];
Console.WriteLine($"[voice] preparing voice '{voice}'");

var prepCaptions = new List<(double At, string Text, string Width)>();
var t0 = DateTime.UtcNow;
await page.SelectOptionAsync(".voice-picker", voice);
while ((DateTime.UtcNow - t0).TotalSeconds < 300)
{
    var (text, width) = await SampleAsync();
    if (text.Length > 0 && (prepCaptions.Count == 0 || prepCaptions[^1].Text != text))
        prepCaptions.Add(((DateTime.UtcNow - t0).TotalSeconds, text, width));
    var busy = await page.EvaluateAsync<bool>("() => !!document.querySelector('.voice-picker[disabled]')");
    if (!busy && prepCaptions.Count > 0) break;
    if (!busy && (DateTime.UtcNow - t0).TotalSeconds > 3) break;   // already prepared - nothing to show
    await Task.Delay(250);
}
Console.WriteLine($"[voice] voice ready in {(DateTime.UtcNow - t0).TotalSeconds:F1}s, "
                + $"{prepCaptions.Count} caption(s)");
foreach (var c in prepCaptions) Console.WriteLine($"[voice]   {c.At,6:F1}s  bar={c.Width,-5}  {c.Text}");

Require(prepCaptions.Count > 0,
    "preparing a voice showed a progress bar (it loads the ZipVoice models the first time)");

// ── 2. THE CHAT TURN, so a voice chosen before the model is loaded is not the only thing measured ──
// ⚠️ SPEAKING IS NOT DRIVEN HERE, and that is a limit of this gate rather than a choice: a reply is only
// spoken when HANDS-FREE is on, which needs a microphone. tools/drive-hands-free.cs owns that path and
// reports the inter-chunk silence from the browser's own playback events. Anything printed below about
// chunks comes from the page's console for free, and is reported rather than asserted.
await page.FillAsync("textarea", "Say hello in one short sentence.");
var turnStarted = DateTime.UtcNow;
await page.PressAsync("textarea", "Enter");
await page.WaitForSelectorAsync("textarea:not([disabled])", new() { Timeout = 420000 });
Console.WriteLine($"[voice] turn done in {(DateTime.UtcNow - turnStarted).TotalSeconds:F1}s");

var gaps = new List<double>();
foreach (var line in chunkLines)
{
    var m = Regex.Match(line, @"silence (\d+(?:\.\d+)?) ms");
    if (m.Success) gaps.Add(double.Parse(m.Groups[1].Value));
}
Console.WriteLine(gaps.Count == 0
    ? "[voice] no inter-chunk timings (nothing was spoken - see drive-hands-free.cs)"
    : $"[voice] inter-chunk silence over {gaps.Count} gap(s): min {gaps.Min():F0} ms, "
      + $"median {gaps.OrderBy(g => g).ElementAt(gaps.Count / 2):F0} ms, max {gaps.Max():F0} ms");

await browser.CloseAsync();
Console.WriteLine(bad == 0 ? "[voice] PASS" : $"[voice] FAIL - {bad} check(s) failed.");
return bad == 0 ? 0 : 1;
