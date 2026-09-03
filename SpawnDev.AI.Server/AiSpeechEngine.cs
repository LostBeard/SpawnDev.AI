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
public sealed record AiTranscription(string Text, string Model, double InferenceMs)
{
    /// <summary>Where the time went, or null if the executor did not report.</summary>
    /// <remarks>
    /// ⚠️ Carried back to the CALLER on purpose. The engine runs in a shared worker, whose console is not
    /// the page console - so a `Console.WriteLine` split here is invisible to the window, to a Playwright
    /// gate, and to anyone with DevTools open on the page. A number nobody can read does not count as
    /// instrumentation.
    /// </remarks>
    public AiInferenceSplit? Split { get; init; }
}

/// <summary>Executor-internal attribution for one logical inference (which may be many graph runs).</summary>
/// <param name="GraphRuns">How many <c>RunAsync</c> calls the operation took.</param>
/// <param name="ExecutorMs">Summed executor-internal wall time.</param>
/// <param name="ReadbackCount">Mid-graph GPU-to-host readbacks (each a round trip).</param>
/// <param name="ReadbackMs">Wall time in those readbacks.</param>
/// <param name="DrainCount">Command-buffer sync drains.</param>
/// <param name="DrainMs">Wall time in those drains.</param>
/// <param name="OutsideExecutorMs">Total minus executor time: mel, tokenizer, host glue.</param>
/// <param name="MelMs">
/// CPU log-mel STFT time. ⚠️ Broken out of <paramref name="OutsideExecutorMs"/> because it is a FIXED
/// per-call cost: the audio is padded to a flat 30 s before the STFT runs, so a four-word turn pays exactly
/// what a full half-minute does. That is precisely why endpointing shortened the recording without
/// shortening the transcription.
/// </param>
public sealed record AiInferenceSplit(int GraphRuns, double ExecutorMs, int ReadbackCount, double ReadbackMs,
    int DrainCount, double DrainMs, double OutsideExecutorMs, double MelMs)
{
    /// <summary>Executor time that is neither readback nor drain: dispatch, CPU work, allocation.</summary>
    public double ResidualMs => ExecutorMs - ReadbackMs - DrainMs;

    /// <summary>WHY the encoder's dispatch-plan capture is or is not live.</summary>
    /// <remarks>
    /// ⚠️ The residual above is exactly what a recorded plan removes, so a reading of it is uninterpretable
    /// without knowing whether a plan was replaying. MEASURED 2026-09-03: transcription did not move after
    /// the encoder capture was wired in, and with no status there was no way to tell "capture engaged and
    /// did not help" from "capture never engaged" - which call for opposite work.
    /// </remarks>
    public string EncoderCaptureStatus { get; init; } = "";
}

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

    /// <summary>Serialises INFERENCE on the pipeline, which <see cref="_gate"/> (a LOAD gate) does not.</summary>
    /// <remarks>
    /// ⚠️ Same exposure as <c>AiVoiceEngine._inferGate</c>, and it arrived the same way: warming this model
    /// in the background while the conversation runs lets the warm forward pass and a real transcription
    /// execute CONCURRENTLY on one pipeline. <c>_gate</c> is released as soon as the model is resident, so
    /// it has never covered inference - once loaded, <see cref="EnsureLoadedAsync"/> returns without
    /// acquiring anything. A <c>SpeechRecognitionPipeline</c> owns device buffers and a KV cache; two
    /// overlapping calls are unsound, not just slow.
    /// </remarks>
    private readonly SemaphoreSlim _inferGate = new(1, 1);

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

        // ⚠️ CUMULATIVE, not LastRun*. A transcription is ONE encoder pass plus N decoder steps, and every
        // LastRun* field is overwritten by the next RunAsync - so reading them afterwards reports the final
        // decode step and makes a 13-second transcription look like a 40 ms one. That is a measurement that
        // invites the wrong conclusion, which is worse than having none.
        // One inference at a time - see _inferGate. The cumulative counters are static and per-process, so
        // this gate is ALSO what makes them meaningful: two overlapping runs would interleave their
        // readback and drain totals into one meaningless sum.
        await _inferGate.WaitAsync(ct).ConfigureAwait(false);
        double inferenceMs;
        SpawnDev.ILGPU.ML.Preprocessing.TranscriptionResult result;
        try
        {
            SpawnDev.ILGPU.ML.Graph.GraphExecutor.CumulativeReset();
            var started = DateTime.UtcNow;
            result = await _pipeline!.TranscribeAsync(samples, sampleRate).ConfigureAwait(false);
            inferenceMs = (DateTime.UtcNow - started).TotalMilliseconds;
        }
        finally { _inferGate.Release(); }

        // ── Where did the time go? ──
        // Whisper pads its input to a FIXED 30 s (AudioPipelines: PadOrTrim to WhisperSampleRate * 30), so
        // the encoder cost does not shrink when the utterance does - endpointing cannot make this number
        // smaller and only this split says what would. MEASURED in the browser demo: 46.9 s for a 30 s
        // buffer, 38.8 s for a 3.7 s one, then 13.6 s once a warm forward pass had compiled the kernels.
        // The residual (total minus readback minus drain) is dispatch + CPU + allocation.
        AiInferenceSplit? split = null;
        try
        {
            var runs = SpawnDev.ILGPU.ML.Graph.GraphExecutor.CumulativeRunCount;
            var execMs = SpawnDev.ILGPU.ML.Graph.GraphExecutor.CumulativeTotalMs;
            var rbMs = SpawnDev.ILGPU.ML.Graph.GraphExecutor.CumulativeReadbackMs;
            var rbN = SpawnDev.ILGPU.ML.Graph.GraphExecutor.CumulativeReadbackCount;
            var drainMs = SpawnDev.ILGPU.ML.Graph.GraphExecutor.CumulativeSyncDrainMs;
            var drainN = SpawnDev.ILGPU.ML.Graph.GraphExecutor.CumulativeSyncDrainCount;
            split = new AiInferenceSplit(runs, execMs, rbN, rbMs, drainN, drainMs, inferenceMs - execMs, result.MelTimeMs)
                { EncoderCaptureStatus = result.EncoderCaptureStatus };

            var seconds = samples.Length / (double)sampleRate;
            Console.WriteLine($"[AiSpeechEngine] {seconds:F2}s of audio in {inferenceMs:F0}ms "
                + $"({(seconds > 0 ? inferenceMs / 1000.0 / seconds : 0):F1}x realtime) | "
                + $"{runs} graph runs, executor {execMs:F0}ms | "
                + $"readbacks {rbN} ({rbMs:F0}ms) | drains {drainN} ({drainMs:F0}ms) | "
                + $"residual {split.ResidualMs:F0}ms (dispatch+CPU+alloc) | "
                + $"outside the executor {split.OutsideExecutorMs:F0}ms, of which CPU mel STFT "
                + $"{split.MelMs:F0}ms (FIXED - the audio is padded to 30s before the STFT, so this costs "
                + "the same for four words as for half a minute)");
        }
        catch { /* a diagnostic must never fail a request */ }

        return new AiTranscription(result.Text ?? "", ModelName, inferenceMs) { Split = split };
    }

    /// <summary>
    /// Load the model AND run one forward pass, so the first real request does not pay for either.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ Loading the bytes is not being ready. Every kernel in this graph is compiled on its FIRST
    /// execution, not at load, so a "warm" that only downloads weights leaves the whole compile inside the
    /// first transcription - where the user is sitting waiting for it. MEASURED in the demo: 38.8 s to
    /// transcribe a 3.7 s utterance, against 46.9 s for a 30 s one. Whisper pads its input to a fixed 30 s
    /// (AudioPipelines: PadOrTrim to WhisperSampleRate * 30), so shortening the recording could not have
    /// accounted for that gap and the per-utterance cost is very nearly constant - which is exactly the
    /// shape of a one-off compile plus a fixed-size graph.
    /// </para>
    /// <para>
    /// One second of silence is enough: the encoder always sees the same padded 30 s tensor, so every
    /// encoder kernel compiles regardless, and Whisper answers silence in a handful of decode steps.
    /// </para>
    /// <para>
    /// ⚠️ The warm pass is TIMED and logged. If the first real transcription is still slow afterwards then
    /// the cost is the graph itself rather than compilation, and that is a different problem needing
    /// per-node attribution - not more warming. Printing the number is what makes those two separable
    /// instead of a guess.
    /// </para>
    /// </remarks>
    public async Task EnsureReadyAsync(CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct).ConfigureAwait(false);
        if (_warmed) return;
        _warmed = true;

        var clock = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            // Under _inferGate, so a real transcription arriving mid-warm WAITS instead of racing it.
            await _inferGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await _pipeline!.TranscribeAsync(new float[16000], 16000).ConfigureAwait(false);
            }
            finally { _inferGate.Release(); }
            Console.WriteLine($"[AiSpeechEngine] warm forward pass: {clock.Elapsed.TotalSeconds:F1}s "
                            + "(kernel compilation; a real transcription after this should be much faster - "
                            + "if it is not, the cost is the graph, not the compile)");
        }
        catch (Exception ex)
        {
            // Never fatal: warming is an optimisation and the lazy path still works.
            Console.WriteLine($"[AiSpeechEngine] warm pass failed ({ex.GetType().Name}: {ex.Message}); "
                            + "the first real transcription will pay for compilation instead.");
        }
    }

    private bool _warmed;

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
