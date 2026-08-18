using SpawnDev.AI.Demo.Pages;
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
});

await builder.Build().RunAsync();
