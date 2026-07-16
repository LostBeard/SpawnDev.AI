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

var url    = args.Length > 0 ? args[0] : "http://localhost:5125/";
var model  = args.Length > 1 ? args[1] : "lfm2";
var prompt = args.Length > 2 ? args[2] : "Explain how you run entirely inside my browser.";

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

Console.WriteLine($"[gate] asking: {prompt}");
var t0 = DateTime.UtcNow;
await page.FillAsync("textarea", prompt);
await page.PressAsync("textarea", "Enter");
// First message downloads + loads the model on the GPU, then streams.
await page.WaitForSelectorAsync("textarea:not([disabled])", new() { Timeout = 600000 });
var total = (DateTime.UtcNow - t0).TotalSeconds;

var transcript = await page.InnerTextAsync(".transcript");
Console.WriteLine($"[gate] TRANSCRIPT ({total:F1}s):\n──────\n{transcript}\n──────");
await browser.CloseAsync();
return 0;
