#:package Microsoft.Playwright@1.49.0
#:property JsonSerializerIsReflectionEnabledByDefault=true
// PROVES the LLM<->image cross-kind eviction fix ("one large GPU model resident per device").
// Reproduces BOTH crashes TJ reported, in one deterministic flow on real Chrome (hardware WebGPU):
//   1) chat        -> loads the LLM
//   2) IMGTEST     -> image-gen (the fix evicts the LLM first; before the fix LLM+SD-Turbo co-resided -> crash)
//   3) IMGTEST     -> image-gen AGAIN (the "twice in a row crashes" case)
//   4) chat        -> LLM again (the fix evicts the image model + reloads the LLM)
// PASS = every step returns (no hang) AND the page never crashes / shows the Blazor error UI.
// args: [url] [profileDir]
using Microsoft.Playwright;

var url = args.Length > 0 ? args[0] : "http://localhost:5125/";
var profileDir = args.Length > 1 ? args[1] : @"C:\Users\TJ\AppData\Local\Temp\claude-imgtest-profile";

bool crashed = false;
using var pw = await Playwright.CreateAsync();
await using var ctx = await pw.Chromium.LaunchPersistentContextAsync(profileDir, new()
{
    Headless = false,
    Channel = "chrome",
});
var page = ctx.Pages.Count > 0 ? ctx.Pages[0] : await ctx.NewPageAsync();
page.Console += (_, msg) => Console.WriteLine($"[console] {msg.Text}");
page.Crash += (_, __) => { crashed = true; Console.WriteLine("[CRASH] renderer process crashed!"); };

async Task<bool> ErrorUi()
{
    try { return await page.IsVisibleAsync("#blazor-error-ui"); }
    catch { crashed = true; return true; } // page/context gone == crashed
}

Console.WriteLine($"[coreside] goto {url}  profile={profileDir}");
await page.GotoAsync(url, new() { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 120000 });
await page.ClickAsync("button:has-text(\"Start the AI server\")", new() { Timeout = 30000 });
await page.WaitForSelectorAsync("textarea", new() { Timeout = 240000 });
Console.WriteLine("[coreside] READY: worker + WebGPU up");

var steps = new System.Collections.Generic.List<string>();

async Task<bool> Chat(string label, string q)
{
    Console.WriteLine($"[coreside] {label}: \"{q}\"");
    try
    {
        await page.FillAsync("textarea", q);
        await page.PressAsync("textarea", "Enter");
        await page.WaitForSelectorAsync("textarea:not([disabled])", new() { Timeout = 600000 });
        if (await ErrorUi()) { Console.WriteLine($"[coreside] {label} -> ERROR UI"); steps.Add($"{label}=ERRUI"); return false; }
        Console.WriteLine($"[coreside] {label} returned (no crash)");
        steps.Add($"{label}=ok");
        return true;
    }
    catch (Exception ex) { crashed = true; Console.WriteLine($"[coreside] {label} threw: {ex.Message}"); steps.Add($"{label}=THROW"); return false; }
}

async Task<bool> Imgtest(int n)
{
    var imgBtn = "button[title^=\"IMGTEST\"]";
    Console.WriteLine($"[coreside] IMGTEST #{n}");
    try
    {
        await page.WaitForSelectorAsync(imgBtn + ":not([disabled])", new() { Timeout = 600000 });
        await page.ClickAsync(imgBtn, new() { Timeout = 30000 });
        await page.WaitForSelectorAsync(imgBtn + ":not([disabled])", new() { Timeout = 1_200_000 });
        if (await ErrorUi()) { Console.WriteLine($"[coreside] IMGTEST #{n} -> ERROR UI"); steps.Add($"img{n}=ERRUI"); return false; }
        Console.WriteLine($"[coreside] IMGTEST #{n} returned (no crash)");
        steps.Add($"img{n}=ok");
        return true;
    }
    catch (Exception ex) { crashed = true; Console.WriteLine($"[coreside] IMGTEST #{n} threw: {ex.Message}"); steps.Add($"img{n}=THROW"); return false; }
}

bool s1 = await Chat("CHAT-1", "What is the capital of France? Answer in one short sentence.");
bool s2 = s1 && await Imgtest(1);
bool s3 = s2 && await Imgtest(2);
bool s4 = s3 && await Chat("CHAT-2", "What is the capital of Japan? Answer in one short sentence.");

Console.WriteLine();
Console.WriteLine($"===== CO-RESIDE RESULT: crashed={crashed}  steps=[{string.Join(", ", steps)}] =====");
bool pass = !crashed && s1 && s2 && s3 && s4;
Console.WriteLine(pass
    ? "[coreside] PASS - LLM -> image -> image -> LLM completed with NO page crash (eviction fix holds)"
    : "[coreside] FAIL - a step crashed/hung/errored (see steps above)");
await ctx.CloseAsync();
return pass ? 0 : 2;
