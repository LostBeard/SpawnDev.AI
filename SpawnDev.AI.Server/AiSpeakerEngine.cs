using ILGPU.Runtime;
using SpawnDev.ILGPU.ML;
using SpawnDev.ILGPU.ML.Hub;
using SpawnDev.ILGPU.ML.Pipelines;
using Float32Array = SpawnDev.SpawnJS.JSObjects.Float32Array;

namespace SpawnDev.AI.Server;

/// <summary>The outcome of listening to one stretch of speech for a voice.</summary>
/// <param name="Match">Who it sounds like, among the enrolled voices (before any guest was filed).</param>
/// <param name="Guest">The provisional guest the voice was filed under, when it was nobody enrolled; null otherwise.</param>
/// <param name="Seconds">How much speech it was.</param>
/// <param name="Reliable">False when the clip was too short to trust an embedding (under <see cref="SpeakerEmbeddingPipeline.MinReliableSeconds"/>).</param>
/// <param name="EmbedMs">Time to compute the embedding.</param>
public sealed record AiSpeakerResult(SpeakerMatch Match, SpeakerProfile? Guest, double Seconds, bool Reliable, double EmbedMs)
{
    /// <summary>The name to use for this voice: the matched person, the guest, or null.</summary>
    public string? Name => Match.Known ? Match.Name : Guest?.Name;
}

/// <summary>
/// Speaker identification for the AI server: ReDimNet2 voice embeddings on the shared accelerator, plus a
/// <see cref="SpeakerRegistry"/> of the voices that have been introduced.
/// </summary>
/// <remarks>
/// <para>
/// Follows <see cref="AiSpeechEngine"/>'s shape on purpose - one resident model, a load gate, an inference gate, delivery
/// through <see cref="IModelSource"/> - because the GPU is shared with chat, speech and voice and the same rules apply.
/// </para>
/// <para>
/// MEASURED (ReDimNet2-B6, LibriSpeech test-clean, 40 speakers): closed-set identification 99.2%, EER 1.2%, and with a
/// threshold that lets 1% of strangers in, 98.9% of enrolled speakers recognised and none named as someone else. Read speech
/// from strangers is easier than a household of relatives on a robot's microphone: calibrate
/// <see cref="SpeakerRegistry.AcceptThreshold"/> on the voices you have.
/// </para>
/// </remarks>
public sealed class AiSpeakerEngine : IDisposable
{
    private readonly IModelSource _source;
    private readonly Accelerator _accelerator;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly SemaphoreSlim _inferGate = new(1, 1);
    private SpeakerEmbeddingPipeline? _pipeline;

    /// <summary>HuggingFace repo of the speaker encoder (MIT; ReDimNet2-B6 exported to ONNX).</summary>
    public string ModelRepo { get; set; } = "soniqo/ReDimNet2-B6-ONNX-FP32";

    /// <summary>File in <see cref="ModelRepo"/>.</summary>
    public string ModelFile { get; set; } = "ReDimNet2B6.onnx";

    /// <summary>The voices that have been introduced. Persist it with <see cref="SpeakerRegistry.ToJson"/>.</summary>
    public SpeakerRegistry Registry { get; set; } = new();

    /// <summary>Speech shorter than this is never filed as a NEW guest: an embedding of a second of speech is not a voice.</summary>
    public double MinGuestSeconds { get; set; } = 1.5;

    /// <summary>Reports load progress as (stage, percent).</summary>
    public Action<string, int>? OnLoadProgress { get; set; }

    /// <summary>Whether the speaker model is resident.</summary>
    public bool IsLoaded => _pipeline != null;

    /// <param name="source">Model delivery - plain HTTP through the hub, cached in OPFS.</param>
    /// <param name="accelerator">The shared accelerator.</param>
    public AiSpeakerEngine(IModelSource source, Accelerator accelerator)
    {
        _source = source;
        _accelerator = accelerator;
    }

    /// <summary>Loads the model and runs it once, so the first real clip does not pay for kernel compilation.</summary>
    public async Task EnsureReadyAsync(CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct).ConfigureAwait(false);
        await _inferGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            await _pipeline!.EmbedAsync(new float[16000 * 2]).ConfigureAwait(false);
            Console.WriteLine($"[AiSpeakerEngine] warm embedding: {clock.Elapsed.TotalSeconds:F1}s (kernel compilation)");
        }
        finally { _inferGate.Release(); }
    }

    /// <summary>The voice embedding of 16 kHz-or-other mono PCM in [-1, 1].</summary>
    public async Task<float[]> EmbedAsync(float[] samples, int sampleRate, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (samples.Length == 0) throw new ArgumentException("no audio samples supplied", nameof(samples));
        return await RunAsync(() => _pipeline!.EmbedAsync(samples, sampleRate), ct).ConfigureAwait(false);
    }

    /// <summary>Browser path: the PCM is a JS typed array, uploaded to the device without crossing the managed heap.</summary>
    public async Task<float[]> EmbedAsync(Float32Array samples, int sampleRate, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (samples.Length == 0) throw new ArgumentException("no audio samples supplied", nameof(samples));
        return await RunAsync(() => _pipeline!.EmbedAsync(samples, sampleRate), ct).ConfigureAwait(false);
    }

    private async Task<float[]> RunAsync(Func<Task<float[]>> embed, CancellationToken ct)
    {
        await EnsureLoadedAsync(ct).ConfigureAwait(false);
        await _inferGate.WaitAsync(ct).ConfigureAwait(false);   // one embedding at a time: the pipeline owns device buffers
        try { return await embed().ConfigureAwait(false); }
        finally { _inferGate.Release(); }
    }

    /// <summary>Who this speech sounds like. Changes nothing.</summary>
    public async Task<AiSpeakerResult> IdentifyAsync(float[] samples, int sampleRate, CancellationToken ct = default)
    {
        var seconds = samples.Length / (double)sampleRate;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var embedding = await EmbedAsync(samples, sampleRate, ct).ConfigureAwait(false);
        var ms = clock.Elapsed.TotalMilliseconds;
        return new AiSpeakerResult(Registry.Identify(embedding), null, seconds, seconds >= SpeakerEmbeddingPipeline.MinReliableSeconds, ms);
    }

    /// <summary>
    /// Listens for a voice and files it: an enrolled person is named; anyone else becomes (or joins) a provisional guest,
    /// unless the clip is shorter than <see cref="MinGuestSeconds"/> - then it is only identified.
    /// </summary>
    public async Task<AiSpeakerResult> ObserveAsync(float[] samples, int sampleRate, CancellationToken ct = default)
    {
        var seconds = samples.Length / (double)sampleRate;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var embedding = await EmbedAsync(samples, sampleRate, ct).ConfigureAwait(false);
        var ms = clock.Elapsed.TotalMilliseconds;
        var reliable = seconds >= SpeakerEmbeddingPipeline.MinReliableSeconds;
        if (seconds < MinGuestSeconds)
            return new AiSpeakerResult(Registry.Identify(embedding), null, seconds, reliable, ms);
        var match = Registry.Observe(embedding, seconds, out var guest);
        return new AiSpeakerResult(match, guest, seconds, reliable, ms);
    }

    /// <summary>Adds this speech to a person's voice (creating them if new). "Vertex, this is Nikki."</summary>
    public async Task<SpeakerProfile> EnrollAsync(string name, float[] samples, int sampleRate, CancellationToken ct = default)
    {
        var embedding = await EmbedAsync(samples, sampleRate, ct).ConfigureAwait(false);
        return Registry.Enroll(name, embedding, samples.Length / (double)sampleRate);
    }

    private async Task EnsureLoadedAsync(CancellationToken ct)
    {
        if (_pipeline != null) return;
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_pipeline != null) return;
            OnLoadProgress?.Invoke("speaker model", 10);
            var stream = await _source.OpenAsync(ModelRepo, ModelFile, ct).ConfigureAwait(false);
            if (stream.Length <= 0) throw new Exception($"hub returned a zero-length stream for {ModelRepo}/{ModelFile}");
            InferenceSession session;
            await using (stream)
                session = await InferenceSession.CreateFromOnnxStreamAsync(_accelerator, stream, ct: ct).ConfigureAwait(false);
            _pipeline = new SpeakerEmbeddingPipeline(session, _accelerator);
            OnLoadProgress?.Invoke("ready", 100);
            Console.WriteLine($"[AiSpeakerEngine] ReDimNet2 ready on {_accelerator.AcceleratorType}");
        }
        finally
        {
            OnLoadProgress?.Invoke("idle", 100);
            _gate.Release();
        }
    }

    /// <summary>Release the resident speaker model (the registry is kept).</summary>
    public Task EvictAsync()
    {
        _pipeline?.Dispose();
        _pipeline = null;
        return Task.CompletedTask;
    }

    /// <summary>Disposes the resident model. Never disposes the accelerator - the app owns it.</summary>
    public void Dispose()
    {
        _pipeline?.Dispose();
        _pipeline = null;
        _gate.Dispose();
        _inferGate.Dispose();
    }
}
