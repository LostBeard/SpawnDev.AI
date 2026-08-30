using ILGPU.Runtime;
using SpawnDev.ILGPU.ML;
using SpawnDev.ILGPU.ML.Hub;
using SpawnDev.ILGPU.ML.Pipelines;
using SpawnDev.WebTorrent;

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
/// <item><description>It loads through a LAZY-HASH torrent (<see cref="OpenModelStreamAsync"/>), not
/// <c>hub.LoadAsync</c>. That reference pulls each ONNX file into a <c>byte[]</c> - in the browser, onto the
/// single-threaded WASM heap - and re-fetches it on every reload. Lazy-hash exists so anything reachable by
/// URL is streamable WITH RANDOM ACCESS, cached to OPFS, restored on reload with zero re-download, and
/// seeded to peers.</description></item>
/// <item><description>It passes the <c>decoder_with_past</c> session. Without it every decode step re-feeds
/// the WHOLE token sequence and recomputes every previous position's K/V - quadratic, and it materialises
/// full-sequence logits (~13 MB) per step. MEASURED by the ML repo at ~3.5x in the browser
/// (84.3s -> 22.9s).</description></item>
/// </list>
/// </remarks>
public sealed class AiSpeechEngine : IDisposable
{
    private readonly WebTorrentClient _webTorrent;
    private readonly HttpClient _http;
    private readonly Accelerator _accelerator;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private SpeechRecognitionPipeline? _pipeline;
    private InferenceSession? _encoder;
    private InferenceSession? _decoder;
    private InferenceSession? _decoderWithPast;
    private string? _residentModel;

    /// <summary>New instance.</summary>
    /// <param name="webTorrent">Delivers models as LAZY-HASH torrents through the hub - see
    /// <see cref="OpenModelStreamAsync"/> for why that and not a plain range stream.</param>
    /// <param name="http">Used by <c>HubModelStream</c> for its size probe and web-seed fetches.</param>
    /// <param name="accelerator">The shared accelerator.</param>
    public AiSpeechEngine(WebTorrentClient webTorrent, HttpClient http, Accelerator accelerator)
    {
        _webTorrent = webTorrent;
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
    private async Task<InferenceSession> LoadSessionAsync(string filename, CancellationToken ct)
    {
        var stream = await OpenModelStreamAsync(filename, ct).ConfigureAwait(false);
        await using (stream)
            return await InferenceSession.CreateFromOnnxStreamAsync(_accelerator, stream, ct: ct)
                .ConfigureAwait(false);
    }

    /// <summary>
    /// Open a repo file as a seekable stream via the hub, as a LAZY-HASH torrent.
    /// </summary>
    /// <remarks>
    /// ⚠️ This deliberately goes through <see cref="HubModelStream.OpenAsync"/> rather than
    /// <c>ModelHub.OpenStreamAsync</c> or a bare <see cref="HttpRangeStream"/>. Lazy-hash exists precisely so
    /// that anything reachable by URL is STREAMABLE WITH RANDOM ACCESS: the model becomes a persistent
    /// torrent from the first byte, downloads on demand from the hub web seed, computes its infohash as it
    /// goes, caches pieces to OPFS under a stable URL-derived key, RESTORES on reload with zero re-download,
    /// and seeds to peers. `HubModelStream.OpenAsync`'s own remarks record that it "replaces the old
    /// non-persistent HttpRangeStream fallback, which made NO torrent ... so every page refresh
    /// re-downloaded the whole file".
    /// <para>
    /// The first cut of this engine used exactly that superseded path. It worked, and it re-downloaded
    /// Whisper on every reload - which is the whole thing lazy-hash was built to stop.
    /// </para>
    /// <para>
    /// A seekable stream is not a nicety here either: <c>CreateFromOnnxStreamAsync</c> SEEKS to each weight,
    /// so random access is a hard requirement of the loader, not just an optimisation.
    /// </para>
    /// </remarks>
    /// <param name="filename">Path within the repo, e.g. <c>onnx/encoder_model.onnx</c>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A seekable stream over the model file.</returns>
    private async Task<Stream> OpenModelStreamAsync(string filename, CancellationToken ct)
    {
        var hub = new HubModelStream(_webTorrent, _http);
        // deselect:false - we need the weights, not just the structure.
        var model = await hub.OpenAsync(ModelRepo, filename, deselect: false, ct).ConfigureAwait(false);
        if (model.Length <= 0)
            throw new Exception($"hub returned a zero-length stream for {ModelRepo}/{filename}");
        return model.Stream;
    }

    /// <summary>
    /// Fetch a small text file (the tokenizer) from the same hub path.
    /// </summary>
    /// <remarks>
    /// Read fully into a string on purpose: a tokenizer is a few hundred KB of JSON, and the
    /// "bulk bytes stay out of the managed heap" rule is about model WEIGHTS. Going through the hub keeps
    /// one delivery path rather than two.
    /// </remarks>
    private async Task<string> LoadTextAsync(string filename, CancellationToken ct)
    {
        var stream = await OpenModelStreamAsync(filename, ct).ConfigureAwait(false);
        await using (stream)
        using (var reader = new StreamReader(stream))
            return await reader.ReadToEndAsync(ct).ConfigureAwait(false);
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
