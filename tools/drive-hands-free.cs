#:package Microsoft.Playwright@1.49.0
#:property JsonSerializerIsReflectionEnabledByDefault=true
// Live browser gate for the HANDS-FREE button in the SpawnDev.AI demo.
//
//   dotnet run tools/drive-hands-free.cs -- [url] [--headless]
//
// ⚠️ WHY THIS EXISTS. AiVoiceTests.HandsFreeTurn_HearsAnswersAndSpeaksBack calls TranscribeAsync,
// ChatStreamAsync and SpeakAsync back to back on AiWorkerClient. That proves three server APIs work in
// sequence. It touches NO microphone, NO endpointing, and NO speaker - so it passed green while the
// button a person actually clicks recorded a fixed 30 s of room tone, spent ~45 s transcribing it, and
// never made a sound. A test whose remarks say "this is the test the demo's hands-free button is backed
// by" and which never presses the button is a claim, not a gate. This one presses the button.
//
// What it measures, with timestamps, on the real UI:
//   1. how long the loop LISTENS before it closes the turn (endpointing, or the absence of it)
//   2. whether an assistant reply appears, and when
//   3. whether audio is ACTUALLY PLAYED OUT - AudioBufferSourceNode.start is hooked, so "it spoke" is an
//      observed browser event, never an inference from a status string the app wrote about itself.
//
// Audio in is supplied here, not by Chrome's fake device (which yields digital silence on this machine -
// measured in SpawnDev.ILGPU.ML/tools/probe-fake-mic.cs). Unlike drive-chat-voice.cs the clip plays ONCE
// and then goes quiet, which is the whole point: a working endpointer stops shortly after the talker does.
using Microsoft.Playwright;

var url = (args.FirstOrDefault(a => a.StartsWith("http")) ?? "http://localhost:5199").TrimEnd('/');
var headed = !args.Contains("--headless");
var profileDir = Path.Combine(Path.GetTempPath(), "spawndev-ai-handsfree-profile");
Directory.CreateDirectory(profileDir);

// WARNING - PAGE-RELATIVE, resolved against document.baseURI at fetch time. A ROOT-relative "/test-audio/..."
// can only ever address a site served at the origin root, so this gate could not be pointed at the
// GitHub Pages build (base /SpawnDev.AI/) - it 404d before the mic was ever faked.
const string WavPath = "test-audio/librivox-public-domain.wav";
// The clip is 4.0 s. Endpointing should close the turn within a second or so of it ending; the fixed
// window this gate was written against ran to MaxUtteranceSeconds (30 s) no matter what was said.
const double ClipSeconds = 4.0;
const double ListenBudgetSeconds = 12.0;

using var pw = await Playwright.CreateAsync();
await using var ctx = await pw.Chromium.LaunchPersistentContextAsync(profileDir, new()
{
    Headless = !headed,
    Channel = "chrome",   // ⚠️ Playwright's bundled chromium exposes a SOFTWARE WebGPU adapter.
    Args = new[] { "--use-fake-ui-for-media-stream", "--autoplay-policy=no-user-gesture-required" },
});

var page = await ctx.NewPageAsync();

// ⚠️ The profile is PERSISTENT so the models stay in OPFS between runs (re-downloading Whisper and
// ZipVoice every gate run is not affordable). The cost of that is a cached Blazor boot manifest, which
// after a rebuild 404s the freshly fingerprinted `_framework` files - the page looks alive and silently
// runs the OLD build, or nothing. Disabling the HTTP cache keeps the OPFS half and drops the stale half.
var cdp = await ctx.NewCDPSessionAsync(page);
await cdp.SendAsync("Network.enable");
await cdp.SendAsync("Network.setCacheDisabled",
    new Dictionary<string, object> { ["cacheDisabled"] = true });

var log = new List<string>();
// Echoed LIVE, not just collected. A gate whose only output arrives at the end is indistinguishable from
// a hung one for however long it runs, and this one legitimately runs for many minutes on a cold cache.
page.Console += (_, m) =>
{
    var line = $"{DateTime.UtcNow:HH:mm:ss} {m.Text}";
    log.Add(line);
    Console.WriteLine($"      | {line}");
};

// ⚠️ NAME the failing request. Chrome logs "Failed to load resource: 404" to the console with no URL, so
// a 404 arriving in the middle of the speak phase is otherwise an unattributable clue. Which URL 404s is
// the difference between "the voice model is missing" and "a favicon is missing".
page.Response += (_, r) =>
{
    if (r.Status >= 400)
    {
        var line = $"{DateTime.UtcNow:HH:mm:ss} HTTP {r.Status} {r.Url}";
        log.Add(line);
        Console.WriteLine($"      ! {line}");
    }
};
page.RequestFailed += (_, r) =>
{
    var line = $"{DateTime.UtcNow:HH:mm:ss} REQUEST FAILED {r.Url} ({r.Failure})";
    log.Add(line);
    Console.WriteLine($"      ! {line}");
};

// Fake mic + speaker tap, both installed before the app boots.
//   - getUserMedia returns our WAV, played ONCE (loop = false). The silence after 4 s is exactly the
//     signal an endpointer is supposed to act on.
//   - AudioBufferSourceNode.start is hooked so every clip the PAGE plays is recorded. Our own mic source
//     is tagged and excluded, so whatever remains is speech the app itself produced.
await page.AddInitScriptAsync(@"
(() => {
  window.__spoken = [];
  window.__micStartedAt = 0;
  const Src = AudioBufferSourceNode.prototype;
  const realStart = Src.start;
  Src.start = function(...a) {
    if (!this.__isFakeMic) {
      window.__spoken.push({
        t: performance.now(),
        duration: this.buffer ? this.buffer.duration : 0,
        sampleRate: this.buffer ? this.buffer.sampleRate : 0
      });
    }
    return realStart.apply(this, a);
  };

  const md = navigator.mediaDevices;
  if (!md) return;
  const real = md.getUserMedia.bind(md);
  md.getUserMedia = async (constraints) => {
    if (!constraints || !constraints.audio) return real(constraints);
    const ac = new AudioContext();
    if (ac.state === 'suspended') { try { await ac.resume(); } catch (e) {} }
    const bytes = await (await fetch(new URL('" + WavPath + @"', document.baseURI))).arrayBuffer();
    const clip = await ac.decodeAudioData(bytes);

    // ⚠️ The clip is followed by REAL SILENCE IN THE SAME BUFFER, not by the source ending. A
    // BufferSource that finishes stops feeding its MediaStreamDestination, so the page's capture simply
    // stops receiving frames - which is not what a microphone does and not what we are testing. A live
    // mic keeps delivering quiet room tone forever, and the endpointer's whole job is to notice that.
    // (Measured the difference: with the source ending, the demo's sample counter froze at 4.0s and the
    // fixed 30 s window never elapsed either, so the gate reported a 75 s hang that a real mic would not
    // have produced.)
    const tailSeconds = 40;
    const buffer = ac.createBuffer(1, clip.length + Math.floor(clip.sampleRate * tailSeconds), clip.sampleRate);
    buffer.copyToChannel(clip.getChannelData(0), 0, 0);

    const src = ac.createBufferSource();
    src.__isFakeMic = true;
    src.buffer = buffer;
    src.loop = false;              // speak once, then a quiet room
    const dest = ac.createMediaStreamDestination();
    src.connect(dest);
    src.start();
    window.__micStartedAt = performance.now();
    return dest.stream;
  };
})();");

int failed = 0;
void Fail(string m) { failed++; Console.WriteLine($"    FAIL {m}"); }
void Ok(string m) => Console.WriteLine($"    OK   {m}");

// ⚠️ Every "Transcribed Xs in Y ms" the status line shows, across turns. Whisper pads its input to a FIXED
// 30 s no matter how long the utterance was, so endpointing cannot make this number smaller - only warming
// can, and only if the cost is first-call shader compilation. One measurement cannot tell those apart;
// turn 1 versus turn 2 can. Quoting the cold number as the steady-state cost would be a guess.
var transcribeMs = new List<double>();
var seenStatuses = new HashSet<string>();
void NoteStatus(string s)
{
    if (!seenStatuses.Add(s)) return;
    var m = System.Text.RegularExpressions.Regex.Match(s, @"Transcribed ([\d.]+)s in (\d+) ms");
    if (m.Success) transcribeMs.Add(double.Parse(m.Groups[2].Value));
}

Console.WriteLine($"--- {url} (hands-free button, end to end)");
try
{
    await page.GotoAsync(url, new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 60_000 });

    // ⚠️ THE APP DOES NOT START ITSELF - see tools/README.md.
    Console.WriteLine("    clicking 'Start the AI server'");
    await page.ClickAsync("button:has-text(\"Start the AI server\")", new() { Timeout = 120_000 });
    await page.Locator(".composer textarea").WaitForAsync(new() { Timeout = 300_000 });
    Console.WriteLine("    app ready");

    // A real user gesture, so the output AudioContext is allowed to make noise.
    await page.Locator(".composer textarea").ClickAsync();

    var handsFree = page.Locator(".composer button[title*='Hands-free']");
    await handsFree.WaitForAsync(new() { Timeout = 30_000 });
    var t0 = DateTime.UtcNow;
    var lastWarmStatus = "";
    await handsFree.ClickAsync();
    Console.WriteLine($"    hands-free ON at t=0 (clip is {ClipSeconds:F1}s, then silence)");

    // ── 1. LISTENING: how long until the loop decides the turn is over? ──
    // ⚠️ The MIC button is the listening indicator even in hands-free, because both share `_listening`:
    // while capturing its title is "Stop and transcribe" and its text counts seconds; when listening ends
    // the title reverts to "Speak your message". That title flip IS the endpoint. (Match the title
    // EXACTLY as rendered - an invented "Stop recording" matches nothing and reports a false "never
    // stopped listening" 75 s later, which is a wrong diagnosis rather than a failed assertion.)
    // ⚠️ Timed from when the MICROPHONE OPENS, not from the click. Turning hands-free on now warms the
    // endpointer, recogniser and voice first, which on a cold cache is minutes - charging that to the
    // endpointer would report a two-minute "utterance" and hide the thing being measured. The fake mic
    // only starts playing when getUserMedia is called, so the clip and this clock start together.
    var micOpen = page.Locator(".composer button[title='Stop and transcribe']");
    var warmDeadline = DateTime.UtcNow.AddMinutes(20);
    while (DateTime.UtcNow < warmDeadline && await micOpen.CountAsync() == 0)
    {
        var ws = await StatusAsync(page);
        if (ws != lastWarmStatus) { lastWarmStatus = ws; Console.WriteLine($"    [t={(DateTime.UtcNow - t0).TotalSeconds,6:F1}s] warming: {ws}"); }
        await Task.Delay(1000);
    }
    var tListen = DateTime.UtcNow;
    Console.WriteLine($"    microphone open at t={(tListen - t0).TotalSeconds:F1}s (warm-up done)");

    var listenDeadline = DateTime.UtcNow.AddSeconds(75);
    double listenedFor = -1;
    string lastMicText = "";
    while (DateTime.UtcNow < listenDeadline)
    {
        var visible = await micOpen.CountAsync() > 0;
        if (visible) lastMicText = (await micOpen.First.InnerTextAsync()).Trim();
        else if (lastMicText.Length > 0) { listenedFor = (DateTime.UtcNow - tListen).TotalSeconds; break; }
        await Task.Delay(250);
    }

    if (listenedFor < 0)
        Fail($"the loop never stopped listening within 75 s (button last read \"{lastMicText}\")");
    else if (listenedFor > ListenBudgetSeconds)
        Fail($"listened {listenedFor:F1}s for a {ClipSeconds:F1}s utterance - budget is {ListenBudgetSeconds:F0}s. "
           + "The turn is being closed by a fixed timer, not by hearing the talker stop.");
    else
        Ok($"endpointed after {listenedFor:F1}s of a {ClipSeconds:F1}s utterance");

    // ── 2. TRANSCRIBE + ANSWER: an assistant bubble has to appear. ──
    // ⚠️ FINISHED assistant bubbles only. While the turn is in flight the page renders an extra
    // `.msg.assistant` placeholder carrying a blinking caret and the phase note, so a bare `.msg.assistant`
    // matches "Transcribing…" and reports a reply that has not happened.
    // ⚠️ FINISHED assistant bubbles only: NOT the in-flight one (blinking caret + phase note) and NOT the
    // speaking one. Both are `.msg.assistant`, so a looser selector reports "Transcribing…" or
    // "🔊 Preparing the voice…" as the model's reply.
    var assistant = page.Locator(".msg.assistant:not(:has(span.caret)):not(.speaking)");
    var replyDeadline = DateTime.UtcNow.AddMinutes(15);
    string reply = "";
    var lastStatus = "";
    while (DateTime.UtcNow < replyDeadline)
    {
        // The status line names the phase (Transcribing… / Speaking… / an error). Echo every CHANGE, so a
        // long wait shows what it is waiting ON rather than only how long it waited.
        var st = await StatusAsync(page);
        if (st != lastStatus) { lastStatus = st; NoteStatus(st); Console.WriteLine($"    [t={(DateTime.UtcNow - t0).TotalSeconds,6:F1}s] status: {st}"); }
        if (await assistant.CountAsync() > 0)
        {
            reply = (await assistant.Last.InnerTextAsync()).Trim();
            if (reply.Length > 0) break;
        }
        await Task.Delay(1000);
    }
    var repliedAt = (DateTime.UtcNow - t0).TotalSeconds;

    if (reply.Length == 0) Fail("no assistant reply arrived in 15 minutes");
    else Ok($"assistant replied at t={repliedAt:F1}s: \"{Trunc(reply, 90)}\"");

    // ── 3. SPEAK: did the page actually make a sound? ──
    // ⚠️ This is the assertion the suite had no equivalent of. A status line reading "Spoke 4.3s" is the
    // app's own account of itself; AudioBufferSourceNode.start firing is the browser's.
    // ⚠️ Read PRIMITIVES out of the page, never a JsonElement. Playwright hands back JsonElements owned by
    // a JsonDocument it disposes as soon as the call returns, so holding one and reading .GetProperty on a
    // later iteration throws "Operation is not valid due to the current state of the object" - which reads
    // as a page failure and is a defect in the gate.
    var speakDeadline = DateTime.UtcNow.AddMinutes(20);
    int spokenCount = 0;
    while (DateTime.UtcNow < speakDeadline)
    {
        spokenCount = await page.EvaluateAsync<int>("window.__spoken.length");
        if (spokenCount > 0) break;
        var st = await StatusAsync(page);
        if (st != lastStatus) { lastStatus = st; NoteStatus(st); Console.WriteLine($"    [t={(DateTime.UtcNow - t0).TotalSeconds,6:F1}s] status: {st}"); }
        await Task.Delay(2000);
    }
    var spokeAt = (DateTime.UtcNow - t0).TotalSeconds;

    if (spokenCount == 0)
    {
        Fail("THE APP NEVER PLAYED ANY AUDIO. It answered in text and stayed silent - "
           + "AudioBufferSourceNode.start was not called once outside the mic tap.");
        Console.WriteLine($"         status line reads: \"{await StatusAsync(page)}\"");
    }
    else
    {
        var d = await page.EvaluateAsync<double>("window.__spoken[0].duration");
        var sr = await page.EvaluateAsync<double>("window.__spoken[0].sampleRate");
        Ok($"spoke at t={spokeAt:F1}s: {d:F2}s of audio @ {sr:F0} Hz");
    }

    Console.WriteLine($"    status: \"{await StatusAsync(page)}\"");
}
catch (Exception ex)
{
    Fail($"{ex.GetType().Name}: {ex.Message.Split('\n')[0]}");
}
finally
{
    Console.WriteLine();
    Console.WriteLine("    --- page console (last 40) ---");
    foreach (var l in log.TakeLast(40)) Console.WriteLine($"      | {l}");
    await page.CloseAsync();
}

Console.WriteLine();
if (transcribeMs.Count > 0)
{
    Console.WriteLine($"    transcription: {string.Join(" then ", transcribeMs.Select(m => $"{m:F0} ms"))}"
                    + (transcribeMs.Count > 1
                        ? "  (turn 1 vs later turns - a big drop means the first call was compiling shaders, "
                          + "not that Whisper is slow)"
                        : "  (ONE sample: cannot tell first-call compilation from steady-state cost)"));
}
Console.WriteLine(failed == 0
    ? "HANDS-FREE verified end to end in a real browser: heard, answered, and SPOKE"
    : $"FAILED ({failed})");
return failed;

static string Trunc(string s, int n) => s.Length <= n ? s : s[..n] + "…";

// The footer status line is ALWAYS rendered (`p.status` in the message area is not), and the in-progress
// bubble's `.meta` carries the phase word - "Transcribing…", "Speaking…". Together they say what the loop
// believes it is doing, which is what makes a long wait diagnosable instead of merely long.
static async Task<string> StatusAsync(IPage p)
{
    var parts = new List<string>();
    // The mic button's own text is the AUDIO clock (seconds captured), which is not the same as wall time
    // and is what the 30 s safety ceiling actually measures. Without it, "still listening after 35 s" is
    // ambiguous between a stuck endpointer and a ceiling that simply has not been reached yet.
    var mic = p.Locator(".composer button[title='Stop and transcribe']");
    if (await mic.CountAsync() > 0)
    {
        var t = (await mic.First.InnerTextAsync()).Trim();
        if (t.Length > 0) parts.Add($"mic={t}");
    }
    var footer = p.Locator(".sdai-ftr span").First;
    if (await footer.CountAsync() > 0)
    {
        var s = (await footer.InnerTextAsync()).Trim();
        if (s.Length > 0) parts.Add(s);
    }
    var note = p.Locator(".msg.assistant:has(span.caret) .meta");
    if (await note.CountAsync() > 0)
    {
        var s = (await note.First.InnerTextAsync()).Trim();
        if (s.Length > 0) parts.Add($"[{s}]");
    }
    return parts.Count > 0 ? string.Join(" ", parts) : "(none)";
}
