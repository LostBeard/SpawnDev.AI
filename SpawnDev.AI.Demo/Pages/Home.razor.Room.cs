using Microsoft.AspNetCore.Components;
using SpawnDev.AI;

namespace SpawnDev.AI.Demo.Pages;

/// <summary>
/// The group-chat half of the demo page: characters the user made, the scene they are in, and the round
/// that lets them talk to each other and to the user.
/// </summary>
/// <remarks>
/// A separate partial because it is a separate feature with its own state. <see cref="AgentRoom"/> owns the
/// part that is easy to get subtly wrong - who said what, from whose point of view - and is unit-tested
/// without a model; everything here is orchestration: run the plan, render it, speak it.
/// </remarks>
public partial class Home
{
    [Inject] CharacterLibrary Characters { get; set; } = default!;
    [Inject] ReachyDriver Robot { get; set; } = default!;

    /// <summary>The robot's address, as typed. Remembered only for this session.</summary>
    string _robotAddress = "";

    bool _robotBusy;

    /// <summary>Connect to or park the physical robot.</summary>
    async Task ToggleRobotAsync()
    {
        if (_robotBusy) return;
        _robotBusy = true;
        StateHasChanged();
        try
        {
            if (Robot.IsConnected) await Robot.DisconnectAsync();
            else await Robot.ConnectAsync(_robotAddress);
            _status = Robot.Status;
        }
        finally { _robotBusy = false; StateHasChanged(); }
    }

    /// <summary>The room. With no agents in it the page behaves exactly as it always did.</summary>
    readonly AgentRoom _room = new();

    /// <summary>Characters saved on this device, whether or not they are currently in the room.</summary>
    List<SavedCharacter> _characters = new();

    bool _showRoom, _savingCharacter;

    // The character editor's fields. An empty `_editingCharId` means "creating a new one".
    string _editingCharId = "", _charName = "", _charPersona = "", _charModel = "", _charVoiceId = "";
    AvatarKind _charAvatar = AvatarKind.None;
    double _charMotionScale = 1.0;

    /// <summary>Tools the character being edited may call.</summary>
    readonly HashSet<string> _charTools = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Every tool the server offers, by name. Empty until the server is up.</summary>
    List<string> _serverToolNames = new();

    /// <summary>
    /// Read the tool names the server publishes, so a character can be granted them by name.
    /// </summary>
    async Task LoadToolNamesAsync()
    {
        try
        {
            var defs = await Ai.ListToolsAsync();
            _serverToolNames = defs.Select(ToolNameOf).Where(n => n.Length > 0).Distinct().ToList();
        }
        catch (Exception ex) { Console.WriteLine($"[ROOM] could not list tools: {ex.Message}"); }
    }

    static string ToolNameOf(string definitionJson)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(definitionJson);
            var root = doc.RootElement;
            if (root.TryGetProperty("function", out var fn) && fn.TryGetProperty("name", out var n1))
                return n1.GetString() ?? "";
            return root.TryGetProperty("name", out var n2) ? n2.GetString() ?? "" : "";
        }
        catch (System.Text.Json.JsonException) { return ""; }
    }

    /// <summary>Grant or revoke one tool for the character being edited.</summary>
    void ToggleCharTool(string name, bool granted)
    {
        if (granted) _charTools.Add(name);
        else _charTools.Remove(name);
        StateHasChanged();
    }

    /// <summary>True when at least one character is in the room, so a turn runs the round instead.</summary>
    bool RoomActive => _room.Agents.Count > 0;

    /// <summary>The motion each character's body is currently performing, by character id.</summary>
    readonly Dictionary<string, SpawnDev.Reachy.Gesture> _avatarAction = new();

    /// <summary>Which character is speaking right now, so its body idles and the others stay still.</summary>
    string _speakingAgentId = "";

    /// <summary>The room members that have a body to draw, in speaking order.</summary>
    /// <remarks>
    /// The robot holder is resolved here rather than at render time so the row and the actual motion agree:
    /// a second character wanting the one physical Reachy is drawn on screen instead, and it must LOOK that
    /// way too or the user cannot tell which one is really driving the hardware.
    /// </remarks>
    IEnumerable<(ChatAgent Agent, AvatarKind Kind)> EmbodiedAgents()
    {
        var holder = AvatarActions.SoleRobotHolder(_room.Agents);
        foreach (var a in _room.Agents)
        {
            var kind = AvatarActions.EffectiveAvatar(a, holder);
            if (kind != AvatarKind.None) yield return (a, kind);
        }
    }

    /// <summary>The motion a character's body should be showing.</summary>
    SpawnDev.Reachy.Gesture ActionFor(string id)
        => _avatarAction.TryGetValue(id, out var a) ? a : SpawnDev.Reachy.Gesture.None;

    /// <summary>
    /// Perform a reply's written actions, in order, while the character speaks.
    /// </summary>
    /// <remarks>
    /// 🔴 FIRE-AND-FORGET, SO IT CATCHES EVERYTHING. This runs unawaited so the body moves WHILE the voice
    /// plays rather than before it - and an unhandled exception on an unawaited task does not fail a turn
    /// in Blazor WASM, it EXITS THE RUNTIME and takes the whole page with it. A decorative animation must
    /// never be able to do that, so the entire body is wrapped.
    /// </remarks>
    async Task PlayActionsAsync(ChatAgent agent, IReadOnlyList<string> written, CancellationToken ct)
    {
        var agentId = agent.Id;
        // Only the character actually holding the robot drives it; everyone else is drawn.
        var drivesRobot = Robot.IsConnected
            && AvatarActions.EffectiveAvatar(agent, AvatarActions.SoleRobotHolder(_room.Agents))
               == AvatarKind.Reachy;
        try
        {
            foreach (var text in written)
            {
                if (ct.IsCancellationRequested) break;

                var gesture = SpawnDev.Reachy.GestureClassifier.Classify(text);
                if (gesture == SpawnDev.Reachy.Gesture.None) continue;

                _avatarAction[agentId] = gesture;
                await InvokeAsync(StateHasChanged);

                if (drivesRobot)
                {
                    // The character's own animation level - ReachyBody clamps it to a safe range.
                    // ⚠️ AWAITED, not fired off. ReachyBody sequences its own movements by waiting out
                    // each one's duration - the daemon's goto only queues - so overlapping calls would
                    // make a gesture interrupt itself, a defect this stack has already paid for once.
                    await Robot.PerformAsync(text, agent.MotionScale, ct);
                }
                else
                {
                    try { await Task.Delay(1300, ct); }
                    catch (OperationCanceledException) { break; }
                }
            }
        }
        catch (Exception ex) { Console.WriteLine($"[ROOM] avatar animation stopped: {ex.Message}"); }
        finally
        {
            // Always return to rest, or the last motion of the round stays frozen on the body.
            _avatarAction[agentId] = SpawnDev.Reachy.Gesture.None;
            try { await InvokeAsync(StateHasChanged); } catch { /* the page is going away */ }
        }
    }

    /// <summary>Is this character currently in the room?</summary>
    bool IsInRoom(string id) => _room.Agents.Any(a => a.Id == id);

    /// <summary>Load the saved characters. Cheap - metadata only, no audio and no model.</summary>
    async Task LoadCharactersAsync()
    {
        try { _characters = await Characters.ListAsync(); }
        catch (Exception ex) { Console.WriteLine($"[ROOM] listing characters failed: {ex.Message}"); }
    }

    /// <summary>Put the editor into "new character" state.</summary>
    void NewCharacter()
    {
        _editingCharId = ""; _charName = ""; _charPersona = "";
        _charModel = ""; _charVoiceId = ""; _charAvatar = AvatarKind.None; _charMotionScale = 1.0;
        _charTools.Clear();
        _showRoom = true;
        StateHasChanged();
    }

    /// <summary>Load an existing character into the editor.</summary>
    void EditCharacter(SavedCharacter c)
    {
        _editingCharId = c.Id; _charName = c.Name; _charPersona = c.Persona;
        _charModel = c.Model; _charVoiceId = c.VoiceId ?? ""; _charAvatar = c.Avatar;
        _charMotionScale = c.MotionScale > 0 ? c.MotionScale : 1.0;
        _charTools.Clear();
        foreach (var t in c.AllowedTools ?? System.Array.Empty<string>()) _charTools.Add(t);
        StateHasChanged();
    }

    /// <summary>Create or update the character in the editor, and refresh it if it is already in the room.</summary>
    async Task SaveCharacterAsync()
    {
        if (string.IsNullOrWhiteSpace(_charName))
        {
            _status = "A character needs a name.";
            StateHasChanged();
            return;
        }
        _savingCharacter = true;
        StateHasChanged();
        try
        {
            var id = string.IsNullOrEmpty(_editingCharId) ? CharacterLibrary.MakeId(_charName) : _editingCharId;
            var saved = await Characters.SaveAsync(id, _charName, _charPersona, _charModel, _charVoiceId,
                allowedTools: _charTools.ToList(), avatar: _charAvatar, motionScale: _charMotionScale);
            await LoadCharactersAsync();

            // ⚠️ A character already IN the room holds a COPY of its settings - ChatAgent is a record built
            // when it was added. Editing the saved character without replacing that copy would show the new
            // persona in the list while the room went on using the old one: the edit would look applied and
            // do nothing. Replace it in place, keeping its position so the speaking order does not jump.
            var at = _room.Agents.FindIndex(a => a.Id == id);
            if (at >= 0)
                _room.Agents[at] = CharacterLibrary.ToAgent(saved, _model,
                    CharacterLibrary.ResolveVoice(saved, _savedVoices));

            _editingCharId = saved.Id;
            _status = $"Saved {saved.Name}.";
        }
        catch (Exception ex) { _status = $"Saving the character failed: {ex.Message}"; }
        finally { _savingCharacter = false; StateHasChanged(); }
    }

    /// <summary>Delete a character. The voice it referenced is left alone - it may front others.</summary>
    async Task DeleteCharacterAsync(string id)
    {
        try
        {
            await Characters.DeleteAsync(id);
            _room.Agents.RemoveAll(a => a.Id == id);
            if (_editingCharId == id) NewCharacter();
            await LoadCharactersAsync();
        }
        catch (Exception ex) { _status = $"Deleting the character failed: {ex.Message}"; }
        StateHasChanged();
    }

    /// <summary>Add a character to the room, or take it out again.</summary>
    void ToggleInRoom(SavedCharacter c)
    {
        if (IsInRoom(c.Id)) _room.Agents.RemoveAll(a => a.Id == c.Id);
        else
        {
            // A character's own model wins; one without a model follows the page's, so a user who never
            // picks per-character models still gets a working room. A voice that has since been deleted
            // resolves to null and that member is simply text-only, rather than failing at its first reply
            // with a voice the engine has never prepared.
            _room.Agents.Add(CharacterLibrary.ToAgent(c, _model, CharacterLibrary.ResolveVoice(c, _savedVoices)));
        }
        StateHasChanged();
    }

    /// <summary>
    /// Run one round of the group chat and put it on screen.
    /// </summary>
    /// <remarks>
    /// The sequencing - who speaks, in what order, and what each of them has heard - is
    /// <see cref="AgentRoom.RunRoundAsync"/>, deliberately not duplicated here: it is the part that is easy
    /// to get subtly wrong and it is unit-tested without a model. This method supplies the two things the
    /// room has no business knowing about: how to run a model, and what to do with a reply once it exists.
    /// </remarks>
    async Task RunRoomRoundAsync(string userText)
    {
        _messages.Add(new Msg { Role = "user", Text = userText });
        _room.Say(AgentRoom.UserId, "You", userText);

        _busy = true;
        _stoppedByUser = false;
        _generationCts = new CancellationTokenSource();
        StateHasChanged();
        await ScrollToBottom();

        // ⚠️ EVERY member's model, not just the page's. A character can be configured with a model
        // nobody has downloaded, and the round would otherwise stop on that member's turn having already
        // started pulling gigabytes.
        var missing = _room.Agents
            .Select(a => a.Model)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(WouldDownload)
            .ToList();
        if (missing.Count > 0)
        {
            _showModels = true;
            _messages.Add(new Msg
            {
                Role = "system",
                Text = $"These characters use models this device does not have yet: "
                     + string.Join(", ", missing.Select(m => $"**{m}** ({FormatSize(ChoiceFor(m)!.SizeBytes)})"))
                     + ". Download them in the model panel (📦), or point those characters at a model that "
                     + "is already here.",
            });
            _busy = false;
            _generationCts?.Dispose();
            _generationCts = null;
            await InvokeAsync(StateHasChanged);
            await ScrollToBottom();
            return;
        }

        var plan = _room.PlanRound();
        var spoken = 0;
        var clock = new System.Diagnostics.Stopwatch();

        // Fetched ONCE per round, and only when somebody in the room can actually use one. A room of
        // characters that just talk should not pay an MCP round trip per turn to be handed a list it is
        // not allowed to touch.
        IReadOnlyList<string> serverTools = System.Array.Empty<string>();
        if (plan.Any(a => a.AllowedTools is { Count: > 0 }))
        {
            try { serverTools = await Ai.ListToolsAsync(_generationCts.Token); }
            catch (Exception ex)
            {
                // Losing tools degrades a character to conversation; it must not abort the round.
                Console.WriteLine($"[ROOM] could not list tools, characters will talk only: {ex.Message}");
            }
        }

        try
        {
            await _room.RunRoundAsync(
                // ⚠️ Every state change in these callbacks goes through InvokeAsync. They resume after an
                // await that crossed into the worker, so there is no guarantee of being on the dispatcher -
                // and calling StateHasChanged off it throws, killing the round after the first speaker.
                generate: async (agent, prompt) =>
                {
                    _streamingWho = agent.Name;
                    _streaming = "";
                    _busyNote = $"{agent.Name} is thinking… ({++spoken}/{plan.Count}, {agent.Model})";
                    await InvokeAsync(StateHasChanged);

                    // Only what THIS character is allowed to call. A tool it never receives is one it
                    // cannot be talked into calling.
                    var agentTools = CharacterLibrary.ToolsFor(agent, serverTools);
                    if (agentTools.Count > 0)
                        _busyNote = $"{agent.Name} is thinking… ({spoken}/{plan.Count}, {agent.Model}, "
                                  + $"{agentTools.Count} tool(s))";

                    clock.Restart();
                    var renderClock = System.Diagnostics.Stopwatch.StartNew();
                    await Ai.ChatStreamAsync(agent.Model, prompt,
                        new AiGenerationOptions
                        {
                            MaxOutputTokens = _maxTokens,
                            Strategy = "top_p",
                            // Warmer than the solo assistant on purpose: a room of characters all answering
                            // at 0.3 converges on the same careful sentence and stops sounding like
                            // different people, which is the entire point of having several of them.
                            Temperature = Math.Max(_temperature, 0.7f),
                            TopP = 0.9f,
                            RepetitionPenalty = 1.15f,
                        },
                        onDelta: delta =>
                        {
                            _streaming += delta;
                            if (renderClock.ElapsedMilliseconds >= 100)
                            {
                                renderClock.Restart();
                                InvokeAsync(async () => { StateHasChanged(); await ScrollToBottom(); });
                            }
                        },
                        // ⚠️ Tools turn token streaming OFF server-side - a tool call only resolves once
                        // the whole message exists. onDelta then fires once with the finished reply, which
                        // is why the caption above says how many tools are in play: otherwise a
                        // tool-enabled turn looks stalled next to a streaming one.
                        toolsJson: agentTools.Count > 0 ? agentTools : null,
                        ct: _generationCts.Token);
                    clock.Stop();
                    var text = _streaming;
                    _streaming = ""; _streamingWho = "";
                    return text;
                },
                onSaid: async (agent, line) =>
                {
                    _messages.Add(new Msg
                    {
                        Role = "assistant",
                        Who = line.SpeakerName,
                        Text = line.Text,
                        Ms = clock.Elapsed.TotalMilliseconds,
                        Stopped = _generationCts?.IsCancellationRequested ?? false,
                    });
                    // Set BEFORE the render, or the speaking body starts its idle motion a frame late -
                    // and for a text-only character with no motions, never at all.
                    _speakingAgentId = agent.Id;
                    await InvokeAsync(StateHasChanged);
                    await ScrollToBottom();

                    // The body acts WHILE the voice plays, so start it unawaited and let it run alongside
                    // the speech below rather than miming the whole reply before saying a word.
                    // The RAW directions, not pre-classified gestures: the on-screen body classifies
                    // them for its animation and the SDK classifies them for the robot, from the same text.
                    var (_, written) = SpawnDev.Reachy.SpokenText.Split(line.Text);
                    var acting = written.Length > 0
                        ? PlayActionsAsync(agent, written, _generationCts?.Token ?? default)
                        : Task.CompletedTask;

                    // Each member speaks in its OWN voice; one with no voice simply stays text-only, which
                    // is a working room rather than a stalled one.
                    // The bubble keeps the actions - reading "*tilts head*" is the point of a scene -
                    // while SpeakReplyAsync strips them before synthesis.
                    // resumeListening: false - the mic reopens ONCE when the whole round is over, below.
                    // Reopening it here would record the user while the next character is still to speak,
                    // and capture that character's synthesised voice as if the user had said it.
                    if (!string.IsNullOrEmpty(agent.VoiceId)
                        && !(_generationCts?.IsCancellationRequested ?? false))
                        await SpeakReplyAsync(line.Text, agent.VoiceId, resumeListening: false);

                    // A text-only character has no speech to act against, so give the motions their own
                    // time rather than flashing them away the instant the reply lands.
                    await acting;
                    _speakingAgentId = "";
                },
                ct: _generationCts.Token);
        }
        catch (Exception ex)
        {
            _messages.Add(new Msg { Role = "system", Text = $"Room error: {ex.Message}" });
        }
        finally
        {
            _generationCts?.Dispose();
            _generationCts = null;
            _streaming = ""; _streamingWho = ""; _busy = false; _busyNote = "";
            _speakingAgentId = "";
            _avatarAction.Clear();
            await InvokeAsync(StateHasChanged);
            await ScrollToBottom();
        }

        // The round is the turn. Hand the microphone back now that every member has had its say and the
        // speakers are quiet - not after each one.
        if (_handsFree && !_listening) await ResumeListeningAsync("after the room finished the round");
    }
}
