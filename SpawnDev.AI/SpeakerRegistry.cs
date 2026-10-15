using System.Text.Json;
using System.Text.Json.Serialization;

namespace SpawnDev.AI;

/// <summary>One enrolled voice: a person's name and the voice embeddings that stand for them.</summary>
public sealed class SpeakerProfile
{
    /// <summary>The name the person is called by.</summary>
    public string Name { get; set; } = "";

    /// <summary>True for a provisional profile made for a voice nobody has named yet ("Guest 1").</summary>
    public bool IsGuest { get; set; }

    /// <summary>Seconds of speech the embeddings stand for.</summary>
    public double Seconds { get; set; }

    /// <summary>When this voice was last heard (UTC ticks), so the least recently heard guest is the one forgotten.</summary>
    public long LastHeardTicks { get; set; }

    /// <summary>When a provisional guest was first filed (UTC ticks); 0 for a profile saved before this was recorded (then <see cref="LastHeardTicks"/> stands in). It is how long a stranger has been remembered.</summary>
    public long FirstSeenTicks { get; set; }

    /// <summary>Unit-length embeddings, oldest first.</summary>
    public List<float[]> Embeddings { get; set; } = new();

    /// <summary>The centre of the voice: the mean of the embeddings, renormalised. Null until there is one.</summary>
    [JsonIgnore]
    public float[]? Centroid { get; private set; }

    internal void Recompute()
    {
        if (Embeddings.Count == 0) { Centroid = null; return; }
        var sum = new float[Embeddings[0].Length];
        foreach (var e in Embeddings) for (int i = 0; i < sum.Length; i++) sum[i] += e[i];
        Centroid = SpeakerRegistry.Normalize(sum);
    }
}

/// <summary>Who a voice sounds like.</summary>
/// <param name="Name">The best match's name, or null when nobody enrolled sounds enough like it.</param>
/// <param name="Score">Cosine similarity to the best profile (0 when there are no profiles).</param>
/// <param name="RunnerUpName">The second-best profile.</param>
/// <param name="RunnerUpScore">Its score.</param>
/// <param name="Known">True when the best profile is far enough above the threshold AND the runner-up.</param>
public sealed record SpeakerMatch(string? Name, float Score, string? RunnerUpName, float RunnerUpScore, bool Known)
{
    /// <summary>Nobody enrolled.</summary>
    public static readonly SpeakerMatch Nobody = new(null, 0f, null, 0f, false);
}

/// <summary>
/// The voices Vertex (or any app) has been introduced to, and the question "who is this?".
/// </summary>
/// <remarks>
/// <para>
/// Pure logic over speaker embeddings (see <c>SpeakerEmbeddingPipeline</c> in SpawnDev.ILGPU.ML): each person is the
/// centroid of the unit-length embeddings enrolled for them, and a voice is matched by cosine similarity. A match
/// needs BOTH a score at or above <see cref="AcceptThreshold"/> and a lead of <see cref="MinMargin"/> over the
/// runner-up - relatives sound alike, so the best score alone is not evidence when two profiles are close.
/// </para>
/// <para>
/// MEASURED (ReDimNet2-B6, LibriSpeech test-clean, 40 speakers, 3 clips to enrol): at a 0.485 threshold, which lets 1% of
/// strangers in, 98.9% of enrolled speakers' clips were recognised and none were given the wrong name. That corpus is
/// read speech from strangers, not a household: calibrate <see cref="AcceptThreshold"/> on the voices you have.
/// </para>
/// <para>
/// A voice nobody matches can become a provisional "Guest N" (<see cref="Observe"/>): later speech from the same voice
/// joins it, and <see cref="Rename"/> turns it into a named person - "Vertex, this is Nikki". Named people are never
/// changed by what is merely heard (a similar voice must not slowly rewrite who someone is); only <see cref="Enroll"/> and
/// <see cref="Refine"/> change them, and only when asked.
/// </para>
/// </remarks>
public sealed class SpeakerRegistry
{
    private readonly object _lock = new();
    private readonly List<SpeakerProfile> _profiles = new();

    /// <summary>The embedding length every profile uses (set by the first enrolment).</summary>
    public int EmbeddingSize { get; private set; }

    /// <summary>Cosine similarity to a profile's centre needed to call a voice that person. Calibrate on the household.</summary>
    public float AcceptThreshold { get; set; } = 0.5f;

    /// <summary>How far the best profile must lead the runner-up for the match to count.</summary>
    /// <remarks>
    /// MEASURED (ReDimNet2-B6, LibriSpeech test-clean, 30 enrolled speakers, strangers competing): for correct matches the lead has
    /// median 0.40, 5th percentile 0.19, minimum 0.087. Recall of enrolled speakers' clips by margin: 0.00 and 0.05 97.8%, 0.10
    /// 96.7%, 0.15 94.4%, 0.20 91.1%; a wrong name never happened at any margin on that corpus. So 0.10 costs about a point of
    /// recall and makes a profile that is only barely closer than another one (relatives, one voice enrolled twice - measured
    /// leads of 0.115) say "not sure" rather than guess: for a companion that greets people by name, declining is the cheaper
    /// error. Read speech from strangers is not a household of relatives - raise it if two people get confused.
    /// </remarks>
    public float MinMargin { get; set; } = 0.10f;

    /// <summary>Embeddings kept per person: the newest win, so a changed microphone or a cold slowly takes over.</summary>
    public int MaxEmbeddingsPerProfile { get; set; } = 12;

    /// <summary>
    /// What provisional profiles are called: "Guest 1", "Guest 2". A registry of FACES uses another word ("Visitor") so that a stranger's voice and a stranger's face - which are not
    /// known to be the same person - never share a name and get merged into one entry by anything that joins registries by name.
    /// </summary>
    public string GuestPrefix { get; set; } = "Guest";

    /// <summary>Provisional guests kept; past this the one heard longest ago is forgotten.</summary>
    public int MaxGuests { get; set; } = 8;

    /// <summary>
    /// How long a stranger is remembered if nobody ever says who they are, counted from the FIRST time they were heard (not the last: a stranger who keeps coming
    /// back does not stay for ever, only a person someone named does). Null (the default) keeps guests until <see cref="MaxGuests"/> pushes them out. <see cref="ExpireGuests"/>
    /// applies it; not saved with the profiles (it is the application's policy, not the data's).
    /// </summary>
    public TimeSpan? GuestLifetime { get; set; }

    /// <summary>
    /// Forgets the guests (never a named person) that have been remembered longer than <see cref="GuestLifetime"/>. Returns their names. Call it on start-up, on a timer, and
    /// before reading the profiles to show them.
    /// </summary>
    public IReadOnlyList<string> ExpireGuests(DateTime? utcNow = null)
    {
        if (GuestLifetime is not { } lifetime) return Array.Empty<string>();
        var cutoff = (utcNow ?? DateTime.UtcNow).Ticks - lifetime.Ticks;
        lock (_lock)
        {
            var expired = _profiles.Where(p => p.IsGuest && (p.FirstSeenTicks != 0 ? p.FirstSeenTicks : p.LastHeardTicks) < cutoff).ToList();
            foreach (var p in expired) _profiles.Remove(p);
            return expired.Select(p => p.Name).ToList();
        }
    }

    /// <summary>A snapshot of the profiles.</summary>
    public IReadOnlyList<SpeakerProfile> Profiles { get { lock (_lock) return _profiles.ToArray(); } }

    /// <summary>Who an embedding sounds like. Never changes the registry.</summary>
    public SpeakerMatch Identify(ReadOnlySpan<float> embedding)
    {
        lock (_lock) return IdentifyLocked(embedding);
    }

    private SpeakerMatch IdentifyLocked(ReadOnlySpan<float> embedding)
    {
        SpeakerProfile? best = null, second = null;
        float bestScore = float.NegativeInfinity, secondScore = float.NegativeInfinity;
        foreach (var p in _profiles)
        {
            if (p.Centroid == null) continue;
            var s = Cosine(embedding, p.Centroid);
            if (s > bestScore) { second = best; secondScore = bestScore; best = p; bestScore = s; }
            else if (s > secondScore) { second = p; secondScore = s; }
        }
        if (best == null) return SpeakerMatch.Nobody;
        var runner = second == null ? 0f : secondScore;
        var known = bestScore >= AcceptThreshold && (second == null || bestScore - runner >= MinMargin);
        return new SpeakerMatch(known ? best.Name : null, bestScore, second?.Name, runner, known);
    }

    /// <summary>Adds a person, or adds to one: the embedding joins that name's profile (a guest becomes named).</summary>
    public SpeakerProfile Enroll(string name, ReadOnlySpan<float> embedding, double seconds)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("a speaker needs a name", nameof(name));
        lock (_lock)
        {
            var p = _profiles.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
            if (p == null) { p = new SpeakerProfile { Name = name.Trim() }; _profiles.Add(p); }
            p.IsGuest = false;
            AddLocked(p, embedding, seconds);
            return p;
        }
    }

    /// <summary>Adds an embedding to an existing named profile (the person said so, or it was a confident match). False when unknown.</summary>
    public bool Refine(string name, ReadOnlySpan<float> embedding, double seconds)
    {
        lock (_lock)
        {
            var p = _profiles.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
            if (p == null) return false;
            AddLocked(p, embedding, seconds);
            return true;
        }
    }

    /// <summary>
    /// Hears a voice: names it if it is someone enrolled, otherwise files it under a provisional guest (an existing one if
    /// it sounds like that guest). Named profiles are not changed. Returns the match for the voice as it was BEFORE filing.
    /// </summary>
    public SpeakerMatch Observe(ReadOnlySpan<float> embedding, double seconds, out SpeakerProfile? guest)
    {
        lock (_lock)
        {
            var match = IdentifyLocked(embedding);
            guest = null;
            var heard = _profiles.FirstOrDefault(x => x.Name == match.Name);
            if (match.Known && heard != null)
            {
                heard.LastHeardTicks = DateTime.UtcNow.Ticks;
                if (heard.IsGuest) { AddLocked(heard, embedding, seconds); guest = heard; }
                return match;
            }
            // Not a known voice. If it is close to a guest (but the margin rule kept it out), still treat it as that
            // guest rather than minting a second one for the same stranger.
            SpeakerProfile? closeGuest = null;
            var closeScore = float.NegativeInfinity;
            foreach (var p in _profiles)
            {
                if (!p.IsGuest || p.Centroid == null) continue;
                var s = Cosine(embedding, p.Centroid);
                if (s >= AcceptThreshold && s > closeScore) { closeGuest = p; closeScore = s; }
            }
            if (closeGuest != null) { AddLocked(closeGuest, embedding, seconds); guest = closeGuest; return match; }

            var number = 1;
            while (_profiles.Any(p => p.Name == $"{GuestPrefix} {number}")) number++;
            guest = new SpeakerProfile { Name = $"{GuestPrefix} {number}", IsGuest = true, FirstSeenTicks = DateTime.UtcNow.Ticks, LastHeardTicks = DateTime.UtcNow.Ticks };
            _profiles.Add(guest);
            AddLocked(guest, embedding, seconds);
            var guests = _profiles.Where(p => p.IsGuest).OrderBy(p => p.LastHeardTicks).ToList();
            while (guests.Count > MaxGuests) { _profiles.Remove(guests[0]); guests.RemoveAt(0); }
            return match;
        }
    }

    /// <summary>Gives a profile (usually a guest) a name. If that name already has a profile the two are merged.</summary>
    public bool Rename(string from, string to)
    {
        if (string.IsNullOrWhiteSpace(to)) return false;
        lock (_lock)
        {
            var src = _profiles.FirstOrDefault(x => string.Equals(x.Name, from, StringComparison.OrdinalIgnoreCase));
            if (src == null) return false;
            var dst = _profiles.FirstOrDefault(x => !ReferenceEquals(x, src) && string.Equals(x.Name, to, StringComparison.OrdinalIgnoreCase));
            if (dst == null) { src.Name = to.Trim(); src.IsGuest = false; return true; }
            foreach (var e in src.Embeddings) AddLocked(dst, e, 0);
            dst.Seconds += src.Seconds;
            dst.IsGuest = false;
            _profiles.Remove(src);
            return true;
        }
    }

    /// <summary>Forgets a person.</summary>
    public bool Forget(string name)
    {
        lock (_lock) return _profiles.RemoveAll(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)) > 0;
    }

    private void AddLocked(SpeakerProfile p, ReadOnlySpan<float> embedding, double seconds)
    {
        if (EmbeddingSize == 0) EmbeddingSize = embedding.Length;
        if (embedding.Length != EmbeddingSize)
            throw new ArgumentException($"embedding has {embedding.Length} values; this registry holds {EmbeddingSize}");
        p.Embeddings.Add(Normalize(embedding.ToArray()));
        while (p.Embeddings.Count > Math.Max(1, MaxEmbeddingsPerProfile)) p.Embeddings.RemoveAt(0);
        p.Seconds += seconds;
        p.LastHeardTicks = DateTime.UtcNow.Ticks;
        p.Recompute();
    }

    private sealed record Saved(float AcceptThreshold, float MinMargin, int MaxEmbeddingsPerProfile, int MaxGuests, List<SpeakerProfile> Profiles);

    /// <summary>The registry as JSON, to keep between runs.</summary>
    public string ToJson()
    {
        lock (_lock) return JsonSerializer.Serialize(new Saved(AcceptThreshold, MinMargin, MaxEmbeddingsPerProfile, MaxGuests, _profiles));
    }

    /// <summary>Reads what <see cref="ToJson"/> wrote.</summary>
    public static SpeakerRegistry FromJson(string json)
    {
        var saved = JsonSerializer.Deserialize<Saved>(json) ?? throw new FormatException("not a speaker registry");
        var r = new SpeakerRegistry
        {
            AcceptThreshold = saved.AcceptThreshold, MinMargin = saved.MinMargin,
            MaxEmbeddingsPerProfile = saved.MaxEmbeddingsPerProfile, MaxGuests = saved.MaxGuests,
        };
        foreach (var p in saved.Profiles)
        {
            if (p.Embeddings.Count > 0) r.EmbeddingSize = p.Embeddings[0].Length;
            p.Recompute();
            r._profiles.Add(p);
        }
        return r;
    }

    /// <summary>Cosine similarity of two vectors.</summary>
    public static float Cosine(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        if (a.Length != b.Length) throw new ArgumentException("embeddings differ in length");
        double dot = 0, na = 0, nb = 0;
        for (int i = 0; i < a.Length; i++) { dot += a[i] * b[i]; na += a[i] * a[i]; nb += b[i] * b[i]; }
        return na == 0 || nb == 0 ? 0f : (float)(dot / Math.Sqrt(na * nb));
    }

    internal static float[] Normalize(float[] v)
    {
        double n = 0;
        foreach (var x in v) n += x * x;
        n = Math.Sqrt(n);
        if (n < 1e-12) return v;
        var o = new float[v.Length];
        for (int i = 0; i < v.Length; i++) o[i] = (float)(v[i] / n);
        return o;
    }
}
