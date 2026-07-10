using System.Text.Json;
using ILGPU.Runtime;
using SpawnDev.ILGPU;
using SpawnDev.ILGPU.ML;
using SpawnDev.WebTorrent;

namespace SpawnDev.AI.Server;

/// <summary>The worker-side API surface (interface-only, per SpawnDev.BlazorJS.WebWorkers service
/// dispatch). Window-side code calls this through a worker proxy; frames stream back via the
/// marshalled <see cref="Action{String}"/> callback.</summary>
public interface IAiWorkerApi
{
    /// <summary>Readiness/status line (also warms the worker instance).</summary>
    Task<string> GetStatusAsync();

    /// <summary>Route one protocol request (same method/path/body as HTTP). Every response frame -
    /// including the single frame of buffered responses - arrives through <paramref name="onFrame"/>
    /// as <see cref="AiWireFrame"/> JSON; the task completes after the terminal frame.</summary>
    Task HandleRequestAsync(string method, string path, string? bodyJson, Action<string> onFrame);
}

/// <summary>Configuration for the in-browser (worker) AI server - register in DI in ALL scopes
/// (the same Program.cs runs in Window and Worker; only the worker instance touches the GPU).</summary>
public sealed class AiWorkerServerOptions
{
    /// <summary>Models the worker can serve (streamed from the hub, browser-cached).</summary>
    public List<HubModelOption> Models { get; } = new();
    /// <summary>Context cap per loaded model.</summary>
    public int MaxSeqLen { get; set; } = 4096;
    /// <summary>Server-wide output-token clamp.</summary>
    public int MaxOutputTokens { get; set; } = 1024;
}

/// <summary>
/// The in-browser AI server: lives in a (shared) web worker, owns the WebGPU accelerator + hub model
/// registry + <see cref="AiApiRouter"/>, and answers the same Ollama/OpenAI/Anthropic protocol as the
/// desktop HTTP host - over marshalled callback frames instead of sockets. Lazy: the GPU and model
/// registry initialize on the first request.
/// </summary>
public sealed class AiWorkerServer : IAiWorkerApi, IAsyncDisposable
{
    private readonly WebTorrentClient _webTorrent;
    private readonly HttpClient _http;
    private readonly AiWorkerServerOptions _options;
    private readonly SemaphoreSlim _initGate = new(1, 1);
    private Accelerator? _accelerator;
    private ModelRegistry? _registry;
    private AiApiRouter? _router;
    private AiImageEngine? _images;
    private AiToolRegistry? _tools;

    public AiWorkerServer(WebTorrentClient webTorrent, HttpClient http, AiWorkerServerOptions options)
    {
        _webTorrent = webTorrent;
        _http = http;
        _options = options;
    }

    public async Task<string> GetStatusAsync()
    {
        await EnsureInitializedAsync().ConfigureAwait(false);
        return $"ready | accelerator: {_accelerator!.Name} ({_accelerator.AcceleratorType}) | models: {_options.Models.Count}";
    }

    public async Task HandleRequestAsync(string method, string path, string? bodyJson, Action<string> onFrame)
    {
        var transport = new FrameTransport(onFrame);
        try
        {
            await EnsureInitializedAsync().ConfigureAwait(false);
            JsonElement? body = null;
            if (!string.IsNullOrWhiteSpace(bodyJson))
            {
                using var doc = JsonDocument.Parse(bodyJson);
                body = doc.RootElement.Clone();
            }
            bool handled = await _router!.TryHandleAsync(method, path, body, transport).ConfigureAwait(false);
            if (!handled)
                await transport.WriteJsonAsync(404, new { error = $"unknown route {method} {path}" }).ConfigureAwait(false);
        }
        catch (FileNotFoundException ex)
        {
            await transport.WriteJsonAsync(404, new { error = ex.Message }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            transport.Error(ex);
        }
        finally
        {
            transport.End();
        }
    }

    private async Task EnsureInitializedAsync()
    {
        if (_router != null) return;
        await _initGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_router != null) return;
            // DIAGNOSTIC (2026-07-05): attribute the WebGPU model-load time (stream/zero-copy vs .NET-chunked
            // CopyFromCPU) so the ILGPU CopyFromCPU-on-WebGPU cost is MEASURED, not assumed. Emits a
            // per-model [WL SUMMARY] to the browser console. Revert once the ILGPU transport fix lands.
            SpawnDev.ILGPU.ML.InferenceSession.TraceWeightLoad = true;
            var builder = MLContext.Create();
            await builder.AllAcceleratorsAsync().ConfigureAwait(false);
            var context = builder.ToContext();
            _accelerator = await context.CreatePreferredAcceleratorAsync().ConfigureAwait(false)
                ?? throw new NotSupportedException("No GPU accelerator is available in this browser (WebGPU required).");
            var provider = new HubModelProvider(_webTorrent, _http, _options.Models);
            _registry = new ModelRegistry(provider, _accelerator, _options.MaxSeqLen);
            var engine = new AiChatEngine(_registry) { MaxOutputTokens = _options.MaxOutputTokens };
            // Image generation + the agentic tool loop IN THE BROWSER: SD-Turbo streams from the
            // hub onto the same WebGPU device (E2E-gated path); generate_image registers once and
            // serves the internal loop, /v1/images/generations, and /ai/artifacts over the worker
            // frames - the public page's chat can paint.
            _images = new AiImageEngine(_webTorrent, _http, _accelerator);
            // One large GPU model resident per device: each kind evicts the other before it loads/runs, so
            // the LLM and SD-Turbo never co-reside (co-residence OOM'd the WebGPU device -> page crash).
            _images.EvictOtherKind = () => _registry!.EvictAsync();
            _registry.EvictOtherKind = () => _images!.EvictAsync();
            _tools = new AiToolRegistry();
            _tools.Register(new GenerateImageTool(_images, _tools));
            engine.Tools = _tools;
            _router = new AiApiRouter(engine) { Images = _images, Tools = _tools };
        }
        finally { _initGate.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        _images?.Dispose();
        if (_registry != null) await _registry.DisposeAsync().ConfigureAwait(false);
        _accelerator?.Dispose();
    }

    /// <summary>The callback skin of <see cref="IAiServerTransport"/>: every write becomes one
    /// <see cref="AiWireFrame"/> pushed through the marshalled callback.</summary>
    private sealed class FrameTransport : IAiServerTransport
    {
        private static readonly JsonSerializerOptions J = new(JsonSerializerDefaults.Web);
        private readonly Action<string> _onFrame;
        private bool _terminal;
        public FrameTransport(Action<string> onFrame) => _onFrame = onFrame;

        public CancellationToken Aborted => CancellationToken.None; // worker requests aren't socket-bound; cancellation lands later via client-side abort frames

        public Task WriteJsonAsync(int statusCode, object payload)
        {
            _terminal = true;
            _onFrame(new AiWireFrame { T = "json", Status = statusCode, Data = JsonSerializer.Serialize(payload, J) }.ToJson());
            return Task.CompletedTask;
        }

        public Task WriteTextAsync(int statusCode, string text)
        {
            _terminal = true;
            _onFrame(new AiWireFrame { T = "text", Status = statusCode, Data = text }.ToJson());
            return Task.CompletedTask;
        }

        public Task StartEventStreamAsync(AiEventStreamKind kind)
        {
            _onFrame(new AiWireFrame { T = "start", Kind = kind == AiEventStreamKind.Sse ? "sse" : "ndjson" }.ToJson());
            return Task.CompletedTask;
        }

        public Task WriteEventAsync(string? eventName, object payload)
        {
            _onFrame(new AiWireFrame { T = "event", Name = eventName, Data = JsonSerializer.Serialize(payload, J) }.ToJson());
            return Task.CompletedTask;
        }

        public Task WriteRawAsync(string text)
        {
            _onFrame(new AiWireFrame { T = "raw", Data = text }.ToJson());
            return Task.CompletedTask;
        }

        public void Error(Exception ex)
        {
            _terminal = true;
            _onFrame(new AiWireFrame { T = "error", Status = 500, Data = ex.Message }.ToJson());
        }

        public void End()
        {
            if (!_terminal) { /* streamed responses end explicitly */ }
            _onFrame(new AiWireFrame { T = "end" }.ToJson());
        }
    }
}
