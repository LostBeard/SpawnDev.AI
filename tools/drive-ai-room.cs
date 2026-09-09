#:package Microsoft.Playwright@1.49.0
#:property JsonSerializerIsReflectionEnabledByDefault=true
// Live browser gate for the SpawnDev.AI.Demo GROUP CHAT: does exactly what a user would do - make two
// characters, set the scene, put them both in the room, send one message, and check that BOTH of them
// answered, under their own names.
//
// Why this exists on top of the unit tests: AgentRoomTests proves the sequencing (who hears what, in what
// order) without a model, and it is the part most likely to be subtly wrong. It cannot press a button. The
// character editor, the room panel, the chips, the round runner and the per-speaker bubbles are all Razor,
// and the suite never touches the page - so without this gate the whole user-facing half is unverified.
using Microsoft.Playwright;

var url = args.Length > 0 ? args[0] : "http://localhost:5199/";

// Distinctive names: they are asserted against the transcript, and a name that could appear in a model's
// own prose (e.g. "Ada") would let this pass on a reply that mentions it rather than one attributed to it.
const string NameA = "Zephrin";
const string NameB = "Qualla";
const string Scene = "You are on the derelict colony of Copper-9, at night, after the lights failed.";

using var pw = await Playwright.CreateAsync();
await using var browser = await pw.Chromium.LaunchAsync(new()
{
    Headless = false,
    Channel = "chrome",   // TJ's installed Chrome build (hardware WebGPU); separate automation profile
});
var page = await browser.NewPageAsync();
page.Console += (_, msg) => Console.WriteLine($"[console] {msg.Text}");

Console.WriteLine($"[gate] goto {url}");
await page.GotoAsync(url, new() { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 60000 });

Console.WriteLine("[gate] clicking 'Start the AI server'");
await page.ClickAsync("button:has-text(\"Start the AI server\")", new() { Timeout = 30000 });
await page.WaitForSelectorAsync("textarea", new() { Timeout = 180000 });
Console.WriteLine("[gate] READY: worker + WebGPU up, composer present");

// ── Open the room panel ─────────────────────────────────────────────────────────────────────────────
Console.WriteLine("[gate] opening the group-chat panel");
await page.ClickAsync("button.gear:has-text(\"🎭\")", new() { Timeout = 15000 });
await page.WaitForSelectorAsync(".settings.room", new() { Timeout = 15000 });

// ── The scene. First .sysbox in the panel; the persona box is the second. ────────────────────────────
await page.FillAsync(".settings.room textarea.sysbox >> nth=0", Scene);

// Two replies per round, not the default four: this gate pays a real generation per reply and one round
// trip through each character is what it is checking.
await page.FillAsync(".settings.room input[type=range]", "2");

async Task CreateCharacter(string name, string persona)
{
    Console.WriteLine($"[gate] creating character {name}");
    await page.FillAsync(".settings.room input.voice-name.wide", name);
    await page.FillAsync(".settings.room textarea.sysbox >> nth=1", persona);
    // Give them a body. Without one there is no stage, and the actions they write go nowhere.
    await page.SelectOptionAsync(".settings.room select >> nth=1", "Screen");
    await page.ClickAsync(".settings.room button.primary:has-text(\"Create\")", new() { Timeout = 15000 });
    // The chip appearing IS the save landing in OPFS and the list reloading from it.
    await page.WaitForSelectorAsync($".charchip:has-text(\"{name}\")", new() { Timeout = 30000 });
    // Back to a blank editor, or the second character would edit the first.
    await page.ClickAsync(".settings.room button.linkbtn:has-text(\"New character\")", new() { Timeout = 15000 });
}

await CreateCharacter(NameA, "You are terse and suspicious. You speak in short, clipped sentences.");
await CreateCharacter(NameB, "You are warm and curious, and you ask one question back.");

// ── Put them both in the room ───────────────────────────────────────────────────────────────────────
foreach (var name in new[] { NameA, NameB })
{
    Console.WriteLine($"[gate] adding {name} to the room");
    await page.ClickAsync($".charchip:has-text(\"{name}\") button.chip:has-text(\"{name}\")",
        new() { Timeout = 15000 });
    await page.WaitForSelectorAsync($".charchip.in:has-text(\"{name}\")", new() { Timeout = 15000 });
}

// The header button counts the room, so this is an independent read of the same state.
var roomBadge = await page.InnerTextAsync("button.gear:has-text(\"🎭\")");
Console.WriteLine($"[gate] room badge reads '{roomBadge.Trim()}'");

// ── One message, one round ──────────────────────────────────────────────────────────────────────────
Console.WriteLine("[gate] sending the opening line");
await page.FillAsync("textarea:not(.sysbox)", "The lights just went out. What do we do?");
var t0 = DateTime.UtcNow;
await page.PressAsync("textarea:not(.sysbox)", "Enter");

// The round is over when the composer re-enables (_busy = false in RunRoomRoundAsync's finally).
// Two generations plus a cold model load on the first.
await page.WaitForSelectorAsync("textarea:not(.sysbox):not([disabled])", new() { Timeout = 420000 });
var total = (DateTime.UtcNow - t0).TotalSeconds;

// ── What actually rendered ──────────────────────────────────────────────────────────────────────────
var fails = new List<string>();
var transcript = await page.InnerTextAsync(".transcript");
Console.WriteLine($"[gate] TRANSCRIPT ({total:F1}s):\n{transcript}");

// The STAGE: both characters should have a body drawn, and their written actions should have been
// recognised as motions. The room is role-play because the characters are embodied, so asterisks are
// actions - which also means none of them should have been spoken aloud.
var bodies = await page.Locator(".stage .reachy").CountAsync();
Console.WriteLine($"[gate] {bodies} body/bodies on stage");
if (bodies != 2)
    fails.Add($"expected 2 bodies on the stage, found {bodies} - an embodied character was not drawn");

// Read the speaker labels, not the prose: a name appearing anywhere in the text proves nothing
// about whether that character actually took a turn.
var speakers = await page.Locator(".transcript .msg .who").AllInnerTextsAsync();
var said = await page.Locator(".transcript .msg.assistant .text").AllInnerTextsAsync();
Console.WriteLine($"[gate] speakers: [{string.Join(" | ", speakers.Select(s => s.Trim()))}]");

foreach (var name in new[] { NameA, NameB })
    if (!speakers.Any(s => s.Trim() == name))
        fails.Add($"{name} never spoke - the round did not reach every member of the room");

if (said.Count < 2)
    fails.Add($"only {said.Count} reply bubble(s) rendered; a two-member round should produce two");
if (said.Any(t => string.IsNullOrWhiteSpace(t)))
    fails.Add("a reply bubble rendered EMPTY - an empty turn should be dropped, not shown");

// Both members are labelled "assistant" in the DOM, so a single shared label would render the whole room
// as one speaker. That is the UI half of the defect AgentRoomTests guards in the prompt.
if (speakers.Count(s => s.Trim() == "Assistant") > 0)
    fails.Add("a room reply rendered as the generic 'Assistant' - the speaker name did not reach the bubble");

// ── Clean up: this gate writes real characters into OPFS ─────────────────────────────────────────────
foreach (var name in new[] { NameA, NameB })
{
    try
    {
        await page.ClickAsync($".charchip:has-text(\"{name}\") button.chip:has-text(\"🗑️\")",
            new() { Timeout = 15000 });
        Console.WriteLine($"[gate] deleted {name}");
    }
    catch (Exception ex)
    {
        // Not a failure of the feature, but say so - the next run would otherwise find them still there.
        Console.WriteLine($"[gate] WARNING: could not delete {name} ({ex.Message}); it is left in OPFS");
    }
}

await browser.CloseAsync();
if (fails.Count == 0)
{
    Console.WriteLine($"[gate] PASS - {NameA} and {NameB} both answered in {total:F1}s");
    return 0;
}
foreach (var f in fails) Console.WriteLine($"[gate] FAIL - {f}");
return 1;
