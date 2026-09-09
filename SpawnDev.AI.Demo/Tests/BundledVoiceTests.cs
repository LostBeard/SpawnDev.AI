namespace SpawnDev.AI.Demo.Tests;

/// <summary>
/// The voices shipped with the app are actually present, decodable, and licensed to be there.
/// </summary>
/// <remarks>
/// ⚠️ NOT heavy - no model. These check the ASSET and its metadata, which is the half that fails silently:
/// a clip missing from the publish output makes the picker offer a voice that errors on click, and a wrong
/// or absent licence is not something any amount of listening will reveal. Whether the clip SOUNDS right
/// once cloned is <c>AiVoiceTests</c>, which clones from this same manifest entry.
/// </remarks>
public sealed class BundledVoiceTests
{
    private readonly HttpClient _http;

    /// <summary>New instance. <paramref name="http"/> is the app's own client, based at the app root.</summary>
    public BundledVoiceTests(HttpClient http) => _http = http;

    /// <summary>Every included voice states a licence and a source, and has a distinct prefixed id.</summary>
    /// <remarks>
    /// 🔴 THE LICENCE IS THE POINT. These clips are redistributed inside a published app and cloned by a
    /// voice model. An entry without a stated licence and attribution is a legal problem that no runtime
    /// check will ever surface, so it is caught here, where adding one to the list is the only way to hit it.
    /// </remarks>
    [AiTest(Timeout = 30_000)]
    public Task EveryIncludedVoiceIsLicensedAndAttributed()
    {
        if (BundledVoices.All.Count == 0)
            throw new Exception("no included voices - a fresh install then has nothing to speak with until "
                + "the user records someone, which is the gap these exist to close");

        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var v in BundledVoices.All)
        {
            if (!BundledVoices.IsBundled(v.Id))
                throw new Exception($"'{v.Id}' is not prefixed '{BundledVoices.IdPrefix}' - bundled and saved "
                    + "voices share one id space, so an unprefixed id can collide with a voice the user saved");
            if (!seenIds.Add(v.Id)) throw new Exception($"two included voices share the id '{v.Id}'");
            if (!seenUrls.Add(v.Url)) throw new Exception($"two included voices share the clip '{v.Url}'");

            if (string.IsNullOrWhiteSpace(v.DisplayName))
                throw new Exception($"'{v.Id}' has no display name - the picker would show a blank row");
            if (string.IsNullOrWhiteSpace(v.Licence))
                throw new Exception($"'{v.Id}' states NO LICENCE. This clip is redistributed in a published "
                    + "app and cloned by a voice model; it does not ship without one.");
            if (string.IsNullOrWhiteSpace(v.Attribution))
                throw new Exception($"'{v.Id}' has no attribution - a licence with no named source cannot "
                    + "be checked by anyone later");
            if (string.IsNullOrWhiteSpace(v.Transcript))
                throw new Exception($"'{v.Id}' has no transcript. The reference text is what conditions the "
                    + "clone; an empty one is not a cosmetic omission.");
            if (BundledVoices.Find(v.Id) != v)
                throw new Exception($"Find('{v.Id}') did not return that entry - the picker resolves ids "
                    + "through it, so a miss makes the voice unselectable");
        }
        return Task.CompletedTask;
    }

    /// <summary>Each included clip is actually served and decodes to real audio.</summary>
    /// <remarks>
    /// 🔴 THIS IS THE ONE THAT CATCHES A BROKEN PUBLISH. The manifest is C# and always compiles; the audio
    /// is a file under wwwroot that a publish, a trim setting, or a moved folder can leave behind. Without
    /// this, the first sign is a user clicking an included voice and getting an error - the app would offer
    /// something it cannot deliver.
    /// </remarks>
    [AiTest(Timeout = 60_000)]
    public async Task EveryIncludedClipIsServedAndDecodes()
    {
        foreach (var v in BundledVoices.All)
        {
            byte[] wav;
            try { wav = await _http.GetByteArrayAsync(v.Url); }
            catch (Exception ex)
            {
                throw new Exception($"'{v.DisplayName}' clip {v.Url} is not being served ({ex.Message}) - the "
                    + "picker offers this voice, so it would fail on click");
            }

            var (samples, rate) = WavCodec.Decode(wav);
            if (samples.Length == 0) throw new Exception($"{v.Url} decoded to zero samples");
            if (rate <= 0) throw new Exception($"{v.Url} reports a sample rate of {rate}");

            // A reference clip has to be long enough to characterise a voice and short enough to stay a
            // prompt. Both ends matter: a fraction of a second conditions nothing, and a long clip is
            // wasted work on every preparation.
            var seconds = samples.Length / (double)rate;
            if (seconds < 1.0 || seconds > 30.0)
                throw new Exception($"'{v.DisplayName}' is {seconds:F1}s - a reference clip should be a few "
                    + "seconds; this is not usable as a voice prompt");

            // Silence decodes perfectly and clones nothing. Peak is the cheap, honest check that the file
            // holds speech rather than a correctly-formatted empty buffer.
            var peak = 0f;
            foreach (var s in samples) { var a = Math.Abs(s); if (a > peak) peak = a; }
            if (peak < 0.05f)
                throw new Exception($"'{v.DisplayName}' peaks at {peak:F3} - that is silence, and it would "
                    + "produce a voice conditioned on nothing rather than an error");

            Console.WriteLine($"[bundled] {v.DisplayName}: {seconds:F1}s @ {rate} Hz, peak {peak:F2} "
                            + $"({v.Licence})");
        }
    }
}
