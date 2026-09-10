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

            // 🔴 A PARROT IS NOT A CONVERSATION, AND THE PROMPT CLAUSE ALONE DOES NOT STOP IT.
            // Every other speaker's line arrives as the most recent `user` turn, and restating the last
            // user turn is a small model's single most common failure. MEASURED on the room gate with the
            // anti-echo clause already in the system prompt: "I *look around* for any survivors." followed
            // by "I *looks around* for any survivors." - two characters, two personas, one voice. Asking
            // once more, this time NAMING the line that is taken, costs one generation on the rare turn
            // that needs it and nothing at all on the turns that do not.
            // ⚠️ RECENT lines only, and that is both cheaper and more correct. The failure is restating
            // what was JUST said - a character reusing its own phrase from twenty turns ago is a
            // catchphrase, not a parrot. Scanning the whole transcript would also normalise every line
            // ever said on every turn, so the cost of a round would grow with the length of the
            // conversation for no gain.
            RoomLine? echoed = null;
            var oldest = Math.Max(0, Transcript.Count - EchoLookback);
            for (var i = Transcript.Count - 1; i >= oldest; i--)
            {
                var l = Transcript[i];
                if (l.SpeakerId == agent.Id || !IsEcho(text, l.Text)) continue;
                echoed = l;
                break;
            }
            if (echoed != null)
            {
                var retryPrompt = BuildPromptFor(agent);
                retryPrompt.Add(new AiChatMessage("system",
                    $"{echoed.SpeakerName} already said \"{echoed.Text}\". That line is taken. Say "
                    + "something of your own instead - react to it, disagree with it, or add to it, but "
                    + "do not repeat it."));
                var retry = (await generate(agent, retryPrompt) ?? "").Trim();
                // ⚠️ The retry is used whenever it produced anything, even if it echoes again. Dropping a
                // second echo would silently remove a member from the round, and a room that loses a
                // speaker is a worse failure than one that repeats itself - the user can see a repeat and
                // change model; they cannot see an absence. When it still echoes, SAY so, because that is
                // the model being too weak for role-play and no prompt fixes it.
                if (retry.Length > 0)
                {
                    if (IsEcho(retry, echoed.Text))
                        Console.WriteLine($"[room] {agent.Name} ({agent.Model}) echoed {echoed.SpeakerName} "
                            + "twice - this model is too weak to hold a separate voice in a room");
                    text = retry;
                }
            }

            var line = new RoomLine(agent.Id, agent.Name, text);
            Transcript.Add(line);
            said.Add(line);
            if (onSaid != null) await onSaid(agent, line);
        }
        return said;
    }

    /// <summary>
    /// Is <paramref name="candidate"/> the same line as <paramref name="existing"/>, said again?
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ STRING EQUALITY IS NOT ENOUGH, which is the whole reason this exists rather than an <c>==</c>.
    /// The observed failure differed by one letter: "I *look around* for any survivors." against
    /// "I *looks around* for any survivors." - byte-different, identical to a reader, and a plain
    /// comparison waves it straight through.
    /// </para>
    /// <para>
    /// So: normalise away case, punctuation and the asterisks that mark stage directions, then compare
    /// word sets. Identical after normalising is an echo at any length. Otherwise it takes four words or
    /// more AND three quarters of them shared, because short replies overlap by coincidence - "I don't
    /// know." and "I don't care." share two words of three and are different answers.
    /// </para>
    /// </remarks>
    /// <param name="candidate">The reply just generated.</param>
    /// <param name="existing">A line already in the transcript.</param>
    /// <returns>True when the two say the same thing.</returns>
    public static bool IsEcho(string? candidate, string? existing)
        // 🔴 COMPARED TWICE: whole lines, and DIALOGUE ONLY. The second is not a refinement - it is the
        // form the listener actually receives. MEASURED on the room gate after the first fix went in:
        //
        //   Zephrin  *looks around*  We need to find a way to stay alive.
        //   Qualla   *glances at the broken lights, then at the empty room*  We need to find a way to stay alive.
        //
        // As whole strings those share 8 distinct words of 16 - half - and passed cleanly. But stage
        // directions are LIFTED OUT before synthesis (that is the entire point of the asterisk convention),
        // so what came out of the speakers was the same sentence twice in two different voices. A longer,
        // more inventive stage direction was hiding an identical line, and the richer the action a model
        // writes, the better it hides it.
        => IsEchoOf(candidate, existing) || IsEchoOf(Dialogue(candidate), Dialogue(existing));

    /// <summary>Word-set comparison of two forms of a line.</summary>
    private static bool IsEchoOf(string? candidate, string? existing)
    {
        var a = NormaliseWords(candidate);
        var b = NormaliseWords(existing);
        if (a.Count == 0 || b.Count == 0) return false;
        if (a.Count == b.Count && a.SequenceEqual(b)) return true;
        if (a.Count < 4 || b.Count < 4) return false;
        var shared = a.Distinct().Count(w => b.Contains(w));
        return shared / (double)Math.Max(a.Distinct().Count(), b.Distinct().Count()) >= 0.8;
    }

    /// <summary>
    /// What is left of a line once the <c>*...*</c> stage directions are taken out - what gets SPOKEN.
    /// </summary>
    /// <remarks>
    /// ⚠️ Deliberately simple and local rather than reusing the SDK splitter: this is a COMPARISON aid, so
    /// it may be approximate, and it must never alter what is stored or said. A line with no asterisks
    /// comes back unchanged, and so is compared exactly once by the caller.
    /// </remarks>
    private static string Dialogue(string? text)
    {
        if (string.IsNullOrEmpty(text) || text.IndexOf('*') < 0) return text ?? "";
        var sb = new System.Text.StringBuilder(text.Length);
        var inAction = false;
        foreach (var ch in text)
        {
            if (ch == '*') { inAction = !inAction; sb.Append(' '); continue; }
            if (!inAction) sb.Append(ch);
        }
        return sb.ToString();
    }

    /// <summary>
    /// How many recent lines a new reply is checked against for being a repeat.
    /// </summary>
    /// <remarks>
    /// Covers a full round of the maximum room size plus the user's turn, which is the span in which
    /// "somebody already said that" actually means anything.
    /// </remarks>
    public int EchoLookback { get; set; } = 6;

    /// <summary>Lower-case words with punctuation and action markers removed.</summary>
    /// <remarks>
    /// ⚠️ AN APOSTROPHE IS DROPPED, NOT TURNED INTO A SPACE, and that one character decided a real case.
    /// Splitting on it makes "don't" two tokens, so "I don't know." and "I don't care." share three of
    /// four tokens - 75% - and a genuine answer gets thrown away and re-rolled as though it were a
    /// repeat. Contractions are one word.
    /// </remarks>
    private static List<string> NormaliseWords(string? text)
    {
        var words = new List<string>();
        if (string.IsNullOrEmpty(text)) return words;
        var sb = new System.Text.StringBuilder(16);
        foreach (var ch in text)
        {
            // An apostrophe joins, everything else that is not a letter or digit separates. Straight and
            // curly are both apostrophes; a model writes either.
            if (ch == '\'' || ch == '\u2019') continue;
            if (char.IsLetterOrDigit(ch)) { sb.Append(char.ToLowerInvariant(ch)); continue; }
            if (sb.Length > 0) { words.Add(sb.ToString()); sb.Clear(); }
        }
        if (sb.Length > 0) words.Add(sb.ToString());
        return words;
    }
}
