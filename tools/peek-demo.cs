#:package Microsoft.Playwright@1.49.0
#:property JsonSerializerIsReflectionEnabledByDefault=true
// Look at the demo tab someone is already using. READ ONLY.
//
//   dotnet run tools/peek-demo.cs
//
// 🔴 IT TOUCHES NOTHING. No clicks, no navigation, no reload - unlike drive-reachy-cdp.cs, which drives
// the app and would destroy whatever state the person was looking at. When they say "look at it", the
// state ON SCREEN is the evidence, and the only tool that can read it is one that cannot change it.
//
// ⚠️ It prints the message TEXT as the DOM holds it. That matters for stage directions: the transcript
// renders markdown, so "*tilts head*" shows as italic "tilts head" with the asterisks gone - which is
// indistinguishable, by eye, from a model that wrote no asterisks at all. Those two need opposite fixes
// (one is the body wiring, the other is the prompt), so the markup is reported as well as the text.
using Microsoft.Playwright;

const string AppHost = "static.hf.space";
var cdp = args.Length > 1 && args[0] == "--cdp" ? args[1] : "http://localhost:9222";

using var pw = await Playwright.CreateAsync();
IBrowser browser;
try { browser = await pw.Chromium.ConnectOverCDPAsync(cdp); }
catch (Exception ex) { Console.WriteLine($"[peek] cannot attach at {cdp}: {ex.Message}"); return 2; }

// ⚠️ EVERY CONTEXT, NOT JUST THE FIRST. A browser someone is actually using has more than one, and the
// app is usually an IFRAME inside the huggingface.co Space page rather than a top-level tab - so the
// search has to walk contexts, then pages, then frames, or it reports "not open" on a browser that has it
// open in front of the person who just asked you to look at it.
IFrame? app = null;
foreach (var ctx in browser.Contexts)
    foreach (var page in ctx.Pages)
        foreach (var f in page.Frames)
            if (f.Url.Contains(AppHost, StringComparison.OrdinalIgnoreCase)) { app = f; break; }

if (app == null)
{
    // Say what IS there. "Not open" is a conclusion; the list is the evidence, and over CDP a
    // cross-origin iframe is not always surfaced as a frame at all.
    Console.WriteLine("[peek] the demo is not among the frames Playwright can see. What it can see:");
    foreach (var ctx in browser.Contexts)
        foreach (var page in ctx.Pages)
        {
            Console.WriteLine($"  page {page.Url}");
            foreach (var f in page.Frames) Console.WriteLine($"    frame {f.Url}");
        }
    return 1;
}
Console.WriteLine($"[peek] {app.Url}");

// Which build is on screen. Without this, every surprising observation has a second explanation -
// "they had not reloaded" - and no way to rule it out.
var build = await app.EvaluateAsync<string?>("() => document.querySelector('.sdai')?.dataset.build ?? null");
Console.WriteLine($"[peek] build: {build ?? "(not reported - this page predates data-build)"}");

// ── The transcript ──────────────────────────────────────────────────────────────────────────────────
var msgs = app.Locator(".msg");
var n = await msgs.CountAsync();
Console.WriteLine($"[peek] {n} message(s)");
for (var i = Math.Max(0, n - 4); i < n; i++)
{
    var who = (await msgs.Nth(i).Locator(".who").TextContentAsync() ?? "?").Trim();
    var text = (await msgs.Nth(i).Locator(".text").TextContentAsync() ?? "").Trim();
    var html = await msgs.Nth(i).Locator(".text").InnerHTMLAsync();
    Console.WriteLine($"  [{who}] {(text.Length > 300 ? text[..300] + "…" : text)}");

    // <em> is the tell: markdown ate a pair of asterisks, so the model DID mark an action.
    if (html.Contains("<em", StringComparison.OrdinalIgnoreCase))
        Console.WriteLine("        ^ contains <em> - the model DID write asterisks; markdown hid them");
    else if (who != "You" && text.Length > 0)
        Console.WriteLine("        ^ no <em> - anything action-like here was written WITHOUT asterisks");
}

// ── Layout, measured ────────────────────────────────────────────────────────────────────────────────
// "It does not scroll" has several distinct causes that look identical: the box has no overflow to
// scroll, the box scrolls but nothing anchors it, or it anchors and then the content grows AFTER the
// anchor (images). The numbers tell them apart.
var m = await app.EvaluateAsync<System.Text.Json.JsonElement>(@"() => {
    const t = document.querySelector('.transcript');
    if (!t) return { error: 'no .transcript' };
    const cs = getComputedStyle(t);
    const imgs = [...t.querySelectorAll('img')];
    return {
        overflowY: cs.overflowY,
        minHeight: cs.minHeight,
        clientH: Math.round(t.clientHeight),
        scrollH: Math.round(t.scrollHeight),
        scrollTop: Math.round(t.scrollTop),
        fromBottom: Math.round(t.scrollHeight - t.scrollTop - t.clientHeight),
        canScroll: t.scrollHeight > t.clientHeight + 1,
        images: imgs.length,
        imagesUndecoded: imgs.filter(i => !i.complete || i.naturalHeight === 0).length,
        imgHeights: imgs.slice(-3).map(i => Math.round(i.getBoundingClientRect().height))
    };
}");
Console.WriteLine($"[peek] transcript: {m}");

// ── The robot ───────────────────────────────────────────────────────────────────────────────────────
if (await app.Locator(".settings.room .robotstatus").CountAsync() > 0)
    Console.WriteLine($"[peek] robot: {(await app.Locator(".settings.room .robotstatus").Last.TextContentAsync() ?? "").Trim()}");
else
    Console.WriteLine("[peek] robot: the room panel is closed, so its status is not in the DOM");

var footer = await app.Locator("footer.sdai-ftr span").First.TextContentAsync();
Console.WriteLine($"[peek] status: {footer?.Trim()}");
return 0;
