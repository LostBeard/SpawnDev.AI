using ILGPU.Runtime;
using SpawnDev.ILGPU.ML.Hub;
using SpawnDev.ILGPU.ML.Pipelines;
using SpawnDev.WebTorrent;

namespace SpawnDev.AI.Server;

/// <summary>An image model the engine can serve (hub coordinates; SD-Turbo-class ONNX trio repo).</summary>
public sealed record ImageModelOption(string Name, string RepoId, string Note = "");

/// <summary>A generated image.</summary>
public sealed record AiGeneratedImage(byte[] Rgba, int Width, int Height, int Seed, string Model, double InferenceMs);

/// <summary>
/// The image-generation engine: its OWN residency slot (an image model lives alongside the LLM -
/// per-kind residency, one resident image pipeline, swap on demand) and its own generation gate.
/// Serves the /v1/images/generations endpoint, the generate_image tool, and the MCP surface.
/// Weights stream from the hub (WebTorrent + HF CDN) on both desktop and browser.
/// </summary>
public sealed class AiImageEngine : IDisposable
{
    private readonly WebTorrentClient _webTorrent;
    private readonly HttpClient _http;
    private readonly Accelerator _accelerator;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ImageGenerationPipeline? _resident;
    private string? _residentName;

    public AiImageEngine(WebTorrentClient webTorrent, HttpClient http, Accelerator accelerator)
    {
        _webTorrent = webTorrent;
        _http = http;
        _accelerator = accelerator;
    }

    /// <summary>Servable image models. Verified-first: SD-Turbo is E2E-gated (WebGPU/CUDA/OpenCL);
    /// add candidates after they pass the inspector pre-flight + a generation gate.</summary>
    public List<ImageModelOption> Models { get; } = new()
    {
        new("sd-turbo", SpawnDev.ILGPU.ML.Hub.ModelHub.KnownModels.SDTurbo,
            "verified - single-step 512x512, E2E-gated on WebGPU/CUDA/OpenCL"),
    };

    /// <summary>The model used when a request names none.</summary>
    public string DefaultModel { get; set; } = "sd-turbo";

    /// <summary>Progress callback while a model loads ((stage, pct)).</summary>
    public Action<string, int>? OnLoadProgress { get; set; }

    /// <summary>Called at the START of <see cref="GenerateAsync"/> to evict the OTHER model kind (the LLM)
    /// from the shared GPU before we load/run SD-Turbo - one large model resident per device. Prevents the
    /// LLM + image co-residence OOM / WebGPU device-loss (page crash). No-op if null.</summary>
    public Func<Task>? EvictOtherKind { get; set; }

    /// <summary>Resolve a requested name to a known option (null = unknown).</summary>
    public ImageModelOption? Resolve(string? name)
    {
        name = string.IsNullOrWhiteSpace(name) ? DefaultModel : name;
        return Models.FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Generate one image (loads/swaps the resident model as needed; serialized).</summary>
    public async Task<AiGeneratedImage> GenerateAsync(string prompt, string? model = null,
        int? seed = null, int? steps = null, CancellationToken ct = default)
    {
        var opt = Resolve(model)
            ?? throw new FileNotFoundException($"Image model '{model}' is not in the image model list.");
        // Free the LLM's GPU memory BEFORE we take our gate / load SD-Turbo - one large model resident per
        // device (LLM + SD-Turbo together OOM'd the WebGPU device -> page crash). Gate-free -> no deadlock.
        if (EvictOtherKind != null) await EvictOtherKind().ConfigureAwait(false);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_resident == null || !string.Equals(_residentName, opt.Name, StringComparison.OrdinalIgnoreCase))
            {
                _resident?.Dispose();
                _resident = null;
                var hub = new HubModelStream(_webTorrent, _http);
                _resident = await ImageGenerationPipeline.CreateAsync(_accelerator, hub, opt.RepoId,
                    onProgress: OnLoadProgress).ConfigureAwait(false);
                _residentName = opt.Name;
            }
            var pipe = _resident;
            pipe.NumInferenceSteps = steps ?? 1;   // SD-Turbo default single-step
            pipe.GuidanceScale = 0f;
            int usedSeed = seed ?? Random.Shared.Next();
            pipe.Seed = usedSeed;

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var result = await pipe.RunAsync(new ImageGenerationInput { Prompt = prompt }).ConfigureAwait(false);
            sw.Stop();
            return new AiGeneratedImage(result.ImageRGBA, result.Width, result.Height, usedSeed,
                opt.Name, sw.Elapsed.TotalMilliseconds);
        }
        finally { _gate.Release(); }
    }

    /// <summary>Free the resident image pipeline from GPU memory (for the LLM registry to call before it
    /// loads a model). Safe when nothing is resident. Serialized on the generation gate.</summary>
    public async Task EvictAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try { _resident?.Dispose(); _resident = null; _residentName = null; }
        finally { _gate.Release(); }
    }

    public void Dispose()
    {
        _resident?.Dispose();
        _resident = null;
        _gate.Dispose();
    }
}
