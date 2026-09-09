using SpawnDev.AI;

namespace SpawnDev.AI.Demo;

/// <summary>One participant in a group chat: a model, a persona, and optionally a voice.</summary>
/// <param name="Id">Stable id, used to tell a speaker's own turns from everyone else's.</param>
/// <param name="Name">What the room calls them. Appears in the transcript, so keep it short.</param>
/// <param name="Model">The model this agent thinks with. Different agents may use different models.</param>
/// <param name="Persona">System-prompt personality. Empty is fine - the model is then just itself.</param>
/// <param name="VoiceId">A prepared voice id, or null to stay silent (text only).</param>
/// <param name="AllowedTools">
/// Names of the tools this character may call. Null or empty means it cannot call any.
/// </param>
/// <remarks>
/// ⚠️ TOOL ACCESS IS PER CHARACTER, not per room, and that is the point rather than a refinement. These
/// tools do things in the world - generate an image, move a physical robot - and "one or more personas can
/// drive Reachy" is a different statement from "anything in the room can". A character that should only
/// talk is given none, and cannot reach the hardware however it is prompted.
/// </remarks>
/// <param name="Avatar">The body this character acts through. None = text and voice only.</param>
/// <param name="MotionScale">
/// How animated this character is, 1.0 being normal. Clamped to 0.3-1.6 when applied.
/// </param>
/// <remarks>
/// <see cref="MotionScale"/> is part of the persona rather than a global setting because it is part of
/// who a character IS - the same written action should read as a twitch from one and a whole-body
/// reaction from another. <c>ReachyBody</c> already takes it, so the robot honours it for free.
/// </remarks>
public sealed record ChatAgent(string Id, string Name, string Model, string Persona, string? VoiceId = null,
    IReadOnlyList<string>? AllowedTools = null, AvatarKind Avatar = AvatarKind.None,
    double MotionScale = 1.0);

/// <summary>One line of the room's shared transcript.</summary>
/// <param name="SpeakerId">Who said it. <see cref="AgentRoom.UserId"/> for the person.</param>
/// <param name="SpeakerName">Display name at the time it was said.</param>
/// <param name="Text">What was said.</param>
public sealed record RoomLine(string SpeakerId, string SpeakerName, string Text);

/// <summary>
/// A group chat where several AIs - each with its own model, persona and voice - talk to each other and to
/// the user.
/// </summary>
/// <remarks>
/// <para>
/// The room is deliberately PURE: it builds prompts and decides who speaks next, and runs nothing. That
/// keeps the part most likely to be subtly wrong - what each agent believes it said - unit-testable without
/// loading a model, which matters because the failure is invisible in a transcript that still reads
/// plausibly.
/// </para>
/// <para>
/// ⚠️ MODEL RESIDENCY IS THE COST. <c>ModelRegistry</c> holds ONE model resident; a turn by an agent using a
/// different model disposes the resident and loads the new one. That works
/// (<c>AiChatTests.ModelSwapMidConversationKeepsWorking</c> passes) but a round table of distinct models
/// pays a load per turn. Agents sharing one model swap nothing, so a mixed room is best arranged so that
/// consecutive speakers share a model where possible - and eventually by letting the chat kind hold several
/// models inside <c>GpuResidency</c>'s budget rather than exactly one.
/// </para>
/// </remarks>
public sealed class AgentRoom
{
    /// <summary>Speaker id reserved for the human.</summary>
    public const string UserId = "user";

    /// <summary>The agents in the room, in speaking order.</summary>
    public List<ChatAgent> Agents { get; } = new();

    /// <summary>The shared transcript, oldest first.</summary>
    public List<RoomLine> Transcript { get; } = new();

    /// <summary>
    /// The SETTING everyone in the room shares - where they are, when it is, what is going on.
    /// </summary>
    /// <remarks>
    /// 🔴 Separate from a character's persona ON PURPOSE, and it is what makes role-play work. A persona is
    /// who someone IS and travels with them between rooms; a scene is where everybody is right now and is
    /// the same for all of them. Folding the scene into each persona would mean editing every character to
    /// change the setting, and characters would drift out of sync the moment one was edited and another was
    /// not. Empty is fine - the room is then just a conversation.
    /// </remarks>
    public string Scene { get; set; } = "";

    /// <summary>
    /// How many agent turns may run before the room waits for the user again.
    /// </summary>
    /// <remarks>
    /// 🔴 THE ONLY THING STOPPING AN INFINITE CONVERSATION. Agents given each other's replies will answer
    /// each other indefinitely - politely, plausibly, and forever - and every turn costs a generation and
    /// possibly a model load. A cap is not a nicety here; it is what makes the feature safe to switch on.
    /// </remarks>
    public int MaxAgentTurnsPerRound { get; set; } = 4;

    /// <summary>
    /// True when this room is ROLE-PLAY: asterisks mean physical action rather than emphasis.
    /// </summary>
    /// <remarks>
    /// 🔴 THIS IS THE SWITCH FOR A DESTRUCTIVE OPERATION, so what turns it on matters. In role-play a
    /// <c>*...*</c> span is lifted out of the reply and never spoken; outside it, the same span is ordinary
    /// markdown emphasis, and removing the words in "I'm *not* doing that" makes the voice say the OPPOSITE
    /// of what is on screen.
    /// <para>
    /// Either signal is sufficient. A SCENE means the characters are somewhere and acting. A character with
    /// a BODY - an on-screen avatar or the robot - has something to act WITH, and its actions are the whole
    /// reason it has one, so requiring a scene as well would leave an embodied character standing still.
    /// </para>
    /// </remarks>
    public bool RolePlay
        => !string.IsNullOrWhiteSpace(Scene) || Agents.Any(a => a.Avatar != AvatarKind.None);

    /// <summary>Add a line to the transcript.</summary>
    public void Say(string speakerId, string speakerName, string text)
        => Transcript.Add(new RoomLine(speakerId, speakerName, text));

    /// <summary>
    /// Who speaks next, round-robin from the agent after <paramref name="lastSpeakerId"/>.
    /// </summary>
    /// <remarks>
    /// Round-robin on purpose for a first cut: it is deterministic, demonstrable, and cannot starve an
    /// agent. Letting a model choose the next speaker is more natural and much harder to reason about when
    /// it goes wrong, so it is not what ships first.
    /// </remarks>
    public ChatAgent? NextSpeaker(string? lastSpeakerId)
    {
        if (Agents.Count == 0) return null;
        if (string.IsNullOrEmpty(lastSpeakerId) || lastSpeakerId == UserId) return Agents[0];
        var i = Agents.FindIndex(a => a.Id == lastSpeakerId);
        return i < 0 ? Agents[0] : Agents[(i + 1) % Agents.Count];
    }

    /// <summary>
    /// Build the prompt one agent sees: its own turns as ASSISTANT, everyone else's as USER, name-prefixed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 THE ROLE MAPPING IS THE WHOLE CORRECTNESS PROBLEM. A chat model learns who it is from which turns
    /// carry the assistant role. Hand an agent its own past lines as `user` and it believes someone else
    /// said them, and starts answering itself; hand it another agent's lines as `assistant` and it believes
    /// it said them, and adopts that agent's persona mid-conversation. Both produce a transcript that still
    /// reads fluently, which is exactly why this is unit-tested rather than eyeballed.
    /// </para>
    /// <para>
    /// ⚠️ Other speakers are name-prefixed ("Ada: ...") because the role alone cannot say WHICH of several
    /// others spoke - every one of them arrives as `user`. Without the prefix a three-way conversation
    /// collapses into a two-way one in the model's view.
    /// </para>
    /// </remarks>
    public List<AiChatMessage> BuildPromptFor(ChatAgent speaker)
    {
        var messages = new List<AiChatMessage>();

        var others = Agents.Where(a => a.Id != speaker.Id).Select(a => a.Name).ToList();
        var whoElse = others.Count == 0
            ? "You are talking with the user."
            : $"You are in a group chat with the user and: {string.Join(", ", others)}.";

        var system = string.IsNullOrWhiteSpace(speaker.Persona)
            ? $"You are {speaker.Name}. {whoElse}"
            : $"You are {speaker.Name}. {speaker.Persona} {whoElse}";
        // The scene goes to EVERY agent, verbatim and identically - that shared frame is the difference
        // between characters role-playing a scene together and several characters answering separately.
        // The asterisk convention is asked for ONLY in a scene: it is how a model marks physical action,
        // it is what drives an avatar or the robot, and it is stripped before anything is spoken aloud.
        // Outside a scene there is nothing to act, and asking for it would just add markup to answers.
        if (!string.IsNullOrWhiteSpace(Scene)) system += $" The scene: {Scene}";
        if (RolePlay)
            system += " Put any physical action between asterisks, like *looks around*, and keep spoken "
                    + "words outside them.";
        // Brevity is a room rule, not a persona choice: several agents each writing an essay turns one
        // exchange into minutes of synthesis and reading.
        // ⚠️ THE ANTI-ECHO CLAUSE IS THERE FOR AN OBSERVED FAILURE, not as boilerplate. Every other
        // speaker's line arrives as a `user` turn, and a small model's most common failure is to restate
        // its most recent user turn - MEASURED: two characters on qwen2.5-0.5b produced BYTE-IDENTICAL
        // replies ("I looks around, looking for any sign of life...") because the second echoed the
        // first. It reads as a conversation and contains one voice. A stronger model is the real fix, and
        // the model picker now offers several; this costs one clause and helps the weak ones.
        messages.Add(new AiChatMessage("system",
            system + " Reply as yourself, briefly - a sentence or two. Do not prefix your reply with your "
                   + "name, and do not speak for anyone else. Never repeat what someone else just said - "
                   + "respond to it with something of your own."));

        foreach (var line in Transcript)
        {
            if (line.SpeakerId == speaker.Id) messages.Add(new AiChatMessage("assistant", line.Text));
            else messages.Add(new AiChatMessage("user", $"{line.SpeakerName}: {line.Text}"));
        }
        return messages;
    }

    /// <summary>
    /// The agents that should speak, in order, in response to the latest user turn.
    /// </summary>
    /// <remarks>
    /// Capped by <see cref="MaxAgentTurnsPerRound"/>. Returns agents in round-robin order starting from the
    /// first, so every agent gets a turn before any agent gets a second one.
    /// </remarks>
    public List<ChatAgent> PlanRound()
    {
        var plan = new List<ChatAgent>();
        if (Agents.Count == 0) return plan;
        string? last = UserId;
        for (int i = 0; i < MaxAgentTurnsPerRound; i++)
        {
            var next = NextSpeaker(last);
            if (next == null) break;
            plan.Add(next);
            last = next.Id;
        }
        return plan;
    }

    /// <summary>
    /// Run one round: every planned member replies in turn, each hearing what was said before it.
    /// </summary>
    /// <param name="generate">
    /// Produces one member's reply from the prompt built for it. This is where a model actually runs; the
    /// room itself stays free of any engine so the sequencing can be tested without loading one.
    /// </param>
    /// <param name="onSaid">
    /// Called once per reply, AFTER it has entered the transcript. The caller renders and speaks here -
    /// speech policy (which voice, whether to speak at all) belongs to the UI, not to the room.
    /// </param>
    /// <param name="ct">Cancels between turns; the round stops without unwinding what was already said.</param>
    /// <returns>The lines this round added, in order.</returns>
    /// <remarks>
    /// 🔴 NO <c>ConfigureAwait(false)</c> IN HERE, EVER. Both callbacks are UI code - they render bubbles
    /// and speak - so the synchronization context has to survive the awaits. It was written with
    /// ConfigureAwait(false) out of library habit, and the second speaker's turn then threw "The current
    /// thread is not associated with the Dispatcher": the first member answered, and the round died. The
    /// unit test stayed green throughout, because a fake generator returning <c>Task.FromResult</c>
    /// completes synchronously and never hops threads - only the browser gate caught it.
    /// </remarks>
    /// <remarks>
    /// <para>
    /// 🔴 THE ORDERING IS THE FEATURE, AND IT LIVES HERE SO IT CAN BE TESTED. Each reply enters
    /// <see cref="Transcript"/> BEFORE the next member's prompt is built, and that is the entire difference
    /// between characters talking to EACH OTHER and characters each answering the user in isolation.
    /// Generate every reply first and append them afterwards - the obvious refactor, and a natural one if
    /// this loop were ever parallelised - and you get a transcript that reads exactly like a conversation
    /// and contains none. Nothing on screen would look wrong.
    /// </para>
    /// <para>
    /// ⚠️ An empty or whitespace reply is DROPPED rather than recorded. A blank turn recorded as a line
    /// teaches every later member that this character answers with silence, and they imitate it.
    /// </para>
    /// </remarks>
    public async Task<List<RoomLine>> RunRoundAsync(
        Func<ChatAgent, List<AiChatMessage>, Task<string>> generate,
        Func<ChatAgent, RoomLine, Task>? onSaid = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(generate);
        var said = new List<RoomLine>();

        foreach (var agent in PlanRound())
        {
            if (ct.IsCancellationRequested) break;

            // Built HERE, one turn at a time, from the transcript as it stands right now - not hoisted out
            // of the loop. Hoisting it is what silently turns this into everyone answering the user.
            var text = (await generate(agent, BuildPromptFor(agent)) ?? "").Trim();
            if (text.Length == 0) continue;

            var line = new RoomLine(agent.Id, agent.Name, text);
            Transcript.Add(line);
            said.Add(line);
            if (onSaid != null) await onSaid(agent, line);
        }
        return said;
    }
}
