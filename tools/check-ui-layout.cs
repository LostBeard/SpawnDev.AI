#:package Microsoft.Playwright@1.49.0
#:property JsonSerializerIsReflectionEnabledByDefault=true
// Layout gate: the app must remain USABLE, not merely correct.
//
// 🔴 WHY IT EXISTS. Every assertion here is a defect Captain found by looking at the deployed page, that
// every other gate passed straight through:
//   - the message box had been squeezed to 59 px of an 848 px composer by nine sibling controls
//     ("the user has zero room to type");
//   - the model picker read `smollm2` while the app was actually running `qwen3:1.7b` - the options
//     arrive after the select first renders, and a browser resets a select to index 0 when its options
//     are replaced, so @bind never re-applied the value;
//   - the header row was 526 px wide at a 405 px viewport, so the whole page scrolled sideways on a phone.
// A functional gate cannot see any of that: the buttons all worked.
//
// ⚠️ Checks BOTH widths. The desktop pass alone would have missed the horizontal scroll entirely.
//
//   dotnet run tools/check-ui-layout.cs -- [url]
using Microsoft.Playwright;
var url = args.Length > 0 ? args[0] : "http://localhost:5299/?worker=dedicated";
using var pw = await Playwright.CreateAsync();
var profileDir = Path.Combine(Path.GetTempPath(), "spawndev-ai-ui-check-profile");
Directory.CreateDirectory(profileDir);
await using var browser = await pw.Chromium.LaunchPersistentContextAsync(profileDir, new() { Headless = false, Channel = "chrome" });
var page = browser.Pages.Count > 0 ? browser.Pages[0] : await browser.NewPageAsync();
await page.SetViewportSizeAsync(1040, 945);
await page.GotoAsync(url, new() { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 60000 });

var fails = new List<string>();
var startText = (await page.InnerTextAsync("button.primary.big")).Trim();
Console.WriteLine($"[ui] landing button: \"{startText}\"");
if (!startText.Contains("GB") && !startText.Contains("MB"))
    fails.Add($"the start button does not state a download size (\"{startText}\") - it is the consent moment");
var heroText = (await page.InnerTextAsync(".hero")).Trim();
Console.WriteLine($"[ui] landing copy length: {heroText.Length} chars");

await page.ClickAsync("button:has-text(\"Start the AI server\")", new() { Timeout = 30000 });
await page.WaitForSelectorAsync(".composer textarea", new() { Timeout = 180000 });
await page.WaitForTimeoutAsync(1500);

var js = @"() => {
  const bar = document.querySelector('.composer');
  const ta = bar.querySelector('textarea');
  const sel = document.querySelector('.pickers select');
  const bodies = document.querySelectorAll('.stage .reachy').length;
  return JSON.stringify({
    composer: Math.round(bar.getBoundingClientRect().width),
    textarea: Math.round(ta.getBoundingClientRect().width),
    selValue: sel ? sel.value : null,
    selIndex: sel ? sel.selectedIndex : -1,
    bodies: bodies,
    overflow: document.documentElement.scrollWidth > document.documentElement.clientWidth
  });
}";
var s = await page.EvaluateAsync<string>(js);
Console.WriteLine($"[ui] {s}");
using var d = System.Text.Json.JsonDocument.Parse(s);
var r = d.RootElement;
int taW = r.GetProperty("textarea").GetInt32(), barW = r.GetProperty("composer").GetInt32();
if (taW < barW * 0.55)
    fails.Add($"the message box is {taW}px of a {barW}px composer - the user has no room to type");
var selValue = r.GetProperty("selValue").GetString();
var expected = await page.EvaluateAsync<string>("() => document.querySelector('.pickers select').options[document.querySelector('.pickers select').selectedIndex].text");
Console.WriteLine($"[ui] model picker shows: {expected}");
if (selValue != null && !selValue.Contains("qwen3:1.7b"))
    fails.Add($"the model picker reads '{selValue}' but the app's default model is qwen3:1.7b-q8_0 - the picker names a model the app is not using");
if (r.GetProperty("bodies").GetInt32() < 1)
    fails.Add("no default avatar on the stage - a first visit shows neither a persona nor a body");
if (r.GetProperty("overflow").GetBoolean()) fails.Add("the page scrolls horizontally");

await page.SetViewportSizeAsync(420, 900);
await page.WaitForTimeoutAsync(800);
var narrow = await page.EvaluateAsync<string>(js);
Console.WriteLine($"[ui] at 420px: {narrow}");
using var d2 = System.Text.Json.JsonDocument.Parse(narrow);
if (d2.RootElement.GetProperty("overflow").GetBoolean()) fails.Add("the page scrolls horizontally at phone width");
int taW2 = d2.RootElement.GetProperty("textarea").GetInt32();
if (taW2 < 150) fails.Add($"at 420px the message box is only {taW2}px");

await browser.CloseAsync();
foreach (var f in fails) Console.WriteLine($"[ui] FAIL - {f}");
Console.WriteLine(fails.Count == 0 ? "[ui] PASS" : $"[ui] {fails.Count} problem(s)");
return fails.Count;
