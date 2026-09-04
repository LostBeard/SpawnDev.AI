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

    /// <summary>
    /// Time a fixed, pure-.NET workload inside the worker. Diagnostic only - no GPU, no interop.
    /// </summary>
    /// <remarks>
    /// ⚠️ WHY THIS EXISTS. The demo hosts the engine in a worker; PlaywrightMultiTest runs it in the page.
    /// MEASURED 2026-09-03: the SAME compiled graph (Whisper decode, enc 227 / dec 374 nodes) costs 972 ms
    /// per step in the worker and 357 ms in the page - 2.67x, with the graph, the model and the WebGPU
    /// adapter flags all ruled out by measurement. Per-node cost in this engine is .NET-side bookkeeping,
    /// so the open question is whether the .NET WASM runtime itself is simply slower in a worker (trace
    /// JIT / jiterpreter warmup differing by scope would look exactly like this).
    /// <para>
    /// This runs the identical loop the caller can run on the window side, so the comparison isolates
    /// managed execution speed from anything GPU. A ratio near 1 exonerates the runtime and points at
    /// interop or contention; a ratio near 2.7 says the runtime IS the gap and no amount of graph work
    /// will close it.
    /// </para>
    /// </remarks>
    Task<double> BenchmarkManagedAsync(int iterations);

    /// <summary>
    /// Time N scheduler round-trips inside the worker. Diagnostic only.
    /// </summary>
    /// <remarks>
    /// ⚠️ The companion to <see cref="BenchmarkManagedAsync"/>, and the one that can see what a tight
    /// managed loop cannot. The executor awaits per node, and an await that actually yields lands on the
    /// host's task queue. In a WINDOW that queue is driven by the page's event loop; in a WORKER it is
    /// driven by the worker's, and a nested <c>setTimeout</c> is clamped there. The observed gap is
    /// ~1 ms per node across 601 nodes - the shape of a per-yield timer clamp, not of slower arithmetic.
    /// <para><paramref name="mode"/>: 0 = <c>Task.Yield</c> (queue microtask/continuation),
    /// 1 = <c>Task.Delay(0)</c> (timer path).</para>
    /// </remarks>
    Task<double> BenchmarkYieldAsync(int iterations, int mode);

    /// <summary>
    /// Time N .NET-&gt;JS-&gt;.NET round trips inside the worker. Diagnostic only.
    /// </summary>
    /// <remarks>
    /// ⚠️ The third leg of the triangulation. MEASURED 2026-09-03: managed execution is NOT the gap - the
    /// worker ran the same pure-.NET workload at 0.89x the window's time, i.e. slightly FASTER. Whatever
    /// costs ~1 ms per graph node in the worker and not in the page therefore leaves the managed heap, and
    /// every dispatch this engine makes crosses to JS. This measures that crossing alone: no GPU, no await.
    /// </remarks>
    Task<double> BenchmarkInteropAsync(int iterations);
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
    /// <summary>
    /// What our models may hold on the GPU in the browser, in bytes.
    /// </summary>
    /// <remarks>
    /// ⚠️ A guess, and necessarily so: WebGPU reports buffer LIMITS, not free memory, so there is nothing
    /// to read. 4 GB comfortably holds the LLM, the recogniser and the voice at once - the set one
    /// conversation turn needs - while still forcing SD-Turbo to trade against the LLM, which is the
    /// pairing that actually OOM'd the device and crashed the page.
    /// </remarks>
    private const long BrowserVramBudgetBytes = 4L * 1024 * 1024 * 1024;

    private AiSpeechEngine? _speech;
    private AiVoiceEngine? _voice;
    private AiVadEngine? _vad;
    private GpuResidency? _residency;
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

    /// <summary>Runs <see cref="ManagedBenchmark.Run"/> in the worker. See IAiWorkerApi for why.</summary>
    public Task<double> BenchmarkManagedAsync(int iterations) =>
        Task.FromResult(ManagedBenchmark.Run(iterations));

    /// <summary>Runs <see cref="ManagedBenchmark.YieldAsync"/> in the worker. See IAiWorkerApi for why.</summary>
    public Task<double> BenchmarkYieldAsync(int iterations, int mode) =>
        ManagedBenchmark.YieldAsync(iterations, mode);

    /// <summary>Runs <see cref="ManagedBenchmark.Interop"/> in the worker. See IAiWorkerApi for why.</summary>
    public Task<double> BenchmarkInteropAsync(int iterations) =>
        Task.FromResult(ManagedBenchmark.Interop(iterations));

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
            _speech = new AiSpeechEngine(_webTorrent, _http, _accelerator);
            _voice = new AiVoiceEngine(_webTorrent, _http, _accelerator);
            // The endpointer. 643 KB, served from this app's own wwwroot rather than the hub, and the
            // reason a hands-free turn now ends when you stop talking instead of when a 30 s timer expires.
            _vad = new AiVadEngine(_http, _accelerator);

            // ⚠️ Per-kind residency is a hard rule here, so every kind must evict every OTHER kind - with
            // three kinds that is no longer a pair of hooks but a ring, and adding a fourth would be worse
            // again. MEASURED 2026-08-30 on the two-kind version: three interleaved image+chat turns spent
            // ~130s re-uploading an SD-Turbo UNet that had just been evicted (38.4s, 44.4s, 46.6s), on a GPU
            // with 10.6 GB free where nothing needed evicting at all. The right shape is a VRAM budget with
            // LRU eviction, so a model is only evicted when the incoming one genuinely does not fit; this
            // ring is the honest interim, not the destination.
            // Residency is a BUDGET, not a ring. Each kind declares what it costs while resident and
            // only yields when the incoming model genuinely does not fit - least-recently-used first.
            //
            // The ring this replaces evicted every kind before every other kind loaded. Safe, and hugely
            // wasteful: MEASURED, three interleaved image+chat turns spent ~130s re-uploading an SD-Turbo
            // UNet that had just been evicted, on a GPU with 10.6 GB free. A hands-free turn makes it worse
            // still - transcribe, chat, speak is three kinds in a row, so a ring means three reloads PER
            // TURN and no conversation is possible at any inference speed.
            //
            // ⚠️ "Evict nothing" is NOT the fix for "evict everything". Rose froze at 96% VRAM when a
            // recogniser joined an 8B LLM and a voice cloner on one 12 GB card - a starved CUDA op wedged
            // with no cancellation, so the turn never ended. Hence a budget with headroom.
            //
            // ⚠️ In the browser there is no free-VRAM number to read - WebGPU exposes buffer limits,
            // not memory availability - so this budget is deliberately conservative and counts only OUR
            // models. It cannot see the page's other GPU use, let alone another tab's.
            _residency = new GpuResidency(BrowserVramBudgetBytes)
            {
                OnLog = msg => Console.WriteLine(msg),
            };
            _residency.Register("image", () => _images!.IsLoaded, 2_600L * 1024 * 1024,
                () => _images!.EvictAsync());
            _residency.Register("chat", () => _registry!.IsLoaded, 1_400L * 1024 * 1024,
                () => _registry!.EvictAsync());
            _residency.Register("speech", () => _speech!.IsLoaded, 200L * 1024 * 1024,
                () => _speech!.EvictAsync());
            _residency.Register("voice", () => _voice!.IsLoaded, 450L * 1024 * 1024,
                () => _voice!.EvictAsync());
            // ⚠️ The endpointer is deliberately tiny in this table and that is the POINT, not an estimate
            // fudge: it runs ~31 times a second for as long as the microphone is open, so it must never be
            // the model something else evicts. A reload mid-utterance loses the turn being spoken.
            _residency.Register("vad", () => _vad!.IsLoaded, 32L * 1024 * 1024,
                () => _vad!.EvictAsync());

            _images.EvictOtherKind = () => _residency.EnsureRoomForAsync("image");
            _registry.EvictOtherKind = () => _residency.EnsureRoomForAsync("chat");
            _speech.EvictOtherKind = () => _residency.EnsureRoomForAsync("speech");
            _voice.EvictOtherKind = () => _residency.EnsureRoomForAsync("voice");
            _vad.EvictOtherKind = () => _residency.EnsureRoomForAsync("vad");

            // ⚠️ These were declared and never subscribed, so a cold model load was SILENT. ZipVoice pulls
            // two int8 graphs, a token table and a 54 MB vocoder out of a remote archive; with nothing
            // reporting stages, the hands-free loop simply stopped talking for minutes and there was no way
            // to tell loading from hung from failed. A progress hook nobody subscribes to is worse than no
            // hook - it reads as instrumentation that already exists.
            _speech.OnLoadProgress = (stage, pct) => Console.WriteLine($"[AiSpeechEngine] {stage} {pct}%");
            _voice.OnLoadProgress = (stage, pct) => Console.WriteLine($"[AiVoiceEngine] {stage} {pct}%");

            _tools = new AiToolRegistry();
            _tools.Register(new GenerateImageTool(_images, _tools));
            // GitHub lookup: the model can answer questions about the SpawnDev libraries + crew by fetching
            // from GitHub (allowlisted hosts, CORS-friendly, so it works in the worker over the same HttpClient).
            _tools.Register(new GitHubTool(_http));
            engine.Tools = _tools;
            _router = new AiApiRouter(engine)
            {
                Images = _images, Tools = _tools, Speech = _speech, Voice = _voice, Vad = _vad,
            };
        }
        finally { _initGate.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        _images?.Dispose();
        if (_registry != null) await _registry.DisposeAsync().ConfigureAwait(false);
        _speech?.Dispose();
        _voice?.Dispose();
        _vad?.Dispose();
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

/// <summary>
/// One fixed, allocation-light managed workload, run identically in the window and in the worker.
/// </summary>
/// <remarks>
/// ⚠️ Deliberately shaped like the executor's per-node bookkeeping rather than like a math kernel:
/// dictionary lookups, string hashing, small list churn and branchy integer work. A tight float loop
/// would measure the jiterpreter's best case and tell us nothing about why a graph walk is slow.
/// No GPU, no JS interop, no allocation spikes - just managed execution speed.
/// </remarks>
public static class ManagedBenchmark
{
    /// <summary>Run the workload and return elapsed milliseconds.</summary>
    public static double Run(int iterations)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        var names = new string[32];
        for (int i = 0; i < names.Length; i++) names[i] = "node_output_" + i;
        long acc = 0;
        var shape = new int[4];
        for (int it = 0; it < iterations; it++)
        {
            var name = names[it & 31];
            map[name] = it;
            if (map.TryGetValue(name, out var v)) acc += v;
            // shape-interpretation-shaped work: small array maths plus a branch per element
            shape[0] = 1; shape[1] = (it & 7) + 1; shape[2] = (it & 3) + 1; shape[3] = 64;
            int count = 1;
            for (int d = 0; d < shape.Length; d++) count *= shape[d] > 0 ? shape[d] : 1;
            acc += count;
            if ((it & 1023) == 0) map.Clear();
        }
        sw.Stop();
        // Consume acc so nothing is optimised away.
        if (acc == long.MinValue) Console.WriteLine("unreachable");
        return sw.Elapsed.TotalMilliseconds;
    }

    /// <summary>N .NET-&gt;JS-&gt;.NET calls through SpawnJS; returns elapsed milliseconds.</summary>
    /// <remarks>
    /// <c>JSEquals</c> on one held reference is chosen because the JS side does almost nothing (<c>a === b</c>)
    /// - anything measured here is the CROSSING, not the work. The reference is resolved once, outside the loop.
    /// </remarks>
    public static double Interop(int iterations)
    {
        var js = SpawnDev.SpawnJS.SpawnJSRuntime.Instance;
        var g = js.GlobalThis!;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        int acc = 0;
        for (int i = 0; i < iterations; i++) if (g.JSEquals(g, true)) acc++;
        sw.Stop();
        if (acc < 0) Console.WriteLine("unreachable");
        return sw.Elapsed.TotalMilliseconds;
    }

    /// <summary>N scheduler round-trips; returns elapsed milliseconds. mode 0 = Yield, 1 = Delay(0).</summary>
    public static async Task<double> YieldAsync(int iterations, int mode)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            if (mode == 0) await Task.Yield();
            else await Task.Delay(0).ConfigureAwait(false);
        }
        sw.Stop();
        return sw.Elapsed.TotalMilliseconds;
    }
}
