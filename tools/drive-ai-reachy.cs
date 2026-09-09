#:package Microsoft.Playwright@1.49.0
#:property JsonSerializerIsReflectionEnabledByDefault=true
// HARDWARE gate: a character in the demo's group chat drives the real Reachy Mini.
//
// Separate from drive-ai-room.cs on purpose - that one must keep running for anyone without a robot.
//
// 🔴 WHAT MAKES THIS A TEST RATHER THAN A SMOKE CHECK: it reads the robot's ACTUAL head pose from the
// daemon, out of band, before and after the round. "We called PerformAsync and nothing threw" would pass
// with the motors off, with a mis-classified gesture, or with the whole gesture path disconnected. A head
// that measurably moved is the only evidence that the character acted.
//
// ⚠️ It ALWAYS parks in a finally: home, then sleep, then motors off. Leaving somebody's robot holding a
// pose with its motors live is not an acceptable way to fail a test.
//
//   dotnet run tools/drive-ai-reachy.cs [robot-ip] [page-url]
using System.Text.Json;
using Microsoft.Playwright;

var robotIp = args.Length > 0 ? args[0] : "192.168.1.170";
var url = args.Length > 1 ? args[1] : "http://localhost:5199/?worker=dedicated";
var robotBase = $"http://{robotIp}:8000";

using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

async Task<(double Roll, double Pitch, double Yaw, double Z)?> HeadPoseAsync()
{
    try
    {
        using var doc = JsonDocument.Parse(await http.GetStringAsync($"{robotBase}/api/state/present_head_pose"));
        var r = doc.RootElement;
        return (r.GetProperty("roll").GetDouble(), r.GetProperty("pitch").GetDouble(),
                r.GetProperty("yaw").GetDouble(), r.GetProperty("z").GetDouble());
    }
    catch { return null; }
}

async Task<string> MotorModeAsync()
{
    try
    {
        using var doc = JsonDocument.Parse(await http.GetStringAsync($"{robotBase}/api/motors/status"));
        return doc.RootElement.GetProperty("mode").GetString() ?? "?";
    }
    catch (Exception ex) { return $"unreadable ({ex.Message})"; }
}

// ── Read-only pre-flight, before anything is commanded ──────────────────────────────────────────────
Console.WriteLine($"[gate] robot {robotBase}");
var before = await HeadPoseAsync();
if (before == null)
{
    Console.WriteLine("[gate] FAIL - the daemon did not answer. Nothing was commanded.");
    return 1;
}
var motorsBefore = await MotorModeAsync();
Console.WriteLine($"[gate] before: motors={motorsBefore} "
    + $"pitch={before.Value.Pitch:F3} yaw={before.Value.Yaw:F3} roll={before.Value.Roll:F3}");

var fails = new List<string>();
using var pw = await Playwright.CreateAsync();
var profileDir = Path.Combine(Path.GetTempPath(), "spawndev-ai-reachy-gate-profile");
Directory.CreateDirectory(profileDir);
await using var browser = await pw.Chromium.LaunchPersistentContextAsync(profileDir, new()
{
    Headless = false,
    Channel = "chrome",
});
var page = browser.Pages.Count > 0 ? browser.Pages[0] : await browser.NewPageAsync();
var gestureLog = new List<string>();
page.Console += (_, m) =>
{
    if (!m.Text.Contains("reachy") && !m.Text.Contains("ROOM") && !m.Text.Contains("HF-SPEAK")) return;
    Console.WriteLine($"[console] {m.Text}");
    // ReachyBody logs each gesture it performs; the driver prefixes those with [reachy]. Their presence
    // is direct evidence the gesture path ran, independent of how far the head happened to move.
    if (m.Text.Contains("[reachy]")) gestureLog.Add(m.Text);
};

const string Character = "Vessel";
try
{
    Console.WriteLine($"[gate] goto {url}");
    await page.GotoAsync(url, new() { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 60000 });
    await page.ClickAsync("button:has-text(\"Start the AI server\")", new() { Timeout = 30000 });
    await page.WaitForSelectorAsync("textarea", new() { Timeout = 180000 });
    Console.WriteLine("[gate] READY: worker + WebGPU up");

    await page.ClickAsync("button.gear:has-text(\"🎭\")", new() { Timeout = 15000 });
    await page.WaitForSelectorAsync(".settings.room", new() { Timeout = 15000 });

    // A page on http:// reaching an http:// robot is same-scheme, and the daemon returns permissive
    // CORS headers (verified: access-control-allow-origin reflects the page origin), so this works.
    // An HTTPS page could not, and the panel says so instead of failing opaquely.
    if (await page.Locator(".settings.room .sethint.warn").CountAsync() > 0)
        fails.Add("the page warned about mixed content, so it is not being served over plain HTTP - the "
            + "robot cannot be reached from here");

    Console.WriteLine($"[gate] connecting to {robotIp}");
    await page.FillAsync(".settings.room input.voice-name", robotIp);
    await page.ClickAsync(".settings.room button.chip:has-text(\"Connect\")", new() { Timeout = 15000 });
    // Connect enables the motors and plays the daemon's wake_up move, which takes a moment.
    await page.WaitForSelectorAsync(".settings.room button.chip:has-text(\"Park & disconnect\")",
        new() { Timeout = 60000 });

    var motorsLive = await MotorModeAsync();
    Console.WriteLine($"[gate] after connect: motors={motorsLive}");

    // 🔴 THE BASELINE IS TAKEN HERE, AWAKE - not from the parked pose at the top. wake_up lifts the head
    // out of the chest by ~0.5 rad, so measuring against the parked pose credits that lift to the
    // character's gesture and the movement assertion passes whether or not any gesture ran. MEASURED: a
    // round whose reply contained no stage direction still showed a 0.736 rad "movement".
    await Task.Delay(1200);
    var awake = await HeadPoseAsync();
    if (awake == null) fails.Add("could not read the head pose after waking");
    Console.WriteLine($"[gate] awake baseline: pitch={awake?.Pitch:F3} yaw={awake?.Yaw:F3} roll={awake?.Roll:F3}");
    if (motorsLive != "enabled")
        fails.Add($"motors report '{motorsLive}' after connecting - a gesture sent to a robot with its "
            + "motors off does nothing and reports nothing, which reads as a broken classifier");

    // ── A character whose body IS the robot ─────────────────────────────────────────────────────────
    Console.WriteLine($"[gate] creating {Character} with the Reachy body");
    await page.FillAsync(".settings.room input.voice-name.wide", Character);
    // ⚠️ THE FORMAT IS DEMANDED EXPLICITLY, WITH AN EXAMPLE. The room already asks an embodied character
    // to write actions in asterisks, but a 0.5B model ignored that and replied with no action at all -
    // leaving the hardware path untested while everything looked fine. An example in the persona is what
    // a small model actually follows.
    await page.FillAsync(".settings.room textarea.sysbox >> nth=1",
        "You are a curious machine. ALWAYS begin your reply with a physical action between asterisks, "
        + "for example *tilts head* or *looks around*. Then say one short sentence.");
    // A scene makes it unambiguously role-play, which is what asks for actions in the first place.
    await page.FillAsync(".settings.room textarea.sysbox >> nth=0",
        "A dim workshop. Something just moved behind you.");
    // Body is the second select in the editor: model, body, voice.
    await page.SelectOptionAsync(".settings.room select >> nth=1", "Reachy");
    await page.ClickAsync(".settings.room button.primary:has-text(\"Create\")", new() { Timeout = 15000 });
    await page.WaitForSelectorAsync($".charchip:has-text(\"{Character}\")", new() { Timeout = 30000 });
    await page.ClickAsync($".charchip:has-text(\"{Character}\") button.chip:has-text(\"{Character}\")",
        new() { Timeout = 15000 });
    await page.WaitForSelectorAsync($".charchip.in:has-text(\"{Character}\")", new() { Timeout = 15000 });

    // One reply is enough - the robot only has one head.
    await page.FillAsync(".settings.room input[type=range]", "1");

    // The badge marks who holds the hardware, and it must be this character.
    if (await page.Locator(".stage-slot.robot .robot-badge").CountAsync() == 0)
        fails.Add("no character is shown as holding the robot, so the stage disagrees with the config");

    Console.WriteLine("[gate] sending the opening line");
    await page.FillAsync("textarea:not(.sysbox)", "Someone just walked into the room behind you.");
    await page.PressAsync("textarea:not(.sysbox)", "Enter");

    // ── Watch the real head while the round runs ────────────────────────────────────────────────────
    // Sampled DURING the round, not only after: a gesture returns to rest by design, so a
    // before/after comparison alone could miss a motion that happened and finished.
    var maxDelta = 0.0;
    var samples = 0;
    var watching = Task.Run(async () =>
    {
        while (true)
        {
            var now = await HeadPoseAsync();
            if (now != null)
            {
                samples++;
                var b = awake ?? before.Value;
                var d = Math.Abs(now.Value.Pitch - b.Pitch)
                      + Math.Abs(now.Value.Yaw - b.Yaw)
                      + Math.Abs(now.Value.Roll - b.Roll);
                if (d > maxDelta) maxDelta = d;
            }
            await Task.Delay(250);
        }
    });

    await page.WaitForSelectorAsync("textarea:not(.sysbox):not([disabled])", new() { Timeout = 420000 });
    await Task.Delay(1500);   // let the last gesture finish

    var transcript = await page.InnerTextAsync(".transcript");
    Console.WriteLine($"[gate] TRANSCRIPT:\n{transcript}");
    Console.WriteLine($"[gate] head movement: peak delta {maxDelta:F3} rad over {samples} samples");

    var speakers = await page.Locator(".transcript .msg .who").AllInnerTextsAsync();
    if (!speakers.Any(x => x.Trim() == Character))
        fails.Add($"{Character} never spoke, so nothing could have driven the robot");

    // ⚠️ FIXTURE CHECK FIRST. A small model often replies with no stage direction at all, and then there
    // is nothing for the robot to perform - which is a WEAK FIXTURE, not a working robot. Saying so beats
    // reporting a pass that proves nothing.
    Console.WriteLine($"[gate] gesture log lines: {gestureLog.Count}");
    foreach (var g in gestureLog) Console.WriteLine($"[gate]   {g}");
    if (gestureLog.Count == 0)
        fails.Add("NO gesture reached the robot: the reply contained no recognised stage direction, so "
            + "this run proves nothing about the hardware path. FIXTURE TOO WEAK - use a model that "
            + "writes actions, or a prompt that forces one.");
    else if (maxDelta < 0.10)
        fails.Add($"a gesture was issued ({gestureLog.Count} line(s)) but the head moved only "
            + $"{maxDelta:F3} rad from its awake pose - the command is not reaching the joints");
}
catch (Exception ex)
{
    fails.Add($"the run threw: {ex.GetType().Name}: {ex.Message}");
}
finally
{
    // ⚠️ ALWAYS park, however this ended. Home, then sleep, then motors off - that order is the
    // confirmed recipe, and it is what the Park button does.
    try
    {
        Console.WriteLine("[gate] parking");
        if (await page.Locator("button.chip:has-text(\"Park & disconnect\")").CountAsync() > 0)
        {
            await page.ClickAsync("button.chip:has-text(\"Park & disconnect\")", new() { Timeout = 20000 });
            await Task.Delay(5000);
        }
        // Clean up the character this gate created.
        if (await page.Locator($".charchip:has-text(\"{Character}\") button.chip:has-text(\"🗑️\")").CountAsync() > 0)
            await page.ClickAsync($".charchip:has-text(\"{Character}\") button.chip:has-text(\"🗑️\")",
                new() { Timeout = 15000 });
    }
    catch (Exception ex) { Console.WriteLine($"[gate] WARNING: parking via the UI failed: {ex.Message}"); }
}

var motorsAfter = await MotorModeAsync();
var after = await HeadPoseAsync();
Console.WriteLine($"[gate] after: motors={motorsAfter} "
    + (after == null ? "(pose unreadable)" : $"pitch={after.Value.Pitch:F3} yaw={after.Value.Yaw:F3}"));

// Left with live motors holding a pose is a bad state to leave somebody's robot in, so it fails the run
// even when everything else passed.
if (motorsAfter != "disabled")
    fails.Add($"motors were left '{motorsAfter}' - the robot should be parked with motors off");

await browser.CloseAsync();
if (fails.Count == 0)
{
    Console.WriteLine("[gate] PASS - the character moved the real robot, and it parked afterwards");
    return 0;
}
foreach (var f in fails) Console.WriteLine($"[gate] FAIL - {f}");
return 1;
