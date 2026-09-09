namespace SpawnDev.AI.Demo.Tests;

/// <summary>
/// The group-chat room hands each agent a correct view of the conversation.
/// </summary>
/// <remarks>
/// ⚠️ NOT heavy - pure text, no model. That is deliberate: the defect this guards against produces a
/// transcript that reads perfectly well. An agent given its own past lines as someone else's answers
/// itself; an agent given another's lines as its own adopts that persona mid-conversation. Both look
/// fluent, so listening to a demo would not catch either.
/// </remarks>
public sealed class AgentRoomTests
{
    private static AgentRoom ThreeWayRoom()
    {
        var room = new AgentRoom();
        room.Agents.Add(new ChatAgent("ada", "Ada", "qwen2.5:0.5b-instruct-q8_0", "You like precision."));
        room.Agents.Add(new ChatAgent("bo", "Bo", "qwen3:0.6b-q8_0", "You like big ideas."));
        return room;
    }

    /// <summary>An agent sees its OWN turns as assistant and everyone else's as named user turns.</summary>
    [AiTest(Timeout = 30_000)]
    public Task EachAgentSeesItsOwnTurnsAsItsOwn()
    {
        var room = ThreeWayRoom();
        room.Say(AgentRoom.UserId, "TJ", "What should we build?");
        room.Say("ada", "Ada", "Something measurable.");
        room.Say("bo", "Bo", "Something ambitious.");

        room.Scene = "You are on the derelict colony of Copper-9, at night.";
        var ada = room.BuildPromptFor(room.Agents[0]);

        // The system turn must name the OTHERS, or the agent does not know it is in a group at all.
        var system = ada.FirstOrDefault(m => m.Role == "system")?.Content ?? "";
        if (!system.Contains("Ada")) throw new Exception($"system turn does not tell Ada who it is: {system}");
        if (!system.Contains("Bo")) throw new Exception($"system turn does not mention the other agent: {system}");

        // The SCENE must reach every agent identically - that shared frame is what makes them role-play
        // one situation together instead of answering separately.
        if (!system.Contains("Copper-9"))
            throw new Exception($"the scene did not reach Ada's system turn: {system}");
        var boSystem = room.BuildPromptFor(room.Agents[1]).FirstOrDefault(m => m.Role == "system")?.Content ?? "";
        if (!boSystem.Contains("Copper-9"))
            throw new Exception("the scene reached one agent but not another - they would be in different "
                + $"settings in the same room: {boSystem}");

        var ownTurns = ada.Where(m => m.Role == "assistant").Select(m => m.Content).ToList();
        if (ownTurns.Count != 1 || ownTurns[0] != "Something measurable.")
            throw new Exception("Ada's own line must arrive as ASSISTANT and be the only one - got "
                + $"[{string.Join(" | ", ownTurns)}]. If Ada's own words come back as someone else's, it "
                + "answers itself.");

        var heard = ada.Where(m => m.Role == "user").Select(m => m.Content).ToList();
        if (!heard.Any(h => h.StartsWith("TJ: ")))
            throw new Exception($"the user's turn is not attributed: [{string.Join(" | ", heard)}]");
        if (!heard.Any(h => h.StartsWith("Bo: ")))
            throw new Exception("Bo's turn must arrive name-prefixed - every other speaker arrives as 'user', "
                + $"so without the prefix a three-way chat collapses to a two-way one: [{string.Join(" | ", heard)}]");
        if (heard.Any(h => h.Contains("Something measurable.")))
            throw new Exception("Ada's OWN line was also handed to it as another speaker's - it will treat "
                + "its own words as something to reply to");

        // And the mirror image, which is the other half of the same defect.
        var bo = room.BuildPromptFor(room.Agents[1]);
        var boOwn = bo.Where(m => m.Role == "assistant").Select(m => m.Content).ToList();
        if (boOwn.Count != 1 || boOwn[0] != "Something ambitious.")
            throw new Exception($"Bo's own line must be its only assistant turn - got [{string.Join(" | ", boOwn)}]");
        return Task.CompletedTask;
    }

    /// <summary>Every agent speaks once before any agent speaks twice, and the round is capped.</summary>
    /// <remarks>
    /// 🔴 The cap is what stops an infinite conversation. Agents handed each other's replies answer each
    /// other indefinitely - politely and forever - and every turn costs a generation and possibly a model
    /// load, so an uncapped round is not a cosmetic problem.
    /// </remarks>
    [AiTest(Timeout = 30_000)]
    public Task RoundRobinGivesEveryoneATurnAndIsCapped()
    {
        var room = ThreeWayRoom();
        room.MaxAgentTurnsPerRound = 3;

        var plan = room.PlanRound();
        if (plan.Count != 3)
            throw new Exception($"round should be capped at 3 turns, planned {plan.Count}");
        if (plan[0].Id != "ada" || plan[1].Id != "bo")
            throw new Exception($"round-robin should start at the first agent and alternate; got "
                + string.Join(",", plan.Select(p => p.Id)));
        if (plan[2].Id != "ada")
            throw new Exception("with two agents the third turn wraps back to the first; got " + plan[2].Id);

        room.MaxAgentTurnsPerRound = 0;
        if (room.PlanRound().Count != 0)
            throw new Exception("a zero cap must plan NO agent turns - that is how a user turns the room off");

        var empty = new AgentRoom();
        if (empty.PlanRound().Count != 0 || empty.NextSpeaker(null) != null)
            throw new Exception("an empty room must plan nothing rather than throwing");
        return Task.CompletedTask;
    }

    /// <summary>Each reply is in the transcript before the next member is asked, so they hear each other.</summary>
    /// <remarks>
    /// 🔴 THE DEFECT THIS EXISTS FOR IS INVISIBLE. Build every prompt up front - or parallelise the round,
    /// which is the same mistake wearing a performance hat - and every member answers the user having heard
    /// nobody. The transcript still reads like a conversation, the characters still sound like themselves,
    /// and nothing on screen is wrong; they are simply not talking to each other, which is the entire
    /// feature. No amount of listening to the demo catches it.
    /// <para>
    /// ⚠️ NOT heavy: the generator is a fake precisely so the sequencing is tested without a model. The
    /// fake is not the thing under test - <see cref="AgentRoom.RunRoundAsync"/> is, and it is the same
    /// method the demo page runs.
    /// </para>
    /// </remarks>
    [AiTest(Timeout = 30_000)]
    public async Task EachSpeakerHearsTheOnesBeforeItInTheSameRound()
    {
        var room = ThreeWayRoom();
        room.Agents.Add(new ChatAgent("cy", "Cy", "qwen2.5:0.5b-instruct-q8_0", "You like questions."));
        room.MaxAgentTurnsPerRound = 3;
        room.Say(AgentRoom.UserId, "TJ", "Say something.");

        // What each agent was actually shown, captured at the moment it was asked.
        var shown = new Dictionary<string, List<AiChatMessage>>();
        var askedInOrder = new List<string>();
        // Recorded from INSIDE onSaid, so it proves the line was in the transcript before the callback ran
        // rather than being appended afterwards.
        var transcriptDepthAtCallback = new List<int>();

        var produced = await room.RunRoundAsync(
            generate: (agent, prompt) =>
            {
                shown[agent.Id] = prompt;
                askedInOrder.Add(agent.Id);
                // Cy says nothing at all - a real model does this, and a blank line recorded in the
                // transcript teaches everyone after it that Cy answers with silence.
                return Task.FromResult(agent.Id == "cy" ? "   " : $"{agent.Name} says hello.");
            },
            onSaid: (agent, line) =>
            {
                if (!room.Transcript.Contains(line))
                    throw new Exception($"{agent.Name}'s line reached onSaid BEFORE it was in the "
                        + "transcript - the next speaker would not have heard it");
                transcriptDepthAtCallback.Add(room.Transcript.Count);
                return Task.CompletedTask;
            });

        if (askedInOrder.Count != 3 || askedInOrder[0] != "ada" || askedInOrder[1] != "bo" || askedInOrder[2] != "cy")
            throw new Exception($"round order was [{string.Join(",", askedInOrder)}], expected ada,bo,cy");

        // 🔴 THE POINT OF THE TEST. Bo must have been shown Ada's brand-new line.
        var boHeard = shown["bo"].Where(m => m.Role == "user").Select(m => m.Content).ToList();
        if (!boHeard.Any(h => h.Contains("Ada says hello.")))
            throw new Exception("Bo was NOT shown Ada's reply from this same round, so it is answering the "
                + $"user rather than Ada. Bo heard: [{string.Join(" | ", boHeard)}]");
        if (!boHeard.Any(h => h.StartsWith("Ada: ")))
            throw new Exception("Ada's reply reached Bo unattributed - with three in the room Bo cannot "
                + $"tell who said it: [{string.Join(" | ", boHeard)}]");

        var cyHeard = shown["cy"].Where(m => m.Role == "user").Select(m => m.Content).ToList();
        if (!cyHeard.Any(h => h.Contains("Ada says hello.")) || !cyHeard.Any(h => h.Contains("Bo says hello.")))
            throw new Exception("the third speaker must hear BOTH earlier replies - it heard "
                + $"[{string.Join(" | ", cyHeard)}]");

        // Ada asked first, so it can only have heard the user - if it saw a later reply the transcript is
        // being built from something other than what had actually been said.
        var adaHeard = shown["ada"].Where(m => m.Role == "user").Select(m => m.Content).ToList();
        if (adaHeard.Any(h => h.Contains("says hello.")))
            throw new Exception($"the first speaker was shown a reply nobody had made yet: "
                + string.Join(" | ", adaHeard));

        // Cy said nothing, so nothing of Cy's is recorded and the round reports two lines, not three.
        if (produced.Count != 2)
            throw new Exception($"an empty reply must be dropped, not recorded; got {produced.Count} lines: "
                + string.Join(" | ", produced.Select(l => $"{l.SpeakerName}='{l.Text}'")));
        if (room.Transcript.Any(l => string.IsNullOrWhiteSpace(l.Text)))
            throw new Exception("a blank line is in the transcript - every later speaker now has an example "
                + "of a character replying with silence, and models imitate what they are shown");
        if (transcriptDepthAtCallback.Count != 2 || transcriptDepthAtCallback[0] != 2
            || transcriptDepthAtCallback[1] != 3)
            throw new Exception("onSaid should fire once per recorded line, with the transcript already "
                + $"grown; depths were [{string.Join(",", transcriptDepthAtCallback)}], expected [2,3]");

        // A cancelled round stops, and keeps what was already said.
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var after = await room.RunRoundAsync((a, p) => Task.FromResult("should never run"), ct: cts.Token);
        if (after.Count != 0)
            throw new Exception("a cancelled round must produce nothing");
        if (room.Transcript.Count != 3)
            throw new Exception("cancelling must not discard what was already said");
    }
}
