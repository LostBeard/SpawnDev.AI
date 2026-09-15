using SpawnDev.AI.Server;
using SpawnDev.AI.Demo.Pages;

namespace SpawnDev.AI.Demo.Tests;

/// <summary>
/// How a reply is cut up for speech, which decides how long the voice stalls between sentences.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 THE MEASUREMENT THIS EXISTS FOR. Captain: "the pause between when it pauses reading and starts
/// again (the tts streaming) is very large." MEASURED on the hands-free gate: a chunk took ~31 s to
/// render 1.9 s of speech, and the first Euler step alone was 8.7 s of 20 s - a FIXED cost per synthesis.
/// Uniform chunks pay that toll once per chunk, and because rendering is ~15x slower than realtime no
/// amount of pipelining hides it. So the FIRST chunk stays short (that one really does buy
/// time-to-first-audio) and the rest are merged into far fewer renders.
/// </para>
/// <para>
/// ⚠️ NOT heavy - this is arithmetic over strings, and the property that matters most is one a listener
/// cannot check for themselves: that merging loses NOTHING. A dropped or reordered sentence would be
/// invisible on screen, because the transcript is rendered from the original text.
/// </para>
/// </remarks>
public sealed class SpeakChunkingTests
{
    private const string Reply =
        "The lights went out about ten minutes ago. I checked the breaker room first and the main bus is "
        + "dead, which means it is not a fuse. There is a service corridor behind the east wall that "
        + "should still have emergency power. If we can reach it we can bring one circuit back up. "
        + "Bring a torch and stay close, because the floor down there is not level and I do not want to "
        + "be carrying anyone back. We should move before it gets colder.";

    /// <summary>Merging must not lose, duplicate or reorder a single word.</summary>
    [AiTest(Timeout = 30_000)]
    public Task MergingKeepsEveryWordInOrder()
    {
        var chunks = Home.SpeakableChunks(Reply);
        if (chunks.Count == 0) throw new Exception("nothing to speak - the whole reply was dropped");

        static string Words(string s) => string.Join(" ",
            s.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries));

        var spoken = Words(string.Join(" ", chunks));
        var original = Words(Reply);
        if (spoken != original)
            throw new Exception("the merged chunks do not say the same thing as the reply - a listener "
                + $"would silently miss or repeat words.\nreply : {original}\nspoken: {spoken}");
        return Task.CompletedTask;
    }

    /// <summary>
    /// No chunk but the last is too small to pay for its own synthesis pass, and the rest are merged.
    /// </summary>
    /// <remarks>
    /// 🔴 THIS USED TO ASSERT THE OPPOSITE - "the first chunk is short, and a long one gives that up
    /// (&lt;= 200 chars)". That was a design belief, and it was measured wrong on 2026-09-15: a synthesis
    /// costs <c>5.04s + 0.22 x audio</c> in the demo's WebGPU worker, so a chunk that carries less than
    /// ~6.4 s of audio (~102 chars) can never cover the next chunk's render. The old splitter had a ceiling
    /// and no floor, the first chunk of a 900-character reply came out at <b>82 characters</b>, and chunk 2
    /// arrived <b>3,498 ms late</b> - an audible gap in every long reply, while the whole-reply average
    /// still read 0.57x realtime.
    /// <para>
    /// ⚠️ The old assertion could not fail on the defect it was next to: it bounded the first chunk from
    /// ABOVE, which is the direction that was never the problem. Starting sooner is worth nothing if the
    /// voice then stops mid-reply.
    /// </para>
    /// </remarks>
    [AiTest(Timeout = 30_000)]
    public Task NoChunkIsTooSmallToPayForItselfAndTheRestAreMerged()
    {
        var chunks = Home.SpeakableChunks(Reply);
        if (chunks.Count < 2)
            throw new Exception($"a {Reply.Length}-character reply produced {chunks.Count} chunk(s); this "
                + "fixture must be long enough to be split, or it proves nothing");

        // Every chunk but the LAST has to cover the render of the one after it. The last is exempt -
        // nothing follows it, so it has nothing to cover.
        const int floorChars = 102;   // 5.04s / (1 - 0.22) = 6.4s of audio at 0.0635 s/char
        for (int i = 0; i < chunks.Count - 1; i++)
            if (chunks[i].Length < floorChars)
                throw new Exception($"chunk {i + 1} of {chunks.Count} is {chunks[i].Length} chars, under the "
                    + $"{floorChars}-char floor where a chunk carries less audio than one synthesis pass "
                    + "costs. It cannot buy enough playing time to cover the next render, so the reply will "
                    + "stutter here no matter how the rest is buffered.");

        // The real claim: fewer renders than a uniform split would have made. Each render costs a fixed
        // ~8.7s first Euler step, so the count IS the latency.
        var uniform = AiVoiceEngine.SplitIntoSpeakableChunks(Reply, 160).Count;
        if (chunks.Count >= uniform)
            throw new Exception($"merging produced {chunks.Count} renders against a uniform split's "
                + $"{uniform} - it saves nothing, so every seam still pays the fixed per-synthesis cost");
        Console.WriteLine($"[chunking] {uniform} uniform renders -> {chunks.Count} merged "
            + $"({string.Join(", ", chunks.Select(c => c.Length + "ch"))})");
        return Task.CompletedTask;
    }

    /// <summary>
    /// A sentence too long for the engine is broken at a CLAUSE, and no word is lost doing it.
    /// </summary>
    /// <remarks>
    /// 🔴 THE RULE THIS CHANGES, AND WHY. The splitter's old rule was "a single sentence longer than the
    /// target is emitted WHOLE", because an audible stop in a strange place is worse than a long chunk.
    /// That holds until the renderer cannot render it. MEASURED 2026-09-15, ZipVoice on WebGPU with
    /// seeded noise, read back through Whisper:
    /// <code>
    ///   250 chars -> 100%      296 chars -> 67%      343 chars -> 42%
    /// </code>
    /// At 42% the listener does not hear an oddly-paused sentence, they hear "Norman praying walkers
    /// eight day so we walked a bone before". A comma is a place speech pauses anyway.
    /// <para>
    /// ⚠️ THE PROPERTY THAT MUST SURVIVE is that nothing is lost. The break works by PROMOTING a comma to
    /// a full stop, never by cutting, so the text still says the same words in the same order - which is
    /// the one thing a listener cannot check for themselves.
    /// </para>
    /// </remarks>
    [AiTest(Timeout = 30_000)]
    public Task AnOverlongSentenceIsBrokenAtAClauseAndLosesNothing()
    {
        // The exact 343-character line the ZipVoice gate reads back at 42%: one sentence, commas, no
        // interior full stop.
        const string OneLongSentence =
            "The morning train was late again, so we walked along the river and talked about the weather "
          + "until the rain finally stopped and the sun came out over the water, warming the stones along "
          + "the path where we sat and rested for a while before walking slowly back home together in the "
          + "quiet evening air, tired and content after a long and useful day.";
        const int Limit = 250;   // measured 100% intelligible at this length

        var chunks = AiVoiceEngine.SplitIntoSpeakableChunks(OneLongSentence, 320, 0, clauseSplitOver: Limit);
        if (chunks.Count < 2)
            throw new Exception($"a {OneLongSentence.Length}-character single sentence was left as "
                + $"{chunks.Count} chunk(s) - it must be broken at a clause, or the voice reads it as "
                + "gibberish (42% intelligible, MEASURED)");

        foreach (var c in chunks)
            if (c.Length > Limit + 40)
                throw new Exception($"a chunk is {c.Length} chars, past the {Limit} the engine renders "
                    + $"well: \"{c}\"");

        // ⚠️ NOTHING LOST. Compare words, ignoring the punctuation the promotion deliberately changes.
        static string Words(string s) => string.Join(" ",
            s.Replace('.', ' ').Replace(',', ' ')
             .Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries));
        if (Words(string.Join(" ", chunks)) != Words(OneLongSentence))
            throw new Exception("breaking the sentence changed the words - a listener would silently "
                + $"miss or repeat something.\noriginal: {Words(OneLongSentence)}\nspoken  : "
                + Words(string.Join(" ", chunks)));

        // A sentence with NO clause punctuation has no good break point; leaving it whole is correct.
        var noCommas = new string('a', 400).Replace("aaaa", "aaa ") + ".";
        var whole = AiVoiceEngine.SplitIntoSpeakableChunks(noCommas, 320, 0, clauseSplitOver: Limit);
        if (whole.Count != 1)
            throw new Exception($"a sentence with no clause punctuation was split into {whole.Count} "
                + "chunks - there is no good break point, and a mid-word cut is worse than a long utterance");

        Console.WriteLine($"[chunking] 343-char sentence -> {chunks.Count} clause chunks "
            + $"({string.Join(", ", chunks.Select(c => c.Length + "ch"))})");
        return Task.CompletedTask;
    }

    /// <summary>A short reply is one chunk, and is never padded into more.</summary>
    [AiTest(Timeout = 30_000)]
    public Task AShortReplyStaysASingleRender()
    {
        var chunks = Home.SpeakableChunks("Yes, that works.");
        if (chunks.Count != 1)
            throw new Exception($"a one-sentence reply became {chunks.Count} renders, each paying the "
                + "fixed per-synthesis cost for nothing");
        return Task.CompletedTask;
    }
}
