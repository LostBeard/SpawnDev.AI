using ILGPU.Runtime;
using SpawnDev.ILGPU.ML;
using SpawnDev.ILGPU.ML.Hub;
using SpawnDev.ILGPU.ML.Pipelines;

namespace SpawnDev.AI.Server;

/// <summary>
/// Speech to text that keeps up with the speaker: audio goes in as it is spoken, words come out within a fraction of a second - the way voice typing on a phone
/// shows what you say while you are still saying it. NVIDIA Nemotron Speech Streaming 0.6B (cache-aware FastConformer + RNN-T) on the shared accelerator.
/// </summary>
/// <remarks>
/// <para>
/// The streaming pass is the FAST pass. It lets an application react EARLY (hear its name, see a sentence stop mid-thought, begin an answer while the last words are
/// still being said); for the final text of a finished utterance use <see cref="AiSpeechEngine"/> (Parakeet TDT, 3.75% WER against this model's 5.63% on the same clips).
/// </para>
/// <para>
/// One stream at a time: the model keeps the speaker's context in device buffers. <see cref="BeginAsync"/> waits for the previous stream to be disposed. Weights are held
/// in half precision by default: the same transcripts (5.63% WER either way, 60 LibriSpeech clips) in half the memory (~1.2 GB instead of 2.4 GB).
/// </para>
/// </remarks>
public sealed class AiStreamingSpeechEngine : IDisposable
{
    private readonly IModelSource _source;
    private readonly Accelerator _accelerator;
    private readonly SemaphoreSlim _loadGate = new(1, 1);
    private readonly SemaphoreSlim _streamGate = new(1, 1);
    private StreamingTranscriber? _transcriber;

    /// <summary>HuggingFace repo of the sherpa-onnx export of Nemotron Speech Streaming (160 ms chunks).</summary>
    public string ModelRepo { get; set; } = "csukuangfj2/sherpa-onnx-nemotron-speech-streaming-en-0.6b-160ms-2026-04-25";

    /// <summary>Hold the encoder's weights in half precision (same transcripts, half the memory, ~5% slower per chunk).</summary>
    public bool HalfWeights { get; set; } = true;

    /// <summary>Reports load progress as (stage, percent).</summary>
    public Action<string, int>? OnLoadProgress { get; set; }

    /// <summary>Whether the model is resident.</summary>
    public bool IsLoaded => _transcriber != null;

    public AiStreamingSpeechEngine(IModelSource source, Accelerator accelerator)
    {
        _source = source;
        _accelerator = accelerator;
    }

    /// <summary>Loads the model and runs a second of silence through it, so the first real utterance does not pay for kernel compilation.</summary>
    public async Task EnsureReadyAsync(CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct).ConfigureAwait(false);
        await using var stream = await BeginAsync(ct).ConfigureAwait(false);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        await stream.AcceptAsync(new float[16000]).ConfigureAwait(false);
        Console.WriteLine($"[AiStreamingSpeechEngine] warm second of audio: {clock.Elapsed.TotalSeconds:F1}s (kernel compilation)");
    }

    /// <summary>
    /// Starts a new utterance. The previous stream must have been disposed (this waits for it). Dispose the returned stream when the utterance is over.
    /// </summary>
    public async Task<AiSpeechStream> BeginAsync(CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct).ConfigureAwait(false);
        await _streamGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _transcriber!.Reset();
            return new AiSpeechStream(_transcriber, _streamGate);
        }
        catch { _streamGate.Release(); throw; }
    }

    private async Task EnsureLoadedAsync(CancellationToken ct)
    {
        if (_transcriber != null) return;
        await _loadGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_transcriber != null) return;
            var (encShapes, decShapes, joiShapes) = StreamingTranscriber.InputShapes();
            async Task<InferenceSession> Open(string stage, int percent, string file, Dictionary<string, int[]> shapes, string? dataFile, WeightStorage storage)
            {
                OnLoadProgress?.Invoke(stage, percent);
                await using var graph = await _source.OpenAsync(ModelRepo, file, ct).ConfigureAwait(false);
                if (dataFile == null)
                    return await InferenceSession.CreateFromOnnxStreamAsync(_accelerator, graph, inputShapes: shapes, ct: ct).ConfigureAwait(false);
                await using var data = await _source.OpenAsync(ModelRepo, dataFile, ct).ConfigureAwait(false);
                return await InferenceSession.CreateFromOnnxStreamAsync(_accelerator, graph, inputShapes: shapes, externalDataStream: data,
                    weightStorage: storage, ct: ct).ConfigureAwait(false);
            }
            var encoder = await Open("streaming encoder", 10, "encoder.onnx", encShapes, "encoder.data", HalfWeights ? WeightStorage.Half : WeightStorage.Source).ConfigureAwait(false);
            var decoder = await Open("streaming decoder", 85, "decoder.onnx", decShapes, null, WeightStorage.Source).ConfigureAwait(false);
            var joiner = await Open("streaming joiner", 90, "joiner.onnx", joiShapes, null, WeightStorage.Source).ConfigureAwait(false);
            string tokens;
            await using (var t = await _source.OpenAsync(ModelRepo, "tokens.txt", ct).ConfigureAwait(false))
            using (var reader = new StreamReader(t))
                tokens = await reader.ReadToEndAsync(ct).ConfigureAwait(false);
            _transcriber = new StreamingTranscriber(encoder, decoder, joiner, _accelerator, ParakeetTdtPipeline.ParseVocabulary(tokens));
            OnLoadProgress?.Invoke("ready", 100);
            Console.WriteLine($"[AiStreamingSpeechEngine] Nemotron streaming ready on {_accelerator.AcceleratorType} ({(HalfWeights ? "half" : "fp32")} weights)");
        }
        finally
        {
            OnLoadProgress?.Invoke("idle", 100);
            _loadGate.Release();
        }
    }

    /// <summary>Release the resident model.</summary>
    public async Task EvictAsync()
    {
        await _streamGate.WaitAsync().ConfigureAwait(false);   // not while a stream is running
        try { _transcriber?.Dispose(); _transcriber = null; }
        finally { _streamGate.Release(); }
    }

    /// <summary>Disposes the resident model. Never disposes the accelerator - the app owns it.</summary>
    public void Dispose()
    {
        _transcriber?.Dispose();
        _transcriber = null;
        _loadGate.Dispose();
        _streamGate.Dispose();
    }
}

/// <summary>One utterance being recognised as it is spoken. Disposing it lets the next utterance begin.</summary>
public sealed class AiSpeechStream : IAsyncDisposable, IDisposable
{
    private readonly StreamingTranscriber _transcriber;
    private readonly SemaphoreSlim _gate;
    private bool _disposed;

    internal AiSpeechStream(StreamingTranscriber transcriber, SemaphoreSlim gate) { _transcriber = transcriber; _gate = gate; }

    /// <summary>Everything recognised so far in this utterance.</summary>
    public string Text => _transcriber.Text;

    /// <summary>Feeds the next 16 kHz mono audio in [-1, 1] (any amount, microphone-sized pieces are fine) and returns what was recognised from it.</summary>
    public Task<StreamingUpdate> AcceptAsync(float[] pcm16k)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _transcriber.AcceptAudioAsync(pcm16k);
    }

    /// <summary>Ends the utterance (a second of silence pushes the last words through) and returns the final streaming text.</summary>
    public Task<StreamingUpdate> FinishAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _transcriber.FinishAsync();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _gate.Release();
    }

    public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
}
