using ILGPU.Runtime;
using SpawnDev.ILGPU.ML;
using SpawnDev.ILGPU.ML.GGUF;
using SpawnDev.ILGPU.ML.Pipelines;
using SpawnDev.ILGPU.ML.Preprocessing;

namespace SpawnDev.AI.Server;

/// <summary>
/// A model loaded onto the accelerator: the session, the reusable generator, the tokenizer, and the
/// detected chat format. One per resident model.
/// </summary>
public sealed class LoadedModel : IDisposable
{
    public required AiModelInfo Info { get; init; }
    public required GGUFModel Gguf { get; init; }
    public required InferenceSession Session { get; init; }
    public required GgufGenerator Generator { get; init; }
    public required SentencePieceTokenizer Tokenizer { get; init; }
    public required ChatTemplates.ChatFormat Format { get; init; }
    /// <summary>An owned backing resource disposed with the model (a hub model stream in the browser).</summary>
    public IDisposable? OwnedStream { get; init; }

    public void Dispose() { Generator.Dispose(); Session.Dispose(); OwnedStream?.Dispose(); }
}

/// <summary>
/// Loads models on demand from an <see cref="IAiModelProvider"/> and serializes generation.
/// <see cref="InferenceSession"/> is single-decode-at-a-time (one mutable KV cursor, no locks), so
/// all generation goes through a single gate - which is also what real Ollama does on one GPU. v1
/// keeps ONE model resident and swaps when a request asks for a different one (these models are
/// GBs; one-at-a-time bounds GPU/VRAM memory).
/// </summary>
public sealed class ModelRegistry : IAsyncDisposable
{
    private readonly IAiModelProvider _provider;
    private readonly Accelerator _accelerator;
    private readonly int _maxSeqLen;
    private readonly SemaphoreSlim _gate = new(1, 1); // serialize decode (and model swaps)
    private LoadedModel? _resident;

    public ModelRegistry(IAiModelProvider provider, Accelerator accelerator, int maxSeqLen = 8192)
    {
        _provider = provider;
        _accelerator = accelerator;
        _maxSeqLen = maxSeqLen;
    }

    /// <summary>The model provider backing this registry (listing / metadata endpoints).</summary>
    public IAiModelProvider Provider => _provider;

    /// <summary>
    /// Enable WebGPU decode capture/replay on loaded generators (the 1.5 -&gt; 34 tok/s browser lever:
    /// the first decode step captures the graph as a dispatch plan, every token after is a patched
    /// single-round-trip replay). No-op on non-WebGPU accelerators, so it defaults ON.
    /// </summary>
    public bool EnableWebGPUDecodeCapture { get; set; } = true;

    /// <summary>
    /// Called at the START of every <see cref="AcquireAsync"/> (before this registry loads/uses its model)
    /// to evict the OTHER model kind (the image pipeline) from the shared GPU - one large model resident per
    /// device. Prevents the LLM + SD-Turbo co-residence OOM / WebGPU device-loss (page crash). No-op if null.
    /// </summary>
    public Func<Task>? EvictOtherKind { get; set; }

    /// <summary>
    /// A serialized lease on a loaded model. Hold it for the duration of ONE generation, then dispose to
    /// release the gate. The model is loaded (or swapped in) before the lease is returned.
    /// </summary>
    public sealed class Lease : IDisposable
    {
        private readonly SemaphoreSlim _gate;
        private bool _released;
        public LoadedModel Model { get; }
        internal Lease(LoadedModel model, SemaphoreSlim gate) { Model = model; _gate = gate; }
        public void Dispose() { if (!_released) { _released = true; _gate.Release(); } }
    }

    /// <summary>
    /// Acquire the generation gate and ensure <paramref name="modelName"/> is the resident model (loading
    /// or swapping as needed). Throws <see cref="FileNotFoundException"/> if the provider can't serve it.
    /// </summary>
    public async Task<Lease> AcquireAsync(string modelName, CancellationToken ct = default)
    {
        // Free the OTHER kind's GPU model (the image pipeline) BEFORE we take our gate / load - one large
        // model resident per device. Called gate-free here so it cannot deadlock against the image gate.
        if (EvictOtherKind != null) await EvictOtherKind().ConfigureAwait(false);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var canonical = await _provider.ResolveAsync(modelName, ct).ConfigureAwait(false)
                ?? throw new FileNotFoundException($"Model '{modelName}' is not available from this provider.");

            if (_resident == null || !string.Equals(_resident.Info.Name, canonical, StringComparison.OrdinalIgnoreCase))
            {
                _resident?.Dispose();
                _resident = null;
                _resident = await _provider.LoadAsync(canonical, _accelerator, _maxSeqLen,
                    EnableWebGPUDecodeCapture, ct).ConfigureAwait(false);
            }
            return new Lease(_resident, _gate);
        }
        catch
        {
            _gate.Release(); // never strand the gate on a load failure
            throw;
        }
    }

    /// <summary>Free the resident LLM from GPU memory (for the image engine to call before it loads
    /// SD-Turbo). Safe when nothing is resident (no-op). Serialized on the same gate as generation.</summary>
    public async Task EvictAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try { _resident?.Dispose(); _resident = null; }
        finally { _gate.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try { _resident?.Dispose(); _resident = null; }
        finally { _gate.Release(); _gate.Dispose(); }
    }
}
