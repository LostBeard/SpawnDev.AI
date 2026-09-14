using System.Text.Json;
using SpawnDev.AsyncFileSystem;

namespace SpawnDev.AI.Demo;

/// <summary>
/// The small choices a user makes once and expects to still be there next visit.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 WHY THIS EXISTS. Every selection in this app lived only in a component field, so a reload put all
/// of them back to their defaults. That is a papercut for most settings and a real defect for the voice:
/// a user records and saves their own voice, picks it, reloads, and is quietly speaking as the bundled
/// voice again with nothing having said so. "It forgot" is indistinguishable from "it ignored me".
/// </para>
/// <para>
/// Deliberately the same shape as <see cref="ModelConsent"/> - same filesystem, same load-once,
/// same never-throw - because they are the same idea, and a second storage style would be one more thing
/// to keep correct for no benefit.
/// </para>
/// <para>
/// ⚠️ PREFERENCES ONLY. Nothing here may be load-bearing: every reader must work when this returns null,
/// because it silently does on a first visit, a private window, or a corrupt file. It is a convenience
/// store, never a source of truth - <see cref="ModelConsent"/> stays separate precisely because consent
/// is a fact about what the user agreed to, not a preference.
/// </para>
/// </remarks>
public sealed class AppPreferences
{
    private const string Path = "preferences.json";

    /// <summary>The voice replies are spoken in.</summary>
    public const string VoiceKey = "voiceId";

    /// <summary>The chat model the picker is set to.</summary>
    public const string ModelKey = "chatModel";

    private readonly IAsyncFS _fs;
    private Dictionary<string, string> _values = new(StringComparer.Ordinal);
    private bool _loaded;

    /// <summary>New instance over the app's OPFS filesystem.</summary>
    public AppPreferences(IAsyncFS fs) => _fs = fs;

    /// <summary>Load the stored preferences. Safe to call repeatedly; only the first read touches storage.</summary>
    public async Task LoadAsync()
    {
        if (_loaded) return;
        _loaded = true;
        try
        {
            if (!await _fs.FileExists(Path)) return;
            var stored = await _fs.ReadJSON<Dictionary<string, string>>(Path);
            if (stored != null) _values = new Dictionary<string, string>(stored, StringComparer.Ordinal);
        }
        catch (Exception ex)
        {
            // A corrupt file means defaults, which is exactly what a first visit gets. Never fatal.
            Console.WriteLine($"[prefs] could not read preferences, using defaults: {ex.Message}");
        }
    }

    /// <summary>
    /// The stored value for <paramref name="key"/>, or null when there is none.
    /// </summary>
    /// <remarks>
    /// ⚠️ An EMPTY stored value is returned as an empty string, not as null, and the difference matters
    /// for the voice: "" is a real choice there (clone my last turn), so collapsing it to null would turn
    /// a deliberate selection back into "nothing chosen" on every reload - the exact bug this class was
    /// added to stop.
    /// </remarks>
    public string? Get(string key) => _values.TryGetValue(key, out var v) ? v : null;

    /// <summary>Remember a choice across reloads. Null removes it.</summary>
    public async Task SetAsync(string key, string? value)
    {
        if (value == null) { if (!_values.Remove(key)) return; }
        else
        {
            if (_values.TryGetValue(key, out var existing) && existing == value) return;
            _values[key] = value;
        }
        try { await _fs.Write(Path, JsonSerializer.Serialize(_values)); }
        catch (Exception ex)
        {
            // Keep it in memory either way: failing to persist must not undo the choice mid-session.
            Console.WriteLine($"[prefs] could not persist '{key}': {ex.Message}");
        }
    }
}
