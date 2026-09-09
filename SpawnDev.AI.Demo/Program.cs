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
// The physical Reachy Mini, for whichever character is given it. Holds no connection until asked, so a
// visitor with no robot pays nothing for this being registered.
builder.Services.AddSingleton<SpawnDev.AI.Demo.ReachyDriver>();
// Which model downloads the user has agreed to. Consent, not cache state - see ModelConsent.
builder.Services.AddSingleton<SpawnDev.AI.Demo.ModelConsent>();
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
    // ── THE MODEL CATALOGUE ────────────────────────────────────────────────────────────────────────
    //
    // 🔴 NOTHING HERE DOWNLOADS BY ITSELF. Every size below is shown in the picker before the user
    // commits, and `GET /ai/models` reports whether this device already has each one (read from the
    // torrent client, not from a note the app keeps about itself). A wrong size in this list misinforms
    // exactly the consent it exists to obtain, so these are measured, not estimated.
    //
    // The Description is what the picker shows. It says what the model is FOR and what it costs, because
    // a name and a byte count do not tell anyone whether a model can hold a character.
    options.Models.Add(new HubModelOption(
        "qwen2.5:0.5b-instruct-q8_0",
        "Qwen/Qwen2.5-0.5B-Instruct-GGUF",
        "qwen2.5-0.5b-instruct-q8_0.gguf",
        ApproxSizeBytes: 531_067_136,
        Description: "Smallest useful chat model - the quickest way to see the demo work. Out of its "
                   + "depth in a group scene: it restates the scene back at you instead of acting in it."));
    options.Models.Add(new HubModelOption(
        "smollm2:360m-instruct-q8_0",
        "HuggingFaceTB/SmolLM2-360M-Instruct-GGUF",
        "smollm2-360m-instruct-q8_0.gguf",
        ApproxSizeBytes: 386_404_352,
        Description: "Tiny and near-instant. Useful for checking the plumbing; loses the thread fast."));
    options.Models.Add(new HubModelOption(
        "qwen3:0.6b-q8_0",
        "Qwen/Qwen3-0.6B-GGUF",
        "Qwen3-0.6B-Q8_0.gguf",
        ApproxSizeBytes: 639_446_688,
        Description: "Small but modern, and tool-capable. The cheapest model here that follows a persona "
                   + "at all. Measured on this machine: ~50s to become resident."));
    options.Models.Add(new HubModelOption(
        "lfm2:1.2b-q4_k_m",
        "LiquidAI/LFM2-1.2B-GGUF",
        "LFM2-1.2B-Q4_K_M.gguf",
        ApproxSizeBytes: 730_893_248,
        Description: "Liquid AI's ShortConv hybrid architecture - fast for its size, and a different "
                   + "family to compare against the Qwen models."));
    options.Models.Add(new HubModelOption(
        "qwen2.5:1.5b-instruct-q4_k_m",
        "Qwen/Qwen2.5-1.5B-Instruct-GGUF",
        "qwen2.5-1.5b-instruct-q4_k_m.gguf",
        ApproxSizeBytes: 1_117_320_000,
        Description: "Knows appreciably more than the 0.5B and still decodes interactively."));
    // ── The role-play tier ─────────────────────────────────────────────────────────────────────────
    // Same "qwen3" architecture as the 0.6B, so no engine work - a size step, not a port. Both are
    // hybrid reasoning models; AiChatEngine suppresses their thinking at the prompt and filters it from
    // the stream (ReasoningModelTests gates both halves), because otherwise the reasoning renders in the
    // bubble AND the TTS reads it aloud.
    options.Models.Add(new HubModelOption(
        "qwen3:1.7b-q8_0",
        "Qwen/Qwen3-1.7B-GGUF",
        "Qwen3-1.7B-Q8_0.gguf",
        ApproxSizeBytes: 1_834_426_016,
        Description: "RECOMMENDED for characters and role-play. The best balance measured here: ~62s to "
                   + "become resident, then 8.8 tok/s, and the only model in this list that actually held "
                   + "a persona in the group-chat gate."));
    // Q4_K_M rather than Q8_0 deliberately: at 4B the 8-bit file is ~4.3GB, and the quality it buys over
    // Q4_K_M does not pay for that in a browser tab.
    options.Models.Add(new HubModelOption(
        "qwen3:4b-q4_k_m",
        "Qwen/Qwen3-4B-GGUF",
        "Qwen3-4B-Q4_K_M.gguf",
        ApproxSizeBytes: 2_497_280_256,
        Description: "More capable on hard questions, but it costs ~2.5 minutes to become resident and "
                   + "did not answer better in role-play. Worth it for reasoning, not for dialogue."));
    // ── Multimodal ─────────────────────────────────────────────────────────────────────────────────
    // 🔴 THE INTERESTING ONE, and the reason it is worth 6.9GB: gemma4 takes TEXT, IMAGES and AUDIO and
    // returns text. That is the path to a character that can look at a Reachy snapshot and hear the room
    // directly, instead of chaining Whisper to a text-only model.
    //
    // ⚠️ SpawnDev.AI currently loads its `model` layer only, which is TEXT-IN, TEXT-OUT. The vision and
    // audio inputs need the separate `projector` layer (~167 MB) plus a content-blocks path through the
    // router - the ILGPU.ML demo's Gemma chat page already does exactly that and is the reference.
    // Listing it text-only is honest and useful now; multimodal is the next step, not a claim made here.
    //
    // Arrives via the OLLAMA registry rather than Hugging Face, because that is how its layers are
    // published - the same coordinates the ILGPU.ML demo uses.
    options.Models.Add(HubModelOption.FromOllama(
        "gemma4:12b",
        "gemma4",
        "12b",
        approxSizeBytes: 7_381_382_048,   // MEASURED from the hub webseed, not estimated
        description: "Text + image + audio in, text out - the multimodal path for Reachy snapshots and "
                   + "microphone input. LARGE: ~6.9 GB for the text decoder alone, and it needs a WebGPU "
                   + "GPU with the VRAM to match. Text-only in this demo so far."));
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
