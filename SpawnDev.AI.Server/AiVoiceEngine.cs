using System.Linq;
using ILGPU.Runtime;
using SpawnDev.ILGPU.ML;
using SpawnDev.ILGPU.ML.Hub;
using SpawnDev.ILGPU.ML.Pipelines;
using SpawnDev.WebTorrent;

namespace SpawnDev.AI.Server;

/// <summary>The result of speaking one line.</summary>
/// <param name="Samples">Mono PCM in [-1, 1].</param>
/// <param name="SampleRate">Sample rate of <paramref name="Samples"/>.</param>
/// <param name="Model">Which voice model produced it.</param>
/// <param name="InferenceMs">Wall time for encode + decode + vocode, excluding model load.</param>
public sealed record AiSpeech(float[] Samples, int SampleRate, string Model, double InferenceMs)
{
    /// <summary>Length of the generated audio in seconds.</summary>
    public double DurationSeconds => SampleRate > 0 ? (double)Samples.Length / SampleRate : 0;

    /// <summary>Length of the reference clip as it was handed to the engine, in seconds.</summary>
    public double ReferenceSeconds { get; init; }

    /// <summary>Length of the reference clip after dead air was removed, in seconds.</summary>
    /// <remarks>
    /// ⚠️ The gap between this and <see cref="ReferenceSeconds"/> is the speaking-rate error that WOULD
    /// have been cloned. ZipVoice derives frames-per-token from the reference and stretches every
    /// generated syllable to match, so a reference that is half dead air used to clone as speech at half
    /// speed - which is what made the hands-free demo unintelligible. Surfaced rather than merely fixed
    /// because "the voice sounds slow" needs a number attached to it, not another round of guessing.
    /// </remarks>
    public double ReferenceSpeechSeconds { get; init; }
}

/// <summary>
/// Text-to-speech for the AI server: ZipVoice on the same accelerator the chat and speech engines use.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately the same shape as <see cref="AiSpeechEngine"/> - one resident model, a load gate, and an
/// <see cref="EvictOtherKind"/> hook - because the GPU is shared and per-kind residency is a hard rule in
/// this repo. Models arrive as LAZY-HASH torrents through the hub for the same reasons documented there:
/// random-access streaming, OPFS caching, and no re-download on reload.
/// </para>
/// <para>
/// ⚠️ ZipVoice CLONES a voice - it needs a reference clip and that clip's transcript, and it speaks the
/// reply in that voice. In a conversation loop the natural reference is the turn the user just spoke, which
/// is why <see cref="SpeakAsync"/> takes one. Without a reference it cannot speak at all, so there is no
/// "default voice" fallback to hide behind.
/// </para>
/// <para>
/// ⚠️ The vocoder is NOT on HuggingFace as a standalone file. The repo that looks right
/// (<c>wetdog/vocos-mel-24khz-onnx</c>) holds the mel EXTRACTOR - the inverse direction - and the two files
/// are 431 bytes apart in size, which is how the wrong one passes for the right one. The real vocoder ships
/// only inside a sherpa-onnx release tarball, so it comes through the hub's source proxy, which can serve a
/// single member out of a remote archive. A wrong vocoder does not throw; it renders noise. Hence the
/// explicit size check.
/// </para>
/// </remarks>
public sealed class AiVoiceEngine : IDisposable
{
    private readonly WebTorrentClient _webTorrent;
    private readonly HttpClient _http;
    private readonly Accelerator _accelerator;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Serialises INFERENCE on the pipeline, which <see cref="_gate"/> (a LOAD gate) does not.</summary>
    /// <remarks>
    /// ⚠️ MEASURED 2026-09-01: warming the voice in the background while the conversation ran let a warm
    /// synthesis and a real reply execute CONCURRENTLY on one pipeline - a 4.0 s reply took <b>145.9 s</b>
    /// (36x realtime) against 4.7x for a much longer one, because two syntheses were fighting for the GPU.
    /// <para>
    /// Speed is the visible symptom and the smaller half. A <c>ZipVoicePipeline</c> owns device buffers and
    /// graph-capture state; two overlapping calls are not merely slow, they are unsound. <c>_gate</c> is
    /// released as soon as the model is RESIDENT, so it has never covered this - once loaded,
    /// <see cref="EnsureLoadedAsync"/> returns without acquiring anything at all.
    /// </para>
    /// </remarks>
    private readonly SemaphoreSlim _inferGate = new(1, 1);

    private ZipVoicePipeline? _pipeline;
    private IlgpuZipVoiceGraphs? _graphs;
    private ZipVoiceTokenizer? _tokenizer;
    private string? _residentModel;

    /// <summary>The vocoder's exact size. A different file here renders noise rather than failing.</summary>
    private const int VocoderBytes = 54_157_409;

    /// <summary>New instance.</summary>
    public AiVoiceEngine(WebTorrentClient webTorrent, HttpClient http, Accelerator accelerator)
    {
        _webTorrent = webTorrent;
        _http = http;
        _accelerator = accelerator;
    }

    /// <summary>HuggingFace repo holding ZipVoice's encoder, decoder and token table.</summary>
    public string ModelRepo { get; set; } = "k2-fsa/ZipVoice";

    /// <summary>Friendly name reported back to callers.</summary>
    public string ModelName { get; set; } = "zipvoice-distill-int8";

    /// <summary>Hub base URL, used for the source proxy that reaches inside the vocoder's archive.</summary>
    public string HubBaseUrl { get; set; } = "https://hub.spawndev.com:44365";

    /// <summary>The sherpa-onnx release archive that contains the vocoder.</summary>
    public string VocoderArchiveUrl { get; set; } =
        "https://github.com/k2-fsa/sherpa-onnx/releases/download/tts-models/sherpa-onnx-zipvoice-distill-zh-en-emilia.tar.bz2";

    /// <summary>Path of the vocoder inside that archive.</summary>
    public string VocoderMember { get; set; } =
        "sherpa-onnx-zipvoice-distill-zh-en-emilia/vocos_24khz.onnx";

    /// <summary>Reports load progress as (stage, percent).</summary>
    public Action<string, int>? OnLoadProgress { get; set; }

    /// <summary>
    /// Called before this engine takes GPU memory, so the host can evict the resident model of another kind.
    /// </summary>
    public Func<Task>? EvictOtherKind { get; set; }

    /// <summary>Whether a voice model is currently resident.</summary>
    public bool IsLoaded => _pipeline != null;

    /// <summary>
    /// Speak <paramref name="text"/> in the voice of <paramref name="referenceSamples"/>.
    /// </summary>
    /// <param name="text">What to say.</param>
    /// <param name="referenceText">
    /// The transcript of the reference clip. ⚠️ Must be accurate: anything present in the reference audio
    /// and missing here bleeds into the start of the generated line, so a sloppy transcript degrades the
    /// clone in a way that is invisible in the text and audible in the output.
    /// </param>
    /// <param name="referenceSamples">Mono PCM of the voice to clone.</param>
    /// <param name="referenceSampleRate">Sample rate of the reference.</param>
    /// <param name="maxSpokenCharacters">
    /// Optional per-call override of <see cref="MaxSpokenCharacters"/>. The default cap is a PRODUCT choice
    /// (a spoken reply should be brief), not an engine limit, so a caller that genuinely wants a long
    /// read-out can ask for one. Null or non-positive keeps the default.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<AiSpeech> SpeakAsync(string text, string referenceText, float[] referenceSamples,
        int referenceSampleRate, int? maxSpokenCharacters = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("nothing to say", nameof(text));
        if (referenceSamples == null || referenceSamples.Length == 0)
            throw new ArgumentException(
                "ZipVoice clones a voice, so it needs reference audio - there is no default voice",
                nameof(referenceSamples));
        if (referenceSampleRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(referenceSampleRate), referenceSampleRate,
                "sample rate must be positive");

        await EnsureLoadedAsync(ct).ConfigureAwait(false);

        text = TrimToSpeakableLength(text, maxSpokenCharacters);

        // One synthesis at a time - see _inferGate. A background warm pass counts as one.
        await _inferGate.WaitAsync(ct).ConfigureAwait(false);
        double inferenceMs;
        SpawnDev.ILGPU.ML.Pipelines.ZipVoiceResult result;
        try
        {
            var started = DateTime.UtcNow;
            result = await _pipeline!
                .SpeakAsync(text, referenceText ?? "", referenceSamples, referenceSampleRate, _tokenizer!)
                .ConfigureAwait(false);
            inferenceMs = (DateTime.UtcNow - started).TotalMilliseconds;
        }
        finally { _inferGate.Release(); }

        // ── Where did the time go? ──
        // Browser TTS is far slower than realtime while CUDA is faster than realtime, and the difference is
        // ORCHESTRATION, not arithmetic - the same shape as the Silero VAD win (177.9 -> 7.81 ms/frame, from
        // capture/replay plus driving readbacks to zero). Printing the executor's own split means the next
        // person cuts the dominant term instead of guessing at one; readbacks in particular are a ~345 ms
        // mapAsync round trip each on WebGPU, and LastRunReadbackNames NAMES the node that caused them.
        try
        {
            var seconds = result.Audio.Length / (double)result.SampleRate;
            var readbacks = SpawnDev.ILGPU.ML.Graph.GraphExecutor.LastRunReadbackCount;

            // ⚠️ The REFERENCE line, and the first one to read when a reply sounds slow. ZipVoice derives
            // frames-per-token from the reference clip, so dead air in it clones as slow speech - MEASURED
            // at 1.94x for a reference with 4 s of silence added. `speech` is what survived the trim; if it
            // is far below `ref`, the microphone handed over a span that was mostly not speech, and that is
            // a capture/endpointing problem rather than a voice one.
            if (result.ReferenceSeconds > 0)
                Console.WriteLine($"[AiVoiceEngine] reference {result.ReferenceSeconds:F2}s -> speech "
                    + $"{result.ReferenceSpeechSeconds:F2}s "
                    + $"({result.ReferenceSpeechSeconds / result.ReferenceSeconds * 100:F0}% kept); "
                    + $"spoke {seconds:F2}s for {text.Length} chars "
                    + $"= {text.Length / Math.Max(seconds, 1e-6):F1} chars/s "
                    + "(natural English is 14-16)");

            Console.WriteLine($"[AiVoiceEngine] {seconds:F2}s of audio in {inferenceMs:F0}ms "
                + $"({(seconds > 0 ? inferenceMs / 1000.0 / seconds : 0):F1}x realtime) | "
                + $"readbacks {readbacks} ({SpawnDev.ILGPU.ML.Graph.GraphExecutor.LastRunReadbackMs:F0}ms) "
                + $"syncs {SpawnDev.ILGPU.ML.Graph.GraphExecutor.LastRunSyncDrainCount} "
                + $"({SpawnDev.ILGPU.ML.Graph.GraphExecutor.LastRunSyncDrainMs:F0}ms)"
                + (readbacks > 0
                    ? $" | last readback names: {string.Join(", ", SpawnDev.ILGPU.ML.Graph.GraphExecutor.LastRunReadbackNames.Take(5))}"
                    : ""));
        }
        catch { /* a diagnostic must never fail a request */ }

        return new AiSpeech(result.Audio, result.SampleRate, ModelName, inferenceMs)
        {
            ReferenceSeconds = result.ReferenceSeconds,
            ReferenceSpeechSeconds = result.ReferenceSpeechSeconds,
        };
    }

    /// <summary>
    /// How many characters of a reply are spoken aloud. Default 320.
    /// </summary>
    /// <remarks>
    /// A spoken reply is not a written one. Nobody wants a chat model's full paragraph read at them, and a
    /// voice assistant that monologues is worse than one that is brief - so this cap is a product decision
    /// first, and it would exist even if everything below it were free.
    ///
    /// ✅ The engine limit this ALSO used to hide is FIXED (ILGPU.ML 5.2.7-local.11, 2026-09-01). An
    /// utterance past ZipVoice's precomputed [1999, 48] positional table - about 21 s of speech - takes a
    /// different If branch that recomputes the table, and that branch used to read a buffer nobody had
    /// written: a Slice under the If was resolved at COMPILE time from the branch the compiler could see, so
    /// its window collapsed to empty and the operator was skipped entirely. Fixed and gated - lenscale x3
    /// (1222 frames) and x4 (1504 frames) now match onnxruntime to 3.9E-4 and 2.1E-4.
    ///
    /// So this cap is now PURELY the product decision above. Raise it freely; long utterances synthesise
    /// correctly.
    /// </remarks>
    public int MaxSpokenCharacters { get; set; } = 320;

    /// <summary>Cut a reply at a sentence end near the cap, rather than mid-word.</summary>
    private string TrimToSpeakableLength(string text, int? overrideCap)
    {
        var cap = overrideCap is > 0 ? overrideCap.Value : MaxSpokenCharacters;
        if (text.Length <= cap) return text;

        // Prefer the last sentence end inside the cap - a reply that stops mid-clause sounds broken, while
        // one that stops a sentence early just sounds brief.
        var window = text[..cap];
        var cut = window.LastIndexOfAny(new[] { '.', '!', '?' });
        var spoken = cut > cap / 3 ? window[..(cut + 1)] : window.TrimEnd();
        Console.WriteLine($"[AiVoiceEngine] speaking {spoken.Length} of {text.Length} characters "
                        + $"(cap={cap})");
        return spoken;
    }

    /// <summary>
    /// Load the model AND synthesise one throwaway line, so the first real reply pays for neither.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ Loading is not being ready - the same lesson as <c>AiSpeechEngine</c> and <c>AiVadEngine</c>.
    /// Every kernel in these three graphs (text encoder, flow decoder, vocoder) compiles on its FIRST
    /// EXECUTION, so a warm that only fetches weights leaves the entire compile inside the first spoken
    /// reply - where the user is sitting in silence waiting for it, having already read the text answer.
    /// </para>
    /// <para>
    /// ⚠️ The reference clip here is SYNTHETIC and its output is discarded. That is legitimate for a warm
    /// pass and would not be for anything else: ZipVoice CLONES, so a synthetic reference produces a
    /// meaningless voice. Nothing listens to it - the point is to execute every kernel once. A real
    /// reference is required for every actual call, and <see cref="SpeakAsync"/> still refuses without one.
    /// </para>
    /// <para>
    /// ⚠️ Best-effort, and it must stay that way. Warming is an optimisation; a failure here has to leave
    /// <see cref="SpeakAsync"/> working exactly as before rather than taking the voice down with it.
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
            // A second of quiet, band-limited noise: enough of a signal for the reference encoder to run
            // on, and short enough that the warm pass stays a warm pass.
            const int rate = 16000;
            var reference = new float[rate];
            var rng = new Random(12345);
            for (int i = 0; i < reference.Length; i++) reference[i] = (float)(rng.NextDouble() - 0.5) * 0.05f;

            // Under _inferGate, so a real reply that arrives mid-warm WAITS instead of racing this one.
            await _inferGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await _pipeline!.SpeakAsync("Hello.", "Hello.", reference, rate, _tokenizer!)
                    .ConfigureAwait(false);
            }
            finally { _inferGate.Release(); }
            Console.WriteLine($"[AiVoiceEngine] warm synthesis: {clock.Elapsed.TotalSeconds:F1}s "
                            + "(kernel compilation; a real reply after this should be much faster - if it "
                            + "is not, the cost is the graph, not the compile)");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AiVoiceEngine] warm synthesis failed ({ex.GetType().Name}: {ex.Message}); "
                            + "the first real reply will pay for compilation instead.");
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

            OnLoadProgress?.Invoke("text-encoder", 10);
            var encoder = await LoadSessionAsync("zipvoice_distill/text_encoder_int8.onnx", ct)
                .ConfigureAwait(false);

            OnLoadProgress?.Invoke("flow-decoder", 40);
            var decoder = await LoadSessionAsync("zipvoice_distill/fm_decoder_int8.onnx", ct)
                .ConfigureAwait(false);

            OnLoadProgress?.Invoke("vocoder", 75);
            var vocoder = await LoadVocoderAsync(ct).ConfigureAwait(false);

            OnLoadProgress?.Invoke("tokens", 92);
            var tokens = await LoadTextAsync("zipvoice_distill/tokens.txt", ct).ConfigureAwait(false);

            _tokenizer = ZipVoiceTokenizer.CreateFromTokens(tokens);
            _graphs = new IlgpuZipVoiceGraphs(encoder, decoder, vocoder, _accelerator);
            _pipeline = new ZipVoicePipeline(_graphs);
            _residentModel = ModelRepo;

            OnLoadProgress?.Invoke("ready", 100);
            Console.WriteLine($"[AiVoiceEngine] {ModelName} ready on {_accelerator.AcceleratorType}");
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Open a repo file through the hub as a seekable lazy-hash stream and build a session.</summary>
    private async Task<InferenceSession> LoadSessionAsync(string filename, CancellationToken ct)
    {
        var stream = await OpenModelStreamAsync(filename, ct).ConfigureAwait(false);
        await using (stream)
            return await InferenceSession.CreateFromOnnxStreamAsync(_accelerator, stream, ct: ct)
                .ConfigureAwait(false);
    }

    /// <summary>
    /// Load the vocoder out of the sherpa-onnx archive, via the hub's source proxy.
    /// </summary>
    /// <remarks>
    /// ⚠️ Warmed first. A <c>.tar.bz2</c> cannot be seeked into, so the hub has to fetch the whole 634 MB
    /// archive and decompress it from the start - minutes on first contact - and a request left waiting on
    /// that is killed by the gateway in front of the hub, which reads as a broken server and is not one.
    /// <c>/src/warm</c> returns immediately and reports progress, so the waiting happens BETWEEN requests
    /// instead of inside one. Once warm, the member request is milliseconds.
    /// </remarks>
    private async Task<InferenceSession> LoadVocoderAsync(CancellationToken ct)
    {
        var archive = Uri.EscapeDataString(VocoderArchiveUrl);
        var member = Uri.EscapeDataString(VocoderMember);
        var warmUrl = $"{HubBaseUrl}/src/warm?url={archive}&member={member}";

        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (clock.Elapsed < TimeSpan.FromMinutes(30))
        {
            using var res = await _http.GetAsync(warmUrl, ct).ConfigureAwait(false);
            if (res.StatusCode == System.Net.HttpStatusCode.NotFound)
                throw new Exception(
                    "the hub has no /src/warm endpoint - it needs a build with SourceProxy deployed");
            if (res.IsSuccessStatusCode && res.StatusCode != System.Net.HttpStatusCode.Accepted) break;
            if (res.StatusCode != System.Net.HttpStatusCode.Accepted)
                throw new Exception($"the hub could not cache the vocoder archive: {(int)res.StatusCode}");
            OnLoadProgress?.Invoke("vocoder (hub caching archive)", 75);
            await Task.Delay(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
        }

        var memberUrl = $"{HubBaseUrl}/src?url={archive}&member={member}";
        var bytes = await InferenceSession.DownloadBytesChunkedAsync(_http, memberUrl).ConfigureAwait(false);
        if (bytes.Length != VocoderBytes)
            throw new Exception($"the vocoder is {bytes.Length} bytes, expected {VocoderBytes:N0}. A "
                              + "different file here does not fail loudly - it renders noise.");
        return InferenceSession.CreateFromFile(_accelerator, bytes);
    }

    /// <summary>Open a repo file as a seekable stream via the hub, as a lazy-hash torrent.</summary>
    private async Task<Stream> OpenModelStreamAsync(string filename, CancellationToken ct)
    {
        var hub = new HubModelStream(_webTorrent, _http);
        var model = await hub.OpenAsync(ModelRepo, filename, deselect: false, ct).ConfigureAwait(false);
        if (model.Length <= 0)
            throw new Exception($"hub returned a zero-length stream for {ModelRepo}/{filename}");
        return model.Stream;
    }

    private async Task<string> LoadTextAsync(string filename, CancellationToken ct)
    {
        var stream = await OpenModelStreamAsync(filename, ct).ConfigureAwait(false);
        await using (stream)
        using (var reader = new StreamReader(stream))
            return await reader.ReadToEndAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Release the resident voice model.</summary>
    public Task EvictAsync()
    {
        DisposeSessions();
        return Task.CompletedTask;
    }

    private void DisposeSessions()
    {
        _pipeline?.Dispose();
        _pipeline = null;
        // ⚠️ IlgpuZipVoiceGraphs owns the three sessions and disposes them; disposing them here as well
        // would be a double dispose.
        _graphs?.Dispose();
        _graphs = null;
        _tokenizer = null;
        _residentModel = null;
    }

    /// <summary>Disposes the resident sessions. Never disposes the accelerator - the app owns it.</summary>
    public void Dispose()
    {
        DisposeSessions();
        _gate.Dispose();
    }
}
