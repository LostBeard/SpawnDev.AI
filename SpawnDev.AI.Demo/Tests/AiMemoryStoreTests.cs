using System.Text;
using System.Text.Json;

namespace SpawnDev.AI.Demo.Tests;

/// <summary>
/// The memory store's own logic: privacy deletes that cascade, filters, the reference search, persistence, the one-model rule, and the link to the voice/face
/// registries' stable ids. Not heavy - no model runs here.
/// </summary>
/// <remarks>
/// The vectors here are fixed pseudo-random unit vectors, not a model's: these tests are about what the STORE does with whatever it is given (which entries a delete
/// removes, whether search agrees with brute force), and a real model would add nothing but noise to that. Retrieval QUALITY is a different question and is gated on real
/// embeddings of real Vertex memories (AiMemoryEngine's tests). The registry-link test uses the real voice fixture.
/// </remarks>
public sealed class AiMemoryStoreTests
{
    private const string Model = "test/unit-vectors";
    private const int Dim = 384;
    private readonly HttpClient _http;

    public AiMemoryStoreTests(HttpClient http) => _http = http;

    private static float[] Vec(int seed)
    {
        var r = new Random(seed);
        var v = new float[Dim];
        for (int i = 0; i < Dim; i++) v[i] = (float)(r.NextDouble() * 2 - 1);
        return v;
    }

    private static AiMemoryEntry E(string id, AiMemoryKind kind, string text, string[]? subjects = null, string[]? from = null, string scope = "", long time = 1000) => new()
    {
        Id = id, Kind = kind, Text = text, Subjects = (subjects ?? Array.Empty<string>()).ToList(), DerivedFrom = (from ?? Array.Empty<string>()).ToList(), Scope = scope, TimeTicks = time,
    };

    private static void Expect(bool ok, string what) { if (!ok) throw new Exception(what); }

    /// <summary>
    /// Forgetting a person removes their episodes AND every fact derived from them, transitively (a fact digested from a fact digested from their words), and nothing
    /// else; the removed entries are absent from the saved files, not hidden. Fails if the cascade stops at one level, if a fact derived partly from someone else
    /// survives, or if a save still carries a forgotten id.
    /// </summary>
    [AiTest(Timeout = 30_000)]
    public async Task ForgettingAPersonRemovesWhatWasDerivedFromThem_AndTheFilesForgetToo()
    {
        var s = new AiMemoryStore(Model, Dim);
        s.Add(E("a1", AiMemoryKind.Episode, "Ben said his favourite block is copper", new[] { "ben" }), Vec(1));
        s.Add(E("a2", AiMemoryKind.Episode, "Ben built a tower at 100 64 -20", new[] { "ben" }), Vec(2));
        s.Add(E("b1", AiMemoryKind.Episode, "Aubs built a garden", new[] { "aubs" }), Vec(3));
        s.Add(E("f1", AiMemoryKind.Fact, "Ben and Aubs both like building", from: new[] { "a2", "b1" }), Vec(4));
        s.Add(E("f2", AiMemoryKind.Fact, "The players like building", from: new[] { "f1" }), Vec(5));
        s.Add(E("f3", AiMemoryKind.Fact, "Aubs likes gardens", from: new[] { "b1" }), Vec(6));
        var removedEvent = new List<string>();
        s.Removed += ids => removedEvent.AddRange(ids);

        var removed = s.ForgetSubject("ben", "ben-minecraft-uuid").OrderBy(x => x).ToList();
        Expect(removed.SequenceEqual(new[] { "a1", "a2", "f1", "f2" }), $"forget removed [{string.Join(",", removed)}], expected [a1,a2,f1,f2]");
        Expect(removedEvent.OrderBy(x => x).SequenceEqual(removed), "the Removed event did not report exactly what was removed");
        var left = s.List().Select(e => e.Id).OrderBy(x => x).ToList();
        Expect(left.SequenceEqual(new[] { "b1", "f3" }), $"left [{string.Join(",", left)}], expected [b1,f3]");

        using var meta = new MemoryStream(); using var emb = new MemoryStream();
        await s.SaveAsync(meta, emb);
        var json = Encoding.UTF8.GetString(meta.ToArray());
        foreach (var gone in removed) Expect(!json.Contains($"\"{gone}\""), $"the saved entries still mention '{gone}'");
        Expect(!json.Contains("copper"), "the saved entries still hold a forgotten person's words");
        // The embedding file holds exactly the two survivors: header (magic, version, model id, dimension, count) + per row (id, Dim floats).
        // (Its bytes are not searched for the ids: random float bytes would match a two-letter id now and then.)
        static int Str(string x) => 1 + Encoding.UTF8.GetByteCount(x);
        long expected = 4 + 4 + Str(Model) + 4 + 4 + (Str("b1") + Dim * 4) + (Str("f3") + Dim * 4);
        Expect(emb.Length == expected, $"embedding file is {emb.Length} bytes; two rows need {expected} - a forgotten vector is still in it");
        meta.Position = 0; emb.Position = 0;
        var back = await AiMemoryStore.LoadAsync(meta, emb);
        Expect(back.Count == 2 && back.Get("f3")?.Text == "Aubs likes gardens", "the store did not survive save + load");
    }

    /// <summary>
    /// The reference search returns exactly the brute-force top k (by score, ties newer first), honours the scope / kind / time filters (an empty-scope entry is in every
    /// scope), and leaves superseded facts out unless asked. 600 entries so the ranking is not decided by a handful.
    /// </summary>
    [AiTest(Timeout = 30_000)]
    public Task SearchIsTheExactTopK_WithFiltersAndSupersededFactsHidden()
    {
        var s = new AiMemoryStore(Model, Dim);
        long now = new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc).Ticks;
        var rnd = new Random(7);
        for (int i = 0; i < 600; i++)
        {
            var e = E($"e{i}", (AiMemoryKind)(i % 3), $"memory {i}", scope: i % 5 == 0 ? "" : (i % 2 == 0 ? "world-a" : "world-b"),
                time: now - TimeSpan.FromHours(rnd.Next(0, 24 * 60)).Ticks);
            e.Importance = (float)rnd.NextDouble();
            s.Add(e, Vec(1000 + i));
        }
        var q = Vec(5);
        var ranking = new AiMemoryRanking();
        var filter = new AiMemoryFilter { Scope = "world-a", Kinds = new[] { AiMemoryKind.Episode, AiMemoryKind.Lesson } };
        var got = s.Search(q, 10, filter, ranking, now);

        // Brute force on its own, from the public listing.
        var qn = Unit(q);
        var want = s.List(filter).Select(e => (e, score: AiMemoryStore.Score(ranking, Dot(qn, Unit(Vec(1000 + int.Parse(e.Id[1..])))), e, now)))
            .OrderByDescending(x => x.score).ThenByDescending(x => x.e.TimeTicks).Take(10).Select(x => x.e.Id).ToList();
        Expect(got.Select(h => h.Entry.Id).SequenceEqual(want), $"search [{string.Join(",", got.Select(h => h.Entry.Id))}] != brute force [{string.Join(",", want)}]");
        foreach (var h in got)
        {
            Expect(h.Entry.Scope is "world-a" or "", $"{h.Entry.Id} is in scope '{h.Entry.Scope}'");
            Expect(h.Entry.Kind != AiMemoryKind.Fact, $"{h.Entry.Id} is a fact");
        }
        Expect(got.Any(h => h.Entry.Scope == ""), "no every-scope entry made the top 10 - the fixture no longer tests that rule");

        // Supersede the best hit: it disappears from search, comes back when asked, and the listing keeps it.
        var top = got[0].Entry.Id;
        Expect(s.Supersede(top, "e1"), "supersede failed");
        Expect(s.Search(q, 10, filter, ranking, now).All(h => h.Entry.Id != top), "a superseded entry was still returned");
        Expect(s.Search(q, 600, null, ranking, now).All(h => h.Entry.Id != top), "a search with NO filter returned a superseded entry (no filter must mean the default filter)");
        var withOld = new AiMemoryFilter { Scope = "world-a", Kinds = filter.Kinds, IncludeSuperseded = true };
        Expect(s.Search(q, 10, withOld, ranking, now)[0].Entry.Id == top, "IncludeSuperseded did not bring it back");
        Expect(s.Get(top)?.SupersededBy == "e1", "the history was lost");
        return Task.CompletedTask;
    }

    /// <summary>"Do not learn from this person" and "pause learning" refuse the write (nothing stored); a merge of two ids carries the block across.</summary>
    [AiTest(Timeout = 30_000)]
    public Task BlockedPeopleAndPausedLearningAreNotStored()
    {
        var s = new AiMemoryStore(Model, Dim);
        s.SetBlocked("visitor-7", true);
        Expect(s.Add(E("", AiMemoryKind.Episode, "the visitor said hi", new[] { "visitor-7" }), Vec(1)) == null, "stored a memory about a blocked person");
        Expect(s.Add(E("", AiMemoryKind.Episode, "Aubs said hi", new[] { "aubs" }), Vec(2)) != null, "refused an unblocked person");
        s.LearningPaused = true;
        Expect(s.Add(E("", AiMemoryKind.Episode, "Aubs said bye", new[] { "aubs" }), Vec(3)) == null, "stored while learning was paused");
        s.LearningPaused = false;
        Expect(s.Count == 1, $"count {s.Count}, expected 1");
        s.RemapSubject("visitor-7", "uncle-joe");
        Expect(s.Add(E("", AiMemoryKind.Episode, "Joe waved", new[] { "uncle-joe" }), Vec(4)) == null, "the block did not follow the merged id");
        return Task.CompletedTask;
    }

    /// <summary>
    /// One embedding space per store: a vector of the wrong size is refused, files from two different models refuse to load together, and switching models needs a new
    /// vector for EVERY entry (a half-switched store would compare two spaces).
    /// </summary>
    [AiTest(Timeout = 30_000)]
    public async Task TwoEmbeddingModelsAreNeverMixed()
    {
        var s = new AiMemoryStore(Model, Dim);
        s.Add(E("x", AiMemoryKind.Fact, "the base is at 0 70 0"), Vec(1));
        s.Add(E("y", AiMemoryKind.Fact, "the farm is north"), Vec(2));
        bool threw = false;
        try { s.Add(E("z", AiMemoryKind.Fact, "wrong size"), new float[Dim + 1]); } catch (ArgumentException) { threw = true; }
        Expect(threw, "accepted an embedding of the wrong length");

        threw = false;
        try { s.ReplaceEmbeddings("other/model", 768, new Dictionary<string, float[]> { ["x"] = new float[768] }); } catch (ArgumentException) { threw = true; }
        Expect(threw && s.ModelId == Model && s.Dimension == Dim, "a partial model switch went through");

        s.ReplaceEmbeddings("other/model", 768, new Dictionary<string, float[]> { ["x"] = Pad(Vec(1), 768), ["y"] = Pad(Vec(2), 768) });
        Expect(s.ModelId == "other/model" && s.Dimension == 768, "the full switch did not take");

        using var meta = new MemoryStream(); using var emb = new MemoryStream();
        await s.SaveAsync(meta, emb);
        var json = Encoding.UTF8.GetString(meta.ToArray()).Replace("other/model", "third/model");
        threw = false;
        try { await AiMemoryStore.LoadAsync(new MemoryStream(Encoding.UTF8.GetBytes(json)), new MemoryStream(emb.ToArray())); } catch (FormatException) { threw = true; }
        Expect(threw, "loaded entries and embeddings that came from different models");
    }

    /// <summary>
    /// Memories refer to people by the registries' stable ids. On the REAL voice fixture: a rename keeps the id, a merge raises ProfileMerged (old id -> surviving id) and
    /// the store remaps; forgetting a person raises ProfileRemoved and the store forgets them; a registry saved before ids existed gets one per profile on load.
    /// </summary>
    [AiTest(Timeout = 30_000)]
    public async Task MemoriesFollowTheRegistriesIds_ThroughRenameMergeAndForget()
    {
        using var doc = JsonDocument.Parse(await _http.GetStringAsync("test-data/speaker-embeddings.json"));
        var clips = doc.RootElement.GetProperty("clips").EnumerateArray()
            .Select(c => (spk: c.GetProperty("speaker").GetString()!, e: c.GetProperty("embedding").EnumerateArray().Select(v => v.GetSingle()).ToArray())).ToList();
        var speakers = clips.Select(c => c.spk).Distinct().ToList();
        var voices = new SpeakerRegistry();
        var store = new AiMemoryStore(Model, Dim);
        voices.ProfileMerged += (from, into) => store.RemapSubject(from.Id, into.Id);
        voices.ProfileRemoved += p => store.ForgetSubject(p.Id);

        var nikkiClips = clips.Where(c => c.spk == speakers[0]).ToList();
        SpeakerProfile nikki = null!;
        foreach (var c in nikkiClips.Take(3)) nikki = voices.Enroll("Nikki", c.e, 5);   // three clips, as SpeakerRegistryTests enrols
        voices.Observe(clips.First(c => c.spk == speakers[1]).e, 5, out var guest);
        Expect(guest != null, "the stranger was not filed as a guest");
        var guestId = guest!.Id;
        store.Add(E("g1", AiMemoryKind.Episode, "the guest asked for a castle", new[] { guestId }), Vec(1));
        store.Add(E("n1", AiMemoryKind.Episode, "Nikki planted wheat", new[] { nikki.Id }), Vec(2));

        Expect(voices.Rename(guest.Name, "Ben") && voices.FindById(guestId)?.Name == "Ben", "a rename changed the id");
        var match = voices.Identify(nikkiClips[3].e);
        Expect(match.Known && match.Id == nikki.Id, $"Identify did not give Nikki's id (known={match.Known}, id={match.Id})");

        // Ben turns out to be Nikki enrolled twice (a test of the plumbing, not of voices): renaming onto her name merges.
        Expect(voices.Rename("Ben", "Nikki"), "merge failed");
        Expect(voices.FindById(guestId) == null, "the merged profile's id survived");
        Expect(store.Get("g1")!.Subjects.SequenceEqual(new[] { nikki.Id }), $"g1 subjects [{string.Join(",", store.Get("g1")!.Subjects)}], expected Nikki's id");

        Expect(voices.Forget("Nikki"), "forget failed");
        Expect(store.Count == 0, $"{store.Count} memories survived forgetting the person they were about");

        // A registry saved before ids existed.
        var old = new SpeakerRegistry();
        old.Enroll("Aubs", clips.First(c => c.spk == speakers[2]).e, 5);
        old.Enroll("Todd", clips.First(c => c.spk == speakers[3]).e, 5);
        var legacy = System.Text.RegularExpressions.Regex.Replace(old.ToJson(), "\"Id\":\"[0-9a-f]{32}\",", "");
        Expect(!legacy.Contains("\"Id\""), "could not build the legacy fixture");
        var loaded = SpeakerRegistry.FromJson(legacy);
        var ids = loaded.Profiles.Select(p => p.Id).ToList();
        Expect(ids.Count == 2 && ids.All(i => i.Length == 32) && ids[0] != ids[1], $"legacy profiles got ids [{string.Join(",", ids)}]");
    }

    private static float[] Unit(float[] v) { var n = Math.Sqrt(v.Sum(x => (double)x * x)); return v.Select(x => (float)(x / n)).ToArray(); }
    private static float Dot(float[] a, float[] b) { float d = 0; for (int i = 0; i < a.Length; i++) d += a[i] * b[i]; return d; }
    private static float[] Pad(float[] v, int n) { var o = new float[n]; v.CopyTo(o, 0); o[n - 1] = 1; return o; }
}
