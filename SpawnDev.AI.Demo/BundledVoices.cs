using SpawnDev.AI.Server;

namespace SpawnDev.AI.Demo;

/// <summary>
/// A voice that ships with the app: audio the project is allowed to redistribute, plus the exact words it
/// says.
/// </summary>
/// <param name="Id">Stable id. Shares the namespace with saved voices, so it is prefixed to avoid collision.</param>
/// <param name="DisplayName">What the picker calls it.</param>
/// <param name="Url">App-relative path to the clip.</param>
/// <param name="Transcript">
/// VERBATIM words in the clip. Not a description of it - see the remarks on <see cref="BundledVoices"/>.
/// </param>
/// <param name="Licence">The licence the clip is redistributed under, named exactly.</param>
/// <param name="Attribution">Who recorded it and where it came from.</param>
public sealed record BundledVoice(
    string Id, string DisplayName, string Url, string Transcript, string Licence, string Attribution);

/// <summary>
/// The voices included with the demo, so a user has something to pick before recording anyone.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 EVERY ENTRY CARRIES ITS LICENCE AND ITS SOURCE, and nothing goes in this list without both. These
/// clips are redistributed inside a published app and cloned by a voice model - "it was on the internet"
/// is not a licence, and a recording of a real person who did not agree to be cloned is not made
/// acceptable by being short. Public domain or an explicit permissive licence only.
/// </para>
/// <para>
/// ⚠️ <see cref="BundledVoice.Transcript"/> MUST be what the clip actually says, word for word. This is not
/// a formality: given a transcript containing words the audio does not, ZipVoice SPEAKS THOSE WORDS at the
/// start of everything it generates. That defect shipped in this project once already - every render
/// carried "Others call me Mother Nature" as a preamble - and it cost ~6 points of word error before
/// anyone noticed it was the transcript and not the model. See wwwroot/test-audio/PROVENANCE.md.
/// </para>
/// </remarks>
public static class BundledVoices
{
    /// <summary>Prefix marking an id as a bundled voice rather than one the user recorded.</summary>
    public const string IdPrefix = "bundled:";

    /// <summary>
    /// The included voices.
    /// </summary>
    /// <remarks>
    /// One so far, and it is the clip the whole voice gate already clones from - so it is the one voice in
    /// the app whose licence, transcript and audibility are all established by tests that run every day
    /// rather than by assertion. A second entry needs the same standard of evidence, not just a file.
    /// </remarks>
    public static IReadOnlyList<BundledVoice> All { get; } = new[]
    {
        new BundledVoice(
            Id: IdPrefix + "librivox-shasta",
            DisplayName: "Shasta (LibriVox)",
            Url: "test-audio/librivox-public-domain.wav",
            // Verbatim. LibriVox's spoken preamble is standardised wording, which is why this clip was
            // chosen: the transcript is KNOWN text rather than a transcription that might be wrong.
            Transcript: "All LibriVox recordings are in the public domain.",
            Licence: "Creative Commons Public Domain Mark 1.0",
            Attribution: "LibriVox recording of \"Jacko and Jumpo Kinkytail\" by Howard R. Garis, "
                       + "read by Shasta (Oakland, California); archive.org item jacko_and_jumpo_2007_librivox"),
    };

    /// <summary>
    /// The voice the app speaks in when the user has not chosen one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 EXISTS SO "THE DEFAULT IS NOT CLONING" IS A TESTABLE FACT. The empty string means "clone whoever
    /// is talking, every turn", and it used to be what you got by choosing nothing - Captain: "it still
    /// seem to clone voice of the user every time ... when it should only clone when 'add a voice' as
    /// selected manually". That was one uninitialised field, the kind of thing an unrelated edit restores
    /// silently, so the intent is pinned here and asserted in VoiceLibraryTests rather than left implicit
    /// in a component's field initialiser.
    /// </para>
    /// <para>
    /// ⭐ It is now a BUILT-IN voice rather than the first bundled clip. Both are "not cloning", so the
    /// property's original point is unchanged - but the bundled clip still had to be cloned to speak,
    /// which meant the do-nothing path ran the slow model. See <see cref="BuiltInIds"/>.
    /// </remarks>
    public static string DefaultId => AiVoiceEngine.DefaultKokoroVoiceId;

    /// <summary>
    /// The BUILT-IN voices: named voices the model already knows, with nothing to clone.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⭐ These are the reason the default is no longer a clone. A bundled clip still has to be cloned -
    /// trimmed, mel'd, turned into prompt features - by a model that renders <b>3.6x slower than
    /// realtime</b>; a built-in voice is a name, and renders <b>faster than realtime in a browser</b>.
    /// Speaking is the slowest thing in a turn, so which of the two a user lands on by doing nothing is
    /// most of how the app feels.
    /// </para>
    /// <para>
    /// ⚠️ No licence or transcript field, and that is not an oversight - there is no redistributed
    /// recording of a real person here to licence. The voice ships inside the model (Apache-2.0), which
    /// is exactly what <see cref="All"/>'s remarks are guarding against having to assert about a clip.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<string> BuiltInIds => AiVoiceEngine.KokoroVoiceIds;

    /// <summary>True when this id names a built-in voice rather than anything cloned.</summary>
    public static bool IsBuiltIn(string? id) => AiVoiceEngine.IsKokoroVoice(id);

    /// <summary>A built-in voice id as a picker would show it, e.g. "Heart (US female)".</summary>
    /// <remarks>
    /// The names encode accent and gender in a two-letter prefix - <c>af_</c>/<c>am_</c> American
    /// female/male, <c>bf_</c>/<c>bm_</c> British female/male - which is information a user picking a
    /// voice wants and cannot get from "af_heart".
    /// </remarks>
    public static string BuiltInDisplayName(string id)
    {
        var name = AiVoiceEngine.KokoroVoiceName(id);
        var sep = name.IndexOf('_');
        if (sep <= 0 || sep + 1 >= name.Length) return name;
        var tag = name[..sep];
        var given = char.ToUpperInvariant(name[sep + 1]) + name[(sep + 2)..];
        var accent = tag.Length > 0 && tag[0] == 'b' ? "UK" : "US";
        var gender = tag.Length > 1 && tag[1] == 'm' ? "male" : "female";
        return $"{given} ({accent} {gender})";
    }

    /// <summary>True when this id refers to a bundled voice rather than a saved one.</summary>
    public static bool IsBundled(string? id) => id != null && id.StartsWith(IdPrefix, StringComparison.Ordinal);

    /// <summary>The bundled voice with this id, or null.</summary>
    public static BundledVoice? Find(string? id)
        => id == null ? null : All.FirstOrDefault(v => v.Id == id);
}
