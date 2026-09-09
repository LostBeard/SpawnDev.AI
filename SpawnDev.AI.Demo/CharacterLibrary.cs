using System.Text.Json;
using SpawnDev.AsyncFileSystem;

namespace SpawnDev.AI.Demo;

/// <summary>
/// A character the user made: who it is, how it behaves, what it thinks with, and how it sounds.
/// </summary>
/// <param name="Id">Stable id. Also the file name stem, so it must stay path-safe.</param>
/// <param name="Name">What the room calls them.</param>
/// <param name="Persona">How they should act - goes into the system turn verbatim.</param>
/// <param name="Model">Which model they think with. Empty means "whatever the room is using".</param>
/// <param name="VoiceId">A saved voice id, or null for text-only.</param>
/// <param name="SavedUtc">When it was saved.</param>
/// <param name="AllowedTools">
/// Tools this character may call, by name. Null (the value a character saved before tools existed reads
/// back as) means none - so an older character never silently gains the ability to act on the world.
/// </param>
/// <param name="Avatar">
/// The body this character acts through. Defaults to None, so a character saved before avatars existed
/// stays text-only rather than suddenly appearing on screen.
/// </param>
/// <param name="MotionScale">
/// How animated this character is, 1.0 normal. A character saved before this existed reads back as 0,
/// which <see cref="CharacterLibrary.ToAgent"/> normalises to 1.0 - a stored zero would otherwise mean
/// "never moves", which is not what anyone chose.
/// </param>
public sealed record SavedCharacter(
    string Id, string Name, string Persona, string Model, string? VoiceId, DateTime SavedUtc,
    IReadOnlyList<string>? AllowedTools = null, AvatarKind Avatar = AvatarKind.None,
    double MotionScale = 1.0);

/// <summary>
/// Characters the user has created, persisted to OPFS.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately the same shape as <see cref="VoiceLibrary"/> - same filesystem, same id rules, same
/// list/save/delete surface - because they are the same idea applied to two kinds of thing, and a second
/// storage style would be one more thing to keep correct for no benefit. A character is pure metadata, so
/// unlike a voice it has no binary sibling file.
/// </para>
/// <para>
/// ⚠️ A character REFERENCES a voice by id rather than containing it, so one voice can front several
/// characters and deleting a character never destroys a voice a family member recorded.
/// <see cref="SavedCharacter.VoiceId"/> may therefore dangle if that voice is later deleted - callers must
/// treat a missing voice as "text only" rather than an error, which is what
/// <see cref="ResolveVoice"/> is for.
/// </para>
/// </remarks>
public sealed class CharacterLibrary
{
    private const string Dir = "characters";
    private readonly IAsyncFS _fs;

    /// <summary>New instance over the app's OPFS filesystem.</summary>
    public CharacterLibrary(IAsyncFS fs) => _fs = fs;

    private static string Path(string id) => $"{Dir}/{id}.json";

    /// <summary>Path-safe id from a name, with a short unique suffix so two "Rose"s cannot collide.</summary>
    public static string MakeId(string name) => VoiceLibrary.MakeId(name);

    /// <summary>Create or update a character.</summary>
    public async Task<SavedCharacter> SaveAsync(string id, string name, string persona, string model,
        string? voiceId, IReadOnlyList<string>? allowedTools = null,
        AvatarKind avatar = AvatarKind.None, double motionScale = 1.0)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("a character needs an id", nameof(id));
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("a character needs a name", nameof(name));

        if (!await _fs.DirectoryExists(Dir)) await _fs.CreateDirectory(Dir);
        var saved = new SavedCharacter(id, name.Trim(), (persona ?? "").Trim(), (model ?? "").Trim(),
            string.IsNullOrWhiteSpace(voiceId) ? null : voiceId, DateTime.UtcNow,
            allowedTools is { Count: > 0 } ? allowedTools.ToList() : null, avatar, motionScale);
        await _fs.Write(Path(id), JsonSerializer.Serialize(saved));
        return saved;
    }

    /// <summary>Every saved character, by name.</summary>
    public async Task<List<SavedCharacter>> ListAsync()
    {
        var found = new List<SavedCharacter>();
        if (!await _fs.DirectoryExists(Dir)) return found;

        foreach (var file in await _fs.GetFiles(Dir))
        {
            if (!file.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;
            try
            {
                var c = await _fs.ReadJSON<SavedCharacter>($"{Dir}/{file}");
                if (c != null && !string.IsNullOrEmpty(c.Id)) found.Add(c);
            }
            catch
            {
                // One unreadable character must not hide the rest - it simply is not offered.
            }
        }
        return found.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Delete a character. The voice it referenced is left alone - it may front others.</summary>
    public async Task DeleteAsync(string id)
    {
        try { if (await _fs.FileExists(Path(id))) await _fs.Remove(Path(id)); }
        catch { /* gone from the picker either way; do not fail the click */ }
    }

    /// <summary>
    /// The voice a character should speak with, or null when it should stay text-only.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ Returns null for a voice id that no longer exists rather than throwing. A character whose voice
    /// was deleted must still be usable - silently text-only is a far better outcome than a room that
    /// refuses to start because one member's voice is missing.
    /// </para>
    /// <para>
    /// ⚠️ A BUNDLED voice ships with the app, so it is never in the user's saved list and checking only
    /// that list would silence every character using one - the built-in voices would appear in the picker
    /// and then do nothing, which reads as a broken feature rather than a missing file.
    /// </para>
    /// </remarks>
    public static string? ResolveVoice(SavedCharacter character, IEnumerable<SavedVoice> availableVoices)
        => character.VoiceId != null
           && (BundledVoices.IsBundled(character.VoiceId) || availableVoices.Any(v => v.Id == character.VoiceId))
            ? character.VoiceId
            : null;

    /// <summary>Turn a saved character into a room participant.</summary>
    /// <param name="fallbackModel">Used when the character has no model of its own.</param>
    public static ChatAgent ToAgent(SavedCharacter character, string fallbackModel, string? voiceId)
        => new(character.Id, character.Name,
            string.IsNullOrWhiteSpace(character.Model) ? fallbackModel : character.Model,
            character.Persona, voiceId, character.AllowedTools, character.Avatar,
            // 🔴 A character saved before MotionScale existed deserialises to 0, and 0 means "never
            // moves" - a body that silently stopped acting. Normalise it to normal.
            character.MotionScale > 0 ? character.MotionScale : 1.0);

    /// <summary>
    /// The tool definitions an agent is allowed to use, picked out of everything the server offers.
    /// </summary>
    /// <param name="agent">The character about to take a turn.</param>
    /// <param name="serverTools">Every tool definition the server published, as JSON.</param>
    /// <returns>Only the allowed ones; empty when the character may use none.</returns>
    /// <remarks>
    /// 🔴 FILTERS RATHER THAN TRUSTING THE PROMPT. Handing a model every tool and asking it not to use
    /// most of them is not a control - a tool the model never receives is one it cannot call, however it
    /// is talked into it. Matching is on the name inside the definition, so a tool the server has stopped
    /// offering simply does not appear and the character carries on without it.
    /// </remarks>
    public static List<string> ToolsFor(ChatAgent agent, IReadOnlyList<string> serverTools)
    {
        var allowed = agent.AllowedTools;
        if (allowed is not { Count: > 0 } || serverTools.Count == 0) return new List<string>();

        var wanted = new HashSet<string>(allowed, StringComparer.OrdinalIgnoreCase);
        return serverTools.Where(def => ToolName(def) is { } n && wanted.Contains(n)).ToList();
    }

    /// <summary>The "name" a tool definition declares, or null when it is not shaped like one.</summary>
    private static string? ToolName(string definitionJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(definitionJson);
            var root = doc.RootElement;
            // OpenAI shape {type:"function", function:{name}}, or a bare {name} as MCP lists it.
            if (root.TryGetProperty("function", out var fn) && fn.TryGetProperty("name", out var n1))
                return n1.GetString();
            return root.TryGetProperty("name", out var n2) ? n2.GetString() : null;
        }
        catch (JsonException) { return null; }
    }
}
