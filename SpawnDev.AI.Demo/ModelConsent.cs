using System.Text.Json;
using SpawnDev.AsyncFileSystem;

namespace SpawnDev.AI.Demo;

/// <summary>
/// Which model downloads the user has agreed to, remembered across reloads.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 THIS TRACKS CONSENT, NOT CACHE STATE, and the distinction is the whole design. Nothing available to
/// the app can reliably answer "is this model already on this device": the hub streams weights over an
/// HTTP-range path as well as over WebTorrent, and the first leaves the bytes in the browser's own cache
/// with no torrent to enumerate. An earlier attempt to read cache state from the torrent client reported
/// "not downloaded" for the model the demo had been running on all session, and the download guard then
/// refused to use it - caught by the browser gate blocking its own default model.
/// </para>
/// <para>
/// Consent, on the other hand, is a fact the app owns. Asking once per model and remembering the answer
/// is both achievable and what "large downloads are opt-in" actually means. If the browser later evicts a
/// model, re-fetching it happens under an approval the user already gave, which is correct - they agreed
/// to run that model, not to one particular copy of its bytes.
/// </para>
/// </remarks>
public sealed class ModelConsent
{
    private const string Path = "approved-models.json";
    private readonly IAsyncFS _fs;
    private HashSet<string> _approved = new(StringComparer.OrdinalIgnoreCase);
    private bool _loaded;

    /// <summary>New instance over the app's OPFS filesystem.</summary>
    public ModelConsent(IAsyncFS fs) => _fs = fs;

    /// <summary>Load the approvals. Safe to call repeatedly; only the first read touches storage.</summary>
    public async Task LoadAsync()
    {
        if (_loaded) return;
        _loaded = true;
        try
        {
            if (!await _fs.FileExists(Path)) return;
            var names = await _fs.ReadJSON<string[]>(Path);
            if (names != null) _approved = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            // A corrupt record means we ask again - annoying, never dangerous. The opposite default would
            // silently approve a download nobody agreed to.
            Console.WriteLine($"[models] could not read approvals, will ask again: {ex.Message}");
        }
    }

    /// <summary>Has the user agreed to download this model?</summary>
    public bool IsApproved(string name) => _approved.Contains(name);

    /// <summary>Record that the user agreed to this model, and remember it across reloads.</summary>
    public async Task ApproveAsync(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || !_approved.Add(name)) return;
        try { await _fs.Write(Path, JsonSerializer.Serialize(_approved.ToArray())); }
        catch (Exception ex)
        {
            // Keep the in-memory approval either way: failing to persist should not re-prompt mid-session.
            Console.WriteLine($"[models] could not persist approval for {name}: {ex.Message}");
        }
    }

    /// <summary>Forget every approval, so each model is asked about again.</summary>
    public async Task ClearAsync()
    {
        _approved.Clear();
        try { if (await _fs.FileExists(Path)) await _fs.Remove(Path); }
        catch { /* gone either way as far as the picker is concerned */ }
    }
}
