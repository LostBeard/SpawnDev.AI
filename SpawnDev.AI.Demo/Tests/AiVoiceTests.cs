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

    /// <summary>Fixed flow-matching noise draw, so this gate is repeatable across runs and backends.</summary>
    private const int NoiseSeed = 12345;

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
        var (samples, rate, model, ms, _) = await _client.SpeakAsync(line, KnownTranscript, reference, referenceRate);
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
    /// <summary>
    /// The brevity limit shortens a reply at a SENTENCE end, and never stops mid-sentence.
    /// </summary>
    /// <remarks>
    /// 🔴 CAPTAIN, 2026-09-08: "Cutting replies mid-sentence needs to be fixed. There should be a limit, at
    /// least not due to something that we can fix." A limit is a product decision and stays; where it CUTS
    /// is the defect. Until this, a reply with no sentence end inside the target was chopped at the
    /// character count - MEASURED on this file's own 343-character fixture, it spoke 320 and stopped after
    /// "tired and content after".
    ///
    /// ⚠️ Pure and model-free on purpose, so it runs in milliseconds on every backend and cannot be
    /// mistaken for a synthesis problem. The read-back gate below can only observe the CONSEQUENCE of a bad
    /// cut, and it scored one as 92% intelligibility rather than as a truncation.
    /// </remarks>
    [AiTest(Timeout = 30_000)]
    public Task BrevityLimitCutsOnlyAtSentenceEnds()
    {
        const int Cap = 320, Ceiling = 1200;
        var failures = new List<string>();
        void Check(string name, string input, Func<string, string, bool> ok, string expectation)
        {
            var got = AiVoiceEngine.TrimToSpeakableLength(input, Cap, Ceiling, out var why);
            var pass = ok(got, why);
            Console.WriteLine($"[AiVoiceTests] {(pass ? "ok  " : "FAIL")} {name}: {input.Length} -> "
                            + $"{got.Length} chars ({why}) ...{(got.Length > 46 ? got[^46..] : got)}");
            if (!pass) failures.Add($"{name}: expected {expectation}; got {got.Length} chars ending "
                                  + $"\"{(got.Length > 60 ? got[^60..] : got)}\" ({why})");
        }

        // The exact line this defect was found on: ONE sentence, 343 characters, no interior full stop.
        const string OneLongSentence =
            "The morning train was late again, so we walked along the river and talked about the weather "
          + "until the rain finally stopped and the sun came out over the water, warming the stones along "
          + "the path where we sat and rested for a while before walking slowly back home together in the "
          + "quiet evening air, tired and content after a long and useful day.";

        Check("under the target is untouched", "Short enough.", (g, _) => g == "Short enough.",
              "the input unchanged");
        Check("one long sentence is spoken WHOLE", OneLongSentence,
              (g, _) => g == OneLongSentence,
              $"all {OneLongSentence.Length} characters, not {Cap}");

        // Many sentences: it must stop ON a terminator, and inside the target.
        var many = string.Concat(Enumerable.Repeat("This is one ordinary sentence of moderate length. ", 12));
        Check("many sentences stop on a terminator", many,
              (g, _) => g.Length <= Cap && (g.EndsWith('.') || g.EndsWith('!') || g.EndsWith('?')),
              $"<= {Cap} chars ending on a terminator");

        // A tiny first sentence must not become the whole spoken reply.
        Check("a tiny first sentence is not the whole reply", "Sure. " + OneLongSentence,
              (g, _) => g.Length > Cap / 3, "more than a one-word answer");

        // The ONLY case allowed to stop mid-sentence, and it must say so.
        var runOn = "The " + string.Concat(Enumerable.Repeat("and then something else happened ", 60)) + "end.";
        Check("a run-on past the ceiling is cut at a word boundary", runOn,
              (g, w) => g.Length <= Ceiling && !g.EndsWith(' ') && w.Contains("ceiling"),
              $"<= {Ceiling} chars, a reason naming the ceiling");

        // ⚠️ THE PROPERTY, over every case above: whatever comes back is a PREFIX of the input that ends at
        // a word boundary. A cut inside a word is heard as a mispronunciation, which is the one thing a
        // brevity limit must never manufacture.
        foreach (var input in new[] { OneLongSentence, many, "Sure. " + OneLongSentence, runOn })
        {
            var got = AiVoiceEngine.TrimToSpeakableLength(input, Cap, Ceiling, out _);
            if (!input.StartsWith(got, StringComparison.Ordinal))
                failures.Add($"result is not a prefix of the input ({got.Length} of {input.Length})");
            else if (got.Length < input.Length && !char.IsWhiteSpace(input[got.Length])
                     && !char.IsPunctuation(input[got.Length]))
                failures.Add($"cut INSIDE a word at {got.Length}: \"...{got[^30..]}|{input[got.Length]}...\"");
        }

        if (failures.Count > 0)
            throw new Exception($"the brevity limit cuts badly in {failures.Count} case(s): "
                              + string.Join(" | ", failures));
        return Task.CompletedTask;
    }

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
        // 🔴 THE LONGEST LINE HERE MUST REACH MaxSpokenCharacters, WHICH IS 320 - it is 343.
        // ⚠️ SINCE 2026-09-08 IT IS SPOKEN WHOLE. MaxSpokenCharacters is a soft TARGET now and shortening
        // happens only at sentence ends, so this single 343-character sentence runs past 320 and is
        // rendered in full - which is the point: it puts a real over-target utterance through the voice.
        // The limit's own decision logic is gated separately and without a model by
        // BrevityLimitCutsOnlyAtSentenceEnds; this row exercises SYNTHESIS past the target, not trimming.
        // MEASURED 2026-09-04: this gate originally stopped at 123 characters and passed at 100%, while
        // the Captain's third live turn spoke a 288-character reply that came back "intermittently
        // garbled" with varying volume. A gate that stops short of the product's OWN cap cannot see the
        // product's worst case - the cap is the contract, so the gate has to sit on it.
        // ⚠️ THE LONG LINES ARE SAMPLED TWICE. ZipVoice draws fresh noise per synthesis and its own
        // SpeakVerifiedAsync remarks document draws that come out as the wrong sentence, so ONE reading at
        // a length cannot tell a systematic defect from an unlucky draw. MEASURED 2026-09-04: the same
        // 343-character line scored 100/100/98% across three CUDA runs, so the spread there is small -
        // a wide spread in the browser would mean something different is happening on WebGPU.
        string[] lines =
        [
            // THE EXACT SEQUENCE THAT FAILED. Output depends on EXECUTION HISTORY: 296 is clean
            // (0b57ba1c..., 100%) when it runs first, and was corrupted (67% / 73%, two different
            // hashes) when it followed 41/123/250. Every shrunken repro removed the history that
            // causes it, which is why they all came out clean. Re-running the full sequence tests
            // whether the CORRUPTION is itself reproducible - the precondition for bisecting it.
            "Paint the sockets in the wall dull green.",
            "The morning train was late again, so we walked along the river and talked about the weather "
                + "until the rain finally stopped.",
            "The morning train was late again, so we walked along the river and talked about the weather "
                + "until the rain finally stopped and the sun came out over the water, warming the stones along the path where we sat and rested for a while before walking home.",
            "The morning train was late again, so we walked along the river and talked about the weather "
                + "until the rain finally stopped and the sun came out over the water, warming the stones along the path where we sat and rested for a while before walking slowly back home together in the quiet evening air.",
            "The morning train was late again, so we walked along the river and talked about the weather "
                + "until the rain finally stopped and the sun came out over the water, warming the stones along the path where we sat and rested for a while before walking slowly back home together in the quiet evening air, tired and content after a long and useful day.",
            "The morning train was late again, so we walked along the river and talked about the weather "
                + "until the rain finally stopped and the sun came out over the water, warming the stones along the path where we sat and rested for a while before walking slowly back home together in the quiet evening air.",
            "The morning train was late again, so we walked along the river and talked about the weather "
                + "until the rain finally stopped and the sun came out over the water, warming the stones along the path where we sat and rested for a while before walking slowly back home together in the quiet evening air, tired and content after a long and useful day.",
        ];

        // The WHOLE curve travels in the failure message, not just the failing rows. The test runner
        // surfaces the exception, not the page console, so a summary that lists only failures throws away
        // exactly the comparison that makes the number mean something.
        var curve = new List<string>();
        var failures = new List<string>();
        var position = 1;   // 1-based row in the sequence above; part of each emitted audio filename
        foreach (var line in lines)
        {
            var sw = Stopwatch.StartNew();
            // PIN THE NOISE. Flow matching re-samples every call, so an unpinned score is a sample from a
            // distribution, not a measurement: MEASURED 2026-09-04, the same 343-character line read back at
            // 30%, 39% and 55% on three runs. A fixed seed makes this gate REPEATABLE and makes a
            // WebGPU-vs-CUDA comparison mean something, since both then integrate the same noise.
            var (samples, rate, model, ms, spokenText) = await _client.SpeakAsync(
                line, KnownTranscript, reference, referenceRate, noiseSeed: NoiseSeed);
            sw.Stop();
            if (samples == null || samples.Length == 0)
                throw new Exception($"speak returned NO audio for a {line.Length}-character line");

            // Straight back into Whisper. The pipeline resamples whatever rate it is handed to 16 kHz, so
            // ZipVoice's 24 kHz output needs no conversion here - and converting it by hand would put OUR
            // resampler inside the measurement of THEIR voice.
            var (heard, _, transcribeMs) = await _client.TranscribeAsync(samples, rate);
            heard = (heard ?? "").Trim();

            // 🔴 SCORE AGAINST WHAT THE ENGINE SPOKE, NEVER AGAINST WHAT WE ASKED FOR.
            // MaxSpokenCharacters is 320 and the longest line here is 343 BY DESIGN - the whole point is to
            // sit on the cap. So for that row the engine renders 320 characters, exactly as specified, and
            // the 23 it drops are 5 whole words. Scoring the transcript against the 343-character request
            // therefore reports 59/64 = 92% for a PERFECT render, and the 6-second tail below reports
            // 12/17 = 71% for the same reason.
            //
            // ⚠️ THAT IS EXACTLY WHAT HAPPENED, 2026-09-06. Both numbers were recorded as "real residual
            // degradation at the end of the longest utterances" and carried into a handoff as the top open
            // item, while `[AiVoiceEngine] speaking 320 of 343 characters (cap=320)` sat in the same log,
            // one line above the score. Whisper had returned the spoken text VERBATIM. A gate that cannot
            // pass on correct behaviour is as broken as one that cannot fail on a defect, and it costs more,
            // because a phantom sends people hunting a bug that is not there.
            var spokenWords = Words(spokenText);
            var matched = new List<string>(Words(heard));
            int hits = 0;
            foreach (var w in spokenWords) if (matched.Remove(w)) hits++;
            var overlap = spokenWords.Count == 0 ? 0.0 : hits / (double)spokenWords.Count;
            var seconds = samples.Length / (double)rate;

            // 🔴 WHERE the words are lost, not just how many. A shortfall reads the same whether the voice
            // degraded or the recogniser stopped early, and those are OPPOSITE conclusions - one is our
            // bug, one is the oracle's limit.
            //
            // ✅ RESOLVED 2026-09-08, and worth keeping written down because the reasoning below was sound
            // and still reached the wrong answer. MEASURED 2026-09-05: the 343-character line read back 92%
            // "with the missing words being exactly the final clause", and the recogniser emitted
            // end-of-transcript early - which is what a degraded tail produces, so this instrument was
            // built to tell the two apart. It could not, because there was a THIRD explanation neither
            // reading covered: the final clause was never spoken at all. MaxSpokenCharacters had removed
            // it before synthesis. Both scores are now taken against the spoken text, so they measure the
            // voice; this instrument stays, because a genuinely degraded tail is still a live failure mode
            // and separating it from an early EOT still needs it.
            //
            // Two things do decide it. Re-transcribing the last seconds ON THEIR OWN removes the decode
            // length from the question entirely: clean tail = the audio is fine and the full-clip decode
            // stopped early; garbled tail = the audio degrades and it is ours. Per-second RMS then tells
            // "quiet" apart from "noise", which sound identical in a word count.
            //
            // ⚠️ Never let a shortfall be attributed to the recogniser without this. The Captain has heard
            // this voice garble with his own ears; a word count that can be explained away is how a real
            // defect gets talked out of existence.
            const double TailSeconds = 6.0;
            string tailHeard = "";
            double tailOverlap = -1;
            if (seconds > TailSeconds + 1.0)
            {
                int tailFrom = samples.Length - (int)(TailSeconds * rate);
                var tail = samples[tailFrom..];
                var (th, _, _) = await _client.TranscribeAsync(tail, rate);
                tailHeard = (th ?? "").Trim();
                // Score the tail against the LAST words of the line, proportional to its share of the clip.
                var tailExpected = spokenWords.Skip(Math.Max(0,
                    spokenWords.Count - (int)Math.Ceiling(spokenWords.Count * TailSeconds / seconds))).ToList();
                var tailMatched = new List<string>(Words(tailHeard));
                int tailHits = 0;
                foreach (var w in tailExpected) if (tailMatched.Remove(w)) tailHits++;
                tailOverlap = tailExpected.Count == 0 ? 0.0 : tailHits / (double)tailExpected.Count;
                // ⚠️ READ THIS NUMBER COMPARATIVELY, NEVER AS AN ABSOLUTE. The window opens six seconds
                // from the end, which lands MID-WORD, and `tailExpected` is a proportional estimate
                // (words x 6s / duration) rather than a count of what is really in there - so the leading
                // word or two are clipped away and score as misses even on a perfect render. MEASURED
                // 2026-09-08, every row scoring 100% on the full clip: 250 chars -> 94%, 296 -> 88%,
                // 320 -> 81%. The signal is whether the tail transcription REACHES THE FINAL WORD, which
                // all of those do. A genuinely degraded tail loses the END, not the beginning.
                Console.WriteLine($"[AiVoiceTests]   TAIL {TailSeconds:F0}s alone: {tailOverlap:P0} "
                                + $"({tailHits}/{tailExpected.Count}) heard \"{tailHeard}\"");
            }

            // Per-second RMS + peak. Silence, clipping and noise are three different failures that a word
            // count cannot tell apart.
            var rms = new List<string>();
            for (int sec = 0; sec * rate < samples.Length; sec++)
            {
                int from = sec * rate, to = Math.Min(samples.Length, from + rate);
                double sum = 0; float peak = 0;
                for (int i = from; i < to; i++) { sum += samples[i] * (double)samples[i]; var a = Math.Abs(samples[i]); if (a > peak) peak = a; }
                rms.Add($"{Math.Sqrt(sum / Math.Max(1, to - from)):F3}/{peak:F2}");
            }
            Console.WriteLine($"[AiVoiceTests]   rms/peak per second: {string.Join(" ", rms)}");

            // DETERMINISM CHECK. With noiseSeed pinned and identical text, two runs must produce
            // BIT-IDENTICAL audio. If they do not, the backend is racing or reading uninitialised memory -
            // a completely different bug class from "the arithmetic differs from CUDA", and word overlap is
            // too coarse to tell them apart (different audio can score the same).
            ulong h = 1469598103934665603UL;   // FNV-1a over the raw sample bits
            foreach (var v in samples)
            {
                uint bits = (uint)BitConverter.SingleToInt32Bits(v);
                for (int b = 0; b < 4; b++) { h ^= (byte)(bits >> (b * 8)); h *= 1099511628211UL; }
            }
            Console.WriteLine($"[AiVoiceTests]   audio fnv1a={h:x16} n={samples.Length} first={samples[0]:F6} last={samples[^1]:F6}");

            // Ship the actual audio out (wav=1 only). The POSITION is in the name, not just the length:
            // 296 and 343 are each spoken twice on purpose, and the whole point of this sequence is that
            // the second reading used to differ from the first. Two files named by length alone would
            // overwrite each other and destroy exactly the comparison worth listening to.
            SpokenAudioDump.Emit($"voice-{position:00}-{line.Length:000}chars-{h:x16}", samples, rate);
            position++;

            // The rate is per SPOKEN character - dividing the request by the duration reported 14.9 chars/s
            // for the capped row while the engine was speaking a perfectly ordinary 13.9, which made the
            // one honest row on the page look like the outlier.
            Console.WriteLine($"[AiVoiceTests] read-back {model}: {spokenText.Length} spoken of {line.Length} "
                            + $"chars -> {seconds:F2}s @ {rate}Hz "
                            + $"({spokenText.Length / seconds:F1} chars/sec), spoke {ms:F0}ms, "
                            + $"transcribed {transcribeMs:F0}ms");
            if (spokenText.Length != line.Length)
                Console.WriteLine($"[AiVoiceTests]   ⚠️ brevity cap removed {line.Length - spokenText.Length} "
                                + $"characters - scoring against the {spokenText.Length} that were spoken");
            Console.WriteLine($"[AiVoiceTests]   said : {spokenText}");
            Console.WriteLine($"[AiVoiceTests]   heard: {heard}");
            Console.WriteLine($"[AiVoiceTests]   word overlap {overlap:P0} ({hits}/{spokenWords.Count})");

            curve.Add($"{spokenText.Length,4} spoken chars, {seconds,5:F2}s: {overlap,4:P0} "
                    + $"({hits}/{spokenWords.Count}) heard \"{heard}\"");
            if (overlap < 0.60)
                failures.Add($"{spokenText.Length} spoken chars: {overlap:P0} ({hits}/{spokenWords.Count})");
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
        var (samples, rate, model, ms, spoken) = await _client.SpeakAsync(
            line, KnownTranscript, reference, referenceRate, maxSpokenCharacters: 4000);
        // The 4000 cap is here so the WHOLE line is spoken - the branch under test is reached by LENGTH,
        // so a silently shortened line would quietly stop exercising it. Assert that, do not assume it.
        if (spoken.Length != line.Length)
            throw new Exception($"only {spoken.Length} of {line.Length} characters were spoken despite "
                              + "maxSpokenCharacters: 4000 - this test reaches the recomputed-table branch "
                              + "by LENGTH, so a shortened line stops testing anything.");
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
        (float[] samples, int rate, _, double ttsMs, _) = await _client.SpeakAsync(reply, heardText, heard, heardRate);
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

    /// <summary>
    /// A voice PREPARED once speaks without re-sending or re-deriving its reference, and sounds like the
    /// same voice saying the right words.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 WHAT THIS PINS. One-shot cloning used to be paid on EVERY reply: the reference PCM crossed the
    /// worker transport as a JSON number array each time - a six-figure array for a few seconds at 24 kHz -
    /// and the engine then re-trimmed it and re-ran the mel to rebuild prompt features that never change for
    /// a voice. `PrepareVoiceAsync` does that once; a reply then carries only its text and a voice id.
    /// </para>
    /// <para>
    /// ⚠️ The assertion is INTELLIGIBILITY, not "some audio came back". A prepared voice that lost its
    /// prompt features would still return plausible audio of about the right length - amplitude, duration
    /// and sample rate all look healthy while the words are gone. So the line is read back with the
    /// product's own recogniser and scored against what the engine says it SPOKE, never against the request
    /// (the brevity cap can shorten one, and scoring against the request measures the cap, not the voice).
    /// </para>
    /// <para>
    /// ⚠️ It speaks TWICE from one preparation. Once proves the path works; twice is what proves the
    /// prepared features are REUSABLE rather than consumed - a voice that only worked on its first line
    /// would pass a single-shot test and fail every real conversation.
    /// </para>
    /// </remarks>
    [AiTest(Heavy = true, Timeout = 1_800_000)]
    public async Task PreparedVoiceSpeaksWithoutResendingItsReference()
    {
        await _client.InitAsync();
        var (reference, referenceRate) = await LoadFixtureAsync();

        var prep = Stopwatch.StartNew();
        var prepared = await _client.PrepareVoiceAsync("test-voice", "Test Voice", KnownTranscript,
            reference, referenceRate);
        prep.Stop();
        if (prepared.PromptFrames <= 0)
            throw new Exception($"preparing produced NO prompt frames (got {prepared.PromptFrames}) - "
                              + "the reference was not turned into features, so nothing conditions the voice");

        var voices = await _client.GetVoicesAsync();
        if (!voices.Contains("test-voice"))
            throw new Exception($"prepared voice is not listed; got [{string.Join(", ", voices)}]");

        Console.WriteLine($"[Benchmark] PreparedVoice: prepared in {prep.ElapsedMilliseconds} ms, "
            + $"{prepared.PromptFrames} prompt frames from {prepared.ReferenceSeconds:F2}s of reference");

        string[] lines =
        {
            "Hello. This is SpawnDev AI, speaking from a prepared voice.",
            "The second line proves the prepared features are reused, not consumed.",
        };

        for (int i = 0; i < lines.Length; i++)
        {
            var sw = Stopwatch.StartNew();
            var (samples, rate, model, ms, spoken) =
                await _client.SpeakInVoiceAsync(lines[i], "test-voice");
            sw.Stop();

            if (samples == null || samples.Length == 0)
                throw new Exception($"line {i + 1}: prepared voice returned NO audio");

            float peak = 0f;
            foreach (var v in samples) peak = MathF.Max(peak, MathF.Abs(v));
            if (peak < 0.01f)
                throw new Exception($"line {i + 1}: prepared voice returned effectively SILENCE (peak {peak:F5})");

            // Score against what the engine SAID it spoke - see AiSpeech.SpokenText.
            var (heard, _, _) = await _client.TranscribeAsync(samples, rate);
            var spokenWords = Words(spoken);
            var matched = new List<string>(Words(heard));
            int hits = 0;
            foreach (var w in spokenWords) if (matched.Remove(w)) hits++;
            double overlap = spokenWords.Count == 0 ? 0.0 : hits / (double)spokenWords.Count;
            Console.WriteLine($"[Benchmark] PreparedVoice line {i + 1}: {sw.ElapsedMilliseconds} ms "
                + $"({ms:F0} ms reported), {samples.Length / (double)rate:F2}s audio, "
                + $"overlap {overlap:P0}, heard \"{heard}\"");

            if (overlap < 0.6)
                throw new Exception($"line {i + 1}: prepared voice is NOT intelligible - only {overlap:P0} of "
                    + $"the spoken words came back. Spoke \"{spoken}\", heard \"{heard}\". Plausible audio "
                    + "with the words gone is exactly what a lost prompt-feature buffer produces.");
        }

    }


    /// <summary>
    /// Chunking says the WHOLE reply, and only ever breaks between sentences.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 The property that matters is that nothing is LOST. Chunking exists to retire the brevity cap,
    /// whose defect was exactly this: the page showed text the voice never read. A chunker that drops or
    /// duplicates a clause reintroduces that silently - every chunk would still synthesise and still sound
    /// fine, and only a careful listener comparing against the page would notice.
    /// </para>
    /// <para>
    /// ⚠️ Not heavy: pure text, no model. The fixture mixes short and very long sentences and abbreviations
    /// so that "split at any period" and "split at a fixed width" both fail it.
    /// </para>
    /// </remarks>
    [AiTest(Timeout = 30_000)]
    public Task ChunkingSpeaksTheWholeReplyAndBreaksOnlyAtSentenceEnds()
    {
        const string reply =
            "Short one. Here is a considerably longer sentence that on its own runs past any sensible chunk "
            + "target and therefore has to be emitted whole rather than cut somewhere in the middle of a "
            + "clause where it would put an audible stop in a strange place. Then a third! And a fourth?";

        var chunks = AiVoiceEngine.SplitIntoSpeakableChunks(reply, 160);
        if (chunks.Count == 0) throw new Exception("chunking produced NOTHING for a non-empty reply");

        // 1. Nothing lost, nothing duplicated - compare ignoring the whitespace the trim removes.
        static string Squash(string s) => new string(s.Where(c => !char.IsWhiteSpace(c)).ToArray());
        var rejoined = Squash(string.Join(" ", chunks));
        if (rejoined != Squash(reply))
            throw new Exception("chunking changed the text - the reply would be spoken wrong or incomplete. "
                + $"Got {rejoined.Length} chars, expected {Squash(reply).Length}");

        // 2. Every break is at a sentence end. A chunk that does not end in a terminator means the next
        //    chunk starts mid-sentence, which is the artefact the sentence rule exists to prevent.
        for (int i = 0; i < chunks.Count - 1; i++)
        {
            var last = chunks[i].TrimEnd();
            if (last.Length == 0 || (last[^1] != '.' && last[^1] != '!' && last[^1] != '?'))
                throw new Exception($"chunk {i + 1} of {chunks.Count} does not end at a sentence: \"{last}\"");
        }

        // 3. The over-long sentence survives INTACT rather than being cut to fit.
        if (!chunks.Any(c => c.Contains("audible stop in a strange place")))
            throw new Exception("the long sentence was split - a sentence longer than the target must be "
                + "spoken whole, because breaking mid-clause is worse than a long chunk");

        Console.WriteLine($"[Benchmark] Chunking: {chunks.Count} chunks, lengths "
            + string.Join(",", chunks.Select(c => c.Length)));
        return Task.CompletedTask;
    }

}
