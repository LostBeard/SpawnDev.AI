using SpawnDev.AI.Demo.Pages;
using SpawnDev.AI.Demo.Tests;
using SpawnDev.AI.Server;
using SpawnDev.AsyncFileSystem;
using SpawnDev.AsyncFileSystem.BrowserWASM;
using SpawnDev.SpawnJS;
using SpawnDev.SpawnJS.RazorRenderer;
using SpawnDev.SpawnJS.WebWorkers;
using SpawnDev.WebTorrent;

var builder = SpawnJSAppBuilder.CreateDefault(args, out var JS);

builder.RootComponents.Add<Home>();

builder.RootComponents.AddSharedStyleSheet("css/app.css");

builder.Services.AddSingleton(sp => new HttpClient { BaseAddress = new Uri(JS.AppBaseUri) });

// SpawnDev stack - the SAME registrations run in Window, Worker, and SharedWorker scopes; only the
// worker instance ends up owning the GPU + model registry.
builder.Services.AddSpawnJSRuntime();

builder.Services.AddWebWorkerService();

// WebTorrent for P2P model delivery, persisted to OPFS so reloads reuse downloaded pieces (bytes
// stay JS-side end-to-end - the loader streams pieces straight to the GPU).
builder.Services.AddSingleton<IAsyncFS, AsyncFSFileSystemDirectoryHandle>();
// Saved voices live in OPFS beside the model cache, on the same filesystem abstraction, so a voice a family
// member trains survives a reload instead of being re-cloned from whatever was last said.
builder.Services.AddSingleton<SpawnDev.AI.Demo.VoiceLibrary>();
// Characters the user creates - name, persona, model and voice - saved beside the voices.
builder.Services.AddSingleton<SpawnDev.AI.Demo.CharacterLibrary>();
builder.Services.AddSingleton<WebTorrentClient>(sp =>
{
    var client = new WebTorrentClient(new WebTorrentClientOptions { AsyncFileSystem = sp.GetRequiredService<IAsyncFS>() });
    _ = client.RestoreFromStorageAsync();
    return client;
});

// The in-browser AI server (lives in the shared worker) + the window-side client.
builder.Services.AddSpawnDevAI(options =>
{
    options.MaxSeqLen = 4096;
    options.Models.Add(new HubModelOption(
        "qwen2.5:0.5b-instruct-q8_0",
        "Qwen/Qwen2.5-0.5B-Instruct-GGUF",
        "qwen2.5-0.5b-instruct-q8_0.gguf",
        ApproxSizeBytes: 531_067_136));
    options.Models.Add(new HubModelOption(
        "smollm2:360m-instruct-q8_0",
        "HuggingFaceTB/SmolLM2-360M-Instruct-GGUF",
        "smollm2-360m-instruct-q8_0.gguf",
        ApproxSizeBytes: 386_404_352));
    // The quality step-up: 1.5B Q4 still decodes interactively on WebGPU and actually knows things
    // the 0.5B gets wrong (~1.1GB one-time download, browser-cached).
    options.Models.Add(new HubModelOption(
        "qwen2.5:1.5b-instruct-q4_k_m",
        "Qwen/Qwen2.5-1.5B-Instruct-GGUF",
        "qwen2.5-1.5b-instruct-q4_k_m.gguf",
        ApproxSizeBytes: 1_117_320_000));
    // Qwen3 0.6B (Q8_0) - newest Qwen small model; standard transformer arch. WebGPU-verified.
    options.Models.Add(new HubModelOption(
        "qwen3:0.6b-q8_0",
        "Qwen/Qwen3-0.6B-GGUF",
        "Qwen3-0.6B-Q8_0.gguf",
        ApproxSizeBytes: 639_446_688));
    // LFM2 1.2B (Q4_K_M) - Liquid AI's ShortConv hybrid arch. WebGPU-verified (ShortConv WGSL).
    options.Models.Add(new HubModelOption(
        "lfm2:1.2b-q4_k_m",
        "LiquidAI/LFM2-1.2B-GGUF",
        "LFM2-1.2B-Q4_K_M.gguf",
        ApproxSizeBytes: 730_893_248));
    // ── The role-play / tool-calling tier ──────────────────────────────────────────────────────────
    // Same "qwen3" architecture as the 0.6B above, so no engine work - these are a size step, not a port.
    // They exist because a 0.5-0.6B model cannot hold a character: it restates the scene back at you and
    // its tool calls are unreliable. Both are hybrid reasoning models, and their <think> blocks are
    // filtered in AiChatEngine (ReasoningModelTests gates it) rather than being shown or spoken.
    //
    // ⚠️ ONE model stays resident. A room whose characters use DIFFERENT models pays a full load per turn,
    // and at this size that dominates everything else - prefer one model for the whole cast and let the
    // personas differentiate them.
    options.Models.Add(new HubModelOption(
        "qwen3:1.7b-q8_0",
        "Qwen/Qwen3-1.7B-GGUF",
        "Qwen3-1.7B-Q8_0.gguf",
        ApproxSizeBytes: 1_834_426_016));
    // Q4_K_M rather than Q8_0 on purpose: at 4B the 8-bit file is ~4.3GB, and the quality gained over
    // Q4_K_M does not pay for that in a browser tab.
    options.Models.Add(new HubModelOption(
        "qwen3:4b-q4_k_m",
        "Qwen/Qwen3-4B-GGUF",
        "Qwen3-4B-Q4_K_M.gguf",
        ApproxSizeBytes: 2_497_280_256));
});

// RunAsync's callback runs after auto-starting services are up. The test suite runs ONLY in the window
// scope and ONLY when asked for with `?tests=1` - the shared worker loads this same Program.cs and must
// serve as a worker rather than re-run the suite, and a normal visitor must not trigger a model download.
// `?filter=Name` scopes the run; `?heavy=1` includes the model-downloading tests.
// The Playwright TestRunner reads the READY/TEST/RESULTS console lines this writes.
await builder.Build().RunAsync(async app =>
{
    if (!JS.IsWindow) return;
    if (!AiTestSuiteRunner.RequestedFromLocation()) return;

    var failed = await AiTestSuiteRunner.RunAllAsync(
        app.Services,
        AiTestSuiteRunner.FilterFromLocation(),
        AiTestSuiteRunner.HeavyFromLocation());

    // Surface the count where a driver can read it without parsing, too.
    JS.Set("spawndevAiTestFailures", failed);
});
