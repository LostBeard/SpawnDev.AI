using ILGPU.Runtime;
using SpawnDev.ILGPU.ML.Pipelines;

namespace SpawnDev.AI.Server;

/// <summary>One utterance the detector closed, addressed by position in the caller's own stream.</summary>
/// <param name="StartSample">Index of the first sample of the utterance, counted from the stream's start.</param>
/// <param name="Length">Length of the utterance in samples.</param>
public sealed record AiSpeechSpan(long StartSample, int Length);

/// <summary>What one <see cref="AiVadEngine.AcceptAsync"/> call observed.</summary>
/// <param name="SpeechActive">Whether speech is open right now - for a live meter.</param>
/// <param name="Probability">The last frame's speech probability.</param>
/// <param name="Spans">Utterances that CLOSED during this call. Usually empty.</param>
/// <param name="FrameMs">Mean wall time per 512-sample frame, which is the realtime budget check.</param>
public sealed record AiVadUpdate(bool SpeechActive, float Probability, IReadOnlyList<AiSpeechSpan> Spans,
    double FrameMs);

/// <summary>
/// Voice activity detection for the AI server: Silero on the same accelerator everything else uses.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ THE DEFECT THIS EXISTS TO FIX. The demo's hands-free loop had no endpointer at all. It recorded for
/// a fixed <c>MaxUtteranceSeconds</c> of 30 and then transcribed whatever it had, so saying four words
/// meant sitting through 26 seconds of silence before anything happened, every turn. Silero, the streaming
/// endpointer ported from RoseEars, and gates for both already existed in SpawnDev.ILGPU.ML - nothing
/// called them from here.
/// </para>
/// <para>
/// ⚠️ <b>Spans, not audio.</b> A closed utterance is returned as (start, length) into the caller's own
/// stream, never as samples. The window already holds every sample it fed us; shipping a 20 s utterance
/// BACK across the worker boundary as a JSON number array would put 320,000 numbers on the WASM heap to
/// tell the caller something it can express in two integers. The offsets are absolute sample counts from
/// the first sample handed to <see cref="AcceptAsync"/> after a <see cref="ResetStream"/>, so the caller
/// slices its own buffer.
/// </para>
/// <para>
/// ⚠️ Silero's clock only advances in whole 512-sample frames, so a span is always frame-aligned. A caller
/// must clamp the slice to its own buffer length rather than assuming the engine and it agree to the
/// sample - they agree to the frame.
/// </para>
/// <para>
/// ⚠️ <b>Residency.</b> This is a 643 KB model and it must stay resident for the life of the conversation:
/// it runs ~31 times a second for as long as the microphone is open, so an eviction that forced a reload
/// mid-utterance would cost more than everything it saves. It is registered with a small footprint for
/// exactly that reason - under any realistic budget it never becomes the LRU victim that matters.
/// </para>
/// <para>
/// ⚠️ <b>The realtime budget is 32 ms per frame</b> (512 samples at 16 kHz) and it is reported, not
/// assumed. MEASURED in the ML repo: WebGPU ran 177.9 ms per frame walking the graph node by node, which
/// is 5.6x too slow to follow a microphone, and 7.81 ms replaying a captured plan. Those are different
/// enough to decide whether this design works at all, so <see cref="MeanFrameMs"/> is surfaced through the
/// API and the UI gate prints it rather than anybody quoting a number from memory.
/// </para>
/// </remarks>
public sealed class AiVadEngine : IDisposable
{
    private readonly HttpClient _http;
    private readonly Accelerator _accelerator;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private SileroVad? _vad;
    private VoiceActivityDetector? _detector;
    private readonly List<AiSpeechSpan> _pending = new();

    private long _frames;
    private double _frameMsTotal;

    /// <summary>New instance.</summary>
    /// <param name="http">App-origin HttpClient; the model is a static asset, see the PROVENANCE note.</param>
    /// <param name="accelerator">The shared accelerator.</param>
    public AiVadEngine(HttpClient http, Accelerator accelerator)
    {
        _http = http;
        _accelerator = accelerator;
    }

    /// <summary>Where the model is served from, relative to the app base.</summary>
    public string ModelUrl { get; set; } = "references/vad/silero_vad.onnx";

    /// <summary>Friendly name reported back to callers.</summary>
    public string ModelName { get; set; } = "silero-vad";

    /// <summary>Endpointing behaviour. Changing this takes effect on the next <see cref="ResetStream"/>.</summary>
    public VadOptions Options { get; set; } = new();

    /// <summary>Called before this engine takes GPU memory, so the host can make room.</summary>
    public Func<Task>? EvictOtherKind { get; set; }

    /// <summary>Whether the model is resident.</summary>
    public bool IsLoaded => _vad != null;

    /// <summary>Mean wall time per 512-sample frame since load. The realtime budget is 32 ms.</summary>
    public double MeanFrameMs => _frames > 0 ? _frameMsTotal / _frames : 0;

    /// <summary>Speech probability of the most recent frame.</summary>
    public float LastProbability => _detector?.LastProbability ?? 0f;

    /// <summary>
    /// Feed the next slice of the microphone stream and learn what the endpointer made of it.
    /// </summary>
    /// <param name="samples">Mono PCM at 16 kHz, continuing the stream fed so far.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<AiVadUpdate> AcceptAsync(float[] samples, CancellationToken ct = default)
    {
        if (samples == null) throw new ArgumentNullException(nameof(samples));
        await EnsureLoadedAsync(ct).ConfigureAwait(false);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _pending.Clear();
            long framesBefore = _detector!.SamplesProcessed / SileroVad.WindowSize;
            var started = DateTime.UtcNow;
            await _detector.AcceptWaveformAsync(samples).ConfigureAwait(false);
            var elapsed = (DateTime.UtcNow - started).TotalMilliseconds;

            long framesRun = _detector.SamplesProcessed / SileroVad.WindowSize - framesBefore;
            if (framesRun > 0) { _frames += framesRun; _frameMsTotal += elapsed; }

            return new AiVadUpdate(_detector.IsSpeechActive, _detector.LastProbability,
                _pending.ToArray(), framesRun > 0 ? elapsed / framesRun : 0);
        }
        finally { _gate.Release(); }
    }

    /// <summary>
    /// Close out speech still in progress - the caller stopped the microphone mid-word.
    /// </summary>
    /// <remarks>
    /// The detector only emits an utterance once it has seen enough trailing silence, so without this the
    /// last thing said before the mic closed is never handed over at all.
    /// </remarks>
    public async Task<AiVadUpdate> FlushAsync(CancellationToken ct = default)
    {
        if (_detector == null) return new AiVadUpdate(false, 0f, Array.Empty<AiSpeechSpan>(), 0);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _pending.Clear();
            await _detector.FlushAsync().ConfigureAwait(false);
            return new AiVadUpdate(false, _detector.LastProbability, _pending.ToArray(), 0);
        }
        finally { _gate.Release(); }
    }

    /// <summary>
    /// Load the model AND run frames through it, so the first frame of a real turn waits for neither.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ MEASURED, and the reason this is a separate call rather than lazy loading: with the load happening
    /// on the first audio frame, a 4.0 s utterance took <b>17.6 s</b> to endpoint. The microphone is already
    /// open while the model downloads, so audio piles up in the queue and the detector spends the rest of
    /// the turn catching up - which looks precisely like the fixed timer this replaced.
    /// </para>
    /// <para>
    /// ⚠️ AND THEN LOADING ALONE WAS STILL NOT ENOUGH. With the download moved out of the turn, the first
    /// turn still took 16.2 s and <see cref="MeanFrameMs"/> read <b>11,073 ms</b> against a 32 ms budget:
    /// every kernel in this graph compiles on its FIRST EXECUTION, so a warm that only fetched weights left
    /// the entire compile inside the first frame of real speech. "The bytes are in memory" is not "ready to
    /// run". Frames have to actually go through it.
    /// </para>
    /// <para>
    /// ⚠️ <see cref="SessionGraphCapture"/> needs several passes before it can record and replay a plan
    /// (the class doc in SpawnDev.ILGPU.ML puts the WebGPU win at 177.9 -> 7.81 ms per frame), so warming
    /// pushes a short run of frames through rather than one. The detector's state and the frame-time
    /// average are both reset afterwards, so a warm frame can neither open a phantom utterance nor be
    /// mistaken for steady-state performance in the number the UI prints.
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
            // Enough frames to get past capture's warm/probe/record passes, and cheap: silence.
            const int warmFrames = 12;
            var silence = new float[SileroVad.WindowSize * warmFrames];
            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                _pending.Clear();
                await _detector!.AcceptWaveformAsync(silence).ConfigureAwait(false);
                _detector.Reset();
                _pending.Clear();
                _frames = 0;
                _frameMsTotal = 0;
            }
            finally { _gate.Release(); }

            Console.WriteLine($"[AiVadEngine] warm: {warmFrames} frames in {clock.Elapsed.TotalSeconds:F1}s "
                            + "(kernel compilation + capture). Steady-state frames must run under 32 ms to "
                            + "keep up with a microphone.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AiVadEngine] warm failed ({ex.GetType().Name}: {ex.Message}); the first "
                            + "real frame will pay for compilation instead.");
        }
    }

    private bool _warmed;

    /// <summary>
    /// Start a new stream: clears the recurrent state and puts the sample clock back to zero.
    /// </summary>
    /// <remarks>
    /// ⚠️ Callers MUST do this when they reopen the microphone, because the spans this returns are offsets
    /// into the caller's buffer and that buffer starts again. Carrying the old clock forward hands back
    /// offsets that point past the end of a fresh recording.
    /// </remarks>
    public async Task ResetStreamAsync(CancellationToken ct = default)
    {
        if (_detector == null) return;
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try { _detector.Reset(); }
        finally { _gate.Release(); }
    }

    private async Task EnsureLoadedAsync(CancellationToken ct)
    {
        if (_vad != null) return;
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_vad != null) return;
            if (EvictOtherKind != null) await EvictOtherKind().ConfigureAwait(false);

            var bytes = await _http.GetByteArrayAsync(ModelUrl, ct).ConfigureAwait(false);
            if (bytes.Length == 0)
                throw new InvalidOperationException(
                    $"{ModelUrl} came back empty - the endpointer cannot run and the hands-free loop would "
                    + "fall back to a fixed timer, which is the defect this replaces");

            _vad = SileroVad.Create(_accelerator, bytes);
            var detector = new VoiceActivityDetector(_vad, Options);
            // The detector raises segments DURING AcceptWaveformAsync; collect them for that call to return.
            detector.OnSegment += seg => _pending.Add(new AiSpeechSpan(seg.StartSample, seg.Samples.Length));
            _detector = detector;
            _frames = 0;
            _frameMsTotal = 0;
        }
        finally { _gate.Release(); }
    }

    /// <summary>Release the model.</summary>
    public Task EvictAsync()
    {
        _detector?.Dispose();
        _detector = null;
        _vad?.Dispose();
        _vad = null;
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _detector?.Dispose();
        _vad?.Dispose();
        _detector = null;
        _vad = null;
    }
}
