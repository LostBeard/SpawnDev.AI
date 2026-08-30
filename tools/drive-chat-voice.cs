#:package Microsoft.Playwright@1.49.0
#:property JsonSerializerIsReflectionEnabledByDefault=true
// Live browser gate for VOICE INPUT in the SpawnDev.AI demo chat.
//
//   dotnet run tools/drive-chat-voice.cs -- [url]
//
// What it proves: clicking the 🎤 button records the microphone, sends the audio to the AI worker, and
// puts a correct transcript in the composer. That is the whole speech-IN half of speech-to-speech, driven
// exactly as a person would drive it.
//
// ⚠️ The audio is supplied HERE, not by Chrome's fake device. MEASURED in the ML repo with
// tools/probe-fake-mic.cs, which opens the microphone in plain browser JS with none of our code involved:
// Chrome's fake audio device yields DIGITAL SILENCE on this machine - 24 consecutive AnalyserNode readings
// of 0.0000 over 6 s - with --use-file-for-fake-audio-capture, with the default device, and with
// echoCancellation/noiseSuppression/autoGainControl disabled. Frames still arrive and sample counters still
// advance, so a gate can cheerfully report "9 seconds of audio captured" when every sample is zero, and
// Whisper turns that silence into confident, fluent, unrelated text. Replacing getUserMedia before boot
// with a looping BufferSource of a known WAV leaves the page's real capture path untouched - only the sound
// source is ours.
//
// The clip's transcript is KNOWN text rather than something itself transcribed:
//     "All LibriVox recordings are in the public domain."   (16 kHz mono, 4.0 s, Public Domain Mark 1.0)
// whisper-tiny renders "LibriVox" as "legal box" - a model limit, not a pipeline defect - so the assertion
// uses content words plus a word-overlap floor rather than an exact string.
using System.Text.RegularExpressions;
using Microsoft.Playwright;

var url = (args.FirstOrDefault(a => a.StartsWith("http")) ?? "http://localhost:5199").TrimEnd('/');
var profileDir = Path.Combine(Path.GetTempPath(), "spawndev-ai-voice-profile");
Directory.CreateDirectory(profileDir);

// Served by the ML demo's wwwroot copy; this gate fetches it from THIS app's origin, so the file has to be
// reachable here too. It is copied into the AI demo's wwwroot for exactly that reason.
const string WavPath = "/test-audio/librivox-public-domain.wav";

using var pw = await Playwright.CreateAsync();
await using var ctx = await pw.Chromium.LaunchPersistentContextAsync(profileDir, new()
{
    Headless = false,
    Channel = "chrome",
    Args = new[] { "--use-fake-ui-for-media-stream" },   // auto-grant; the stream itself is ours
});

var page = await ctx.NewPageAsync();
var log = new List<string>();
page.Console += (_, m) => log.Add(m.Text);

await page.AddInitScriptAsync(@"
(() => {
  const WAV = '" + WavPath + @"';
  const md = navigator.mediaDevices;
  if (!md) return;
  const real = md.getUserMedia.bind(md);
  md.getUserMedia = async (constraints) => {
    if (!constraints || !constraints.audio) return real(constraints);
    const ac = new AudioContext();
    if (ac.state === 'suspended') { try { await ac.resume(); } catch (e) {} }
    const bytes = await (await fetch(WAV)).arrayBuffer();
    const buffer = await ac.decodeAudioData(bytes);
    const src = ac.createBufferSource();
    src.buffer = buffer;
    src.loop = true;
    const dest = ac.createMediaStreamDestination();
    src.connect(dest);
    src.start();
    return dest.stream;
  };
})();");

int failed = 0;
Console.WriteLine($"--- {url} (chat voice input)");
try
{
    await page.GotoAsync(url, new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 60_000 });

    // ⚠️ THE APP DOES NOT START ITSELF. Until StartAsync runs, `_ready` is false and the page shows only
    // a "Start the AI server" button - the composer is not in the DOM at all, so waiting for it waits
    // forever on a page that looks alive and logs nothing. Click Start first, then wait for the composer,
    // which appearing IS the signal that the worker and WebGPU came up.
    // Same selector as tools/drive-ai-demo.cs, which already encoded this - reading that sibling first
    // would have saved the stall.
    Console.WriteLine("    clicking 'Start the AI server'");
    await page.ClickAsync("button:has-text(\"Start the AI server\")", new() { Timeout = 120_000 });

    var composer = page.Locator(".composer textarea");
    await composer.WaitForAsync(new() { Timeout = 300_000 });   // worker boot + WebGPU device
    Console.WriteLine("    app ready");

    // The mic button is the one whose title offers to speak.
    var mic = page.Locator(".composer button[title*='Speak']");
    await mic.WaitForAsync(new() { Timeout = 30_000 });
    await mic.ClickAsync();

    // Recording state: the button switches to the stop title and starts counting.
    var stop = page.Locator(".composer button[title*='Stop']");
    await stop.WaitForAsync(new() { Timeout = 60_000 });
    Console.WriteLine("    recording…");

    await Task.Delay(9000);   // two passes of the 4 s clip, so one sentence is intact

    var elapsed = (await stop.InnerTextAsync()).Trim();
    Console.WriteLine($"    button reads: {elapsed}");

    // Stop. Force + long timeout: the worker may be loading Whisper, which saturates the WASM thread and
    // fails Playwright's stability check even though the button is present and enabled.
    await stop.ClickAsync(new() { Timeout = 180_000, Force = true });
    Console.WriteLine("    transcribing (first run downloads Whisper)…");

    // The transcript lands in the COMPOSER, to be edited before sending - that is the feature.
    var deadline = DateTime.UtcNow.AddMinutes(15);
    string text = "";
    while (DateTime.UtcNow < deadline)
    {
        text = (await composer.InputValueAsync()).Trim();
        if (text.Length > 0) break;
        await Task.Delay(2000);
    }

    if (text.Length == 0)
    {
        failed++;
        Console.WriteLine("    FAIL the composer is still empty - no transcript arrived");
    }
    else
    {
        var norm = Regex.Replace(text.ToLowerInvariant(), @"[^a-z0-9 ]", " ");
        norm = Regex.Replace(norm, @"\s+", " ").Trim();
        var refWords = "all librivox recordings are in the public domain".Split(' ');
        var got = norm.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        var overlap = refWords.Count(w => got.Contains(w)) / (double)refWords.Length;
        var missing = new[] { "recordings", "public domain" }.Where(w => !norm.Contains(w)).ToArray();

        if (missing.Length > 0 || overlap < 0.7)
        {
            failed++;
            Console.WriteLine($"    FAIL missing {string.Join(", ", missing.DefaultIfEmpty("nothing"))}; "
                            + $"overlap {overlap:P0} (floor 70%)");
            Console.WriteLine($"         got: \"{text}\"");
        }
        else
        {
            Console.WriteLine($"    OK   {overlap:P0} word overlap in the composer: \"{text}\"");
        }
    }
}
catch (Exception ex)
{
    failed++;
    Console.WriteLine($"    FAIL {ex.GetType().Name}: {ex.Message.Split('\n')[0]}");
    foreach (var l in log.TakeLast(10)) Console.WriteLine($"      | {l}");
}
finally
{
    foreach (var l in log.Where(x => x.Contains("Transcrib") || x.Contains("Microphone")))
        Console.WriteLine($"    {l}");
    await page.CloseAsync();
}

Console.WriteLine();
Console.WriteLine(failed == 0
    ? "chat VOICE INPUT verified end to end in a real browser"
    : $"FAILED ({failed})");
return failed;
