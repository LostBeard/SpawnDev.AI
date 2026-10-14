using System.Text.Json;

namespace SpawnDev.AI.Demo.Tests;

/// <summary>
/// Who-is-speaking logic, on REAL voice embeddings: 72 ReDimNet2-B6 embeddings (onnxruntime) of 12 LibriSpeech speakers,
/// six clips each (<c>test-data/speaker-embeddings.json</c>). Not heavy - no model runs here, the vectors are the data.
/// </summary>
/// <remarks>
/// ⚠️ The numbers asserted below were MEASURED on this fixture (see each test), not chosen to pass. They are floors for a
/// regression, and a test that could not fail would prove nothing: the stranger test fails when the threshold is zero, the
/// margin test fails when the margin rule is removed.
/// </remarks>
public sealed class SpeakerRegistryTests
{
    private const string Fixture = "test-data/speaker-embeddings.json";
    private const int Enrol = 3;    // clips per speaker used to enrol; the other three are the test

    private readonly HttpClient _http;

    public SpeakerRegistryTests(HttpClient http) => _http = http;

    private sealed record Clip(string Speaker, string Id, float[] Embedding);

    private async Task<List<Clip>> LoadAsync()
    {
        using var doc = JsonDocument.Parse(await _http.GetStringAsync(Fixture));
        return doc.RootElement.GetProperty("clips").EnumerateArray().Select(c => new Clip(
            c.GetProperty("speaker").GetString()!, c.GetProperty("id").GetString()!,
            c.GetProperty("embedding").EnumerateArray().Select(v => v.GetSingle()).ToArray())).ToList();
    }

    /// <summary>The first eight speakers are enrolled from three clips; the rest are strangers.</summary>
    private static (SpeakerRegistry Registry, List<Clip> Held, List<Clip> Strangers) Setup(List<Clip> clips, SpeakerRegistry? into = null)
    {
        var registry = into ?? new SpeakerRegistry();
        var speakers = clips.Select(c => c.Speaker).Distinct().ToList();
        var known = speakers.Take(8).ToHashSet();
        var held = new List<Clip>();
        foreach (var s in known)
        {
            var mine = clips.Where(c => c.Speaker == s).ToList();
            foreach (var c in mine.Take(Enrol)) registry.Enroll("spk" + s, c.Embedding, 5);
            held.AddRange(mine.Skip(Enrol));
        }
        return (registry, held, clips.Where(c => !known.Contains(c.Speaker)).ToList());
    }

    /// <summary>An enrolled speaker is recognised by name from clips they were not enrolled with, and never as someone else.</summary>
    [AiTest(Timeout = 30_000)]
    public async Task EnrolledSpeakersAreRecognised()
    {
        var (registry, held, _) = Setup(await LoadAsync());
        int right = 0, wrong = 0;
        foreach (var c in held)
        {
            var m = registry.Identify(c.Embedding);
            if (m.Known && m.Name == "spk" + c.Speaker) right++;
            else if (m.Known) wrong++;
        }
        if (wrong != 0) throw new Exception($"{wrong} clips were called the wrong person");
        if (right < held.Count * 0.9)
            throw new Exception($"only {right}/{held.Count} held-out clips were recognised (floor 90%)");
    }

    /// <summary>Voices nobody enrolled are not given a name.</summary>
    [AiTest(Timeout = 30_000)]
    public async Task StrangersAreNotNamed()
    {
        var (registry, _, strangers) = Setup(await LoadAsync());
        var named = strangers.Count(c => registry.Identify(c.Embedding).Known);
        if (named > strangers.Count / 10)
            throw new Exception($"{named}/{strangers.Count} strangers were named (ceiling 10%)");

        // The test can fail: with no threshold, every stranger is "someone".
        registry.AcceptThreshold = -1f; registry.MinMargin = 0f;
        var namedAnyway = strangers.Count(c => registry.Identify(c.Embedding).Known);
        if (namedAnyway != strangers.Count)
            throw new Exception("with the threshold removed every stranger should be named: the check is not exercising the threshold");
    }

    /// <summary>
    /// The margin rule: when two profiles are nearly as close to a voice as each other, the voice is not named. The fixture makes
    /// the situation on purpose - one speaker enrolled under two names, each from two clips. MEASURED: the clip scores 0.828
    /// against one and 0.713 against the other, a lead of 0.115 - above the default margin (0.10), so the default names it, and a
    /// margin above the observed lead refuses to. The test pins the mechanism and the observed numbers, not a claim that the
    /// default catches every pair of lookalikes (it cannot: clips of one voice differ by more than that).
    /// </summary>
    [AiTest(Timeout = 30_000)]
    public async Task TheMarginRuleDeclinesWhenTwoProfilesAreCloseTogether()
    {
        var clips = await LoadAsync();
        var mine = clips.Where(c => c.Speaker == clips[0].Speaker).ToList();
        var registry = new SpeakerRegistry();
        registry.Enroll("first", mine[0].Embedding, 5); registry.Enroll("first", mine[1].Embedding, 5);
        registry.Enroll("second", mine[2].Embedding, 5); registry.Enroll("second", mine[3].Embedding, 5);
        var probe = mine[4].Embedding;
        var m = registry.Identify(probe);
        if (m.Score < registry.AcceptThreshold)
            throw new Exception($"setup: the voice scored {m.Score:F3} against its own profile, below the threshold - the fixture is not one voice");
        var lead = m.Score - m.RunnerUpScore;
        if (lead is < 0.05f or > 0.20f)
            throw new Exception($"setup: the lead between the two profiles is {lead:F3}, expected about 0.115 (the margin mechanism below is only exercised between 0.05 and 0.20)");

        registry.MinMargin = lead + 0.05f;       // wider than the observed lead: must decline
        var declined = registry.Identify(probe);
        if (declined.Known || declined.Name != null)
            throw new Exception($"named '{declined.Name}' although the lead {lead:F3} is inside the margin {registry.MinMargin:F3}");
        if (declined.Score != m.Score || declined.RunnerUpName == null)
            throw new Exception("a declined match must still report the scores it saw");

        registry.MinMargin = 0f;                 // no margin: the same voice is named, so the margin is what declined it
        if (!registry.Identify(probe).Known)
            throw new Exception("without the margin rule the voice should have matched: the margin check is not what rejected it");
    }

    /// <summary>
    /// A voice nobody matches becomes "Guest 1"; its later speech joins that guest instead of making more; a second stranger
    /// is "Guest 2"; naming a guest names the person; named people are not changed by merely being heard.
    /// </summary>
    [AiTest(Timeout = 30_000)]
    public async Task UnknownVoicesBecomeGuestsAndCanBeNamed()
    {
        var clips = await LoadAsync();
        var (registry, held, strangers) = Setup(clips);
        var speakers = strangers.Select(c => c.Speaker).Distinct().ToList();
        var a = strangers.Where(c => c.Speaker == speakers[0]).ToList();
        var b = strangers.Where(c => c.Speaker == speakers[1]).ToList();

        var namedBefore = registry.Profiles.Where(p => !p.IsGuest).Select(p => (p.Name, p.Embeddings.Count)).ToList();
        foreach (var c in held) registry.Observe(c.Embedding, 5, out _);        // known voices heard: nothing may change
        var namedAfter = registry.Profiles.Where(p => !p.IsGuest).Select(p => (p.Name, p.Embeddings.Count)).ToList();
        if (!namedBefore.SequenceEqual(namedAfter))
            throw new Exception("hearing known people changed their enrolled voices");
        if (registry.Profiles.Any(p => p.IsGuest))
            throw new Exception("a known voice was filed as a guest");

        registry.Observe(a[0].Embedding, 5, out var g1);
        if (g1?.Name != "Guest 1") throw new Exception($"first stranger filed as '{g1?.Name}'");
        foreach (var c in a.Skip(1))
        {
            registry.Observe(c.Embedding, 5, out var again);
            if (again?.Name != "Guest 1") throw new Exception($"the same stranger was filed as '{again?.Name}' on a later clip");
        }
        registry.Observe(b[0].Embedding, 5, out var g2);
        if (g2?.Name != "Guest 2") throw new Exception($"second stranger filed as '{g2?.Name}'");

        if (!registry.Rename("Guest 1", "Nikki")) throw new Exception("rename failed");
        var m = registry.Identify(a[0].Embedding);
        if (!m.Known || m.Name != "Nikki") throw new Exception($"after naming the guest, her voice is '{m.Name}' (known {m.Known}, score {m.Score:F3})");
        if (registry.Profiles.First(p => p.Name == "Nikki").IsGuest) throw new Exception("a renamed guest is still a guest");
    }

    /// <summary>What was enrolled survives a save and a load, and answers the same way.</summary>
    [AiTest(Timeout = 30_000)]
    public async Task RegistrySurvivesJson()
    {
        var (registry, held, strangers) = Setup(await LoadAsync());
        registry.Observe(strangers[0].Embedding, 5, out _);
        var restored = SpeakerRegistry.FromJson(registry.ToJson());
        if (restored.Profiles.Count != registry.Profiles.Count)
            throw new Exception($"{restored.Profiles.Count} profiles after reload, {registry.Profiles.Count} before");
        foreach (var c in held.Concat(strangers))
        {
            var a = registry.Identify(c.Embedding); var r = restored.Identify(c.Embedding);
            if (a.Name != r.Name || a.Known != r.Known || MathF.Abs(a.Score - r.Score) > 1e-4f)
                throw new Exception($"{c.Id}: {a.Name}/{a.Score:F4} before, {r.Name}/{r.Score:F4} after the round trip");
        }
    }
}
