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
    /// The first chunk stays short and the rest are bigger - the whole point of the split being uneven.
    /// </summary>
    [AiTest(Timeout = 30_000)]
    public Task TheFirstChunkIsShortAndTheRestAreMerged()
    {
        var chunks = Home.SpeakableChunks(Reply);
        if (chunks.Count < 2)
            throw new Exception($"a {Reply.Length}-character reply produced {chunks.Count} chunk(s); this "
                + "fixture must be long enough to be split, or it proves nothing");
        if (chunks[0].Length > 200)
            throw new Exception($"the first chunk is {chunks[0].Length} chars - it exists to make the "
                + "voice start SOON, and a long one gives that up");

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
