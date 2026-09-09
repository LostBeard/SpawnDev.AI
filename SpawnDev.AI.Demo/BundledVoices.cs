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

    /// <summary>True when this id refers to a bundled voice rather than a saved one.</summary>
    public static bool IsBundled(string? id) => id != null && id.StartsWith(IdPrefix, StringComparison.Ordinal);

    /// <summary>The bundled voice with this id, or null.</summary>
    public static BundledVoice? Find(string? id)
        => id == null ? null : All.FirstOrDefault(v => v.Id == id);
}
