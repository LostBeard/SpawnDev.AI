using ILGPU.Runtime;
using SpawnDev.ILGPU.ML;
using SpawnDev.ILGPU.ML.Hub;
using SpawnDev.ILGPU.ML.Pipelines;
using SpawnDev.SpawnJS;

namespace SpawnDev.AI.Server;

/// <summary>The result of transcribing one utterance.</summary>
/// <param name="Text">The transcript.</param>
/// <param name="Model">Which speech model produced it.</param>
/// <param name="InferenceMs">Wall time for encode + decode, excluding model load.</param>
public sealed record AiTranscription(string Text, string Model, double InferenceMs);

/// <summary>
/// Speech-to-text for the AI server: Whisper on the same accelerator the chat and image engines use.
/// </summary>
/// <remarks>
/// Follows <see cref="AiImageEngine"/>'s shape deliberately - one resident model, a load gate, and an
/// <see cref="EvictOtherKind"/> hook - because the GPU is shared and per-kind residency is a hard rule in
/// this repo.
/// <para>
/// ⚠️ Two things this does differently from the working reference implementation in
/// <c>SpawnDev.ILGPU.ML.Demo/Pages/WhisperPage.razor</c>, and both matter:
/// </para>
/// <list type="number">
/// <item><description>It loads by STREAM, not <c>hub.LoadAsync</c>. That reference pulls each ONNX file
/// into a <c>byte[]</c> - in the browser, onto the single-threaded WASM heap. Streaming is what
/// SpawnDev.ILGPU.ML 5.2.0 added <c>OpenStreamAsync</c> for, and it keeps the bytes JS-side.</description></item>
/// <item><description>It passes the <c>decoder_with_past</c> session. Without it every decode step re-feeds
/// the WHOLE token sequence and recomputes every previous position's K/V - quadratic, and it materialises
/// full-sequence logits (~13 MB) per step. MEASURED by the ML repo at ~3.5x in the browser
/// (84.3s -> 22.9s).</description></item>
/// </list>
/// </remarks>
public sealed class AiSpeechEngine : IDisposable
{
    private readonly HttpClient _http;
    private readonly Accelerator _accelerator;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private SpeechRecognitionPipeline? _pipeline;
    private InferenceSession? _encoder;
    private InferenceSession? _decoder;
    private InferenceSession? _decoderWithPast;
    private string? _residentModel;

    /// <summary>New instance.</summary>
    /// <param name="http">Used for the desktop load path, where the browser OPFS hub is unavailable.</param>
    /// <param name="accelerator">The shared accelerator.</param>
    public AiSpeechEngine(HttpClient http, Accelerator accelerator)
    {
        _http = http;
        _accelerator = accelerator;
    }

    /// <summary>HuggingFace repo of the speech model. Whisper tiny is the interactive-speed default.</summary>
    public string ModelRepo { get; set; } = ModelHub.KnownModels.WhisperTiny;

    /// <summary>Friendly name reported back to callers.</summary>
    public string ModelName { get; set; } = "whisper-tiny";

    /// <summary>Reports load progress as (stage, percent).</summary>
    public Action<string, int>? OnLoadProgress { get; set; }

    /// <summary>
    /// Called before this engine takes GPU memory, so the host can evict the resident model of another
    /// kind. Per-kind residency is a hard rule here - chat, image and speech must not silently coexist.
    /// </summary>
    public Func<Task>? EvictOtherKind { get; set; }

    /// <summary>Whether a speech model is currently resident.</summary>
    public bool IsLoaded => _pipeline != null;

    /// <summary>Whether the loaded pipeline uses the O(n) with-past decode path.</summary>
    public bool UsesKVCache => _pipeline?.UsesKVCache ?? false;

    /// <summary>
    /// Transcribe PCM samples. Loads the model on first use.
    /// </summary>
    /// <param name="samples">Mono PCM in [-1, 1].</param>
    /// <param name="sampleRate">Sample rate of <paramref name="samples"/>; resampled to 16 kHz internally.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The transcript.</returns>
    public async Task<AiTranscription> TranscribeAsync(float[] samples, int sampleRate,
        CancellationToken ct = default)
    {
        if (samples == null || samples.Length == 0)
            throw new ArgumentException("no audio samples supplied", nameof(samples));
        if (sampleRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampleRate), sampleRate, "sample rate must be positive");

        await EnsureLoadedAsync(ct).ConfigureAwait(false);

        var started = DateTime.UtcNow;
        var result = await _pipeline!.TranscribeAsync(samples, sampleRate).ConfigureAwait(false);
        return new AiTranscription(result.Text ?? "", ModelName, (DateTime.UtcNow - started).TotalMilliseconds);
    }

    private async Task EnsureLoadedAsync(CancellationToken ct)
    {
        if (_pipeline != null && _residentModel == ModelRepo) return;
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_pipeline != null && _residentModel == ModelRepo) return;
            if (EvictOtherKind != null) await EvictOtherKind().ConfigureAwait(false);
            DisposeSessions();

            OnLoadProgress?.Invoke("encoder", 10);
            _encoder = await LoadSessionAsync("onnx/encoder_model.onnx", ct).ConfigureAwait(false);

            OnLoadProgress?.Invoke("decoder", 45);
            _decoder = await LoadSessionAsync("onnx/decoder_model.onnx", ct).ConfigureAwait(false);

            // Optional: absence costs speed, not correctness, so a repo without it still works.
            OnLoadProgress?.Invoke("decoder-with-past", 70);
            try
            {
                _decoderWithPast = await LoadSessionAsync("onnx/decoder_with_past_model.onnx", ct)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AiSpeechEngine] no with-past decoder ({ex.GetType().Name}); decode will be "
                                + "quadratic. This costs speed, not correctness.");
                _decoderWithPast = null;
            }

            OnLoadProgress?.Invoke("tokenizer", 90);
            var tokenizerJson = await LoadTextAsync("tokenizer.json", ct).ConfigureAwait(false);

            var pipeline = new SpeechRecognitionPipeline(_encoder!, _decoder!, _accelerator, _decoderWithPast);
            pipeline.LoadTokenizer(tokenizerJson);
            _pipeline = pipeline;
            _residentModel = ModelRepo;

            OnLoadProgress?.Invoke("ready", 100);
            Console.WriteLine($"[AiSpeechEngine] {ModelName} ready on {_accelerator.AcceleratorType} "
                            + $"(kvcache: {(pipeline.UsesKVCache ? "on" : "off")})");
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Open a repo file as a SEEKABLE stream and build a session from it.
    /// </summary>
    /// <remarks>
    /// Browser: the OPFS-cached <c>BlobStream</c>, which is an <c>IJSReadStream</c>, so the model never
    /// enters the .NET heap. Elsewhere (and if OPFS is unavailable): an <see cref="HttpRangeStream"/>,
    /// whose peak memory is one chunk rather than the whole file.
    /// </remarks>
    private async Task<InferenceSession> LoadSessionAsync(string filename, CancellationToken ct)
    {
        var stream = await OpenModelStreamAsync(filename, ct).ConfigureAwait(false);
        await using (stream)
            return await InferenceSession.CreateFromOnnxStreamAsync(_accelerator, stream, ct: ct)
                .ConfigureAwait(false);
    }

    private async Task<Stream> OpenModelStreamAsync(string filename, CancellationToken ct)
    {
        var js = SpawnJSRuntime.Instance;
        if (js is { IsBrowser: true })
        {
            using var hub = new ModelHub(js);
            var blob = await hub.OpenStreamAsync(ModelRepo, filename).ConfigureAwait(false);
            if (blob != null) return blob;
            // null means OPFS is unavailable in this context - fall through rather than fail.
        }

        var url = $"https://huggingface.co/{ModelRepo}/resolve/main/{filename}";
        using var head = new HttpRequestMessage(HttpMethod.Head, url);
        using var res = await _http.SendAsync(head, ct).ConfigureAwait(false);
        res.EnsureSuccessStatusCode();
        var length = res.Content.Headers.ContentLength
                     ?? throw new Exception($"HEAD {url} returned no Content-Length; cannot range-read it");
        return new HttpRangeStream(_http, url, length);
    }

    private async Task<string> LoadTextAsync(string filename, CancellationToken ct)
    {
        var js = SpawnJSRuntime.Instance;
        if (js is { IsBrowser: true })
        {
            using var hub = new ModelHub(js);
            var bytes = await hub.LoadAsync(ModelRepo, filename).ConfigureAwait(false);
            // A tokenizer is a small JSON file, so the byte[] path is the right one here - the
            // "bulk bytes stay in JS" rule is about model weights, not a few hundred KB of metadata.
            if (bytes != null) return System.Text.Encoding.UTF8.GetString(bytes);
        }
        return await _http.GetStringAsync(
            $"https://huggingface.co/{ModelRepo}/resolve/main/{filename}", ct).ConfigureAwait(false);
    }

    /// <summary>Release the resident speech model.</summary>
    public Task EvictAsync()
    {
        DisposeSessions();
        return Task.CompletedTask;
    }

    private void DisposeSessions()
    {
        _pipeline?.Dispose();
        _pipeline = null;
        _encoder?.Dispose(); _encoder = null;
        _decoder?.Dispose(); _decoder = null;
        _decoderWithPast?.Dispose(); _decoderWithPast = null;
        _residentModel = null;
    }

    /// <summary>Disposes the resident sessions. Never disposes the accelerator - the app owns it.</summary>
    public void Dispose()
    {
        DisposeSessions();
        _gate.Dispose();
    }
}
