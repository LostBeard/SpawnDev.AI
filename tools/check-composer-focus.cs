#:package Microsoft.Playwright@1.49.0
#:property JsonSerializerIsReflectionEnabledByDefault=true
// Does the message box keep the keyboard across a turn? Captain: "i send a message with the enter key...
// the ai replies... i have to click the message box to give it focus again before i can type again."
// The box was disabled while busy, and disabling a focused element drops its focus for good.
//
// Keyboard only after one initial focus: type, Enter, wait for the reply, then TYPE AGAIN WITHOUT CLICKING
// and require the keystrokes to land in the box. Also types DURING the reply, which the old disabled box
// refused. PASS = focus is still on the box after the reply and both typed strings are in it.
// args: [url] [--persistent]
using Microsoft.Playwright;

var url = args.FirstOrDefault(a => !a.StartsWith("--")) ?? "http://localhost:5199/";
var persistent = args.Contains("--persistent");
using var pw = await Playwright.CreateAsync();
IBrowserContext ctx;
IBrowser? browser = null;
if (persistent)
{
    var dir = Path.Combine(Path.GetTempPath(), "spawndev-ai-demo-profile");   // same cached model as drive-ai-demo
    Directory.CreateDirectory(dir);
    ctx = await pw.Chromium.LaunchPersistentContextAsync(dir, new() { Headless = false, Channel = "chrome" });
}
else
{
    browser = await pw.Chromium.LaunchAsync(new() { Headless = false, Channel = "chrome" });
    ctx = await browser.NewContextAsync();
}
var page = ctx.Pages.Count > 0 ? ctx.Pages[0] : await ctx.NewPageAsync();

const string Box = "#composer-input";
await page.GotoAsync(url, new() { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 60000 });
await page.ClickAsync("button:has-text(\"Start the AI server\")", new() { Timeout = 30000 });
await page.WaitForSelectorAsync(Box, new() { Timeout = 180000 });
Console.WriteLine("[focus] composer ready");

async Task<string> Active() => await page.EvaluateAsync<string>("() => document.activeElement?.id || document.activeElement?.tagName || ''");

await page.FocusAsync(Box);   // the one and only time the test puts focus there
await page.Keyboard.TypeAsync("Say hello in three words.");
await page.Keyboard.PressAsync("Enter");
await page.WaitForSelectorAsync(".composer[data-busy=\"true\"]", new() { Timeout = 30000 });
var duringFocus = await Active();
await page.Keyboard.TypeAsync("typed while busy ");
Console.WriteLine($"[focus] reply in flight; focus on '{duringFocus}'");

await page.WaitForSelectorAsync(".composer[data-busy=\"false\"]", new() { Timeout = 600000 });
var afterFocus = await Active();
await page.Keyboard.TypeAsync("typed after");   // NO click before this
// Shift+Enter is a line break in the box, not a send (the Enter listener must leave it alone).
await page.Keyboard.PressAsync("Shift+Enter");
await page.Keyboard.TypeAsync("line two");
var sentByShiftEnter = await page.Locator(".composer[data-busy=\"true\"]").CountAsync() > 0;
var value = await page.InputValueAsync(Box);
var transcript = await page.InnerTextAsync(".transcript");
Console.WriteLine($"[focus] after the reply focus is on '{afterFocus}'; box holds \"{value.Replace("\n", "\\n")}\"");
Console.WriteLine($"[focus] transcript tail: ...{transcript[Math.Max(0, transcript.Length - 160)..].Replace('\n', ' ')}");

var fails = new List<string>();
if (afterFocus != "composer-input") fails.Add($"focus after the reply is on '{afterFocus}', not the message box");
if (!value.Contains("typed after")) fails.Add("keystrokes after the reply did not reach the box");
if (!value.Contains("typed while busy")) fails.Add("keystrokes during the reply did not reach the box");
if (value.Contains("Say hello")) fails.Add("the sent message was not cleared from the box");
if (!value.Contains("typed after\nline two")) fails.Add("Shift+Enter did not put a line break in the box");
if (sentByShiftEnter) fails.Add("Shift+Enter sent the message");
if (value.StartsWith('\n')) fails.Add("a stray newline from the Enter that sent the message is left in the box");
Console.WriteLine(fails.Count == 0 ? "RESULT: PASS" : "RESULT: FAIL - " + string.Join("; ", fails));
if (browser != null) await browser.DisposeAsync(); else await ctx.DisposeAsync();
return fails.Count;
