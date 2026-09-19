using System.Linq;
using ILGPU.Runtime;
using SpawnDev.ILGPU.ML;
using SpawnDev.ILGPU.ML.Hub;
using SpawnDev.ILGPU.ML.Pipelines;

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

    /// <summary>Wall time in the flow-matching decoder, in ms - the stage that dominates a synthesis.</summary>
    public double DecoderMs { get; init; }

    /// <summary>Wall time of the decoder's FIRST Euler step alone, in ms.</summary>
    /// <remarks>
    /// ⚠️ All the Euler steps run at identical shapes, so per-shape setup lands entirely in step 1 and the
    /// rest are steady state. Carried back to the page because these two numbers call for opposite work -
    /// a large first step is setup to be amortised or avoided, a large remainder is the decoder itself -
    /// and the engine runs in a SHARED WORKER whose console the window never sees.
    /// </remarks>
    public double DecoderFirstStepMs { get; init; }

    /// <summary>WHY the decoder's dispatch-plan capture is or is not live.</summary>
    /// <remarks>
    /// ⚠️ MEASURED 2026-09-03: capture was refused for this graph on every backend and nothing anywhere
    /// said so, while the decoder cost 8.4 s per Euler step. A boolean would not have helped - the reason
    /// is what points at the fix.
    /// </remarks>
    public string CaptureStatus { get; init; } = "";

    /// <summary>The text that was actually rendered, after <see cref="AiVoiceEngine.MaxSpokenCharacters"/>.</summary>
    /// <remarks>
    /// 🔴 THE ONLY HONEST THING TO SCORE A READ-BACK AGAINST. <see cref="AiVoiceEngine.MaxSpokenCharacters"/>
    /// is a product decision that cuts the request before synthesis, so the caller's string and the spoken
    /// string are DIFFERENT whenever a reply is long - and a transcript compared against the request then
    /// reports a shortfall the voice did not cause.
    ///
    /// ⚠️ MEASURED 2026-09-06, and it cost a day: the voice gate scored a 343-character line at 92% and its
    /// last-6-seconds instrument at 71%, and both numbers were written up as "real residual degradation at
    /// the end of the longest utterances". The engine had spoken 320 of those characters, exactly as
    /// designed, and had SAID SO one line above in the same log. The 5 words missing from the transcript are
    /// precisely the 5 words the cap removed - 64 - 5 = 59 = the 92%, and 17 - 5 = 12 = the 71%. ZipVoice
    /// rendered every word it was given, verbatim.
    ///
    /// A caller reconstructing this by re-applying the cap itself would be a second copy of
    /// <see cref="AiVoiceEngine.TrimToSpeakableLength"/> that can drift from the one that ran. The engine
    /// knows what it spoke; it returns it.
    /// </remarks>
    public string SpokenText { get; init; } = "";
}

/// <summary>
/// Text-to-speech for the AI server: two models on the same accelerator the chat and speech engines use -
/// Kokoro for built-in voices, ZipVoice for cloning one.
/// </summary>
/// <remarks>
/// <para>
/// ⭐ WHICH MODEL SPEAKS IS DECIDED BY THE VOICE ID, and the split is about speed. ZipVoice is the only
/// thing here that can speak in a voice somebody recorded, and it renders <b>3.6x SLOWER than realtime</b>
/// (MEASURED: 73.0 s of compute for 20.4 s of audio; its flow decoder is 8,621 nodes run four times per
/// utterance) - that is the pause between spoken chunks, and it is the renderer rather than anything
/// chunking can fix. Kokoro renders <b>faster than realtime in a browser</b> (MEASURED on WebGPU, real
/// RTX 4070: 1.84 s for 2.27 s of audio) because it is 1,885 nodes in one pass, and it needs no reference
/// at all. So Kokoro is the default and handles ordinary replies; ZipVoice is opt-in, for a voice the user
/// deliberately cloned. See AiVoiceEngine.Kokoro.cs.
/// </para>
/// <para>
/// Deliberately the same shape as <see cref="AiSpeechEngine"/> - one resident model, a load gate, and an
/// <see cref="EvictOtherKind"/> hook - because the GPU is shared and per-kind residency is a hard rule in
/// this repo. Models arrive as LAZY-HASH torrents through the hub for the same reasons documented there:
/// random-access streaming, OPFS caching, and no re-download on reload.
/// </para>
/// <para>
/// ⚠️ ZipVoice CLONES a voice - it needs a reference clip and that clip's transcript, and it speaks the
/// reply in that voice. In a conversation loop the natural reference is the turn the user just spoke, which
/// is why <see cref="SpeakAsync"/> takes one. Without a reference it cannot speak at all: the default voice
/// is a BUILT-IN one, never a fallback to cloning whoever happens to be talking.
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
public sealed partial class AiVoiceEngine : IDisposable
{
    private readonly IModelSource _source;
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
    public AiVoiceEngine(IModelSource source, HttpClient http, Accelerator accelerator)
    {
        _source = source;
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
    /// <remarks>
    /// ⚠️ EITHER model. GpuResidency asks this to decide whether the voice kind is holding VRAM, and with
    /// only the built-in model resident the old answer was false - so the kind was invisible to the
    /// budget: never counted against it, never evicted to make room for anything else.
    /// </remarks>
    public bool IsLoaded => _pipeline != null || _kokoro != null;

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
    /// <summary>
    /// Log the per-synthesis shape diagnostics (decoder <c>If</c> branch census, Slice compile-time
    /// fallback count). Default off - this is debugging instrumentation, not operational logging.
    /// </summary>
    /// <remarks>
    /// The census is what identified the ILGPU.ML shape defect fixed in 5.2.9: <c>else=0</c> on every
    /// intelligible utterance and <c>else=24</c> on the first garbled one, which turned "the voice is
    /// bad" into a specific branch. Kept for the next time a synthesis comes out wrong.
    /// </remarks>
    public bool VerboseLogging { get; set; }

    /// <summary>
    /// Speak, transcribe the result, and re-roll the noise when the words that come back are not the words
    /// asked for. Default OFF - see the note at the call site in <c>AiApiRouter.ApiSpeak</c>.
    /// </summary>
    /// <remarks>
    /// Flow matching can draw noise that yields fluent speech of the WRONG sentence, so this is a real
    /// safeguard - but it costs a full transcription per synthesis, and the garbled replies that prompted
    /// it on 2026-09-04 turned out to be an ILGPU.ML shape defect, fixed in 5.2.9. Left available and off.
    /// </remarks>
    public bool VerifyByReadBack { get; set; }

    /// <param name="transcribe">
    /// 🔴 A RECOGNISER, AND WHY SPEAKING NEEDS ONE. ZipVoice is a flow-matching model that starts from
    /// fresh noise, and on some draws it produces confident, well-formed speech that is NOT the sentence
    /// it was asked for. <c>ZipVoicePipeline.SpeakVerifiedAsync</c> documents its own measurement of this:
    /// four seeds, three clean, one that transcribed as "Loner's call, Nanawa, Nenfer" - and the reference
    /// implementation does the same. Nothing INSIDE the synthesiser can see it, because a garbled draw has
    /// the same amplitude, the same duration and the same spectral character as a good one.
    /// <para>
    /// MEASURED 2026-09-04: the Captain heard a reply as "really odd sounding with high pitch weird
    /// noises... all over the place in pitch and variable", and a read-back gate on the same path scored
    /// <b>0% word overlap - Whisper returned "[INAUDIBLE]"</b> for a line whose peak, RMS and 17.9
    /// chars/sec all looked perfectly healthy. Supply this and a garbled draw is caught and re-rolled;
    /// leave it null and the engine ships whatever the first draw produced.
    /// </para>
    /// </param>
    /// <param name="noiseSeed">
    /// Pin the flow-matching noise draw, making this synthesis REPRODUCIBLE. Null (default) re-samples,
    /// which is the shipping behaviour.
    /// </param>
    /// <remarks>
    /// ⚠️ WITHOUT THIS, TWO RUNS ARE NOT COMPARABLE AND NEITHER ARE TWO BACKENDS. Zero-shot flow matching
    /// starts from fresh noise every call, so the same line scores differently each time - MEASURED
    /// 2026-09-04 on WebGPU, a 343-character line read back at 30%, 39% and 55% across three runs of the
    /// identical fixture. Comparing WebGPU against CUDA, or a fix against its baseline, is meaningless
    /// while that term is free. Pinning it is what turns "it sounds worse" into a difference with a cause.
    /// </remarks>
    public async Task<AiSpeech> SpeakAsync(string text, string referenceText, float[] referenceSamples,
        int referenceSampleRate, int? maxSpokenCharacters = null,
        Func<float[], int, Task<string>>? transcribe = null, int? noiseSeed = null,
        CancellationToken ct = default)
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

        // 🔴 ZIPVOICE CANNOT RENDER A LONG UTTERANCE, SO IT IS NOT GIVEN ONE. MEASURED 2026-09-15 on
        // WebGPU with seeded noise, read back through Whisper:
        //
        //   250 chars -> 100%      296 chars -> 67%      343 chars -> 42%
        //
        // At 42% a listener does not hear an oddly-paused sentence, they hear "Norman praying walkers
        // eight day so we walked a bone before". This splits the text at sentence ends - promoting a
        // clause boundary when a single sentence is itself too long - and renders each piece as its own
        // synthesis, then concatenates. The reference prompt is identical for every piece, so the voice
        // does not change across a seam.
        //
        // ⚠️ THE SPLIT ALONE IS NOT THE FIX. Promoting a comma to a full stop shortens nothing by itself;
        // it only matters because each piece is SYNTHESISED SEPARATELY here. I shipped the promotion on
        // its own first and it changed nothing, because the whole string still went to one render.
        var segments = SplitIntoSpeakableChunks(text, MaxRenderableCharacters, 0,
                                                clauseSplitOver: MaxRenderableCharacters);
        if (segments.Count == 0) segments = new List<string> { text };

        // One synthesis at a time - see _inferGate. A background warm pass counts as one.
        await _inferGate.WaitAsync(ct).ConfigureAwait(false);
        double inferenceMs;
        SpawnDev.ILGPU.ML.Pipelines.ZipVoiceResult result;
        try
        {
            var started = DateTime.UtcNow;
            // 🔴 WHICH BRANCH DID THE DECODER TAKE? ZipVoice's relative positional encoding arrives
            // through an If: the THEN branch reads a precomputed [1999, 48] table, the ELSE branch
            // RECOMPUTES it. A relative table of 1999 rows spans 2N-1 for N = 1000 frames, so at
            // 93.75 frames/sec (hop 256 @ 24 kHz) anything past ~10.7 s of TOTAL sequence - reference
            // prompt included - must take the else path. MEASURED 2026-09-04: intelligibility is 100%
            // at 6.58 s and 0% ("[MUSIC PLAYING]") at 8.82 s, and the 2026-09-04 branch census that
            // recorded "then=21, else=0" was taken on a SHORT utterance, so the else branch's 254 nodes
            // have never been shown to be correct. Censusing per synthesis is what turns that from a
            // story into a fact.

            // Pin (or release) the noise draw for THIS call. See the noiseSeed parameter.
            _pipeline!.NoiseSeed = noiseSeed;

            SpawnDev.ILGPU.ML.Operators.IfOperator.ResetBranchCensus();
            SpawnDev.ILGPU.ML.Graph.GraphExecutor.ResetSliceAttrFallbackDiagnostics();
            async Task<SpawnDev.ILGPU.ML.Pipelines.ZipVoiceResult> RenderOneAsync(string piece)
            {
                if (transcribe != null)
                {
                    // Speak it, listen to it, and re-roll the noise if the words that come back are not
                    // the words asked for. The best attempt is returned even when none passes the
                    // tolerance, because a flawed line is still better than silence.
                    var verified = await _pipeline!
                        .SpeakVerifiedAsync(piece, referenceText ?? "", referenceSamples, referenceSampleRate,
                            _tokenizer!, transcribe)
                        .ConfigureAwait(false);
                    // Say what the check concluded. A re-roll that silently happened is a cost nobody can
                    // account for, and a FAILED verification that silently shipped is the original defect.
                    Console.WriteLine($"[voice] read-back check: WER {verified.WordErrorRate:F2} "
                        + $"({(verified.Passed ? "PASSED" : "FAILED - shipping the best of the attempts")}), "
                        + $"heard \"{verified.Transcript}\"");
                    return verified.Speech;
                }
                return await _pipeline!
                    .SpeakAsync(piece, referenceText ?? "", referenceSamples, referenceSampleRate, _tokenizer!)
                    .ConfigureAwait(false);
            }

            if (segments.Count == 1)
            {
                result = await RenderOneAsync(segments[0]).ConfigureAwait(false);
            }
            else
            {
                // Render each piece and join. Straight concatenation: each piece is a whole sentence, so
                // it already carries its own leading/trailing silence - inserting a gap would add a pause
                // the text does not ask for.
                var rendered = new List<SpawnDev.ILGPU.ML.Pipelines.ZipVoiceResult>(segments.Count);
                foreach (var piece in segments)
                {
                    ct.ThrowIfCancellationRequested();
                    rendered.Add(await RenderOneAsync(piece).ConfigureAwait(false));
                }
                var rate = rendered[0].SampleRate;
                var total = 0;
                foreach (var r in rendered) total += r.Audio.Length;
                var joined = new float[total];
                var at = 0;
                foreach (var r in rendered)
                {
                    if (r.SampleRate != rate)
                        throw new InvalidOperationException(
                            $"ZipVoice returned {r.SampleRate} Hz for one segment and {rate} Hz for another; "
                          + "concatenating them would change the pitch of part of the reply");
                    Array.Copy(r.Audio, 0, joined, at, r.Audio.Length);
                    at += r.Audio.Length;
                }
                result = rendered[0] with { Audio = joined };
                Console.WriteLine($"[voice] {text.Length} chars exceeded {MaxRenderableCharacters}, rendered as "
                    + $"{segments.Count} segments ({string.Join(", ", segments.Select(s => s.Length + "ch"))}) "
                    + $"-> {joined.Length / (double)rate:F2}s joined");
            }
            inferenceMs = (DateTime.UtcNow - started).TotalMilliseconds;
            if (VerboseLogging)
                Console.WriteLine($"[voice] If census for this synthesis: "
                    + $"then={SpawnDev.ILGPU.ML.Operators.IfOperator.ThenBranchCount} "
                    + $"else={SpawnDev.ILGPU.ML.Operators.IfOperator.ElseBranchCount} "
                    + $"| sliceAttrFallback={SpawnDev.ILGPU.ML.Graph.GraphExecutor.SliceAttrFallbackCount} "
                    + $"({SpawnDev.ILGPU.ML.Graph.GraphExecutor.LastSliceAttrFallbackInfo ?? "none"}) "
                    + $"| {result.Audio.Length / (double)result.SampleRate:F2}s of audio for {text.Length} chars");
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
            DecoderMs = result.DecoderMs,
            DecoderFirstStepMs = result.DecoderFirstStepMs,
            CaptureStatus = result.DecoderCaptureStatus,
            // `text` is post-trim by this point - see the SpeakAsync body. This is what the vocoder rendered.
            SpokenText = text,
        };
    }

    /// <summary>
    /// Soft target for how many characters of a reply are spoken aloud. Default 320.
    /// </summary>
    /// <remarks>
    /// A spoken reply is not a written one. Nobody wants a chat model's full paragraph read at them, and a
    /// voice assistant that monologues is worse than one that is brief - so a limit is a product decision
    /// first, and it would exist even if everything below it were free.
    ///
    /// 🔴 IT IS A TARGET, NOT A CUT. Speech is shortened only at SENTENCE ends. A reply that stops
    /// mid-clause does not sound brief, it sounds broken - and by ear it is indistinguishable from the voice
    /// itself failing at length, which is a defect we have actually had. When the sentence in progress runs
    /// past this number, the whole sentence is still spoken, bounded by
    /// <see cref="MaxSpokenCharactersCeiling"/>.
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

    // ── PREPARED VOICES: clone ONCE, speak many ────────────────────────────────────────────────────────
    //
    // 🔴 WHY. ZipVoice is one-shot cloning, and this engine was handing it RAW reference audio on every
    // call - so every single reply re-trimmed the reference, re-ran the mel over it and rebuilt the prompt
    // features, work that is identical for the life of a voice. The ML pipeline already anticipates the fix
    // and says so on SynthesizeFromFeaturesAsync: "the reference features are the expensive, unchanging
    // part of a voice, so a speaking robot computes them once per voice rather than once per sentence."
    // ZipVoiceFeatures.TrimReferenceSilence is public for exactly this, per its own note.
    //
    // It also makes cloning a CHOICE instead of a per-question tax: a voice is added and prepared once
    // (a family member records a clip, or a bundled voice ships with the app), then selected by id.
    // ⚠️ Preparation needs the model loaded, because the feature geometry comes from the pipeline's Config.
    private readonly Dictionary<string, PreparedVoice> _voices = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Ids of the voices prepared and ready to speak.</summary>
    // ⚠️ Built-in voices FIRST. This is what a picker renders in order, and the fast no-clone voices are
    // the ones almost everybody wants - a list that opens with someone's recorded clone invites the user
    // into the slow path by accident. See AiVoiceEngine.Kokoro.cs.
    public IReadOnlyCollection<string> VoiceIds
        => KokoroVoiceIds.Concat(_voices.Keys).ToArray();

    /// <summary>Is this voice prepared and ready to speak without re-deriving its reference?</summary>
    public bool HasVoice(string voiceId)
        => voiceId != null && (IsKokoroVoice(voiceId) || _voices.ContainsKey(voiceId));

    /// <summary>Forget a prepared voice (the user removed it).</summary>
    public bool ForgetVoice(string voiceId) => voiceId != null && _voices.Remove(voiceId);

    /// <summary>
    /// Derive a voice's reference features ONCE. This is the "train" step: everything here is what used to
    /// happen on every reply.
    /// </summary>
    /// <param name="referenceText">
    /// The clip's exact transcript. ⚠️ Anything present in the audio and missing here bleeds into the start
    /// of every generated line, so a sloppy transcript degrades the voice in a way that is invisible in the
    /// text and audible in the output.
    /// </param>
    public async Task<PreparedVoice> PrepareVoiceAsync(string voiceId, string displayName,
        string referenceText, float[] referenceSamples, int referenceSampleRate,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(voiceId)) throw new ArgumentException("a voice needs an id", nameof(voiceId));
        if (referenceSamples == null || referenceSamples.Length == 0)
            throw new ArgumentException("a cloned voice needs reference audio", nameof(referenceSamples));
        if (referenceSampleRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(referenceSampleRate), referenceSampleRate,
                "sample rate must be positive");

        await EnsureLoadedAsync(ct).ConfigureAwait(false);

        // Same order as ZipVoicePipeline.SynthesizeAsync, and the order matters: dead air comes off FIRST,
        // then the deliberate tail goes on. Reversed, the trim takes back the pad that stops the reference's
        // last word bleeding into the generated line.
        var speech = _pipeline!.TrimReferenceSilence
            ? SpawnDev.ILGPU.ML.Preprocessing.ZipVoiceFeatures.TrimReferenceSilence(
                referenceSamples, referenceSampleRate, _pipeline.ReferenceSilenceGateDb,
                maxPauseSeconds: _pipeline.ReferenceMaxPauseSeconds)
            : referenceSamples;

        int tail = (int)(_pipeline.ReferenceTailSilenceSeconds * referenceSampleRate);
        var padded = tail > 0 ? new float[speech.Length + tail] : speech;
        if (tail > 0) Array.Copy(speech, padded, speech.Length);

        var features = SpawnDev.ILGPU.ML.Preprocessing.ZipVoiceFeatures.ComputePromptFeatures(
            padded, referenceSampleRate, _pipeline.Config, out int promptFrames);

        var voice = new PreparedVoice
        {
            Id = voiceId,
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? voiceId : displayName,
            ReferenceText = referenceText ?? "",
            PromptTokens = _tokenizer!.Encode(referenceText ?? ""),
            PromptFeatures = features,
            PromptFrames = promptFrames,
            ReferenceSeconds = referenceSamples.Length / (double)referenceSampleRate,
            ReferenceSpeechSeconds = speech.Length / (double)referenceSampleRate,
        };
        _voices[voiceId] = voice;
        if (VerboseLogging)
            Console.WriteLine($"[voice] prepared '{voiceId}' ({voice.DisplayName}): "
                + $"{voice.ReferenceSeconds:F2}s reference -> {promptFrames} prompt frames, reused every reply");
        return voice;
    }

    /// <summary>
    /// Speak in an already-prepared voice. No trimming, no mel, no prompt rebuild - the reference work was
    /// done by <see cref="PrepareVoiceAsync"/>.
    /// </summary>
    public async Task<AiSpeech> SpeakWithVoiceAsync(string text, string voiceId,
        int? maxSpokenCharacters = null, int? noiseSeed = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("nothing to say", nameof(text));

        // ⭐ A built-in voice needs no preparation at all - there is no reference clip to trim, no mel to
        // compute and no prompt to build, which is most of why it is the fast path. Dispatching here
        // rather than at the router means the wire shape does not change: a reply already carries only
        // its text and a voice id. See AiVoiceEngine.Kokoro.cs.
        if (IsKokoroVoice(voiceId))
            return await SpeakKokoroAsync(text, voiceId!, maxSpokenCharacters, ct).ConfigureAwait(false);

        if (!_voices.TryGetValue(voiceId ?? "", out var voice))
            throw new InvalidOperationException(
                $"voice '{voiceId}' is not prepared; call PrepareVoiceAsync first. Prepared: "
                + (_voices.Count == 0 ? "(none)" : string.Join(", ", _voices.Keys)));

        await EnsureLoadedAsync(ct).ConfigureAwait(false);
        text = TrimToSpeakableLength(text, maxSpokenCharacters);

        await _inferGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var started = DateTime.UtcNow;
            _pipeline!.NoiseSeed = noiseSeed;
            var result = await _pipeline.SynthesizeFromFeaturesAsync(
                _tokenizer!.Encode(text), voice.PromptTokens, voice.PromptFeatures, voice.PromptFrames)
                .ConfigureAwait(false);
            double ms = (DateTime.UtcNow - started).TotalMilliseconds;
            return new AiSpeech(result.Audio, result.SampleRate, ModelName, ms)
            {
                ReferenceSeconds = voice.ReferenceSeconds,
                ReferenceSpeechSeconds = voice.ReferenceSpeechSeconds,
                DecoderMs = result.DecoderMs,
                SpokenText = text,
            };
        }
        finally { _inferGate.Release(); }
    }

    /// <summary>Hard ceiling in characters. Only a single sentence longer than this is ever cut. Default 1200.</summary>
    /// <remarks>
    /// ⚠️ This exists so that "never cut mid-sentence" cannot become "read a 4,000-character run-on
    /// paragraph at the user". It is the ONLY path that can still stop mid-sentence, it sits far above any
    /// ordinary sentence, and when it fires it says so in the log - at that length the text is malformed
    /// rather than merely long, and that is worth seeing rather than silently trimming.
    /// </remarks>
    public int MaxSpokenCharactersCeiling { get; set; } = 1200;

    /// <summary>
    /// The longest utterance ZipVoice renders intelligibly. Longer text is split at sentence ends and
    /// rendered as several syntheses - see <see cref="SpeakAsync"/>.
    /// </summary>
    /// <remarks>
    /// 🔴 MEASURED, NOT CHOSEN. `SpokenReplyIsIntelligibleWhenReadBack` on WebGPU with seeded noise, read
    /// back through Whisper, 2026-09-15:
    /// <code>
    ///   250 chars -> 100% (47/47)    296 chars -> 67% (37/55)    343 chars -> 42% (27/64)
    /// </code>
    /// 250 is the longest measured point that is still perfect. Raising it trades intelligibility for
    /// fewer seams, and the curve above is what that trade costs.
    /// <para>
    /// ⚠️ This is a ZIPVOICE limit and lives on this engine deliberately. Kokoro does not need it - it
    /// renders 351-token chunks cleanly (see the ML streaming gate) - and giving it an unnecessary split
    /// would add seams for nothing.
    /// </para>
    /// </remarks>
    public int MaxRenderableCharacters { get; set; } = 250;

    private string TrimToSpeakableLength(string text, int? overrideCap)
    {
        var cap = overrideCap is > 0 ? overrideCap.Value : MaxSpokenCharacters;
        var spoken = TrimToSpeakableLength(text, cap, MaxSpokenCharactersCeiling, out var why);
        if (spoken.Length != text.Length)
            Console.WriteLine($"[AiVoiceEngine] speaking {spoken.Length} of {text.Length} characters "
                            + $"(target={cap}, ceiling={MaxSpokenCharactersCeiling}): {why}");
        return spoken;
    }

    /// <summary>
    /// Shorten a reply for speech at a SENTENCE boundary. Pure, so it can be gated without a model.
    /// </summary>
    /// <param name="text">The reply as written.</param>
    /// <param name="cap">Soft target - see <see cref="MaxSpokenCharacters"/>.</param>
    /// <param name="ceiling">Hard ceiling - see <see cref="MaxSpokenCharactersCeiling"/>.</param>
    /// <param name="why">One phrase naming which rule decided, for the log.</param>
    /// <remarks>
    /// <para>
    /// 🔴 THE RULE IS: NEVER END MID-SENTENCE. Speak whole sentences while they fit the target; if the very
    /// first sentence already exceeds the target, speak that sentence WHOLE rather than cutting it. Only a
    /// single sentence longer than <paramref name="ceiling"/> is cut, and then at a word boundary.
    /// </para>
    /// <para>
    /// ⚠️ This replaces a rule that cut at <c>text[..cap]</c> whenever no sentence end fell inside the
    /// window. MEASURED 2026-09-06 on the voice gate's own fixture - a single 343-character sentence - it
    /// spoke 320 characters, stopping after "tired and content after" and dropping "a long and useful day."
    /// The audio was perfect; the sentence was not. Worse, the read-back then scored that as 92%
    /// intelligibility and the shortfall was written up as voice degradation.
    /// </para>
    /// <para>
    /// ⚠️ A terminator counts as a sentence end only at end-of-text or before whitespace, which keeps "3.5"
    /// and "example.com" intact; runs like "?!" and "..." collapse to one boundary. An abbreviation
    /// ("Dr. Smith") is still read as a boundary - the cost is an utterance ending slightly early at a
    /// plausible place, never one ending mid-clause, and only when the reply is over target anyway.
    /// </para>
    /// <para>
    /// ⚠️ Stopping too EARLY is a real failure too: "Sure." followed by a long paragraph would otherwise
    /// speak one word. When the whole sentences that fit come to less than a third of the target, one more
    /// whole sentence is taken if the ceiling allows - the same judgement the old <c>cap / 3</c> test was
    /// making, now made without ever splitting a sentence.
    /// </para>
    /// </remarks>
    public static string TrimToSpeakableLength(string text, int cap, int ceiling, out string why)
    {
        why = "";
        if (string.IsNullOrEmpty(text) || cap <= 0 || text.Length <= cap) return text;
        if (ceiling < cap) ceiling = cap;

        var ends = SentenceEndOffsets(text);

        // The most whole sentences that fit inside the target.
        int chosen = 0;
        foreach (var e in ends) { if (e <= cap) chosen = e; else break; }

        if (chosen == 0)
        {
            // The first sentence alone is past the target. Speak it whole - that is the entire point.
            int first = ends[0];
            if (first <= ceiling)
            {
                why = "first sentence runs past the target, spoken whole";
                return text[..first].TrimEnd();
            }

            // A single sentence longer than the ceiling: the one case that can still stop mid-sentence.
            var window = text[..ceiling];
            var lastSpace = window.LastIndexOf(' ');
            why = $"ONE sentence of {first} characters exceeds the ceiling - cut mid-sentence at a word boundary";
            return (lastSpace > ceiling / 3 ? window[..lastSpace] : window).TrimEnd();
        }

        if (chosen < cap / 3)
        {
            // What fits is too short to sound like an answer; take one more whole sentence if allowed.
            foreach (var e in ends)
            {
                if (e <= chosen) continue;
                if (e <= ceiling) { chosen = e; why = "one sentence over the target, to avoid a clipped reply"; }
                break;
            }
        }
        if (why.Length == 0) why = "whole sentences within the target";
        return text[..chosen].TrimEnd();
    }

    /// <summary>Offsets just past each sentence terminator, always ending with <c>text.Length</c>.</summary>

    /// <summary>
    /// Split a reply into speakable chunks at SENTENCE ends, each at most <paramref name="maxChars"/>
    /// and - except for the last - at least <paramref name="minChars"/>.
    /// </summary>
    /// <param name="text">The reply as written.</param>
    /// <param name="maxChars">Soft ceiling; a single sentence longer than this is emitted WHOLE.</param>
    /// <param name="minChars">
    /// 🔴 THE FLOOR EXISTS BECAUSE A SYNTHESIS PASS COSTS THE SAME WHATEVER IT SAYS, and a chunk below it
    /// makes the reply STUTTER. MEASURED 2026-09-15 in the demo's WebGPU worker (RTX 4070), Kokoro:
    /// <code>
    ///    82 chars ->  5.30s audio in  6,189 ms      315 chars -> 19.98s audio in 10,287 ms
    ///   278 chars -> 17.30s audio in  8,798 ms      246 chars -> 15.68s audio in  8,151 ms
    /// </code>
    /// which is <c>render = 5.04s + 0.22 x audio</c>, with audio a very steady 0.0635 s per character. So a
    /// pass costs ~5 s of FIXED host work before it says a word, and the playback lead only GROWS when a
    /// chunk carries more than <c>5.04 / (1 - 0.22)</c> = 6.4 s of audio, i.e. ~102 characters. The old
    /// splitter had a ceiling and no floor, so the first chunk of this fixture came out at 82 characters
    /// (5.30 s of audio) and the next chunk needed 8,798 ms to render - arriving 3,498 ms after the speaker
    /// needed it, an audible gap. Below the floor, no amount of chunking or buffering can help: the chunk
    /// cannot buy enough playing time to cover even one more pass.
    /// <para>
    /// ⚠️ THE FLOOR WINS OVER THE CEILING. A chunk under <paramref name="minChars"/> keeps taking whole
    /// sentences even past <paramref name="maxChars"/> - a minimum that yields to the maximum is not a
    /// minimum. The LAST chunk is exempt: nothing follows it, so it has nothing to cover.
    /// </para>
    /// <para>
    /// ⚠️ 0.0635 s/char and that 5.04 s intercept are THIS card and THIS model. A slower device needs a
    /// larger floor, and past some point cannot stream at all - which is a property of the device, not a
    /// number to tune away. The gate is <c>AiVoiceStreamingTests.ChunkedReplyStreamsWithoutAPause</c>.
    /// </para>
    /// </param>
    /// <remarks>
    /// <para>
    /// 🔴 THIS IS WHAT REPLACES THE CHARACTER CAP. <see cref="MaxSpokenCharacters"/> makes a long reply
    /// stop early - the page shows text the voice never reads. Chunking says the whole thing, and because a
    /// caller can synthesise chunk N+1 while chunk N plays, it also cuts time-to-first-audio from "the
    /// entire reply" to "the first sentence".
    /// </para>
    /// <para>
    /// ⚠️ Uses the same <c>SentenceEndOffsets</c> the cap uses, deliberately. A second notion of "where a
    /// sentence ends" would drift from the first, and the two disagreeing is how a reply gets cut in a place
    /// neither of them intended.
    /// </para>
    /// <para>
    /// ⚠️ A single sentence longer than <paramref name="maxChars"/> is emitted WHOLE rather than cut. The
    /// cap's own ceiling is the only thing allowed to break a sentence; splitting mid-clause here would put
    /// an audible stop in the middle of a phrase, which is exactly the artefact the "sentence ends only"
    /// rule exists to prevent.
    /// </para>
    /// </remarks>
    public static List<string> SplitIntoSpeakableChunks(string text, int maxChars, int minChars = 0,
                                                        int clauseSplitOver = 0)
    {
        var chunks = new List<string>();
        if (string.IsNullOrWhiteSpace(text)) return chunks;
        if (maxChars <= 0) maxChars = int.MaxValue;

        // 🔴 A SENTENCE TOO LONG FOR THE ENGINE IS SPLIT AT A CLAUSE, because the alternative is not a
        // long sentence - it is gibberish. This rule used to be "a single sentence longer than maxChars
        // is emitted WHOLE", on the reasoning that an audible stop in a strange place is worse than a
        // long chunk. That reasoning holds right up until the renderer cannot render it. MEASURED
        // 2026-09-15 on ZipVoice (WebGPU, seeded noise, read back through Whisper):
        //
        //   250 chars -> 100% intelligible      296 chars -> 67%      343 chars -> 42%
        //
        // At 42% the listener does not hear a sentence with an odd pause; they hear "Norman praying
        // walkers eight day so we walked a bone before". A comma is a place speech pauses ANYWAY, so
        // splitting there costs a breath and buys the words back.
        //
        // ⚠️ 0 (the default) keeps the old emit-whole behaviour, so every existing caller is unchanged.
        // Only a caller that knows its engine's limit opts in.
        if (clauseSplitOver > 0)
            text = BreakOverlongSentences(text, clauseSplitOver);

        var ends = SentenceEndOffsets(text);
        int start = 0;
        int lastEnd = 0;

        foreach (var end in ends)
        {
            if (end <= start) continue;
            // Adding this sentence would overrun the chunk: close the chunk at the previous sentence end -
            // but only once the chunk is worth a synthesis pass. See minChars.
            if (end - start > maxChars && lastEnd > start && lastEnd - start >= minChars)
            {
                chunks.Add(text[start..lastEnd].Trim());
                start = lastEnd;
            }
            lastEnd = end;
        }
        if (start < text.Length)
        {
            var tail = text[start..].Trim();
            if (tail.Length > 0) chunks.Add(tail);
        }
        return chunks;
    }

    /// <summary>
    /// Rewrites any sentence longer than <paramref name="limit"/> so its clause breaks become sentence
    /// breaks, by promoting a comma/semicolon/colon/dash to a full stop. Text is otherwise unchanged.
    /// </summary>
    /// <remarks>
    /// ⚠️ IT PROMOTES PUNCTUATION RATHER THAN CUTTING, so every downstream rule keeps working: the
    /// chunker still only ever breaks at a sentence end, the "nothing is lost" property still holds
    /// word-for-word, and the spoken text still reads as the reply. Cutting here would have meant two
    /// notions of "where a sentence ends" in one file, which is how a reply gets broken in a place
    /// neither of them intended.
    /// <para>
    /// ⚠️ It walks clause boundaries greedily from the LAST one that fits, so a 343-character sentence
    /// with commas becomes two ordinary sentences rather than one long one and a fragment. A sentence
    /// with NO clause punctuation at all is left alone - there is no good place to break it, and a
    /// mid-word cut is worse than a long utterance.
    /// </para>
    /// </remarks>
    private static string BreakOverlongSentences(string text, int limit)
    {
        var ends = SentenceEndOffsets(text);
        var sb = new System.Text.StringBuilder(text.Length + 8);
        int start = 0;
        foreach (var end in ends)
        {
            var sentence = text[start..end];
            sb.Append(sentence.Length > limit ? PromoteClauses(sentence, limit) : sentence);
            start = end;
        }
        return sb.ToString();

        static string PromoteClauses(string sentence, int limit)
        {
            var chars = sentence.ToCharArray();
            int segmentStart = 0;
            while (sentence.Length - segmentStart > limit)
            {
                // The last clause boundary that still fits the limit. Search from the far end so each
                // emitted piece is as long as it can be without exceeding what the engine renders well.
                int cut = -1;
                for (int i = Math.Min(segmentStart + limit, sentence.Length - 1); i > segmentStart; i--)
                {
                    var c = chars[i];
                    if ((c == ',' || c == ';' || c == ':') && i + 1 < sentence.Length && char.IsWhiteSpace(sentence[i + 1]))
                    { cut = i; break; }
                }
                if (cut < 0) break;          // no clause punctuation in range - leave it whole
                chars[cut] = '.';
                segmentStart = cut + 1;
            }
            return new string(chars);
        }
    }

    private static List<int> SentenceEndOffsets(string text)
    {
        var ends = new List<int>();
        for (int i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c != '.' && c != '!' && c != '?') continue;
            int j = i;
            while (j + 1 < text.Length && (text[j + 1] == '.' || text[j + 1] == '!' || text[j + 1] == '?')) j++;
            if (j + 1 >= text.Length || char.IsWhiteSpace(text[j + 1])) ends.Add(j + 1);
            i = j;
        }
        // Text after the last terminator - or text with none at all - is a final sentence in its own right.
        if (ends.Count == 0 || ends[^1] != text.Length) ends.Add(text.Length);
        return ends;
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

        // 🔴 DELIBERATELY LOAD-ONLY. This used to run a full warm synthesis of "Hello." and it was a bad
        // trade - MEASURED, not assumed.
        //
        // Three renders on WebGPU 2026-09-03, in one process: 51.2 s for the first, 46.9 s for a DIFFERENT
        // length, 36.3 s for a REPEAT of the first. So the decoder's steady-state cost is flat at ~8.4 s
        // per Euler step no matter what has run before; a warm buys only the ~4 s of global kernel
        // compilation (51.2 -> 46.9), and the larger ~7.5 s saving on the repeat is PER-SHAPE setup that a
        // warm on some other sentence cannot provide, because this decoder's shape is the utterance length.
        //
        // Against that ~4 s, a warm synthesis costs ~50 s of GPU AND holds _inferGate, so a real reply that
        // arrives during it simply queues. That is exactly what the hands-free baseline showed: synthesis
        // "returned after 172.5 s" while the engine reported only 84 s of work - the other ~88 s was the
        // turn waiting behind a warm that saved it four seconds.
        //
        // ⚠️ This is NOT the "loaded is not warm" lesson being forgotten - it is that lesson MEASURED for
        // this engine and coming out the other way. The lesson holds for the VAD and the recogniser, where
        // a first real frame paid a compile far larger than the warm. Here the compile is a rounding error
        // next to the model download, and the download is what EnsureLoadedAsync above already did.
        //
        // Revisit if the decoder ever becomes capturable (see IlgpuZipVoiceGraphs.AllowControlFlowCapture):
        // if a synthesis drops to a few seconds, a warm synthesis stops being a turn-blocking cost and the
        // arithmetic changes.
        Console.WriteLine($"[AiVoiceEngine] loaded and ready (no warm synthesis: MEASURED to save ~4s of "
                        + "compile while costing ~50s of GPU that a real reply would queue behind)");
        await Task.CompletedTask;
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
            // The end-of-load marker - see AiSpeechEngine for why it is in the finally and not after the
            // "ready" line.
            OnLoadProgress?.Invoke("idle", 100);
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

    /// <summary>Open a repo file as a seekable stream via the hub - plain HTTP, cached in OPFS.</summary>
    private async Task<Stream> OpenModelStreamAsync(string filename, CancellationToken ct)
    {
        var stream = await _source.OpenAsync(ModelRepo, filename, ct).ConfigureAwait(false);
        if (stream.Length <= 0)
            throw new Exception($"hub returned a zero-length stream for {ModelRepo}/{filename}");
        return stream;
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
        // ⚠️ UnloadKokoro, not DisposeKokoro: an evict must leave the engine USABLE. Disposing the load
        // gate here would make the next reply throw ObjectDisposedException from the reload, which is a
        // dead engine rather than a freed one.
        UnloadKokoro();
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
        DisposeKokoro();
        _gate.Dispose();
    }
}

/// <summary>
/// A voice whose reference has already been turned into prompt features - the "trained" form of a clone.
/// </summary>
/// <remarks>
/// Holding the FEATURES rather than the audio is the whole point: they are what ZipVoice actually conditions
/// on, they are identical for every line the voice speaks, and deriving them was previously repeated on
/// every single reply. A voice is prepared once (a family member records a clip, or a bundled voice ships
/// with the app) and then selected by id.
/// </remarks>
public sealed class PreparedVoice
{
    /// <summary>Stable id used to select this voice.</summary>
    public required string Id { get; init; }
    /// <summary>Name to show a person picking a voice.</summary>
    public required string DisplayName { get; init; }
    /// <summary>The reference clip's exact transcript, kept so a voice can be re-derived or inspected.</summary>
    public string ReferenceText { get; init; } = "";
    /// <summary>Token ids of <see cref="ReferenceText"/>.</summary>
    public required long[] PromptTokens { get; init; }
    /// <summary>Mel prompt features ZipVoice conditions on.</summary>
    public required float[] PromptFeatures { get; init; }
    /// <summary>Frame count of <see cref="PromptFeatures"/>.</summary>
    public required int PromptFrames { get; init; }
    /// <summary>Length of the original reference clip.</summary>
    public double ReferenceSeconds { get; init; }
    /// <summary>Length of the reference after silence trimming - what actually conditions the model.</summary>
    public double ReferenceSpeechSeconds { get; init; }
}
