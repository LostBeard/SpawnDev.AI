using SpawnDev.AI.Server;

namespace SpawnDev.AI.Demo.Tests;

/// <summary>
/// Chat through the in-browser worker, including the MULTI-TURN path.
/// </summary>
/// <remarks>
/// ⚠️ Why this class exists: the repo's only browser gate (<c>tools/drive-ai-demo.cs</c>) sends ONE
/// message and checks the answer contains "Paris". A single turn cannot catch anything that goes wrong as
/// a conversation GROWS - which is exactly the failure the Captain reported ("it can throw an error after
/// a short chat of only a few messages"), and the desktop host does NOT reproduce it (verified 2026-08-30:
/// 12 turns to ~7.6k tokens against a 4096 cap, no error). So the browser, multi-turn, is the gap.
/// <para>
/// These drive <c>AiWorkerClient</c> - the same client the UI uses - so the worker transport, the GGUF
/// decode path and the KV cache are all real. Marked Heavy because the first run downloads a model.
/// </para>
/// </remarks>
public sealed class AiChatTests
{
    private readonly AiWorkerClient _client;

    /// <summary>The smallest configured model, to keep the first download as short as possible.</summary>
    private const string Model = "smollm2:360m-instruct-q8_0";

    /// <summary>New instance.</summary>
    /// <param name="client">The window-side client the UI itself uses.</param>
    public AiChatTests(AiWorkerClient client) => _client = client;

    /// <summary>One turn answers with non-empty text.</summary>
    [AiTest(Heavy = true, Timeout = 900_000)]
    public async Task SingleTurnProducesText()
    {
        await _client.InitAsync();
        var messages = new List<AiChatMessage> { new("user", "Reply with exactly one short sentence about the sea.") };
        var reply = await _client.ChatStreamAsync(Model, messages, Short());
        if (string.IsNullOrWhiteSpace(reply))
            throw new Exception("single turn produced empty text");
        Console.WriteLine($"[AiChatTests] turn 1: {Clip(reply)}");
    }

    /// <summary>
    /// EIGHT turns, each feeding the previous reply back in - the shape a real conversation has, and the
    /// shape a single-turn gate cannot exercise.
    /// </summary>
    /// <remarks>
    /// The assertion is per-TURN, so a failure names the turn it happened on and how much history was in
    /// play. That distinction is the whole point: "it breaks after a few messages" is a growth problem, and
    /// a test that only reports pass/fail at the end cannot tell you WHERE the growth stopped working.
    /// </remarks>
    [AiTest(Heavy = true, Timeout = 1_800_000)]
    public async Task MultiTurnConversationSurvivesEightTurns()
    {
        await _client.InitAsync();

        string[] prompts =
        {
            "In one short sentence: what is a GPU?",
            "Name two GPU vendors.",
            "Which of those makes the RTX line?",
            "What does VRAM stand for?",
            "Why does VRAM size matter for large models?",
            "Name one way to reduce a model's memory use.",
            "Was my first question about GPUs or about cooking?",
            "Summarise this conversation in one sentence.",
        };

        var messages = new List<AiChatMessage>();
        for (var turn = 0; turn < prompts.Length; turn++)
        {
            messages.Add(new AiChatMessage("user", prompts[turn]));
            string reply;
            try
            {
                reply = await _client.ChatStreamAsync(Model, messages, Short());
            }
            catch (Exception ex)
            {
                throw new Exception(
                    $"turn {turn + 1} of {prompts.Length} THREW with {messages.Count} messages in history " +
                    $"(~{ApproxTokens(messages)} tokens): {ex.GetType().Name}: {ex.Message}", ex);
            }

            if (string.IsNullOrWhiteSpace(reply))
                throw new Exception(
                    $"turn {turn + 1} produced EMPTY text with {messages.Count} messages in history " +
                    $"(~{ApproxTokens(messages)} tokens) - the request did not throw, it just stopped answering");

            messages.Add(new AiChatMessage("assistant", reply));
            Console.WriteLine($"[AiChatTests] turn {turn + 1}/{prompts.Length} ok " +
                              $"(~{ApproxTokens(messages)} tok): {Clip(reply)}");
        }
    }

    /// <summary>
    /// A conversation whose history is deliberately LONG, to push past the 4096-token context cap in few
    /// turns and prove the prompt trimming holds in the browser as it does on the desktop host.
    /// </summary>
    [AiTest(Heavy = true, Timeout = 1_800_000)]
    public async Task LongHistoryIsTrimmedRatherThanFailing()
    {
        await _client.InitAsync();

        // ~600 tokens of filler per turn, so six turns comfortably exceed the 4096 cap.
        var filler = string.Concat(Enumerable.Repeat(
            "The following is background context that must be retained. ", 40));

        var messages = new List<AiChatMessage>();
        for (var turn = 1; turn <= 6; turn++)
        {
            messages.Add(new AiChatMessage("user",
                $"[turn {turn}] {filler} Given all that, answer in one short sentence: what is a GPU?"));
            string reply;
            try
            {
                reply = await _client.ChatStreamAsync(Model, messages, Short());
            }
            catch (Exception ex)
            {
                throw new Exception(
                    $"turn {turn} THREW at ~{ApproxTokens(messages)} tokens of history - the context cap is " +
                    $"4096, so this is the trimming path failing rather than a model limit: " +
                    $"{ex.GetType().Name}: {ex.Message}", ex);
            }
            if (string.IsNullOrWhiteSpace(reply))
                throw new Exception($"turn {turn} produced empty text at ~{ApproxTokens(messages)} tokens");
            messages.Add(new AiChatMessage("assistant", reply));
            Console.WriteLine($"[AiChatTests] long-history turn {turn} ok (~{ApproxTokens(messages)} tok)");
        }
    }

    /// <summary>Keep replies short so a multi-turn run is about the CONVERSATION, not generation length.</summary>
    private static AiGenerationOptions Short() => new() { MaxOutputTokens = 64, Temperature = 0f, Seed = 1234 };

    /// <summary>Rough token estimate (~4 chars/token) - for error messages, not for control flow.</summary>
    private static int ApproxTokens(List<AiChatMessage> messages)
        => messages.Sum(m => m.Content.Length) / 4;

    private static string Clip(string s)
    {
        s = s.Replace("\r", " ").Replace("\n", " ").Trim();
        return s.Length <= 120 ? s : s[..120] + "...";
    }
}
