using ILGPU.Runtime;
using SpawnDev.ILGPU.ML;
using SpawnDev.ILGPU.ML.Hub;
using SpawnDev.ILGPU.ML.Pipelines;
using Float32Array = SpawnDev.SpawnJS.JSObjects.Float32Array;

namespace SpawnDev.AI.Server;

/// <summary>
/// End-of-turn detection for the AI server: Smart Turn v3.2 (pipecat-ai, BSD-2) on the shared accelerator. Given the audio of what
/// someone has just said, how likely is it that they are finished? A voice agent uses this to answer sooner when a sentence is complete
/// and to keep listening when it is not, instead of waiting a fixed stretch of silence for everyone.
/// </summary>
/// <remarks>
/// Follows <see cref="AiSpeakerEngine"/>'s shape on purpose: one resident model, a load gate, an inference gate, delivery through
/// <see cref="IModelSource"/>. The model judges the audio alone (pitch, pauses, trailing-off), not the words, and looks at the last 8 s.
/// </remarks>
public sealed class AiTurnEngine : IDisposable
{
    private readonly IModelSource _source;
    private readonly Accelerator _accelerator;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly SemaphoreSlim _inferGate = new(1, 1);
    private SmartTurnPipeline? _pipeline;

    /// <summary>HuggingFace repo of the end-of-turn model (BSD-2).</summary>
    public string ModelRepo { get; set; } = "pipecat-ai/smart-turn-v3";

    /// <summary>File in <see cref="ModelRepo"/> (the GPU export, fp32).</summary>
    public string ModelFile { get; set; } = "smart-turn-v3.2-gpu.onnx";

    /// <summary>Reports load progress as (stage, percent).</summary>
    public Action<string, int>? OnLoadProgress { get; set; }

    /// <summary>Whether the model is resident.</summary>
    public bool IsLoaded => _pipeline != null;

    public AiTurnEngine(IModelSource source, Accelerator accelerator)
    {
        _source = source;
        _accelerator = accelerator;
    }

    /// <summary>Loads the model and runs it once, so the first real turn does not pay for kernel compilation.</summary>
    public async Task EnsureReadyAsync(CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct).ConfigureAwait(false);
        await _inferGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            await _pipeline!.PredictAsync(new float[16000]).ConfigureAwait(false);
            Console.WriteLine($"[AiTurnEngine] warm prediction: {clock.Elapsed.TotalSeconds:F1}s (kernel compilation)");
        }
        finally { _inferGate.Release(); }
    }

    /// <summary>P(the speaker has finished their turn) for mono PCM in [-1, 1]; the END of the audio is "now". Any rate is resampled to 16 kHz.</summary>
    public async Task<float> TurnCompleteAsync(float[] samples, int sampleRate = 16000, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(samples);
        return await RunAsync(() => _pipeline!.PredictAsync(samples, sampleRate), ct).ConfigureAwait(false);
    }

    /// <summary>Browser path: the PCM is a JS typed array, uploaded to the device without crossing the managed heap.</summary>
    public async Task<float> TurnCompleteAsync(Float32Array samples, int sampleRate = 16000, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(samples);
        return await RunAsync(() => _pipeline!.PredictAsync(samples, sampleRate), ct).ConfigureAwait(false);
    }

    private async Task<float> RunAsync(Func<Task<float>> predict, CancellationToken ct)
    {
        await EnsureLoadedAsync(ct).ConfigureAwait(false);
        await _inferGate.WaitAsync(ct).ConfigureAwait(false);   // one prediction at a time: the pipeline owns device buffers
        try { return await predict().ConfigureAwait(false); }
        finally { _inferGate.Release(); }
    }

    private async Task EnsureLoadedAsync(CancellationToken ct)
    {
        if (_pipeline != null) return;
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_pipeline != null) return;
            OnLoadProgress?.Invoke("turn model", 10);
            var stream = await _source.OpenAsync(ModelRepo, ModelFile, ct).ConfigureAwait(false);
            if (stream.Length <= 0) throw new Exception($"hub returned a zero-length stream for {ModelRepo}/{ModelFile}");
            InferenceSession session;
            await using (stream)
                session = await InferenceSession.CreateFromOnnxStreamAsync(_accelerator, stream,
                    inputShapes: SmartTurnPipeline.InputShapes.ToDictionary(kv => kv.Key, kv => kv.Value), ct: ct).ConfigureAwait(false);
            _pipeline = new SmartTurnPipeline(session, _accelerator);
            OnLoadProgress?.Invoke("ready", 100);
            Console.WriteLine($"[AiTurnEngine] Smart Turn ready on {_accelerator.AcceleratorType}");
        }
        finally
        {
            OnLoadProgress?.Invoke("idle", 100);
            _gate.Release();
        }
    }

    /// <summary>Release the resident model.</summary>
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
