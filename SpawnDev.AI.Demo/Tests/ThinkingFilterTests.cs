using SpawnDev.AI.Server;

namespace SpawnDev.AI.Demo.Tests;

/// <summary>
/// The thinking filter removes exactly the reasoning, whatever way the deltas happen to be cut.
/// </summary>
/// <remarks>
/// ⚠️ NOT heavy - pure string logic, no model. That is deliberate and it covers something the model-backed
/// gate CANNOT: <c>&lt;think&gt;</c> is a single token in Qwen3's vocabulary, so a real stream almost never
/// splits it, and the delta-boundary handling - the subtle half of this filter - would go permanently
/// unexercised by any number of live runs.
/// </remarks>
public sealed class ThinkingFilterTests
{
    /// <summary>Blocks are removed, and text outside them survives intact.</summary>
    [AiTest(Timeout = 30_000)]
    public Task ThinkBlocksAreRemovedAndTheAnswerSurvives()
    {
        Check("<think>I should be brief.</think>Hello there.", "Hello there.");
        Check("Before.<think>hmm</think>After.", "Before.After.");
        Check("<think>one</think>A<think>two</think>B", "AB");
        // Nothing to strip must come back byte-identical - the filter is on every reply from every model,
        // so a non-reasoning model must not have its text reshaped in passing.
        Check("Plain answer with no markup.", "Plain answer with no markup.");
        Check("", "");
        // A lone closer is not a block. Removing it would eat text on a model that merely mentions the tag.
        Check("An answer </think> with a stray closer.", "An answer </think> with a stray closer.");

        // 🔴 UNCLOSED: the budget ran out mid-monologue. Everything after the opener is a severed
        // half-thought, not an answer, and showing it would put the model's private deliberation on screen.
        Check("<think>I was still reasoning when the budget ran", "");
        Check("Some answer.<think>then it kept going", "Some answer.");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Streaming produces exactly what a whole-string strip would, at EVERY possible delta boundary.
    /// </summary>
    /// <remarks>
    /// 🔴 THE REAL GUARD. The filter has to decide what is safe to emit before it has seen the rest, so the
    /// failure mode is a tag split across two deltas: "&lt;thi" + "nk&gt;" flashes the opener on screen and
    /// then leaks the entire monologue, and "&lt;/thin" + "k&gt;" never closes so the answer is swallowed.
    /// Splitting at one arbitrary point would test one of hundreds of boundaries; this walks all of them,
    /// which is the only way the property is actually established.
    /// </remarks>
    [AiTest(Timeout = 30_000)]
    public async Task StreamingMatchesAWholeStringStripAtEverySplit()
    {
        string[] cases =
        {
            "<think>reasoning here</think>The answer.",
            "Lead in.<think>a</think>tail",
            "<think>x</think><think>y</think>done",
            "no markup at all",
            "<think>unclosed and truncated",
            "answer first<think>trailing thought</think>",
        };

        foreach (var text in cases)
        {
            var expected = AiChatEngine.StripThinking(text);

            // Every single-cut split, plus per-character deltas - the worst case a tokenizer can produce.
            for (int cut = 0; cut <= text.Length; cut++)
                await AssertStream(text, new[] { text[..cut], text[cut..] }, expected, $"split at {cut}");

            await AssertStream(text, text.Select(c => c.ToString()).ToArray(), expected, "per character");
        }
    }

    private static async Task AssertStream(string text, string[] deltas, string expected, string how)
    {
        var got = "";
        var filter = new AiChatEngine.ThinkingStreamer(d => { got += d; return Task.CompletedTask; });
        foreach (var d in deltas) await filter.PushAsync(d);
        await filter.FlushTailAsync();

        // TrimStart: the whole-string strip trims leading space left where a block was removed, and the
        // streaming path cannot - it has already emitted. Compare on the same footing.
        if (got.TrimStart() != expected)
            throw new Exception($"streaming ({how}) gave \"{got}\" but a whole-string strip of "
                + $"\"{text}\" gives \"{expected}\" - a tag split across deltas is mishandled");
    }

    private static void Check(string input, string expected)
    {
        var got = AiChatEngine.StripThinking(input);
        if (got != expected)
            throw new Exception($"StripThinking(\"{input}\") = \"{got}\", expected \"{expected}\"");
    }
}
