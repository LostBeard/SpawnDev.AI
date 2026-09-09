using SpawnDev.AI.Server;

namespace SpawnDev.AI.Demo.Tests;

/// <summary>
/// A reasoning model's private thinking never reaches the user - not the chat bubble, not the speakers.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 WHY THIS EXISTS. Qwen3 is a HYBRID reasoning model: by default it emits its chain of thought inside
/// <c>&lt;think&gt;…&lt;/think&gt;</c> before the actual answer. Nothing in the engine strips it, so that
/// reasoning is the reply as far as the app is concerned - it renders in the bubble, it is what gets saved
/// to the transcript, and in a room or hands-free it is what the TTS READS ALOUD. The user hears a
/// character muttering its own deliberations before answering, and a group chat feeds one character's
/// private reasoning to the next as though it had been said out loud.
/// </para>
/// <para>
/// ⚠️ This is not hypothetical or future-facing: <c>qwen3:0.6b-q8_0</c> already ships in the demo's model
/// list. The test covers every Qwen3 model the demo offers, because the defect is a property of the family
/// and adding a bigger one must not quietly reintroduce it.
/// </para>
/// </remarks>
public sealed class ReasoningModelTests
{
    private readonly AiWorkerClient _client;

    /// <summary>New instance.</summary>
    /// <param name="client">The window-side client the UI itself uses.</param>
    public ReasoningModelTests(AiWorkerClient client) => _client = client;

    /// <summary>
    /// The Qwen3 models the demo offers. Kept explicit rather than filtered from the served list so that
    /// adding a Qwen3 model without considering its thinking output shows up as a test that never ran.
    /// </summary>
    private static readonly string[] ReasoningModels =
    {
        "qwen3:0.6b-q8_0",
        "qwen3:1.7b-q8_0",
        "qwen3:4b-q4_k_m",
    };

    /// <summary>A reasoning model's reply carries no thinking markup.</summary>
    // 30 min: three models, two of which are a first-time multi-GB download from the hub. The budget is
    // dominated by transfer, not generation.
    [AiTest(Heavy = true, Timeout = 1_800_000)]
    public async Task ThinkingNeverReachesTheReply()
    {
        var failures = new List<string>();

        foreach (var model in ReasoningModels)
        {
            // A prompt with something to reason about. "Say hi" can be answered without thinking at all,
            // and would let this pass on a model that emits a think block whenever it actually thinks.
            var reply = "";
            var deltas = 0;
            DateTime? first = null;
            var started = DateTime.UtcNow;
            await _client.ChatStreamAsync(model,
                new List<AiChatMessage>
                {
                    new("system", "You are Uzi, a sardonic robot. Stay in character. Reply in one sentence."),
                    new("user", "We have three power cells and four doors to open. What do we do first?"),
                },
                new AiGenerationOptions { MaxOutputTokens = 384, Strategy = "top_p", Temperature = 0.7f, TopP = 0.9f },
                onDelta: d => { first ??= DateTime.UtcNow; reply += d; deltas++; });

            // ⚠️ Time to first token and decode rate are SEPARATE quantities. The first turn on a model
            // pays download + GPU load + kernel compilation; folding that into a per-token number reports a
            // decode rate that was never measured. Reported apart so the numbers mean something.
            var ttft = first is { } f ? (f - started).TotalSeconds : 0;
            var decode = first is { } g ? (DateTime.UtcNow - g).TotalSeconds : 0;
            Console.WriteLine($"[reasoning] {model}: first token {ttft:F1}s (load included), then "
                + $"{(decode > 0 ? deltas / decode : 0):F1} tok/s over {decode:F1}s");
            Console.WriteLine($"[reasoning] {model} replied: {reply.Replace("\n", " ")}");

            // The opener alone is the tell. A reply can legitimately contain the WORD "think", so match the
            // tag, and check the closer separately: a truncated think block leaves an opener with no closer
            // and is the worse case, because everything after it is missing entirely.
            if (reply.Contains("<think>", StringComparison.OrdinalIgnoreCase)
                || reply.Contains("</think>", StringComparison.OrdinalIgnoreCase))
                failures.Add($"{model}: thinking markup reached the reply. The user would SEE this, and "
                    + $"hands-free would SPEAK it. Reply began: \"{Head(reply, 160)}\"");
            else if (string.IsNullOrWhiteSpace(reply))
                failures.Add($"{model}: produced NO text at all - if the whole reply was a think block that "
                    + "something stripped without keeping the answer, the user gets silence");
        }

        if (failures.Count > 0)
            throw new Exception(string.Join(" | ", failures));
    }

    private static string Head(string s, int n)
    {
        s = s.Replace("\r", " ").Replace("\n", " ").Trim();
        return s.Length <= n ? s : s[..n] + "…";
    }
}
