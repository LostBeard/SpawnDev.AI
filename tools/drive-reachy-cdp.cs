#:package Microsoft.Playwright@1.49.0
#:property JsonSerializerIsReflectionEnabledByDefault=true
// Drive the Reachy path in a browser SOMEONE ELSE is already signed into.
//
//   dotnet run tools/drive-reachy-cdp.cs                 attach, report, run the motion self-test
//   dotnet run tools/drive-reachy-cdp.cs -- --speak      also make a bodied character speak
//   dotnet run tools/drive-reachy-cdp.cs -- --cdp http://localhost:9222
//   dotnet run tools/drive-reachy-cdp.cs -- --expect-build 2026-09-16T14:39:11Z
//
// 🔴 WHY THIS ATTACHES INSTEAD OF LAUNCHING. Every other gate here launches its own Chrome with its own
// profile. The Reachy WebRTC path cannot be reached that way: it needs a Hugging Face OAuth sign-in and a
// robot registered to that account, and nothing can automate an HF login. So the one configuration in
// which this path is testable is a browser a PERSON signed in - and Playwright can attach to that over
// CDP instead of starting a fresh, signed-out one.
//
// ⚠️ IT IS SOMEBODY'S REAL BROWSER. Do not close it, do not clear storage, do not navigate their tab away
// from what they were doing.
//
// 🔴 AND CLOSE WHAT YOU OPEN. This comment used to CLAIM it cleaned up and did not. A left-open demo tab
// is not a stray tab: the app loads a ~1.8 GB model into RAM and onto the GPU, writes it to OPFS, and
// keeps a SHARED worker alive that OUTLIVES the page - so two loaded tabs pegged a 16 GB machine's RAM,
// VRAM and disk until someone noticed. The page this tool opens is tracked in `opened` and closed in the
// finally below, whatever happens.
using Microsoft.Playwright;
using System.Runtime.CompilerServices;

var cdp = ArgValue("--cdp") ?? "http://localhost:9222";
var doSpeak = args.Contains("--speak");
var expectBuild = ArgValue("--expect-build") ?? LastDeployedBuild();
const string AppHost = "static.hf.space";
// 🔴 ?worker=dedicated IS NOT OPTIONAL. A SHARED worker's console never reaches page.Console, and this
// whole test reads console lines - [reachy-speak] play start/end is the only evidence a clip reached the
// robot. Against a shared worker the run looks silent and reports a failure that did not happen.
const string AppUrl = "https://lostbeard-spawndev-ai.static.hf.space/?worker=dedicated";
// ⚠️ NOT just "textarea". Once the character editor is in the DOM there are THREE, and a bare textarea
// selector is a strict-mode violation that fails the run at the last step, after all the expensive parts
// have already succeeded. The composer is the single-row one.
const string Composer = "textarea[placeholder^=\"Message\"]";
// The button carries the model SIZE when one is known ("Start the AI server (1.2 GB model)"), so match
// on the stable prefix, never the whole label.
const string StartText = "Start the AI server";

using var pw = await Playwright.CreateAsync();

IBrowser browser;
try
{
    browser = await pw.Chromium.ConnectOverCDPAsync(cdp);
}
catch (Exception ex)
{
    Console.WriteLine($"[cdp] cannot attach at {cdp}: {ex.Message}");
    Console.WriteLine("[cdp] start Chrome with --remote-debugging-port=9222 and sign in to Hugging Face.");
    return 2;
}

var ctx = browser.Contexts.FirstOrDefault();
if (ctx == null) { Console.WriteLine("[cdp] attached, but the browser has no context."); return 2; }

// The app is served from static.hf.space; when opened through the Space listing it is an IFRAME inside a
// huggingface.co page, so the frame is what we drive, not the tab.
IFrame? app = null;
IPage? host = null;
IPage? opened = null;    // only ever a page WE created, so only ever a page we may close
IPage? adopted = null;   // an app tab that was ALREADY open, which we reload rather than replace

// Console lines are the instrument for everything below: the app stamps [BUILD] on startup, gestures log
// [reachy], the robot speaker logs [reachy-speak] play start/end, and the speech loop logs [HF-SPEAK].
//
// 🔴 HOOKED BEFORE NAVIGATION, not after. [BUILD] is the FIRST line the app writes, so a listener attached
// after GotoAsync misses the one line that says whether this page is even running the code under test.
var log = new List<string>();
void Hook(IPage p)
{
    // ALWAYS CAPTURE ERRORS, whatever the filter says. An unhandled exception on a runtime callback
    // EXITS the .NET WASM runtime and takes the page's UI with it - from outside that looks like an
    // element "disappearing", and every locator after it times out pointing at the wrong thing. The one
    // line that says what actually happened is the one a topic filter throws away.
    p.PageError += (_, e) => Console.WriteLine($"[pageerror] {e}");
    p.Crash += (_, _) => Console.WriteLine("[pagecrash] the renderer process crashed");
    p.Console += (_, m) =>
    {
        if (m.Type == "error") Console.WriteLine($"[console.error] {m.Text}");
    };
    p.Console += (_, m) =>
{
    var t = m.Text;
    if (!t.Contains("[BUILD]") && !t.Contains("reachy", StringComparison.OrdinalIgnoreCase)
        && !t.Contains("HF-MIC") && !t.Contains("[capture]") && !t.Contains("[reachy-mood]") && !t.Contains("[scroll]")
        && !t.Contains("HF-SPEAK") && !t.Contains("ROOM")) return;
    lock (log) log.Add(t);
    Console.WriteLine($"[console] {t}");
    };
}

foreach (var p in ctx.Pages)
    foreach (var f in p.Frames)
        if (f.Url.Contains(AppHost, StringComparison.OrdinalIgnoreCase)) { app = f; host = p; break; }

// ⚠️ If the app is not currently loaded, open it DIRECTLY rather than through the Space listing. The
// listing embeds it in an iframe that comes and goes, and the Hugging Face token the SDK stores lives in
// the APP's origin (static.hf.space) - it signed in from there - so a top-level tab on that origin is
// signed in too, and is far easier to drive. A new tab, never the person's existing one.
// 🔴 THE SPACE LISTING HIDES THE APP FROM PLAYWRIGHT. Opened through huggingface.co the app is a
// CROSS-ORIGIN IFRAME, and Playwright over CDP does not surface out-of-process iframes as frames at all -
// so the app is running, visible to the person, and invisible here. Worse, that iframe HOLDS THE ROBOT
// SESSION: the signalling server only auto-picks a FREE robot, so opening a second tab reports "No
// reachable robots" and the run fails for a reason that has nothing to do with the code.
//
// So take over that tab rather than adding one. Navigating it to the app directly makes the same app a
// TOP-LEVEL page - drivable, and its old session is released on unload, which frees the robot for the
// page that replaces it. One tab, one model load, one session.
var listing = ctx.Pages.FirstOrDefault(p => p.Url.Contains("huggingface.co/spaces", StringComparison.OrdinalIgnoreCase));
if (app == null && listing != null)
{
    Console.WriteLine("[cdp] the app is an iframe on the Space listing, which Playwright cannot see - "
                    + "taking that tab to the app directly...");
    Hook(listing);
    await listing.GotoAsync(AppUrl, new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 60000 });
    host = listing;
    app = listing.MainFrame;
    adopted = listing;   // not ours to close - reloaded on the way out, like any adopted tab
}

if (app == null)
{
    Console.WriteLine($"[cdp] app not loaded; opening {AppUrl} in a new tab...");
    var page = await ctx.NewPageAsync();
    Hook(page);
    await page.GotoAsync(AppUrl, new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 60000 });
    host = page;
    app = page.MainFrame;
    opened = page;
}

// 🔴 AN ALREADY-OPEN APP TAB IS NOT A FREE HEAD START - IT IS A STALE BUILD WAITING TO LIE.
// The tab was loaded at some earlier point, which by definition is before the deploy this run is meant to
// test, and it has already printed its [BUILD] line to a console nobody was listening to. Adopting it as
// found means testing yesterday's code and reporting the result as today's. So: hook it, then reload it.
// Reloading is safe in a way that opening a second tab is not - two app tabs are two full model loads.
if (opened == null)
{
    Hook(host!);
    adopted = host;
    // ⚠️ RELOAD TO A CLEAN URL, never a bare reload. A previous run's OAuth round trip leaves ?code= in
    // the address bar, and an authorization code is SINGLE USE - reloading with a spent one makes the SDK
    // start an exchange that cannot succeed, on every load, forever. Navigating to AppUrl also restores
    // ?worker=dedicated, which the redirect drops and which is what makes the worker console readable.
    Console.WriteLine("[cdp] an app tab was already open - reloading it so this run tests the deployed build...");
    await host!.GotoAsync(AppUrl, new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 60000 });
    app = host.MainFrame;
}

Console.WriteLine($"[cdp] attached. tab={host!.Url}");
Console.WriteLine($"[cdp] app frame={app.Url}");

var fails = new List<string>();
try
{
    // ── Is the app actually up? ─────────────────────────────────────────────────────────────────────
    await EnsureStartedAsync();

    // ── Is this even the build we are testing? ──────────────────────────────────────────────────────
    // 🔴 THE FIRST QUESTION, ASKED FIRST. A hosted page is served through a CDN and a browser cache, so
    // the page can be running code from before the deploy while every symptom points at the source. Every
    // failure below is worthless until this line agrees with what was last deployed - and a mismatch is
    // reported as a mismatch rather than debugged as a bug.
    string? pageBuild;
    lock (log) pageBuild = log.FirstOrDefault(l => l.Contains("[BUILD]"));
    if (pageBuild == null)
        fails.Add("the page printed no [BUILD] line - it is running a build from before the stamp existed");
    else
    {
        Console.WriteLine($"[cdp] build: {pageBuild.Trim()}");
        if (expectBuild != null && !pageBuild.Contains(expectBuild))
            fails.Add($"STALE PAGE: expected the build deployed at {expectBuild}, the page is running "
                    + $"'{pageBuild.Trim()}'. Hard-reload or wait for the Space to finish rebuilding; "
                    + "nothing below this line is evidence about the current code.");
    }

    // ── LAYOUT: does the page still hold together? ───────────────────────────────────────────────
        // 🔴 A LAYOUT BUG IS INVISIBLE TO EVERY FUNCTIONAL TEST. Every feature can work perfectly while the
        // conversation scrolls off the bottom of the window and the composer is unreachable - which is
        // exactly what happened: `.transcript` is a flex child with `overflow-y: auto` and no
        // `min-height: 0`, so it grew instead of scrolling. These are the two facts that say the shell is
        // intact, and both are measurable.
        var layout = await app.EvaluateAsync<System.Text.Json.JsonElement>(@"() => {
            const t = document.querySelector('.transcript');
            const c = document.querySelector('.composer');
            const panels = document.querySelectorAll('.settings');
            return {
                scrollable: t ? getComputedStyle(t).overflowY : 'missing',
                minHeight: t ? getComputedStyle(t).minHeight : 'missing',
                grows: t ? (t.scrollHeight > t.clientHeight + 4) : false,
                composerInView: c ? (c.getBoundingClientRect().bottom <= window.innerHeight + 1) : false,
                openPanels: panels.length
            };
        }");
        Console.WriteLine($"[cdp] layout: {layout}");

        // min-height:auto is the defect. Naming it directly means the failure says what to change.
        if (layout.GetProperty("minHeight").GetString() is "auto" or "missing")
            fails.Add("the transcript has min-height:auto, so overflow-y cannot engage - the conversation "
                    + "will grow and push the composer off screen instead of scrolling");
        if (!layout.GetProperty("composerInView").GetBoolean())
            fails.Add("the composer is below the bottom of the window - the user cannot reach the box "
                    + "they type into");
        if (layout.GetProperty("openPanels").GetInt32() > 1)
            fails.Add($"{layout.GetProperty("openPanels").GetInt32()} panels are open at once - they stack "
                    + "and push the conversation away");

        // 🔴 MEASURE THE CHAT WITH A PANEL OPEN. "Only one panel opens at a time" says nothing about how
        // much room is left for the conversation, and the honest answer was about twenty pixels - which,
        // as Captain put it, "might as well be zero". Closed-state layout passes happily while the open
        // state is unusable, so the open state is what has to be measured.
        await app.ClickAsync("button.gear:has-text(\"📦\")", new() { Timeout = 15000 });
        var withPanel = await app.EvaluateAsync<System.Text.Json.JsonElement>(@"() => {
            const t = document.querySelector('.transcript');
            const p = document.querySelector('.settings');
            const c = document.querySelector('.composer');
            return {
                transcript: t ? Math.round(t.getBoundingClientRect().height) : 0,
                panel: p ? Math.round(p.getBoundingClientRect().height) : 0,
                composerInView: c ? (c.getBoundingClientRect().bottom <= window.innerHeight + 1) : false,
                windowH: window.innerHeight
            };
        }");
        Console.WriteLine($"[cdp] layout with a panel open: {withPanel}");

        var chatH = withPanel.GetProperty("transcript").GetInt32();
        if (chatH < 120)
            fails.Add($"with a panel open the conversation is {chatH}px tall - the panel has taken the "
                    + "chat window rather than sharing the space with it");
        if (!withPanel.GetProperty("composerInView").GetBoolean())
            fails.Add("with a panel open the composer is off the bottom of the window");

        // Put it back the way it was found.
        await app.ClickAsync("button.gear:has-text(\"📦\")", new() { Timeout = 15000 });

    // ── DOES THE TRANSCRIPT FOLLOW AN IMAGE? ────────────────────────────────────────────────────────
    // 🔴 THE CASE THAT BROKE TWICE, AND THE ONLY ONE THAT COULD. A text reply is added and measured in
    // the same frame; a generated image is added with NO HEIGHT, decodes later, and adds ~514px below the
    // fold - so every scroll the app makes happens while the picture is still 0px tall. Text alone can
    // never catch this, which is why a gate full of passing chat turns sat alongside a transcript that
    // did not scroll.
    Console.WriteLine("[cdp] generating an image to check the transcript follows it...");
    if (await app.Locator(".settings").CountAsync() == 0)
        await app.ClickAsync("button.gear:has-text(\"⚙️\")", new() { Timeout = 15000 });
    await app.FillAsync(Composer, "a lighthouse in a storm");
    await app.ClickAsync(".settings button.linkbtn:has-text(\"Draw what is typed\")", new() { Timeout = 15000 });

    var imgDeadline = DateTime.UtcNow.AddMinutes(4);
    System.Text.Json.JsonElement scroll = default;
    while (DateTime.UtcNow < imgDeadline)
    {
        scroll = await app.EvaluateAsync<System.Text.Json.JsonElement>(@"() => {
            const t = document.querySelector('.transcript');
            const imgs = [...t.querySelectorAll('img')];
            return {
                images: imgs.length,
                decoded: imgs.filter(i => i.complete && i.naturalHeight > 0).length,
                fromBottom: Math.round(t.scrollHeight - t.scrollTop - t.clientHeight),
                canScroll: t.scrollHeight > t.clientHeight + 1
            };
        }");
        if (scroll.GetProperty("decoded").GetInt32() > 0 && scroll.GetProperty("canScroll").GetBoolean())
        {
            await Task.Delay(1500);   // let the observer react, then read the settled position
            scroll = await app.EvaluateAsync<System.Text.Json.JsonElement>(@"() => {
                const t = document.querySelector('.transcript');
                return { fromBottom: Math.round(t.scrollHeight - t.scrollTop - t.clientHeight),
                         canScroll: t.scrollHeight > t.clientHeight + 1 };
            }");
            break;
        }
        await Task.Delay(2000);
    }
    Console.WriteLine($"[cdp] after the image: {scroll}");

    if (scroll.ValueKind == System.Text.Json.JsonValueKind.Undefined
        || !scroll.TryGetProperty("fromBottom", out var fb))
        fails.Add("could not measure the transcript after generating an image");
    else if (scroll.GetProperty("canScroll").GetBoolean() && fb.GetInt32() > 24)
        fails.Add($"the transcript is {fb.GetInt32()}px from the bottom after an image loaded - it did "
                + "not follow the content it just added");

    if (await app.Locator(".settings").CountAsync() > 0)
        await app.ClickAsync("button.gear:has-text(\"⚙️\")", new() { Timeout = 15000 });

    // ── The room panel, where the robot lives ───────────────────────────────────────────────────────
    if (await app.Locator(".settings.room").CountAsync() == 0)
        await app.ClickAsync("button.gear:has-text(\"🎭\")", new() { Timeout = 15000 });
    await app.WaitForSelectorAsync(".settings.room", new() { Timeout = 15000 });

    var status = await app.Locator(".settings.room .robotstatus").Last.TextContentAsync() ?? "";
    Console.WriteLine($"[cdp] robot status: {status.Trim()}");

    // ── Connect, unless a robot is already connected ────────────────────────────────────────────────
    // 🔴 CONNECTING CAN NAVIGATE THE PAGE AWAY. If there is no Hugging Face token yet, the SDK redirects
    // to HF to sign in and comes back to ?code=..., which RELOADS the app - so the click that started the
    // connection is on a document that no longer exists, and everything after it is waiting on a dead
    // frame. That is not a failure, it is the documented handoff; it just has to be ridden out and the
    // connect repeated on the page that comes back.
    for (var attempt = 1; attempt <= 2; attempt++)
    {
        // 🔴 "CONNECTED" HAS ITS OWN EVIDENCE - don't infer it from the connect button being unavailable.
        // That button is disabled while connected AND while a connect is in flight, so reading it as
        // "already connected" turned a busy moment into a silent pass that then failed three steps later
        // with a message that pointed nowhere. The Test motion button only exists when a robot is on the
        // other end, so it is the fact; everything else is a wait or a failure with a reason attached.
        if (await app.Locator(".settings.room button.chip:has-text(\"Test motion\")").CountAsync() > 0)
        {
            Console.WriteLine("[cdp] already connected to a robot");
            break;
        }

        var connectBtn = app.Locator(".settings.room button.chip:has-text(\"Connect my Reachy\")");
        if (await connectBtn.CountAsync() == 0)
        {
            fails.Add("the room panel has no 'Connect my Reachy' button at all");
            break;
        }
        if (!await connectBtn.IsEnabledAsync())
        {
            Console.WriteLine("[cdp] connect button is busy; waiting for it...");
            try { await connectBtn.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30000 }); } catch { }
            for (var w = 0; w < 30 && !await connectBtn.IsEnabledAsync(); w++) await Task.Delay(1000);
            if (!await connectBtn.IsEnabledAsync())
            {
                var why0 = await app.Locator(".settings.room .robotstatus").Last.TextContentAsync() ?? "";
                fails.Add($"'Connect my Reachy' never became enabled. Robot status: {why0.Trim()}");
                break;
            }
        }

        Console.WriteLine($"[cdp] connecting over WebRTC (attempt {attempt})...");
        var urlBefore = host.Url;
        await connectBtn.ClickAsync(new() { Timeout = 20000 });

        // autoConnect does auth, signalling, robot pick, session and wake - or the page leaves for HF.
        try
        {
            await app.WaitForSelectorAsync(".settings.room button.chip:has-text(\"Test motion\")",
                new() { Timeout = 60000 });
            break;
        }
        catch (TimeoutException)
        {
            if (!host.Url.Contains("code=") && host.Url == urlBefore) throw;
            Console.WriteLine("[cdp] came back from the Hugging Face sign-in; restarting the app...");
            app = host.MainFrame;
            await EnsureStartedAsync();
            if (await app.Locator(".settings.room").CountAsync() == 0)
                await app.ClickAsync("button.gear:has-text(\"🎭\")", new() { Timeout = 15000 });
            await app.WaitForSelectorAsync(".settings.room", new() { Timeout = 15000 });
        }
    }

    var connected = await app.Locator(".settings.room button.chip:has-text(\"Test motion\")").CountAsync() > 0;
    if (!connected)
    {
        var why = await app.Locator(".settings.room .robotstatus").Last.TextContentAsync() ?? "";
        fails.Add($"not connected to a robot: {why.Trim()}");
    }
    else
    {
        // ── The motion self-test: proves the pose FORMAT, which nothing else can ────────────────────
        Console.WriteLine("[cdp] running the motion self-test (watch the head lift and return)...");
        await app.ClickAsync(".settings.room button.chip:has-text(\"Test motion\")", new() { Timeout = 15000 });
        await Task.Delay(9000);
        var verdict = await app.Locator(".settings.room .robotstatus").Last.TextContentAsync() ?? "";
        Console.WriteLine($"[cdp] self-test: {verdict.Trim()}");
        if (!verdict.Contains("ROW-MAJOR") && !verdict.Contains("COLUMN-MAJOR"))
            fails.Add($"the motion self-test did not report a layout: {verdict.Trim()}");

        // ── THE SPEAKER, PROVED WITHOUT A MODEL ─────────────────────────────────────────────────────
        // 🔴 RUN THIS BEFORE THE REAL REPLY. Reaching the robot's speaker through a chat turn costs a
        // language model and then a cold voice-model load, and a failure anywhere in that chain looks
        // identical to a broken audio path. The tone isolates resample -> 16 kHz WAV -> upload -> play,
        // which is the part that has never been verified, and answers in seconds.
        Console.WriteLine("[cdp] testing the robot speaker (a 1-second tone should come out of Reachy)...");
        lock (log) log.Clear();
        await app.ClickAsync(".settings.room button.chip:has-text(\"Test speaker\")", new() { Timeout = 15000 });

        var toneDeadline = DateTime.UtcNow.AddSeconds(45);
        while (DateTime.UtcNow < toneDeadline)
        {
            lock (log)
                if (log.Any(l => l.Contains("[reachy-speak] play end"))) break;
            await Task.Delay(500);
        }

        string[] tone;
        lock (log) tone = log.ToArray();
        var toneVerdict = await app.Locator(".settings.room .robotstatus").Last.TextContentAsync() ?? "";
        Console.WriteLine($"[cdp] speaker test: {toneVerdict.Trim()}");

        if (!tone.Any(l => l.Contains("[reachy-speak] uploading")))
            fails.Add($"the speaker path was never entered: {toneVerdict.Trim()}");
        else if (!tone.Any(l => l.Contains("[reachy-speak] uploaded as")))
            fails.Add("the clip was encoded but the upload to the robot never completed");
        else if (!tone.Any(l => l.Contains("[reachy-speak] play end")))
            fails.Add("the clip uploaded and started but never finished playing");
        else
            Console.WriteLine("[cdp] ROBOT SPEAKER: tone encoded, uploaded and played to completion.");

        // -- THE ROBOT'S EARS, PROVED THE SAME WAY --------------------------------------------------
        // A robot that speaks through its own speaker but hears through the laptop is not in the room
        // with anyone. This does not need a person to talk: the array picks up the room, so chunks
        // arriving with a non-zero peak is the stream flowing. What it CANNOT check is intelligibility.
        Console.WriteLine("[cdp] checking that listening goes through the robot...");
        lock (log) log.Clear();
        await app.ClickAsync("button.primary:has-text(\"\U0001F4AC\U0001F50A\")", new() { Timeout = 15000 });

        var earsDeadline = DateTime.UtcNow.AddSeconds(25);
        var listenStatus = "";
        while (DateTime.UtcNow < earsDeadline)
        {
            listenStatus = (await app.Locator("footer.sdai-ftr span").First.TextContentAsync() ?? "").Trim();
            if (listenStatus.StartsWith("Listening", StringComparison.OrdinalIgnoreCase)) break;
            await Task.Delay(1000);
        }
        Console.WriteLine($"[cdp] listen status: {listenStatus}");

        // Let the array deliver a few seconds of room tone.
        await Task.Delay(8000);

        string[] earLog;
        lock (log) earLog = log.ToArray();
        var micLines = earLog.Where(l => l.Contains("[HF-MIC]")).ToArray();

        // Stop hands-free before doing anything else - leaving it on would have the room talking to
        // itself for the rest of the run.
        try { await app.ClickAsync("button.primary:has-text(\"Hands-free\")", new() { Timeout = 10000 }); }
        catch (Exception ex) { Console.WriteLine($"[cdp] could not stop hands-free: {ex.Message}"); }

        // WHICH MICROPHONE OPENED IS A LOG LINE, NOT THE STATUS. The status string is replaced within a
        // second by the live level readout, so asserting on it failed a run in which everything worked.
        var throughRobot = earLog.Any(l => l.Contains("listening through the robot"));
        var throughDevice = earLog.Any(l => l.Contains("listening through this device"));

        if (!listenStatus.StartsWith("Listening", StringComparison.OrdinalIgnoreCase))
            fails.Add($"listening never started: {listenStatus}");
        else if (throughDevice && !throughRobot)
            fails.Add("listening opened THIS DEVICE's microphone while a robot was connected - the "
                    + "robot's ears were not used");
        else if (!throughRobot)
            fails.Add("listening started but never said which microphone it opened");
        else if (micLines.Length == 0)
            fails.Add("listening says it is using the robot, but no audio arrived from it at all");
        else
        {
            Console.WriteLine($"[cdp] ROBOT EARS: {micLines.Length} mic report(s), last: {micLines[^1]}");
            // A stream that delivers only digital silence is a connected-but-dead microphone, which is
            // exactly what the SDK's outbound placeholder would look like if it were ever used by mistake.
            var silent = micLines.All(l => l.Contains("raw peak=0.0000"));
            if (silent)
                fails.Add("the robot's audio arrived but every chunk was pure silence - the stream is "
                        + "connected to something that is not a microphone");
        }

        if (doSpeak)
        {
            // ── Does a bodied character speak OUT OF THE ROBOT? ─────────────────────────────────────
            // The speaker path is the one thing no harness could reach, so this is the whole point: send
            // a turn and watch for the robot-speaker timeline rather than the page's own audio.
            Console.WriteLine("[cdp] sending a turn; watching for [reachy-speak]...");
            lock (log) log.Clear();
            // 🔴 ASK FOR WORDS, EXPLICITLY. "Say hello in one short sentence" invites an EMBODIED character
            // to answer with a gesture, and it did: three runs in a row replied with nothing but
            // "*tilts head slightly, then waves with both antennae*". Stage directions are correctly not
            // spoken, so there was no audio to find - and the gate reported that as a broken speaker path
            // for nine minutes at a time. The prompt has to make speech the only way to comply.
            // Asks for BOTH halves: words to speak and an action to perform. A reply with only one of
            // them cannot exercise the path where they happen together, which is every real reply.
            await app.FillAsync(Composer, "Say hello, and tilt your head while you do.");
            await app.Locator(Composer).PressAsync("Enter");

            // ⚠️ THE VOICE MODEL MAY BE COLD. Reloading the tab drops it, and the first chunk after that
            // is a model load plus a synthesis - documented in this repo at 88.7 s cold for the load
            // alone. Four minutes ran out mid-render and the run reported a failure that had not
            // happened yet.
            // ⚠️ WATCH THE PAGE, NOT ONLY THE CONSOLE. The speak path reports where it has got to by
            // writing the page's status line ("Generating speech…", voice-model progress), not by logging
            // - so a run stuck in a cold model load looks IDENTICAL to one that never started, for nine
            // minutes, and then reports a timeout that names nothing. Echoing the footer makes the wait
            // legible while it happens.
            var deadline = DateTime.UtcNow.AddMinutes(9);
            var lastStatus = "";
            var lastReplyLength = -1;
            while (DateTime.UtcNow < deadline)
            {
                lock (log)
                    if (log.Any(l => l.Contains("[reachy-speak] play end"))) break;

                try
                {
                    var now = (await app.Locator("footer.sdai-ftr span").First.TextContentAsync() ?? "").Trim();
                    if (now.Length > 0 && now != lastStatus)
                    {
                        lastStatus = now;
                        Console.WriteLine($"[status] {now}");
                        // Nothing is coming. Waiting out the remaining minutes proves nothing.
                        if (now.Contains("action only", StringComparison.OrdinalIgnoreCase)) break;
                    }

                    // ⚠️ THE FOOTER GOES STALE DURING GENERATION. While the model is writing, progress is
                    // reported through `_busyNote` and the reply bubble, NOT through `_status` - so the
                    // footer holds whatever it last said and a busy run looks like a wedged one. The
                    // bubble is the honest signal: it grows.
                    var bubble = (await app.Locator(".msg.assistant .text").Last.TextContentAsync() ?? "").Trim();
                    if (bubble.Length != lastReplyLength)
                    {
                        lastReplyLength = bubble.Length;
                        Console.WriteLine($"[writing] {bubble.Length} chars: "
                            + (bubble.Length > 90 ? bubble[..90] + "…" : bubble));
                    }
                }
                catch { /* neither is load-bearing for this wait */ }

                await Task.Delay(2000);
            }

            // 🔴 PRINT WHAT THE CHARACTER ACTUALLY WROTE. Without it, "no audio" has at least two causes
            // that look identical from outside: the model replied with nothing but a stage direction, or
            // it replied normally and the stage-direction splitter lifted the words out too. Those need
            // opposite fixes - one is the prompt, the other is SpokenText - and guessing between them
            // costs a full run each time.
            // ⚠️ READ EVERY ROLE, NOT JUST THE ASSISTANT'S. When a turn fails, the app replaces the reply
            // with a SYSTEM message - so ".msg.assistant" is simply absent and the locator times out
            // pointing at nothing, while the message that explains the failure sits on screen unread.
            try
            {
                var msgs = app.Locator(".msg");
                var n = await msgs.CountAsync();
                for (var i = Math.Max(0, n - 3); i < n; i++)
                {
                    var who = (await msgs.Nth(i).Locator(".who").TextContentAsync() ?? "?").Trim();
                    var text = (await msgs.Nth(i).Locator(".text").TextContentAsync() ?? "").Trim();
                    Console.WriteLine($"[msg {who}] {(text.Length > 400 ? text[..400] + "…" : text)}");
                }
                if (n == 0) Console.WriteLine("[msg] the transcript is EMPTY - the turn left nothing behind");
            }
            catch (Exception ex) { Console.WriteLine($"[msg] could not read the transcript: {ex.Message}"); }

            string[] snapshot;
            lock (log) snapshot = log.ToArray();

            // DOES THE ROBOT SHOW WHAT IT IS DOING? To someone in another room the posture is the entire
            // interface, and a robot that is merely CORRECT while motionless is indistinguishable from
            // one that did not hear the question.
            var moods = snapshot.Where(l => l.Contains("[reachy-mood]")).ToArray();
            Console.WriteLine($"[cdp] moods: {string.Join(" -> ", moods.Select(m => m.Split(' ').Last()))}");
            if (!moods.Any(m => m.Contains("Thinking")))
                fails.Add("the robot never showed it was thinking - a person in another room has nothing "
                        + "to distinguish a reply being generated from the question never landing");
            // 🔴 A GESTURE FROM THE REPLY, not just from the mood loop. ReachyBody logs a text-derived
            // gesture as `gesture Tilt <- "tilts head"` and a state animation as `gesture Tilt (direct)`,
            // and only the first proves the reply's own stage directions reached the hardware. Accepting
            // any gesture would have passed the exact bug this checks for: the solo assistant's actions
            // moved the on-screen avatar and the robot stood still through "*tilts head*".
            if (!snapshot.Any(l => l.Contains("gesture ") && l.Contains("<-")))
                fails.Add("the robot performed none of the reply's OWN actions - a stage direction moved "
                        + "the on-screen avatar and the hardware stood still");

            var started = snapshot.Any(l => l.Contains("[reachy-speak] play start"));
            var ended = snapshot.Any(l => l.Contains("[reachy-speak] play end"));

            // 🔴 "IT SPOKE" NEEDS AUDIO, NOT A LOG LINE ABOUT TEXT. This matched any [HF-SPEAK] line, and
            // the FIRST one the app writes is "N stage direction(s) not spoken" - emitted before a single
            // sample is rendered. So a run that timed out while the voice model was still loading was
            // reported as "the reply was spoken on the PAGE, not the robot", sending me to debug holder
            // resolution that was working correctly. Only the lines that follow real audio count.
            var spokeAtAll = snapshot.Any(l => l.Contains("[HF-SPEAK] chunk")
                                            || l.Contains("[HF-SPEAK] first audio"));

            Console.WriteLine();
            if (started && ended) Console.WriteLine("[cdp] ROBOT SPEAKER: clip sent and completed.");
            else if (started) Console.WriteLine("[cdp] ROBOT SPEAKER: started but never completed.");
            else if (spokeAtAll)
                fails.Add("the reply was spoken on the PAGE, not the robot - no character in the room "
                        + "holds the Reachy body, or the holder resolution disagreed");
            else
            {
                // Say which half did not happen. "No reply at all", "the reply had no words in it" and
                // "the words never became audio" are three different bugs, and one timeout message for
                // all three sends whoever reads it to the wrong place - which is exactly what happened.
                var gotReply = snapshot.Any(l => l.Contains("HF-SPEAK"));
                var mimedOnly = lastStatus.Contains("action only", StringComparison.OrdinalIgnoreCase);
                fails.Add(mimedOnly
                    ? "the character MIMED instead of speaking - its whole reply was a stage direction, so "
                    + "there was nothing to synthesise. Not a speaker fault; the prompt has to ask for words."
                    : gotReply
                    ? "the reply arrived but no audio was produced within the timeout - the voice model "
                    + "was probably still loading; look for [HF-SPEAK] first audio"
                    : "nothing was spoken at all within the timeout - no reply reached the speech path");
            }
        }
    }
}
catch (Exception ex)
{
    fails.Add($"{ex.GetType().Name}: {ex.Message}");
}
finally
{
    // Closing the page releases the model it loaded. Without this the worker stays resident and the
    // memory never comes back until the browser is restarted.
    if (opened != null)
    {
        try { await opened.CloseAsync(); Console.WriteLine("[cdp] closed the tab this tool opened."); }
        catch (Exception ex) { Console.WriteLine($"[cdp] could not close our tab: {ex.Message}"); }
    }
    else if (adopted != null)
    {
        // ⚠️ A tab we did not open is a tab we do not close - but we DID start its AI server, and that
        // leaves ~1.8 GB resident in RAM and VRAM behind a worker. A reload kills the dedicated worker and
        // puts the tab back in exactly the state it was found in: loaded, parked on the start button,
        // costing nothing.
        try
        {
            await adopted.ReloadAsync(new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 60000 });
            Console.WriteLine("[cdp] reloaded the pre-existing tab - the model it loaded is released.");
        }
        catch (Exception ex) { Console.WriteLine($"[cdp] could not reload the adopted tab: {ex.Message}"); }
    }
}

Console.WriteLine();
if (fails.Count == 0)
{
    Console.WriteLine("PASS");
    return 0;
}
foreach (var f in fails) Console.WriteLine($"FAIL: {f}");
return 1;

/// <summary>Get the app from "a URL was navigated to" all the way to "the composer is usable".</summary>
/// <remarks>
/// 🔴 DOMContentLoaded IS NOT "LOADED" FOR A WASM APP. It fires when the HTML shell is parsed, which is
/// long before the .NET runtime has booted and Blazor has rendered its first component - so a page that
/// has "loaded" has an EMPTY BODY. Looking for the start button at that moment finds nothing, skips the
/// click as though the app were already running, and then waits out the full composer timeout on a page
/// that will never start. The symptom is a script that appears to do nothing until a person clicks the
/// button for it, which is precisely backwards from what a gate is for.
///
/// So the first wait is for EITHER of the two things the app can legitimately render - the start button on
/// a cold page, or the composer if it is already running - and only then is it fair to ask which it is.
/// </remarks>
async Task EnsureStartedAsync()
{
    await app!.WaitForSelectorAsync($"button.primary.big, {Composer}", new() { Timeout = 180000 });

    var start = app.Locator($"button.primary.big:has-text(\"{StartText}\")");
    if (await start.CountAsync() > 0)
    {
        Console.WriteLine("[cdp] starting the AI server...");
        await start.ClickAsync(new() { Timeout = 30000 });
    }

    // Loading the model is the slow part, and on a cold OPFS cache it is a download.
    await app.WaitForSelectorAsync(Composer, new() { Timeout = 240000 });
    Console.WriteLine("[cdp] app is up (worker + WebGPU)");
}

/// <summary>The stamp deploy-space.cs recorded for the build it last pushed, or null if it never has.</summary>
/// <remarks>
/// Located from THIS FILE's own path: a single-file `dotnet run` builds into %TEMP%\dotnet\..., so
/// nothing relative to the assembly or the working directory finds the repo.
/// </remarks>
static string? LastDeployedBuild([CallerFilePath] string thisFile = "")
{
    var f = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(thisFile))!, ".last-deployed-build");
    return File.Exists(f) ? File.ReadAllText(f).Trim() is { Length: > 0 } v ? v : null : null;
}

string? ArgValue(string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}
