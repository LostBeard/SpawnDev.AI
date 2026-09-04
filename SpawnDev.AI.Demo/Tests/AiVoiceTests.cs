using System.Linq;
using System.Diagnostics;
using SpawnDev.AI.Server;

namespace SpawnDev.AI.Demo.Tests;

/// <summary>
/// Voice OUT, and the hands-free loop end to end: hear a turn, answer it, speak the answer back.
/// </summary>
/// <remarks>
/// <para>
/// The transcription tests next door prove the demo can LISTEN. These prove it can TALK, and then that the
/// two halves actually connect - which is a separate claim, because a VAD segment has to be something the
/// recogniser can transcribe and the recogniser's text has to be something the voice can pronounce.
/// </para>
/// <para>
/// ⚠️ These run through <c>AiWorkerClient</c>, the same client the UI uses, so the worker transport, the
/// engines and the GPU residency policy are all real. A test that called the engine directly would prove
/// the model works and say nothing about the demo.
/// </para>
/// </remarks>
public sealed class AiVoiceTests
{
    private readonly AiWorkerClient _client;
    private readonly HttpClient _http;

    /// <summary>What the fixture actually says, verbatim.</summary>
    private const string KnownTranscript = "All LibriVox recordings are in the public domain.";

    private const string FixtureUrl = "test-audio/librivox-public-domain.wav";

    /// <summary>The chat model, matching AiChatTests and the demo's default.</summary>
    /// <remarks>
    /// ⚠️ Must be a model the demo actually serves. A hardcoded guess fails with "model not found" AFTER
    /// paying for a full transcription, which reads as a speech failure and is not one.
    /// </remarks>
    private const string Model = "qwen2.5:0.5b-instruct-q8_0";

    /// <summary>New instance.</summary>
    public AiVoiceTests(AiWorkerClient client, HttpClient http)
    {
        _client = client;
        _http = http;
    }

    /// <summary>
    /// Speaking a line returns audible speech in the reference voice.
    /// </summary>
    /// <remarks>
    /// ⚠️ Asserts AMPLITUDE, not just length. A pipeline that returns zeros has the right sample count and
    /// the right type and passes every other check - silence is the failure mode that looks exactly like
    /// success. The duration bound catches the opposite failure: a model whose duration prediction has gone
    /// wrong emits a plausible-sounding fraction of a second, or forty seconds, for one short sentence.
    /// </remarks>
    [AiTest(Heavy = true, Timeout = 1_800_000)]
    public async Task SpeaksInTheReferenceVoice()
    {
        await _client.InitAsync();
        var (reference, referenceRate) = await LoadFixtureAsync();

        const string line = "Hello. This is SpawnDev AI, speaking with your own voice.";
        var sw = Stopwatch.StartNew();
        var (samples, rate, model, ms) = await _client.SpeakAsync(line, KnownTranscript, reference, referenceRate);
        sw.Stop();

        if (samples == null || samples.Length == 0)
            throw new Exception("speak returned NO audio");

        float peak = 0f;
        double energy = 0;
        foreach (var v in samples) { peak = MathF.Max(peak, MathF.Abs(v)); energy += (double)v * v; }
        var rms = Math.Sqrt(energy / samples.Length);
        var seconds = samples.Length / (double)rate;

        Console.WriteLine($"[AiVoiceTests] {model}: {seconds:F2}s @ {rate}Hz in {ms:F0}ms "
                        + $"(wall {sw.Elapsed.TotalSeconds:F1}s), peak {peak:F3} rms {rms:F4}");

        if (peak < 0.01f || rms < 0.005)
            throw new Exception($"the reply is effectively SILENCE (peak {peak:F5}, rms {rms:F5}) - zeros "
                              + "have the right length and type and pass every check except this one");
        if (seconds < 0.5 || seconds > 30.0)
            throw new Exception($"{seconds:F2}s for a {line.Length}-character line - the duration prediction "
                              + "inside the encoder decides this, so a wild value means the encoder ran "
                              + "wrong rather than the vocoder");
    }

    /// <summary>
    /// The speech we synthesise must be INTELLIGIBLE, established by reading it back.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 WHY THIS EXISTS. <see cref="SpeaksInTheReferenceVoice"/> asserts amplitude and duration, and
    /// those are exactly the two properties that bad speech KEEPS. MEASURED 2026-09-04: the Captain
    /// listened to a spoken reply and reported "I could understand a few words but it was really odd
    /// sounding with high pitch weird noises... all over the place in pitch and variable". That audio had
    /// a healthy peak, a healthy RMS and a duration of 12.31 s for 220 characters - 17.9 chars/sec, dead
    /// centre of natural speech - so every existing assertion passed on it. A gate that cannot fail on the
    /// defect the product actually has is not a gate.
    /// </para>
    /// <para>
    /// THE ORACLE IS ALREADY IN THE PRODUCT. Whisper is right here, it is independent of ZipVoice, and it
    /// answers the only question that matters about synthesised speech: can a listener make out the words?
    /// Speak a known line, transcribe the RESULT, and compare. Warble, wandering pitch and vocoder noise
    /// all destroy the read-back; a clean voice survives it. This is a round trip through two real models,
    /// no fixture of expected samples to go stale, and it fails loudly on the exact complaint.
    /// </para>
    /// <para>
    /// ⚠️ The floor is word OVERLAP, not equality. Whisper is allowed to mishear a word, punctuate
    /// differently, or drop a filler - <c>drive-chat-voice.cs</c> uses a 70% overlap floor for the
    /// microphone path for the same reason. Speech a person could follow clears this comfortably;
    /// the audio described above does not come close.
    /// </para>
    /// </remarks>
    [AiTest(Heavy = true, Timeout = 1_800_000)]
    public async Task SpokenReplyIsIntelligibleWhenReadBack()
    {
        await _client.InitAsync();
        var (reference, referenceRate) = await LoadFixtureAsync();

        // Ordinary words, no proper nouns and no jargon: anything Whisper gets wrong here is the VOICE's
        // fault, not the recogniser reaching for an unusual spelling.
        //
        // ⚠️ THREE LENGTHS, MEASURED TOGETHER, because length is a live suspect and one line cannot tell
        // a broken voice from a length-dependent one. The first is the exact line ILGPU.ML's own
        // Pipeline_ZipVoice_SpeaksInTheBrowser speaks, so a failure THERE indicts synthesis outright; the
        // demo's real replies are capped at 320 characters, so the long one is the product's actual worst
        // case, not a stress test. ZipVoice already has a documented, uninvestigated shape dependence
        // (2026-09-04: one utterance rendering 2-3x faster than a shorter one on BOTH CUDA and OpenCL) and
        // a history of long-utterance defects - a Slice under an If that collapsed to empty past ~21 s.
        // Reporting the whole curve costs one extra synthesis and turns "the voice is bad" into a shape.
        // MEASURED 2026-09-04 on WebGPU: 40 chars and 62 chars both read back cleanly, 123 chars came
        // back "[INAUDIBLE]" at 0% (0/22). So this is not "the voice is bad" - synthesis FALLS OFF A
        // CLIFF with length, and the demo's own MaxSpokenCharacters is 320, which puts every real spoken
        // reply past the edge. These steps bracket the knee; the growth is one clause at a time so the
        // words and the voice stay ordinary and only LENGTH varies.
        string[] lines =
        [
            "Paint the sockets in the wall dull green.",
            "The morning train was late again, so we walked along the river.",
            "The morning train was late again, so we walked along the river and talked a while.",
            "The morning train was late again, so we walked along the river and talked about the weather.",
            "The morning train was late again, so we walked along the river and talked "
                + "about the weather until the rain finally stopped.",
        ];

        // The WHOLE curve travels in the failure message, not just the failing rows. The test runner
        // surfaces the exception, not the page console, so a summary that lists only failures throws away
        // exactly the comparison that makes the number mean something.
        var curve = new List<string>();
        var failures = new List<string>();
        foreach (var line in lines)
        {
            var sw = Stopwatch.StartNew();
            var (samples, rate, model, ms) = await _client.SpeakAsync(line, KnownTranscript, reference, referenceRate);
            sw.Stop();
            if (samples == null || samples.Length == 0)
                throw new Exception($"speak returned NO audio for a {line.Length}-character line");

            // Straight back into Whisper. The pipeline resamples whatever rate it is handed to 16 kHz, so
            // ZipVoice's 24 kHz output needs no conversion here - and converting it by hand would put OUR
            // resampler inside the measurement of THEIR voice.
            var (heard, _, transcribeMs) = await _client.TranscribeAsync(samples, rate);
            heard = (heard ?? "").Trim();

            var spokenWords = Words(line);
            var matched = new List<string>(Words(heard));
            int hits = 0;
            foreach (var w in spokenWords) if (matched.Remove(w)) hits++;
            var overlap = spokenWords.Count == 0 ? 0.0 : hits / (double)spokenWords.Count;
            var seconds = samples.Length / (double)rate;

            Console.WriteLine($"[AiVoiceTests] read-back {model}: {line.Length} chars -> {seconds:F2}s @ {rate}Hz "
                            + $"({line.Length / seconds:F1} chars/sec), spoke {ms:F0}ms, transcribed {transcribeMs:F0}ms");
            Console.WriteLine($"[AiVoiceTests]   said : {line}");
            Console.WriteLine($"[AiVoiceTests]   heard: {heard}");
            Console.WriteLine($"[AiVoiceTests]   word overlap {overlap:P0} ({hits}/{spokenWords.Count})");

            curve.Add($"{line.Length,4} chars, {seconds,5:F2}s: {overlap,4:P0} ({hits}/{spokenWords.Count})"
                    + $" heard \"{heard}\"");
            if (overlap < 0.60)
                failures.Add($"{line.Length} chars: {overlap:P0} ({hits}/{spokenWords.Count})");
        }

        Console.WriteLine("[AiVoiceTests] intelligibility vs length:"
                        + string.Concat(curve.Select(c => "\n  " + c)));

        if (failures.Count > 0)
            throw new Exception(
                $"the synthesised speech is not intelligible in {failures.Count} of {lines.Length} lengths "
                + $"({string.Join("; ", failures)}). FULL CURVE:"
                + string.Concat(curve.Select(c => "\n  * " + c))
                + "\nAmplitude and duration can both be perfect while the voice is unusable - that is "
                + "precisely what this test is here to catch.");
    }

    /// <summary>Lower-case alphabetic words, for comparing a spoken line with what came back.</summary>
    private static List<string> Words(string s)
    {
        var outp = new List<string>();
        var cur = new System.Text.StringBuilder();
        foreach (var ch in s)
        {
            if (char.IsLetterOrDigit(ch)) cur.Append(char.ToLowerInvariant(ch));
            else if (cur.Length > 0) { outp.Add(cur.ToString()); cur.Clear(); }
        }
        if (cur.Length > 0) outp.Add(cur.ToString());
        return outp;
    }

    /// <summary>
    /// A reply LONGER than ZipVoice's precomputed positional table still synthesises correctly.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ THE FIXTURE MUST CROSS THE BOUNDARY OR IT PROVES NOTHING. Past roughly 21 s of speech the decoder
    /// takes a DIFFERENT If branch - one that recomputes the relative-position table instead of reading the
    /// precomputed [1999, 48] constant - and until 2026-09-01 that branch read a buffer nobody had written:
    /// a Slice under the If was resolved at COMPILE time from the branch the compiler could see, its window
    /// collapsed to empty, and a zero-element output SKIPS its operator entirely. A pooled buffer holds the
    /// previous tensor's plausible values, so the failure was audible-but-wrong speech rather than an error.
    /// </para>
    /// <para>
    /// This is why the line below is long and why the cap is raised for this call: the demo's default
    /// <c>MaxSpokenCharacters</c> of 320 exists to keep spoken replies brief, and it also meant the product
    /// never reached the branch. A short fixture passes against the broken engine.
    /// </para>
    /// <para>
    /// Asserts amplitude and a duration FLOOR - the point is that the long path produced a long, audible
    /// utterance, not a truncated one that merely avoided crashing.
    /// </para>
    /// </remarks>
    [AiTest(Heavy = true, Timeout = 1_800_000)]
    public async Task SpeaksALongReplyPastThePositionalTable()
    {
        await _client.InitAsync();
        var (reference, referenceRate) = await LoadFixtureAsync();

        // ~700 characters: comfortably past the ~21 s branch boundary, and past the 320-char default cap.
        const string line =
            "Here is a longer answer, read aloud in full. The engine that produces this speech builds a "
          + "relative position table for every utterance it generates. For short lines it reads a table "
          + "that was computed ahead of time and stored inside the model. For longer lines that stored "
          + "table is too small, so the model takes a different path and rebuilds the table from scratch "
          + "while it is running. That second path is the one this sentence is here to exercise, because "
          + "for a long time it quietly produced the wrong numbers and nobody could hear the difference "
          + "until the speech had already been played out loud to somebody.";

        var sw = Stopwatch.StartNew();
        var (samples, rate, model, ms) = await _client.SpeakAsync(
            line, KnownTranscript, reference, referenceRate, maxSpokenCharacters: 4000);
        sw.Stop();

        if (samples == null || samples.Length == 0)
            throw new Exception("speak returned NO audio for the long line");

        float peak = 0f;
        double energy = 0;
        foreach (var v in samples) { peak = MathF.Max(peak, MathF.Abs(v)); energy += (double)v * v; }
        var rms = Math.Sqrt(energy / samples.Length);
        var seconds = samples.Length / (double)rate;

        Console.WriteLine($"[AiVoiceTests] LONG {model}: {seconds:F2}s @ {rate}Hz for {line.Length} chars "
                        + $"in {ms:F0}ms (wall {sw.Elapsed.TotalSeconds:F1}s), peak {peak:F3} rms {rms:F4}");

        if (peak < 0.01f || rms < 0.005)
            throw new Exception($"the long reply is effectively SILENCE (peak {peak:F5}, rms {rms:F5})");
        if (seconds < 21.0)
            throw new Exception($"{seconds:F2}s for {line.Length} characters - too SHORT to have crossed the "
                              + "recomputed-table boundary, so this run did not exercise the long path at "
                              + "all. Check that the cap override reached the engine.");
    }

    /// <summary>
    /// The whole hands-free turn: hear it, answer it, speak the answer in the voice that asked.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>THIS IS NOT A TEST OF THE HANDS-FREE BUTTON, and this remark used to claim it was.</b> It
    /// drives three server APIs in sequence on a fixture. It opens no microphone, does no endpointing, and
    /// plays no audio - so it stayed green while the button a person clicks recorded a fixed 30 s of room
    /// tone regardless of what was said, spent ~45 s transcribing it, and produced no sound. Every one of
    /// those three defects lived in a part of the loop this file cannot reach. The UI gate is
    /// <c>tools/drive-hands-free.cs</c>; if you are changing the hands-free loop, that is the one that has
    /// to go green, and this one cannot substitute for it.
    /// </para>
    /// <para>
    /// What it DOES prove, and it is worth keeping: the stages compose. Each stage passing alone says
    /// nothing about whether a transcript is something the tokenizer can speak, or whether three model
    /// kinds can be used in one turn without evicting each other. That last one is why a symmetric
    /// eviction ring was replaced by a budget - with a ring this turn re-uploads every model, three times,
    /// and no conversation is possible at any inference speed.
    /// </para>
    /// <para>
    /// ⚠️ Asserts on transcript CONTENT before answering. A recogniser handed a bad segment returns
    /// confident, fluent, WRONG words - and the assistant would answer them, out loud, in the user's voice.
    /// </para>
    /// </remarks>
    [AiTest(Heavy = true, Timeout = 2_700_000)]
    public async Task HandsFreeTurn_HearsAnswersAndSpeaksBack()
    {
        await _client.InitAsync();
        var (heard, heardRate) = await LoadFixtureAsync();
        var total = Stopwatch.StartNew();

        // 1. LISTEN
        var sw = Stopwatch.StartNew();
        var (heardText, _, asrMs) = await _client.TranscribeAsync(heard, heardRate);
        heardText = (heardText ?? "").Trim();
        Console.WriteLine($"[AiVoiceTests] heard \"{heardText}\" in {asrMs:F0}ms");

        if (string.IsNullOrWhiteSpace(heardText))
            throw new Exception("the turn transcribed to nothing - the loop has nothing to answer");
        var words = heardText.ToLowerInvariant();
        foreach (var required in new[] { "recordings", "public", "domain" })
            if (!words.Contains(required))
                throw new Exception($"the transcript is missing \"{required}\": \"{heardText}\". The "
                                  + "assistant would answer the wrong question, out loud, in the user's voice.");

        // 2. ANSWER
        sw.Restart();
        // ⚠️ ChatStreamAsync returns the STOP REASON, not the reply - the text arrives through onDelta.
        // Using its return value as the answer would hand the voice the word "stop" to say out loud.
        var replyBuilder = new System.Text.StringBuilder();
        await _client.ChatStreamAsync(
            Model,
            new[] { new AiChatMessage("user", heardText) },
            new AiGenerationOptions { MaxOutputTokens = 48 },
            delta => replyBuilder.Append(delta));
        var chatMs = sw.Elapsed.TotalMilliseconds;
        var reply = replyBuilder.ToString().Trim();
        Console.WriteLine($"[AiVoiceTests] answered in {chatMs:F0}ms: \"{reply}\"");
        if (string.IsNullOrWhiteSpace(reply))
            throw new Exception("the model produced no reply to speak");

        // 3. SPEAK IT BACK, in the voice that asked
        sw.Restart();
        (float[] samples, int rate, _, double ttsMs) = await _client.SpeakAsync(reply, heardText, heard, heardRate);
        if (samples == null || samples.Length == 0)
            throw new Exception("the loop produced no reply audio");

        float peak = 0f;
        foreach (var v in samples) peak = MathF.Max(peak, MathF.Abs(v));
        if (peak < 0.01f)
            throw new Exception($"the spoken reply is silence (peak {peak:F5})");

        var turnSeconds = heard.Length / (double)heardRate;
        var loopMs = asrMs + chatMs + ttsMs;
        Console.WriteLine($"[AiVoiceTests] HANDS-FREE TURN: asr {asrMs:F0} + chat {chatMs:F0} + tts {ttsMs:F0} "
                        + $"= {loopMs:F0}ms to answer a {turnSeconds:F2}s turn with "
                        + $"{samples.Length / (double)rate:F2}s of speech "
                        + $"({loopMs / 1000 / turnSeconds:F2}x the turn; under 1.0 keeps up with a talker) "
                        + $"| wall {total.Elapsed.TotalSeconds:F1}s");
    }

    private async Task<(float[] Samples, int SampleRate)> LoadFixtureAsync()
    {
        var bytes = await _http.GetByteArrayAsync(FixtureUrl);
        var (samples, rate) = WavFixture.Decode(bytes);
        if (samples.Length == 0) throw new Exception($"{FixtureUrl} decoded to zero samples");
        return (samples, rate);
    }
}
