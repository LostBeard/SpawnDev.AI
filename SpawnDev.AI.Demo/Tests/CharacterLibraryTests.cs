namespace SpawnDev.AI.Demo.Tests;

/// <summary>
/// Characters persist, and a character whose voice has gone still works.
/// </summary>
/// <remarks>
/// ⚠️ NOT heavy - pure storage and mapping, no model. The interesting case is the DANGLING voice: a
/// character references a voice by id so one voice can front several characters, which means deleting a
/// voice can leave a character pointing at nothing. That must degrade to text-only, because a room that
/// refuses to start over one missing voice is a far worse outcome than a member who does not speak aloud.
/// </remarks>
public sealed class CharacterLibraryTests
{
    private readonly CharacterLibrary _characters;

    /// <summary>New instance. <paramref name="characters"/> is the app's own library over OPFS.</summary>
    public CharacterLibraryTests(CharacterLibrary characters) => _characters = characters;

    /// <summary>A saved character lists with its settings intact, and deletes.</summary>
    [AiTest(Timeout = 60_000)]
    public async Task SavedCharacterSurvivesAnOpfsRoundTrip()
    {
        var id = CharacterLibrary.MakeId("Test Character");
        if (id.Contains('/') || id.Contains('\\') || id.Contains(' '))
            throw new Exception($"MakeId produced an unsafe file stem: '{id}'");

        try
        {
            await _characters.SaveAsync(id, "Test Character", "You are terse and precise.",
                "qwen3:0.6b-q8_0", "voice-abc");

            var listed = await _characters.ListAsync();
            var mine = listed.FirstOrDefault(c => c.Id == id);
            if (mine == null)
                throw new Exception($"saved character '{id}' is not listed; got "
                    + (listed.Count == 0 ? "(none)" : string.Join(", ", listed.Select(c => c.Id))));

            // Every field is settings the user chose. Losing one silently gives them a character that
            // behaves, sounds, or thinks differently than the one they configured.
            if (mine.Name != "Test Character") throw new Exception($"name came back '{mine.Name}'");
            if (mine.Persona != "You are terse and precise.")
                throw new Exception($"persona came back '{mine.Persona}' - this is the whole behaviour");
            if (mine.Model != "qwen3:0.6b-q8_0") throw new Exception($"model came back '{mine.Model}'");
            if (mine.VoiceId != "voice-abc") throw new Exception($"voice came back '{mine.VoiceId}'");
        }
        finally
        {
            await _characters.DeleteAsync(id);
        }

        if ((await _characters.ListAsync()).Any(c => c.Id == id))
            throw new Exception($"deleted character '{id}' is still listed");
    }

    /// <summary>A character keeps working when the voice it referenced has been deleted.</summary>
    [AiTest(Timeout = 30_000)]
    public Task ACharacterWhoseVoiceIsGoneFallsBackToTextOnly()
    {
        var withVoice = new SavedCharacter("c1", "Ada", "Precise.", "m1", "voice-gone", DateTime.UtcNow);
        var available = new List<SavedVoice>
        {
            new("voice-here", "Here", "transcript", 16000, 100, DateTime.UtcNow),
        };

        if (CharacterLibrary.ResolveVoice(withVoice, available) != null)
            throw new Exception("a character pointing at a DELETED voice must resolve to null (text-only), "
                + "not to a voice id the engine has never prepared - that would fail at the first reply");

        var present = withVoice with { VoiceId = "voice-here" };
        if (CharacterLibrary.ResolveVoice(present, available) != "voice-here")
            throw new Exception("a character whose voice EXISTS must resolve to it");

        // A BUNDLED voice ships with the app and is never in the saved list. Resolving it against that
        // list alone would silence every character using one: the built-in voices would sit in the picker
        // and do nothing, which reads as a broken feature rather than a missing file.
        var bundledId = BundledVoices.All[0].Id;
        var withBundled = withVoice with { VoiceId = bundledId };
        if (CharacterLibrary.ResolveVoice(withBundled, new List<SavedVoice>()) != bundledId)
            throw new Exception("a character using an INCLUDED voice must resolve to it even with nothing "
                + "saved on this device - that is the entire point of shipping voices");

        // A character with no model of its own follows the room, rather than failing or silently
        // picking something the user never chose.
        var agent = CharacterLibrary.ToAgent(withVoice with { Model = "" }, "room-model", null);
        if (agent.Model != "room-model")
            throw new Exception($"a character with no model should use the room's; got '{agent.Model}'");
        if (agent.Name != "Ada" || agent.Persona != "Precise.")
            throw new Exception("mapping a character to an agent lost its name or persona");
        return Task.CompletedTask;
    }
}
