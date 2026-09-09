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

    /// <summary>The room. With no agents in it the page behaves exactly as it always did.</summary>
    readonly AgentRoom _room = new();

    /// <summary>Characters saved on this device, whether or not they are currently in the room.</summary>
    List<SavedCharacter> _characters = new();

    bool _showRoom, _savingCharacter;

    // The character editor's fields. An empty `_editingCharId` means "creating a new one".
    string _editingCharId = "", _charName = "", _charPersona = "", _charModel = "", _charVoiceId = "";

    /// <summary>True when at least one character is in the room, so a turn runs the round instead.</summary>
    bool RoomActive => _room.Agents.Count > 0;

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
        _charModel = ""; _charVoiceId = "";
        _showRoom = true;
        StateHasChanged();
    }

    /// <summary>Load an existing character into the editor.</summary>
    void EditCharacter(SavedCharacter c)
    {
        _editingCharId = c.Id; _charName = c.Name; _charPersona = c.Persona;
        _charModel = c.Model; _charVoiceId = c.VoiceId ?? "";
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
            var saved = await Characters.SaveAsync(id, _charName, _charPersona, _charModel, _charVoiceId);
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

        var plan = _room.PlanRound();
        var spoken = 0;
        var clock = new System.Diagnostics.Stopwatch();

        try
        {
            await _room.RunRoundAsync(
                generate: async (agent, prompt) =>
                {
                    _streamingWho = agent.Name;
                    _streaming = "";
                    _busyNote = $"{agent.Name} is thinking… ({++spoken}/{plan.Count}, {agent.Model})";
                    StateHasChanged();

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
                    StateHasChanged();
                    await ScrollToBottom();

                    // Each member speaks in its OWN voice; one with no voice simply stays text-only, which
                    // is a working room rather than a stalled one.
                    // resumeListening: false - the mic reopens ONCE when the whole round is over, below.
                    // Reopening it here would record the user while the next character is still to speak,
                    // and capture that character's synthesised voice as if the user had said it.
                    if (!string.IsNullOrEmpty(agent.VoiceId)
                        && !(_generationCts?.IsCancellationRequested ?? false))
                        await SpeakReplyAsync(line.Text, agent.VoiceId, resumeListening: false);
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
            StateHasChanged();
            await ScrollToBottom();
        }

        // The round is the turn. Hand the microphone back now that every member has had its say and the
        // speakers are quiet - not after each one.
        if (_handsFree && !_listening) await ResumeListeningAsync("after the room finished the round");
    }
}
