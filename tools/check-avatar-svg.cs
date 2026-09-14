#:package Microsoft.Playwright@1.49.0
#:property JsonSerializerIsReflectionEnabledByDefault=true
// AVATAR gate: the Reachy avatar must actually be DRAWN, not merely present in the DOM.
//
// 🔴 WHY IT EXISTS. Captain, looking at the deployed page: "I can use devtools to see the svg there.. but
// nothing is actually visible. Just an empty borderless box." Every existing gate passed straight through
// that, because the DOM really did contain elements named <svg>, <ellipse> and <circle> - they were just
// in the XHTML namespace, where they are HTMLUnknownElements that inherit the CSS aimed at them and
// report zero geometry. SpawnDomRenderer parsed static markup through an HTML <template>, and the Razor
// compiler coalesces every static element run into exactly one of those Markup frames.
//
// ⚠️ THE PRESENCE CHECK IS THE TRAP, NOT THE TEST. A CSS type selector matches an element's LOCAL NAME,
// so querySelector(".skull") or ("ellipse") matches the broken element as happily as the working one.
// This asserts namespaceURI AND a non-zero painted box - the two things that were false while every
// selector was true.
//
//   dotnet run tools/check-avatar-svg.cs -- [url]
using Microsoft.Playwright;

const string SvgNs = "http://www.w3.org/2000/svg";
var url = args.Length > 0 ? args[0] : "http://localhost:5199/?worker=dedicated";

using var pw = await Playwright.CreateAsync();
var profileDir = Path.Combine(Path.GetTempPath(), "spawndev-ai-avatar-check-profile");
Directory.CreateDirectory(profileDir);
await using var browser = await pw.Chromium.LaunchPersistentContextAsync(profileDir,
    new() { Headless = false, Channel = "chrome" });
var page = browser.Pages.Count > 0 ? browser.Pages[0] : await browser.NewPageAsync();
page.Console += (_, m) => { if (m.Type == "error") Console.WriteLine($"  [console error] {m.Text}"); };

Console.WriteLine($"[avatar] {url}");
await page.GotoAsync(url, new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 60000 });

// The stage only exists inside the running chat, so the server has to come up first. The model load is
// what makes this slow; the persistent profile keeps it in OPFS between runs.
await page.ClickAsync("button:has-text(\"Start the AI server\")", new() { Timeout = 30000 });
Console.WriteLine("[avatar] started, waiting for the chat to come up (model load)...");
await page.WaitForSelectorAsync("textarea", new() { Timeout = 420000 });
await page.WaitForSelectorAsync(".stage-slot svg", new() { Timeout = 60000 });
await page.WaitForTimeoutAsync(500);

var probe = @"() => {
  const svg = document.querySelector('.stage-slot svg');
  if (!svg) return { found: false };
  const shapes = [...svg.querySelectorAll('ellipse, circle, path, polygon, rect')];
  const box = svg.getBoundingClientRect();
  const skull = svg.querySelector('.skull');
  const skullBox = skull ? skull.getBoundingClientRect() : null;
  return {
    found: true,
    svgNs: svg.namespaceURI,
    svgW: box.width, svgH: box.height,
    shapeCount: shapes.length,
    htmlNsShapes: shapes.filter(e => e.namespaceURI !== 'http://www.w3.org/2000/svg')
                        .map(e => e.tagName).slice(0, 10),
    skullNs: skull ? skull.namespaceURI : null,
    skullW: skullBox ? skullBox.width : 0,
    skullH: skullBox ? skullBox.height : 0,
    isSvgElement: svg instanceof SVGElement,
    skullIsSvgElement: skull instanceof SVGElement,
  };
}";
var r = await page.EvaluateAsync<System.Text.Json.JsonElement>(probe);

var fails = new List<string>();
if (!r.GetProperty("found").GetBoolean())
{
    fails.Add("no .stage-slot svg in the DOM at all");
}
else
{
    var svgNs = r.GetProperty("svgNs").GetString();
    var skullNs = r.GetProperty("skullNs").GetString();
    var shapeCount = r.GetProperty("shapeCount").GetInt32();
    var skullW = r.GetProperty("skullW").GetDouble();
    var skullH = r.GetProperty("skullH").GetDouble();
    var htmlNs = r.GetProperty("htmlNsShapes").EnumerateArray().Select(e => e.GetString()).ToList();

    Console.WriteLine($"[avatar] <svg> namespaceURI : {svgNs}");
    Console.WriteLine($"[avatar] instanceof SVGElement: {r.GetProperty("isSvgElement").GetBoolean()}");
    Console.WriteLine($"[avatar] shapes              : {shapeCount}");
    Console.WriteLine($"[avatar] .skull namespaceURI : {skullNs}");
    Console.WriteLine($"[avatar] .skull painted box  : {skullW:F1} x {skullH:F1}");
    Console.WriteLine($"[avatar] svg painted box     : {r.GetProperty("svgW").GetDouble():F1} x {r.GetProperty("svgH").GetDouble():F1}");

    if (svgNs != SvgNs) fails.Add($"<svg> is in '{svgNs}', not the SVG namespace");
    if (shapeCount == 0) fails.Add("the avatar svg has no shape children");
    if (htmlNs.Count > 0)
        fails.Add($"{htmlNs.Count} shape(s) are NOT in the SVG namespace: {string.Join(", ", htmlNs)}");
    if (skullNs is null) fails.Add("no .skull element - the avatar head did not render");
    else if (skullNs != SvgNs) fails.Add($".skull is in '{skullNs}', not the SVG namespace");
    // The whole point: an HTML element named "ellipse" reports a zero box. A real one does not.
    if (skullW <= 0 || skullH <= 0)
        fails.Add($".skull paints nothing ({skullW:F1} x {skullH:F1}) - this is the empty-box symptom");
}

var shot = Path.Combine(Path.GetTempPath(), "avatar-check.png");
try { await page.Locator(".stage").ScreenshotAsync(new() { Path = shot }); Console.WriteLine($"[avatar] screenshot: {shot}"); }
catch (Exception ex) { Console.WriteLine($"[avatar] screenshot failed: {ex.Message}"); }

Console.WriteLine();
if (fails.Count == 0)
{
    Console.WriteLine("AVATAR OK - every shape is a real SVG element and the head paints a non-zero box.");
    return 0;
}
foreach (var f in fails) Console.WriteLine($"AVATAR FAIL: {f}");
return 1;
