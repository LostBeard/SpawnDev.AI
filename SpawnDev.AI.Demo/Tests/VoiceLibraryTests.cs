namespace SpawnDev.AI.Demo.Tests;

/// <summary>
/// Saved voices survive a round trip through OPFS.
/// </summary>
/// <remarks>
/// ⚠️ NOT heavy - no model is loaded. This covers the storage half of saved voices, which is exactly the
/// half a model-backed test cannot see: a voice whose PCM came back truncated or byte-swapped would still
/// PREPARE and still SPEAK, in a subtly wrong voice, and every audible check would pass.
/// </remarks>
public sealed class VoiceLibraryTests
{
    private readonly VoiceLibrary _voices;

    /// <summary>New instance. <paramref name="voices"/> is the app's own library over OPFS.</summary>
    public VoiceLibraryTests(VoiceLibrary voices) => _voices = voices;

    /// <summary>A saved voice lists, reads back BIT-IDENTICAL, and deletes.</summary>
    /// <remarks>
    /// 🔴 The assertion that matters is per-sample equality, not length. PCM is stored as raw float32 bytes,
    /// so the failure modes are truncation and a byte-order or stride mistake - all of which preserve the
    /// sample COUNT while changing the audio. A length check would pass on every one of them.
    /// ⚠️ The fixture is a ramp plus alternating signs rather than silence or a constant: zeros survive a
    /// byte-order bug unchanged, and a constant survives a stride bug, so neither could fail this test.
    /// </remarks>
    [AiTest(Timeout = 60_000)]
    public async Task SavedVoiceSurvivesAnOpfsRoundTrip()
    {
        const int rate = 16000;
        var samples = new float[4000];
        for (int i = 0; i < samples.Length; i++)
            samples[i] = (i % 2 == 0 ? 1f : -1f) * (i / (float)samples.Length);

        var id = VoiceLibrary.MakeId("Round Trip");
        if (id.Contains('/') || id.Contains('\\') || id.Contains(' '))
            throw new Exception($"MakeId produced an unsafe file stem: '{id}'");

        try
        {
            var saved = await _voices.SaveAsync(id, "Round Trip", "the exact transcript", samples, rate);
            if (saved.SampleCount != samples.Length)
                throw new Exception($"saved metadata says {saved.SampleCount} samples, wrote {samples.Length}");

            var listed = await _voices.ListAsync();
            var mine = listed.FirstOrDefault(v => v.Id == id);
            if (mine == null)
                throw new Exception($"saved voice '{id}' is not listed; got "
                    + (listed.Count == 0 ? "(none)" : string.Join(", ", listed.Select(v => v.Id))));
            if (mine.ReferenceText != "the exact transcript")
                throw new Exception($"reference text came back as '{mine.ReferenceText}' - a transcript that "
                    + "is not verbatim bleeds into the start of every line the voice speaks");
            if (mine.SampleRate != rate)
                throw new Exception($"sample rate came back {mine.SampleRate}, saved {rate}");

            var read = await _voices.ReadSamplesAsync(mine);
            if (read == null) throw new Exception("saved voice read back NULL - its audio is missing");
            if (read.Length != samples.Length)
                throw new Exception($"read back {read.Length} samples, saved {samples.Length}");
            for (int i = 0; i < samples.Length; i++)
            {
                if (read[i] != samples[i])
                    throw new Exception($"sample {i} came back {read[i]} but {samples[i]} was saved - the PCM "
                        + "round trip is not exact, so every saved voice is subtly the wrong voice");
            }
        }
        finally
        {
            // Always clean up: a test that leaves voices behind pollutes the picker of whoever runs it next.
            await _voices.DeleteAsync(id);
        }

        var after = await _voices.ListAsync();
        if (after.Any(v => v.Id == id))
            throw new Exception($"deleted voice '{id}' is still listed - \"remove this voice\" would be a lie");
    }
}
