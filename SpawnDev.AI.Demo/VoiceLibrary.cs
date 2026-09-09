using System.Runtime.InteropServices;
using System.Text.Json;
using SpawnDev.AsyncFileSystem;

namespace SpawnDev.AI.Demo;

/// <summary>What a saved voice is, apart from its audio.</summary>
/// <param name="Id">Stable id. Also the file name stem, so it must stay path-safe.</param>
/// <param name="DisplayName">What a person sees in the picker.</param>
/// <param name="ReferenceText">
/// The clip's transcript. ⚠️ Must be what the clip ACTUALLY says: anything in the audio and missing here
/// bleeds into the start of every line the voice speaks, invisibly in text and audibly in output.
/// </param>
/// <param name="SampleRate">Sample rate of the stored PCM.</param>
/// <param name="SampleCount">How many samples the PCM file holds - lets a truncated file be spotted.</param>
/// <param name="SavedUtc">When it was saved.</param>
public sealed record SavedVoice(
    string Id, string DisplayName, string ReferenceText, int SampleRate, int SampleCount, DateTime SavedUtc);

/// <summary>
/// Saved voices, persisted to OPFS so they survive a reload.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 WHY THE AUDIO IS STORED AND NOT THE FEATURES. The prompt features are what ZipVoice conditions on and
/// what makes a prepared voice fast, so storing them looks like the obvious win. They are also derived from
/// the model's own feature geometry (<c>ZipVoicePipeline.Config</c>): a model or config change silently
/// invalidates them, and a stale feature blob does not fail - it produces confident wrong-sounding speech.
/// The audio is the source of truth and re-deriving costs one preparation per session, not per reply, so
/// that is the honest trade.
/// </para>
/// <para>
/// ⚠️ Metadata and audio are SEPARATE files on purpose. Base64-ing PCM into the JSON would inflate it by a
/// third and force the whole clip through a string on every listing, when a listing only ever needs the
/// name. <see cref="ListAsync"/> reads metadata alone.
/// </para>
/// <para>
/// ⚠️ OPFS is invisible to DevTools' "Clear site data" - the page's own storage panel is how these are
/// managed, which is why <see cref="DeleteAsync"/> exists rather than leaving it to the browser.
/// </para>
/// </remarks>
public sealed class VoiceLibrary
{
    private const string Dir = "voices";
    private readonly IAsyncFS _fs;

    /// <summary>New instance over the app's OPFS filesystem.</summary>
    public VoiceLibrary(IAsyncFS fs) => _fs = fs;

    private static string MetaPath(string id) => $"{Dir}/{id}.json";
    private static string PcmPath(string id) => $"{Dir}/{id}.pcm";

    /// <summary>
    /// Path-safe id from a display name plus a short unique suffix, so two "Mum"s cannot collide and a name
    /// with a slash or a quote in it cannot escape the directory.
    /// </summary>
    public static string MakeId(string displayName)
    {
        var stem = new string((displayName ?? "").Trim().ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');
        if (stem.Length > 24) stem = stem[..24];
        if (stem.Length == 0) stem = "voice";
        return $"{stem}-{Guid.NewGuid().ToString("n")[..6]}";
    }

    /// <summary>Save a voice's reference clip and metadata.</summary>
    public async Task<SavedVoice> SaveAsync(string id, string displayName, string referenceText,
        float[] samples, int sampleRate)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("a voice needs an id", nameof(id));
        if (samples == null || samples.Length == 0)
            throw new ArgumentException("a voice needs reference audio", nameof(samples));

        if (!await _fs.DirectoryExists(Dir)) await _fs.CreateDirectory(Dir);

        var meta = new SavedVoice(id, string.IsNullOrWhiteSpace(displayName) ? id : displayName,
            referenceText ?? "", sampleRate, samples.Length, DateTime.UtcNow);

        // PCM first, metadata second: metadata is what ListAsync trusts, so writing it last means a listing
        // can never name a voice whose audio is missing or half-written.
        await _fs.Write(PcmPath(id), MemoryMarshal.AsBytes(samples.AsSpan()).ToArray());
        await _fs.Write(MetaPath(id), JsonSerializer.Serialize(meta));
        return meta;
    }

    /// <summary>Every saved voice, metadata only - no audio is read.</summary>
    public async Task<List<SavedVoice>> ListAsync()
    {
        var found = new List<SavedVoice>();
        if (!await _fs.DirectoryExists(Dir)) return found;

        foreach (var file in await _fs.GetFiles(Dir))
        {
            if (!file.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;
            try
            {
                var meta = await _fs.ReadJSON<SavedVoice>($"{Dir}/{file}");
                if (meta != null && !string.IsNullOrEmpty(meta.Id)) found.Add(meta);
            }
            catch
            {
                // One unreadable voice must not hide the rest. Skipping it is right; the picker simply does
                // not offer it, and DeleteAsync can clear it.
            }
        }
        return found.OrderBy(v => v.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Read a saved voice's reference PCM back. Null when it is missing or truncated.</summary>
    public async Task<float[]?> ReadSamplesAsync(SavedVoice voice)
    {
        if (!await _fs.FileExists(PcmPath(voice.Id))) return null;
        var bytes = await _fs.ReadBytes(PcmPath(voice.Id));
        if (bytes == null || bytes.Length < voice.SampleCount * sizeof(float)) return null;
        var samples = new float[voice.SampleCount];
        MemoryMarshal.Cast<byte, float>(bytes.AsSpan(0, voice.SampleCount * sizeof(float))).CopyTo(samples);
        return samples;
    }

    /// <summary>Delete a saved voice, both files. Missing files are not an error.</summary>
    public async Task DeleteAsync(string id)
    {
        foreach (var path in new[] { MetaPath(id), PcmPath(id) })
        {
            try { if (await _fs.FileExists(path)) await _fs.Remove(path); }
            catch { /* a voice half-deleted is still gone from the picker; do not fail the click */ }
        }
    }
}
