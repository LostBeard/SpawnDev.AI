#:package Microsoft.Playwright@1.49.0
#:property JsonSerializerIsReflectionEnabledByDefault=true
// Live browser gate for a SPECIFIC chat model: open the demo, start the AI server (shared worker +
// hardware WebGPU), switch models via /model, ask a question, and print the model's actual answer.
//
// Why this exists (2026-07-16): LFM2 shipped "WebGPU-verified" on a Contains("Paris") oracle and a
// CUDA-only coherence read, while the browser produced token soup. Backend-specific numerical bugs
// only show up here - on the real WebGPU device, reading the real answer.
//
//   dotnet run tools/drive-ai-model.cs -- <url> <model-substring> <prompt>
using Microsoft.Playwright;

var url     = args.Length > 0 ? args[0] : "http://localhost:5125/";
var model   = args.Length > 1 ? args[1] : "lfm2";
// Every arg after the model is a prompt, asked IN SEQUENCE in the SAME conversation. Multi-turn is not a
// nicety: the KV-prefix cache only reuses on turn 2+, and a conv-state model that mishandles the reused
// cursor answers turn 1 perfectly and turn 2 wrong (2026-07-16). One-shot driving cannot see that.
var prompts = args.Length > 2 ? args[2..] : new[] { "Explain how you run entirely inside my browser." };

using var pw = await Playwright.CreateAsync();
await using var browser = await pw.Chromium.LaunchAsync(new()
{
    Headless = false,
    Channel = "chrome",   // TJ's installed Chrome build (hardware WebGPU), separate automation profile
});
var page = await browser.NewPageAsync();
page.Console += (_, msg) => Console.WriteLine($"[console] {msg.Text}");

Console.WriteLine($"[gate] goto {url}");
await page.GotoAsync(url, new() { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 60000 });

Console.WriteLine("[gate] clicking 'Start the AI server'");
await page.ClickAsync("button:has-text(\"Start the AI server\")", new() { Timeout = 30000 });
await page.WaitForSelectorAsync("textarea", new() { Timeout = 180000 });
Console.WriteLine("[gate] READY: worker + WebGPU up");

// Switch model (slash command is handled client-side, no generation).
Console.WriteLine($"[gate] /model {model}");
await page.FillAsync("textarea", $"/model {model}");
await page.PressAsync("textarea", "Enter");
await Task.Delay(500);

foreach (var prompt in prompts)
{
    Console.WriteLine($"[gate] asking: {prompt}");
    var t0 = DateTime.UtcNow;
    await page.FillAsync("textarea", prompt);
    await page.PressAsync("textarea", "Enter");
    // First message downloads + loads the model on the GPU, then streams.
    await page.WaitForSelectorAsync("textarea:not([disabled])", new() { Timeout = 600000 });
    Console.WriteLine($"[gate] answered in {(DateTime.UtcNow - t0).TotalSeconds:F1}s");
}

var transcript = await page.InnerTextAsync(".transcript");
Console.WriteLine($"[gate] TRANSCRIPT:\n──────\n{transcript}\n──────");
await browser.CloseAsync();
return 0;
