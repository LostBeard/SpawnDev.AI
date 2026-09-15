using System.Diagnostics;
using SpawnDev.AI.Demo.Pages;
using SpawnDev.AI.Server;

namespace SpawnDev.AI.Demo.Tests;

/// <summary>
/// A spoken reply, chunked and rendered exactly the way the demo does it - does it play without a gap?
/// </summary>
/// <remarks>
/// <para>
/// 🔴 PER-CHUNK RTF CANNOT ANSWER THIS, AND IT IS THE ONLY THING ANY OTHER VOICE TEST MEASURES. The demo
/// splits a reply into chunks and plays each as it lands, so what a person hears is a QUEUE: a chunk that
/// renders slower than realtime is fine when the chunks before it banked a lead, and a chunk that renders
/// faster than realtime is NOT fine if it is first and there is nothing behind it. A per-chunk threshold
/// either fails on a chunk that is perfectly fine in context or passes a reply that audibly stutters.
/// </para>
/// <para>
/// ⭐ SO THIS MODELS THE QUEUE. Playback starts when chunk 0 is rendered and then runs in realtime; chunk
/// i has to be finished before playback arrives at it:
/// <code>
///   renderDoneBy[i]  &lt;=  renderDoneBy[0] + (a[0] + ... + a[i-1])
/// </code>
/// A violation is an audible gap. TJ, 2026-09-15: "tts in the Ai demo needs to be realtime so that there
/// are not pauses between streaming chunks and so that the entire response is read. that needs to be
/// verified."
/// </para>
/// <para>
/// ⭐⭐ IT CALLS <see cref="Home.SpeakableChunks"/> - the demo's OWN chunker - rather than restating its
/// policy. That policy is two constants (160 characters for the first chunk, 320 for the merged rest) and
/// a copy of them here would drift silently; when somebody retunes the chunker, this gate has to move with
/// it or it is measuring a product that no longer exists. The engine-side gate in SpawnDev.ILGPU.ML
/// (Kokoro_StreamingReply_PlaysWithoutAnUnderrun) had to hard-code ~180/~360 tokens BECAUSE it cannot
/// reference this assembly; this one can, so it does.
/// </para>
/// <para>
/// ⚠️ It renders through <c>_client</c>, the same worker client the UI uses, so the worker transport and
/// everything else resident in that worker are in the measurement. A page-level engine number is not this
/// number - MEASURED 2026-09-14, the same utterance was 4,493 ms in this worker against 1,837 ms in PMT's
/// page.
/// </para>
/// <para>
/// ⚠️ It does NOT play audio or assert intelligibility - BuiltInVoiceSpeaksIntelligiblyAndFasterThanRealtime
/// covers the words. This is the schedule alone.
/// </para>
/// </remarks>
public sealed class AiVoiceStreamingTests
{
    private readonly AiWorkerClient _client;

    // Injected, like AiVoiceTests - AiWorkerClient is a DI service and the suite hands it the same
    // instance the UI uses. Newing one up here would be a second client against the same worker.
    public AiVoiceStreamingTests(AiWorkerClient client) => _client = client;

    /// <summary>~900 characters of ordinary prose - several chunks through the demo's own splitter.</summary>
    private const string Reply =
        "Sure. Here is the longer answer you asked for, read out loud from start to finish. "
      + "A speech model in a browser has to produce audio faster than the audio lasts, or the person "
      + "listening hears the machine thinking between sentences. That sounds like a stutter rather than a "
      + "pause for breath, and it is the single thing people notice first about a synthetic voice. "
      + "The interesting part is that the cost of one pass barely depends on how long the sentence is, "
      + "because almost all of it is preparing work for the graphics card rather than doing it. "
      + "So a reply split into many small pieces pays that fixed cost many times over, while the audio "
      + "only adds up in proportion to the words. Splitting less often is therefore faster overall, "
      + "even though each individual piece takes longer to appear. "
      + "The only thing that genuinely has to be quick is the very first piece, because that is the one "
      + "the listener is waiting through in silence.";

    [AiTest(Heavy = true, Timeout = 1_800_000)]
    public async Task ChunkedReplyStreamsWithoutAPause()
    {
        await _client.InitAsync();

        var voiceId = AiVoiceEngine.DefaultKokoroVoiceId;
        var voices = await _client.GetVoicesAsync();
        if (!voices.Contains(voiceId))
            throw new Exception($"the default built-in voice '{voiceId}' is not listed by the engine; "
                + $"got [{string.Join(", ", voices)}]");

        // The demo's own chunker, not a restatement of it.
        var chunks = Home.SpeakableChunks(Reply);
        if (chunks.Count < 2)
            throw new Exception($"the fixture produced {chunks.Count} chunk(s), so there is no chunk-to-chunk "
                + "seam to test. Either the reply got shorter or the chunk target grew - lengthen the fixture, "
                + "do not lower the bar.");

        // ⚠️ One throwaway utterance FIRST so the measured reply does not also pay one-time kernel
        // compilation and pool growth. A real session pays that at load, before anybody speaks - charging
        // it to the first chunk would describe the first reply of a session as though it were every reply.
        await _client.SpeakInVoiceAsync("Ready.", voiceId);

        var renderMs = new double[chunks.Count];
        var audioSec = new double[chunks.Count];

        for (int i = 0; i < chunks.Count; i++)
        {
            var sw = Stopwatch.StartNew();
            var (samples, rate, _, _, spoken) = await _client.SpeakInVoiceAsync(chunks[i], voiceId);
            sw.Stop();

            if (samples == null || samples.Length == 0)
                throw new Exception($"chunk {i + 1} of {chunks.Count} produced NO audio, so the reply would be "
                    + "silently truncated - \"the entire response is read\" fails here regardless of timing. "
                    + $"Text was: \"{chunks[i]}\"");
            float peak = 0f;
            foreach (var v in samples) peak = MathF.Max(peak, MathF.Abs(v));
            if (peak < 0.01f)
                throw new Exception($"chunk {i + 1} of {chunks.Count} is effectively SILENCE (peak {peak:F5}) - "
                    + "a graph that ran and produced nothing audible");

            renderMs[i] = sw.Elapsed.TotalMilliseconds;
            audioSec[i] = samples.Length / (double)rate;
            _ = spoken;
        }

        // ── The queue ───────────────────────────────────────────────────────────────────────────────
        double firstReadyMs = renderMs[0];
        double cumRenderMs = 0, cumAudioMsBefore = 0, totalAudioSec = 0;
        double worstMarginMs = double.MaxValue;
        int worstChunk = -1;
        var late = new List<string>();

        for (int i = 0; i < chunks.Count; i++)
        {
            cumRenderMs += renderMs[i];
            double neededAtMs = firstReadyMs + cumAudioMsBefore;
            double marginMs = neededAtMs - cumRenderMs;

            Console.WriteLine($"[Benchmark] VoiceStreaming chunk {i + 1}/{chunks.Count}: "
                + $"{chunks[i].Length,4} chars -> {audioSec[i]:F2}s audio in {renderMs[i]:F0} ms "
                + $"(RTF {renderMs[i] / 1000.0 / Math.Max(audioSec[i], 1e-6):F2}x) | ready {cumRenderMs:F0} ms, "
                + $"needed {neededAtMs:F0} ms, margin {marginMs:F0} ms");

            if (marginMs < worstMarginMs) { worstMarginMs = marginMs; worstChunk = i; }
            if (marginMs < 0) late.Add($"chunk {i + 1} was {-marginMs:F0} ms late");

            cumAudioMsBefore += audioSec[i] * 1000.0;
            totalAudioSec += audioSec[i];
        }

        double replyRtf = (cumRenderMs / 1000.0) / Math.Max(totalAudioSec, 1e-6);
        Console.WriteLine($"[Benchmark] VoiceStreaming: {chunks.Count} chunks, {totalAudioSec:F2}s of audio in "
            + $"{cumRenderMs:F0} ms (whole-reply RTF {replyRtf:F2}x) | tightest margin {worstMarginMs:F0} ms at "
            + $"chunk {worstChunk + 1} | time to first audio {firstReadyMs:F0} ms");

        if (late.Count > 0)
            throw new Exception(
                $"the reply STALLS - {late.Count} of {chunks.Count} chunks arrive after playback needs them "
              + $"({string.Join("; ", late)}). Whole-reply RTF {replyRtf:F2}x, tightest margin "
              + $"{worstMarginMs:F0} ms at chunk {worstChunk + 1} ({audioSec[worstChunk]:F2}s of audio for "
              + $"{renderMs[worstChunk]:F0} ms of work). A pass's host cost is roughly fixed regardless of "
              + "length, so the lever is the CHUNKER (Home.SpeakChunkCharacters / "
              + "SpeakChunkCharactersAfterFirst) before it is the renderer.");

        if (replyRtf >= 1.0)
            throw new Exception(
                $"the whole reply renders at {replyRtf:F2}x realtime ({cumRenderMs:F0} ms for "
              + $"{totalAudioSec:F2}s of audio). It happens not to stall only because the chunk sizes hide "
              + "it; a longer reply at this rate cannot keep up.");
    }
}
