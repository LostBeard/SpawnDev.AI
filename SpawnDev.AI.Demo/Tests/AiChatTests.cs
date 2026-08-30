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
        var reply = (await SayAsync(Model, messages)).Text;
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
                reply = (await SayAsync(Model, messages)).Text;
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
                reply = (await SayAsync(Model, messages)).Text;
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

    /// <summary>
    /// The SAME multi-turn conversation against EVERY model the demo offers, one test per model, so a
    /// failure names the model instead of "chat is broken".
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>LFM2 is the one to watch.</b> SpawnDev.ILGPU.ML's own CLAUDE.md records that per-step
    /// STATEFUL caches (LFM2's ShortConv state, unlike a position-addressed KV cache) violated all three
    /// of its decode-path contracts and shipped broken - "token soup on WebGPU, and a different answer to
    /// the same prompt twice on every backend" - and says plainly that the prefix-cache hazard "fires on a
    /// new conversation without a reload, an edited system prompt, or a re-ask - NOT on the append-only
    /// case, which is why single-turn testing misses it". A multi-turn chat is precisely the case a
    /// single-shot gate cannot reach, which is why these exist.
    /// <para>
    /// Each is a separate test method rather than a loop so one bad model does not mask the rest, and so
    /// the runner's filter can target a single one.
    /// </para>
    /// </remarks>
    [AiTest(Heavy = true, Timeout = 1_800_000)]
    public Task MultiTurn_Lfm2_1_2b() => MultiTurnBody("lfm2:1.2b-q4_k_m");

    /// <summary>Multi-turn against qwen2.5 0.5B - the demo's usual default.</summary>
    [AiTest(Heavy = true, Timeout = 1_800_000)]
    public Task MultiTurn_Qwen25_0_5b() => MultiTurnBody("qwen2.5:0.5b-instruct-q8_0");

    /// <summary>Multi-turn against qwen3 0.6B.</summary>
    [AiTest(Heavy = true, Timeout = 1_800_000)]
    public Task MultiTurn_Qwen3_0_6b() => MultiTurnBody("qwen3:0.6b-q8_0");

    /// <summary>Multi-turn against qwen2.5 1.5B - the quality step-up, and the largest download.</summary>
    [AiTest(Heavy = true, Timeout = 2_400_000)]
    public Task MultiTurn_Qwen25_1_5b() => MultiTurnBody("qwen2.5:1.5b-instruct-q4_k_m");

    /// <summary>
    /// Six turns against <paramref name="model"/>, feeding each reply back in, asserting per turn.
    /// </summary>
    /// <remarks>
    /// Also checks the model does not answer the SAME question with a DIFFERENT answer across a reload of
    /// the conversation - the "different answer to the same prompt twice" symptom a corrupted per-step
    /// state cache produces. Sampling is greedy with a fixed seed, so a difference is the engine's, not
    /// the sampler's.
    /// </remarks>
    /// <param name="model">Model name as listed by /api/tags.</param>
    private async Task MultiTurnBody(string model)
    {
        await _client.InitAsync();

        string[] prompts =
        {
            "In one short sentence: what is a GPU?",
            "Name two GPU vendors.",
            "Which of those makes the RTX line?",
            "What does VRAM stand for?",
            "Why does VRAM size matter for large models?",
            "Summarise this conversation in one sentence.",
        };

        var messages = new List<AiChatMessage>();
        string? firstAnswer = null;
        for (var turn = 0; turn < prompts.Length; turn++)
        {
            messages.Add(new AiChatMessage("user", prompts[turn]));
            string reply;
            try
            {
                reply = (await SayAsync(model, messages)).Text;
            }
            catch (Exception ex)
            {
                throw new Exception(
                    $"[{model}] turn {turn + 1}/{prompts.Length} THREW with {messages.Count} messages in " +
                    $"history (~{ApproxTokens(messages)} tokens): {ex.GetType().Name}: {ex.Message}", ex);
            }
            if (string.IsNullOrWhiteSpace(reply))
                throw new Exception(
                    $"[{model}] turn {turn + 1} produced EMPTY text with {messages.Count} messages in " +
                    $"history (~{ApproxTokens(messages)} tokens)");
            if (turn == 0) firstAnswer = reply;
            messages.Add(new AiChatMessage("assistant", reply));
            Console.WriteLine($"[AiChatTests] {model} turn {turn + 1}/{prompts.Length} ok " +
                              $"(~{ApproxTokens(messages)} tok): {Clip(reply)}");
        }

        // Re-ask the FIRST question in a FRESH conversation. A position-addressed KV cache is fine with
        // this; a per-step state cache that reuses a prefix it no longer describes is not.
        var reask = (await SayAsync(model, new List<AiChatMessage> { new("user", prompts[0]) })).Text;
        if (string.IsNullOrWhiteSpace(reask))
            throw new Exception($"[{model}] re-ask in a fresh conversation produced EMPTY text");
        if (!string.Equals(reask.Trim(), firstAnswer?.Trim(), StringComparison.Ordinal))
        {
            // Greedy + fixed seed, so this SHOULD be identical. Report both rather than assert blindly -
            // a benign difference still tells us the decode path is not deterministic across conversations.
            Console.WriteLine($"[AiChatTests] ⚠️ {model} answered the same prompt DIFFERENTLY after a "
                            + $"conversation:\n  first: {Clip(firstAnswer ?? "")}\n  re-ask: {Clip(reask)}");
            throw new Exception(
                $"[{model}] same prompt, greedy sampling, fixed seed, but a DIFFERENT answer after a " +
                $"6-turn conversation - first='{Clip(firstAnswer ?? "")}' reask='{Clip(reask)}'");
        }
    }

    /// <summary>
    /// SWAP the model mid-conversation and keep talking - A, B, then back to A, all on one growing history.
    /// </summary>
    /// <remarks>
    /// ⚠️ This is a real user path, not a synthetic one: the demo's composer placeholder is
    /// "Message — or /model…", so switching model mid-chat is a documented feature. And it drives machinery
    /// nothing else here touches - <c>ModelRegistry</c> holds ONE resident LLM, so a swap EVICTS the loaded
    /// model and loads another, then swapping back evicts and reloads again. An evict/reload cycle on a
    /// shared GPU, several messages into a conversation, is exactly the shape of "it throws an error after a
    /// short chat of only a few messages".
    /// <para>
    /// The history is carried ACROSS the swap deliberately - a fresh conversation per model would not
    /// exercise the case where a reloaded model has to prefill a history it did not build.
    /// </para>
    /// </remarks>
    [AiTest(Heavy = true, Timeout = 2_400_000)]
    public async Task ModelSwapMidConversationKeepsWorking()
    {
        await _client.InitAsync();

        const string a = "smollm2:360m-instruct-q8_0";
        const string b = "qwen2.5:0.5b-instruct-q8_0";

        var messages = new List<AiChatMessage>();
        // model, prompt - the swap points are turns 3 and 5.
        (string Model, string Prompt)[] steps =
        {
            (a, "In one short sentence: what is a GPU?"),
            (a, "Name two GPU vendors."),
            (b, "Which of those makes the RTX line?"),
            (b, "What does VRAM stand for?"),
            (a, "Summarise what we discussed in one sentence."),
        };

        for (var i = 0; i < steps.Length; i++)
        {
            var (model, prompt) = steps[i];
            var swapped = i > 0 && steps[i - 1].Model != model;
            messages.Add(new AiChatMessage("user", prompt));
            string reply;
            try
            {
                reply = (await SayAsync(model, messages)).Text;
            }
            catch (Exception ex)
            {
                throw new Exception(
                    $"turn {i + 1}/{steps.Length} on '{model}'{(swapped ? " (JUST SWAPPED - this is the " +
                    "evict + reload path)" : "")} THREW with {messages.Count} messages in history " +
                    $"(~{ApproxTokens(messages)} tokens): {ex.GetType().Name}: {ex.Message}", ex);
            }
            if (string.IsNullOrWhiteSpace(reply))
                throw new Exception(
                    $"turn {i + 1} on '{model}'{(swapped ? " (just swapped)" : "")} produced EMPTY text with " +
                    $"{messages.Count} messages in history (~{ApproxTokens(messages)} tokens)");
            messages.Add(new AiChatMessage("assistant", reply));
            Console.WriteLine($"[AiChatTests] swap-test turn {i + 1}/{steps.Length} on {model}"
                            + $"{(swapped ? " [SWAPPED]" : "")} ok: {Clip(reply)}");
        }
    }

    /// <summary>
    /// SEVERAL images interleaved with chat, on one growing conversation - the cross-KIND eviction cycle,
    /// repeated.
    /// </summary>
    /// <remarks>
    /// ⚠️ This is the Captain's ACTUAL scenario, as described 2026-08-30: the chat that threw "had a bunch
    /// of images I was testing (boxing chickens, the default lighthouse image, etc)" mixed with chat. So the
    /// reproduction is not one image - it is REPEATED cross-kind eviction while a conversation grows.
    /// <para>
    /// <c>AiChatEngine.HasImageIntent</c> routes an image turn to <c>AiImageEngine</c>, whose
    /// <c>EvictOtherKind</c> unloads the LLM; the next chat turn reloads it; the next image turn evicts it
    /// again. Per-kind residency makes that cycle by design, and nothing tested it. Every cycle here also
    /// grows the history, so a leak or a stale buffer gets more room to show itself each time.
    /// </para>
    /// <para>
    /// The prompts are his: boxing chickens and a lighthouse. Downloads an image model as well as an LLM,
    /// so this is by far the slowest test in the suite. A missing image model SKIPS rather than fails - the
    /// chat half is covered elsewhere and an absent image model is a capability gap, not the defect under
    /// test - but a failure ANYWHERE in the cycle is reported with the cycle number and the history size.
    /// </para>
    /// </remarks>
    [AiTest(Heavy = true, Timeout = 5_400_000)]
    public async Task InterleavedImagesAndChatSurviveRepeatedEviction()
    {
        await _client.InitAsync();

        string[] imagePrompts =
        {
            "draw a picture of two chickens boxing in a ring",
            "draw a picture of a lighthouse in a storm",
            "draw a picture of a red apple on a wooden table",
        };
        string[] chatPrompts =
        {
            "In one short sentence: what is a GPU?",
            "What does VRAM stand for?",
            "Name one way to reduce a model's memory use.",
            "Summarise this conversation in one sentence.",
        };

        var messages = new List<AiChatMessage>();

        // Opening chat turn, so the LLM is resident BEFORE the first eviction.
        await ChatTurn(messages, chatPrompts[0], cycle: 0, afterImage: false);

        for (var cycle = 0; cycle < imagePrompts.Length; cycle++)
        {
            // ── image turn: evicts the LLM ──
            messages.Add(new AiChatMessage("user", imagePrompts[cycle]));
            try
            {
                var imageReply = (await SayAsync(Model, messages)).Text;
                messages.Add(new AiChatMessage("assistant",
                    string.IsNullOrWhiteSpace(imageReply) ? "(image)" : imageReply));
                Console.WriteLine($"[AiChatTests] cycle {cycle + 1} image ok " +
                                  $"(~{ApproxTokens(messages)} tok): {Clip(imageReply)}");
            }
            catch (Exception ex) when (cycle == 0)
            {
                throw new SkipTestException(
                    $"image generation unavailable here ({ex.GetType().Name}: {ex.Message}) - the repeated " +
                    "cross-kind eviction cycle cannot be exercised without it");
            }
            catch (Exception ex)
            {
                throw new Exception(
                    $"image turn in cycle {cycle + 1} of {imagePrompts.Length} THREW after " +
                    $"{cycle} completed eviction cycles, with {messages.Count} messages in history " +
                    $"(~{ApproxTokens(messages)} tokens): {ex.GetType().Name}: {ex.Message}", ex);
            }

            // ── chat turn: must RELOAD the evicted LLM ──
            await ChatTurn(messages, chatPrompts[(cycle + 1) % chatPrompts.Length],
                cycle + 1, afterImage: true);
        }
    }

    /// <summary>One chat turn with reporting that names the eviction cycle it belongs to.</summary>
    private async Task ChatTurn(List<AiChatMessage> messages, string prompt, int cycle, bool afterImage)
    {
        messages.Add(new AiChatMessage("user", prompt));
        string reply;
        try
        {
            reply = (await SayAsync(Model, messages)).Text;
        }
        catch (Exception ex)
        {
            throw new Exception(
                $"chat turn{(afterImage ? $" AFTER image {cycle} (the LLM had been evicted and must reload)" : "")} " +
                $"THREW with {messages.Count} messages in history (~{ApproxTokens(messages)} tokens): " +
                $"{ex.GetType().Name}: {ex.Message}", ex);
        }
        if (string.IsNullOrWhiteSpace(reply))
            throw new Exception(
                $"chat turn{(afterImage ? $" AFTER image {cycle}" : "")} produced EMPTY text with " +
                $"{messages.Count} messages in history (~{ApproxTokens(messages)} tokens)" +
                (afterImage ? " - the LLM was evicted for the image model and came back unable to answer" : ""));
        messages.Add(new AiChatMessage("assistant", reply));
        Console.WriteLine($"[AiChatTests] chat turn{(afterImage ? $" after image {cycle}" : "")} ok " +
                          $"(~{ApproxTokens(messages)} tok): {Clip(reply)}");
    }

    /// <summary>
    /// Send one chat request and return the text the model ACTUALLY generated.
    /// </summary>
    /// <remarks>
    /// 🔴 <c>AiWorkerClient.ChatStreamAsync</c> RETURNS THE STOP REASON, not the reply - the generated text
    /// arrives only through its <c>onDelta</c> callback:
    /// <code>
    /// string doneReason = "stop";
    /// ...
    /// return doneReason;      // always non-empty
    /// </code>
    /// Every test in this class originally asserted <c>!IsNullOrWhiteSpace(reply)</c> on that return value,
    /// which is the literal string "stop". They all passed without a model ever answering - a browser suite
    /// that proved only "the call did not throw". Caught 2026-08-30 by turning the runner's --verbose on and
    /// reading a logged reply that said <c>stop</c>.
    /// <para>
    /// So: accumulate the deltas, and assert on THAT. Every caller here uses this helper; none of them
    /// touches the return value of <c>ChatStreamAsync</c> directly.
    /// </para>
    /// </remarks>
    /// <param name="model">Model name as listed by /api/tags.</param>
    /// <param name="messages">Conversation so far.</param>
    /// <returns>The concatenated generated text, and the stop reason.</returns>
    private async Task<(string Text, string StopReason)> SayAsync(string model, IReadOnlyList<AiChatMessage> messages)
    {
        var sb = new System.Text.StringBuilder();
        var stopReason = await _client.ChatStreamAsync(model, messages, Short(), delta => sb.Append(delta));
        return (sb.ToString(), stopReason);
    }

    /// <summary>
    /// Assert a reply is real text, not an artefact. Throws with the model, turn and history size.
    /// </summary>
    /// <remarks>
    /// Rejects empty/whitespace AND the bare stop-reason words, because "stop" arriving AS the content is
    /// the exact symptom of reading the wrong value - a guard against re-introducing the bug this class
    /// already shipped once.
    /// </remarks>
    private static void AssertRealReply(string text, string model, string where, int historyCount, int approxTokens)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new Exception(
                $"[{model}] {where} produced NO generated text ({historyCount} messages in history, " +
                $"~{approxTokens} tokens) - the request completed but the model emitted nothing");
        var t = text.Trim();
        if (t is "stop" or "length" or "tool_calls" or "cancelled")
            throw new Exception(
                $"[{model}] {where} returned the bare stop reason '{t}' as its CONTENT - that is the " +
                "ChatStreamAsync return value leaking in, not a reply. Accumulate onDelta instead.");
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
