namespace SpawnDev.AI.Demo.Tests;

/// <summary>
/// A room where every character says the same line is not a room.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 THE OBSERVED FAILURE, from the browser gate on 2026-09-10 with the anti-echo clause ALREADY in the
/// system prompt: two characters with opposite personas ("terse and suspicious" / "warm and curious, and
/// you ask one question back") answered "I *look around* for any survivors." and "I *looks around* for any
/// survivors.". One voice, two names, and a transcript that reads like a conversation.
/// </para>
/// <para>
/// ⚠️ NOT heavy, and that is the point. The mechanism is entirely in <see cref="AgentRoom"/>: every other
/// speaker's line arrives as the most recent <c>user</c> turn, and restating the last user turn is the
/// commonest failure of a small model. A fake generator can reproduce that exactly, so the retry is
/// verifiable without loading anything - and, unlike a model, it can be made to echo ON PURPOSE, which is
/// what lets this test fail when the retry is removed.
/// </para>
/// </remarks>
public sealed class AgentRoomEchoTests
{
    private static AgentRoom TwoWayRoom()
    {
        var room = new AgentRoom();
        room.Agents.Add(new ChatAgent("ada", "Ada", "qwen3:0.6b-q8_0", "You are terse."));
        room.Agents.Add(new ChatAgent("bo", "Bo", "qwen3:0.6b-q8_0", "You are warm."));
        // ⚠️ ONE turn each. The default is four, which with two members is ada, bo, ada, bo - and a
        // second turn per member makes "how many times was Bo asked?" mean two different things at once.
        room.MaxAgentTurnsPerRound = 2;
        room.Say(AgentRoom.UserId, "TJ", "The lights just went out. What do we do?");
        return room;
    }

    /// <summary>A near-identical reply is asked again, and the second answer is the one that is kept.</summary>
    [AiTest(Timeout = 30_000)]
    public async Task AnEchoedReplyIsAskedAgainNamingTheLineThatIsTaken()
    {
        var room = TwoWayRoom();
        // The EXACT pair the gate produced - differing by one letter, which is why plain equality misses it.
        const string AdaLine = "I *look around* for any survivors.";
        const string BoEcho = "I *looks around* for any survivors.";
        const string BoOwn = "Careful. Give me your hand and stay close.";

        var asked = new List<string>();
        var boAttempts = 0;
        var produced = await room.RunRoundAsync(generate: async (agent, prompt) =>
        {
            await Task.Yield();
            asked.Add(agent.Id);
            if (agent.Id == "ada") return AdaLine;
            return ++boAttempts == 1 ? BoEcho : BoOwn;
        });

        if (boAttempts != 2)
            throw new Exception($"Bo was asked {boAttempts} time(s). It echoed Ada word for word bar one "
                + "letter, and the room accepted it - the transcript now reads as a conversation and "
                + "contains one voice.");

        var bo = room.Transcript.LastOrDefault(l => l.SpeakerId == "bo")
            ?? throw new Exception("Bo produced no line at all");
        if (bo.Text != BoOwn)
            throw new Exception($"Bo's kept line is \"{bo.Text}\" - the retry ran but its answer was "
                + "discarded, so the round still shows the echo");
        if (produced.Count != 2 || produced[1].Text != BoOwn)
            throw new Exception("the returned round does not match the transcript, so the UI renders "
                + "something other than what was said");

        // The retry has to SAY what is taken. Without the line itself in the prompt, "say something
        // different" gives the model nothing to be different FROM, and it re-rolls the same answer.
        if (asked.Count != 3)
            throw new Exception($"expected 3 generate calls (ada, bo, bo-retry), got {asked.Count}");
    }

    /// <summary>The retry prompt quotes the taken line and names who took it.</summary>
    [AiTest(Timeout = 30_000)]
    public async Task TheRetryPromptCarriesTheEchoedLine()
    {
        var room = TwoWayRoom();
        const string AdaLine = "We should find the breaker room before anything else moves.";
        List<AiChatMessage>? retryPrompt = null;
        var boAttempts = 0;

        await room.RunRoundAsync(generate: async (agent, prompt) =>
        {
            await Task.Yield();
            if (agent.Id == "ada") return AdaLine;
            if (++boAttempts == 1) return AdaLine;              // a verbatim echo
            retryPrompt = prompt;
            return "I would rather stay where the light was.";
        });

        if (retryPrompt == null) throw new Exception("no retry happened on a VERBATIM echo");
        var system = string.Join("\n", retryPrompt.Where(m => m.Role == "system").Select(m => m.Content));
        if (!system.Contains(AdaLine))
            throw new Exception("the retry prompt does not contain the line being repeated, so the model "
                + $"is told to differ from nothing. System turns were:\n{system}");
        if (!system.Contains("Ada"))
            throw new Exception("the retry prompt does not say WHO said the taken line");
    }

    /// <summary>
    /// A reply of its own is NOT regenerated - or every round silently costs twice what it should.
    /// </summary>
    /// <remarks>
    /// ⚠️ This is the half that keeps the fix honest. A retry that fires on everything would make this
    /// suite's other test pass just as well, and would double the generations in every real round.
    /// </remarks>
    [AiTest(Timeout = 30_000)]
    public async Task DistinctRepliesAreNeverRegenerated()
    {
        var room = TwoWayRoom();
        var calls = 0;
        await room.RunRoundAsync(generate: async (agent, prompt) =>
        {
            await Task.Yield();
            calls++;
            return agent.Id == "ada"
                ? "Torches. Now."
                : "I would rather find out what tripped them first, if you will come with me.";
        });

        if (calls != 2)
            throw new Exception($"{calls} generations for a 2-member round - two different replies were "
                + "treated as an echo, so every round costs double");
    }

    /// <summary>What counts as the same line said twice.</summary>
    /// <remarks>
    /// ⚠️ The negative cases matter more than the positive ones. Short replies overlap by coincidence, and
    /// a comparison that calls "I don't know." and "I don't care." the same line would throw away a real
    /// answer and re-roll it.
    /// </remarks>
    [AiTest(Timeout = 30_000)]
    public Task IsEchoSeparatesARepeatFromAnAnswer()
    {
        (string A, string B, bool Echo)[] cases =
        {
            // The measured pair: one letter apart.
            ("I *look around* for any survivors.", "I *looks around* for any survivors.", true),
            // Same words, different punctuation, casing and action markers.
            ("We should go now", "*We should go, now!*", true),
            // Verbatim.
            ("Stay close.", "Stay close.", true),
            // Short and genuinely different - must NOT be treated as a repeat.
            ("I don't know.", "I don't care.", false),
            ("Torches. Now.", "I would rather find out what tripped them first.", false),
            ("Yes.", "No.", false),
            // Long, sharing a subject but saying opposite things.
            ("We should find the breaker room before anything else moves.",
             "We should stay exactly here until somebody else moves first.", false),
            // Nothing to compare against.
            ("", "Stay close.", false),
            (null!, null!, false),
        };

        foreach (var (a, b, expected) in cases)
        {
            var got = AgentRoom.IsEcho(a, b);
            if (got != expected)
                throw new Exception($"IsEcho(\"{a}\", \"{b}\") returned {got}, expected {expected}");
            // Order must not change the answer: whichever line arrived first, it is the same repeat.
            if (AgentRoom.IsEcho(b, a) != expected)
                throw new Exception($"IsEcho is not symmetric for (\"{a}\", \"{b}\")");
        }
        return Task.CompletedTask;
    }
}
