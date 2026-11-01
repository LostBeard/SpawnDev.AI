using System.Buffers.Binary;
using System.Text;
using System.Text.Json;

namespace SpawnDev.AI;

/// <summary>What a memory is.</summary>
public enum AiMemoryKind
{
    /// <summary>Something that happened: a task done or failed, something built or found, someone asked it to remember.</summary>
    Episode,
    /// <summary>A durable fact, usually digested from episodes (<see cref="AiMemoryEntry.DerivedFrom"/>).</summary>
    Fact,
    /// <summary>What was learned from trying: "tried X, failed because Y", retrieved before a similar goal.</summary>
    Lesson,
}

/// <summary>One memory. Its embedding is held by the <see cref="AiMemoryStore"/>, never here, so a listing never carries vectors.</summary>
public sealed class AiMemoryEntry
{
    /// <summary>Stable id (assigned by the store when empty).</summary>
    public string Id { get; set; } = "";

    /// <summary>Episode, fact or lesson.</summary>
    public AiMemoryKind Kind { get; set; }

    /// <summary>The memory in words. It is what gets embedded and what a prompt quotes.</summary>
    public string Text { get; set; } = "";

    /// <summary>Free tags for filtering ("build", "place", "promise").</summary>
    public List<string> Tags { get; set; } = new();

    /// <summary>
    /// Who it is about: STABLE ids (a <see cref="SpeakerProfile.Id"/> of the voice or face registry, a game account id), never names - a name changes on rename,
    /// and deleting a person must find everything about them. One person may carry several ids until an app links them; <see cref="AiMemoryStore.ForgetSubject"/> takes them all.
    /// </summary>
    public List<string> Subjects { get; set; } = new();

    /// <summary>Which world it belongs to (a server "host:port", "robot"): "home" on one world must never answer on another. Empty = every scope.</summary>
    public string Scope { get; set; } = "";

    /// <summary>What wrote it (which body, which subsystem).</summary>
    public string Source { get; set; } = "";

    /// <summary>When it happened (UTC ticks).</summary>
    public long TimeTicks { get; set; }

    /// <summary>How much it matters, 0 to 1, set by the writer (a promise or a death outranks a walk).</summary>
    public float Importance { get; set; } = 0.5f;

    /// <summary>Someone said "never forget this": consolidation never compresses or drops it (a delete still removes it).</summary>
    public bool Pinned { get; set; }

    /// <summary>The entries a fact or lesson was digested from. Deleting a source deletes what was derived from it.</summary>
    public List<string> DerivedFrom { get; set; } = new();

    /// <summary>The newer entry that replaced this one ("favourite colour: pink" -> "purple"). Kept for history; searches skip it unless asked.</summary>
    public string? SupersededBy { get; set; }

    /// <summary>True once consolidation has digested this episode.</summary>
    public bool Consolidated { get; set; }

    internal AiMemoryEntry Clone() => new()
    {
        Id = Id, Kind = Kind, Text = Text, Tags = new(Tags), Subjects = new(Subjects), Scope = Scope, Source = Source, TimeTicks = TimeTicks,
        Importance = Importance, Pinned = Pinned, DerivedFrom = new(DerivedFrom), SupersededBy = SupersededBy, Consolidated = Consolidated,
    };
}

/// <summary>Which memories a query may return. Every set condition must hold; an unset one does not filter.</summary>
public sealed class AiMemoryFilter
{
    /// <summary>Only this scope (entries with an empty scope match every scope).</summary>
    public string? Scope { get; set; }
    /// <summary>Only these kinds.</summary>
    public IReadOnlyCollection<AiMemoryKind>? Kinds { get; set; }
    /// <summary>Only entries about at least one of these subject ids.</summary>
    public IReadOnlyCollection<string>? AnySubject { get; set; }
    /// <summary>Only entries carrying every one of these tags.</summary>
    public IReadOnlyCollection<string>? AllTags { get; set; }
    /// <summary>Only entries at or after this time (UTC ticks).</summary>
    public long? FromTicks { get; set; }
    /// <summary>Only entries before this time (UTC ticks).</summary>
    public long? ToTicks { get; set; }
    /// <summary>Include entries a newer one superseded (off: a search answers from the current facts).</summary>
    public bool IncludeSuperseded { get; set; }

    /// <summary>True when <paramref name="e"/> passes.</summary>
    public bool Matches(AiMemoryEntry e)
    {
        if (!IncludeSuperseded && e.SupersededBy != null) return false;
        if (Scope != null && e.Scope.Length > 0 && !string.Equals(e.Scope, Scope, StringComparison.Ordinal)) return false;
        if (Kinds != null && !Kinds.Contains(e.Kind)) return false;
        if (AnySubject != null && !e.Subjects.Any(AnySubject.Contains)) return false;
        if (AllTags != null && !AllTags.All(t => e.Tags.Contains(t, StringComparer.OrdinalIgnoreCase))) return false;
        if (FromTicks is { } from && e.TimeTicks < from) return false;
        if (ToTicks is { } to && e.TimeTicks >= to) return false;
        return true;
    }
}

/// <summary>How search ranks: relevance, recency and importance (the Generative Agents retrieval score), each weighted.</summary>
/// <remarks>
/// score = <see cref="Relevance"/> * cosine + <see cref="Recency"/> * 0.5^(age / <see cref="RecencyHalfLife"/>) + <see cref="Importance"/> * importance.
/// The default weights are starting values, NOT measured: they are to be calibrated on real query / expected-hit pairs (Vertex's fixture) before anyone relies on them.
/// </remarks>
public sealed record AiMemoryRanking(float Relevance = 1f, float Recency = 0.25f, float Importance = 0.25f, TimeSpan? RecencyHalfLife = null)
{
    /// <summary>Relevance only: the plain nearest neighbours.</summary>
    public static readonly AiMemoryRanking RelevanceOnly = new(1f, 0f, 0f);
    /// <summary>The recency half-life in ticks (default 7 days).</summary>
    public double HalfLifeTicks => (RecencyHalfLife ?? TimeSpan.FromDays(7)).Ticks;
}

/// <summary>One search result.</summary>
/// <param name="Entry">The memory (a copy; changing it changes nothing in the store).</param>
/// <param name="Similarity">Cosine similarity to the query.</param>
/// <param name="Score">The ranked score (<see cref="AiMemoryRanking"/>).</param>
public sealed record AiMemoryHit(AiMemoryEntry Entry, float Similarity, float Score);

/// <summary>
/// Long-term memory: episodes, facts and lessons with an embedding each, filtered and searched by meaning.
/// </summary>
/// <remarks>
/// <para>
/// Pure logic, no model and no device (the same split as <see cref="SpeakerRegistry"/>): the caller embeds. <c>AiMemoryEngine</c> in SpawnDev.AI.Server embeds with a
/// feature-extraction model and searches on the accelerator; <see cref="Search"/> here is the reference that engine must agree with.
/// </para>
/// <para>
/// Privacy is part of the data, not the UI: deletes are HARD (the entry and its embedding are gone from the next save), deleting an entry deletes everything derived from it
/// (a fact digested from a forgotten person's episodes must not survive as a "fact"), <see cref="BlockedSubjects"/> and <see cref="LearningPaused"/> make <see cref="Add"/> refuse,
/// and nothing about a subject is reachable by name.
/// </para>
/// <para>
/// One embedding space per store: <see cref="ModelId"/> and <see cref="Dimension"/> are fixed by the first entry, and the saved embedding file records them, so a changed model
/// is detected and re-embedded rather than mixed (<see cref="ReplaceEmbeddings"/>).
/// </para>
/// </remarks>
public sealed class AiMemoryStore
{
    private readonly object _lock = new();
    private readonly List<AiMemoryEntry> _entries = new();
    private readonly List<float[]> _vectors = new();   // parallel to _entries, unit length
    private readonly Dictionary<string, int> _index = new(StringComparer.Ordinal);
    private readonly HashSet<string> _blocked = new(StringComparer.Ordinal);

    /// <param name="modelId">The embedding model's id (e.g. "sentence-transformers/all-MiniLM-L6-v2").</param>
    /// <param name="dimension">Its embedding length.</param>
    public AiMemoryStore(string modelId, int dimension)
    {
        if (string.IsNullOrWhiteSpace(modelId)) throw new ArgumentException("a memory store needs the embedding model's id", nameof(modelId));
        if (dimension <= 0) throw new ArgumentOutOfRangeException(nameof(dimension));
        ModelId = modelId; Dimension = dimension;
    }

    /// <summary>The embedding model every vector in this store came from.</summary>
    public string ModelId { get; private set; }

    /// <summary>The embedding length.</summary>
    public int Dimension { get; private set; }

    /// <summary>When true, <see cref="Add"/> refuses everything ("pause learning").</summary>
    public bool LearningPaused { get; set; }

    /// <summary>Subjects nothing may be learned about ("do not learn from this person"): <see cref="Add"/> refuses an entry about any of them.</summary>
    public IReadOnlyCollection<string> BlockedSubjects { get { lock (_lock) return _blocked.ToArray(); } }

    /// <summary>Raised (outside the store's lock) with the ids of entries a delete removed, including derived ones - an engine drops its device copy of them.</summary>
    public event Action<IReadOnlyList<string>>? Removed;

    /// <summary>Raised (outside the lock) after <see cref="Add"/> stored an entry: a copy of it and its unit-length embedding. An engine uploads them.</summary>
    public event Action<AiMemoryEntry, float[]>? Added;

    /// <summary>Raised (outside the lock) with an entry whose metadata or embedding changed (<see cref="Update"/>, <see cref="Supersede"/>, <see cref="RemapSubject"/>), so a device copy can follow.</summary>
    public event Action<string>? Changed;

    /// <summary>Raised (outside the lock) after <see cref="ReplaceEmbeddings"/> switched the store to another model: every vector changed.</summary>
    public event Action? Reembedded;

    /// <summary>Number of entries.</summary>
    public int Count { get { lock (_lock) return _entries.Count; } }

    /// <summary>Blocks or unblocks learning about a subject. Blocking does not delete what is already known: call <see cref="ForgetSubject"/> for that.</summary>
    public void SetBlocked(string subjectId, bool blocked)
    {
        lock (_lock) { if (blocked) _blocked.Add(subjectId); else _blocked.Remove(subjectId); }
    }

    /// <summary>
    /// Adds a memory with its embedding. Returns the stored id, or null when learning is paused or the entry is about a blocked subject (nothing is stored).
    /// </summary>
    public string? Add(AiMemoryEntry entry, ReadOnlySpan<float> embedding)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (embedding.Length != Dimension) throw new ArgumentException($"embedding has {embedding.Length} values; this store holds {Dimension} ({ModelId})");
        AiMemoryEntry e; float[] v;
        lock (_lock)
        {
            if (LearningPaused || entry.Subjects.Any(_blocked.Contains)) return null;
            e = entry.Clone();
            if (string.IsNullOrEmpty(e.Id)) e.Id = Guid.NewGuid().ToString("N");
            if (_index.ContainsKey(e.Id)) throw new ArgumentException($"memory '{e.Id}' already exists");
            if (e.TimeTicks == 0) e.TimeTicks = DateTime.UtcNow.Ticks;
            e.Importance = Math.Clamp(e.Importance, 0f, 1f);
            _index[e.Id] = _entries.Count;
            _entries.Add(e);
            v = SpeakerRegistry.Normalize(embedding.ToArray());
            _vectors.Add(v);
        }
        Added?.Invoke(e.Clone(), (float[])v.Clone());
        return e.Id;
    }

    /// <summary>A copy of one entry's unit-length embedding, or null.</summary>
    public float[]? GetEmbedding(string id)
    {
        lock (_lock) return _index.TryGetValue(id, out var i) ? (float[])_vectors[i].Clone() : null;
    }

    /// <summary>A copy of one entry, or null.</summary>
    public AiMemoryEntry? Get(string id)
    {
        lock (_lock) return _index.TryGetValue(id, out var i) ? _entries[i].Clone() : null;
    }

    /// <summary>Copies of the entries that pass <paramref name="filter"/> (all when null), oldest first. For listing (the privacy page), not for answering.</summary>
    public IReadOnlyList<AiMemoryEntry> List(AiMemoryFilter? filter = null)
    {
        lock (_lock) return _entries.Where(e => filter?.Matches(e) ?? true).OrderBy(e => e.TimeTicks).Select(e => e.Clone()).ToList();
    }

    /// <summary>Changes an entry's words or metadata. A new <paramref name="embedding"/> must come with changed text (the old one no longer describes it).</summary>
    public bool Update(string id, Action<AiMemoryEntry> change, ReadOnlySpan<float> embedding = default)
    {
        if (!UpdateLocked(id, change, embedding)) return false;
        Changed?.Invoke(id);
        return true;
    }

    private bool UpdateLocked(string id, Action<AiMemoryEntry> change, ReadOnlySpan<float> embedding)
    {
        lock (_lock)
        {
            if (!_index.TryGetValue(id, out var i)) return false;
            var e = _entries[i];
            var text = e.Text;
            change(e);
            e.Id = id;
            e.Importance = Math.Clamp(e.Importance, 0f, 1f);
            if (embedding.Length > 0)
            {
                if (embedding.Length != Dimension) throw new ArgumentException($"embedding has {embedding.Length} values; this store holds {Dimension}");
                _vectors[i] = SpeakerRegistry.Normalize(embedding.ToArray());
            }
            else if (!string.Equals(text, e.Text, StringComparison.Ordinal))
                throw new ArgumentException("the text changed: pass its new embedding too, or searches would match the old words");
            return true;
        }
    }

    /// <summary>Marks <paramref name="oldId"/> as replaced by <paramref name="newId"/> (both must exist). The old entry stays for history.</summary>
    public bool Supersede(string oldId, string newId)
    {
        lock (_lock)
        {
            if (oldId == newId || !_index.TryGetValue(oldId, out var o) || !_index.ContainsKey(newId)) return false;
            _entries[o].SupersededBy = newId;
        }
        Changed?.Invoke(oldId);
        return true;
    }

    /// <summary>Deletes entries, and everything derived from them (transitively). Returns every id removed.</summary>
    public IReadOnlyList<string> Delete(params string[] ids) => RemoveWhere(e => ids.Contains(e.Id));

    /// <summary>
    /// Forgets a person: every entry about any of <paramref name="subjectIds"/> AND every entry derived from those (a fact digested partly from them goes too - it may
    /// say what they said). Returns every id removed.
    /// </summary>
    public IReadOnlyList<string> ForgetSubject(params string[] subjectIds)
    {
        var set = new HashSet<string>(subjectIds, StringComparer.Ordinal);
        return RemoveWhere(e => e.Subjects.Any(set.Contains));
    }

    /// <summary>
    /// Compaction, NOT forgetting: removes digested episodes WITHOUT removing what was derived from them (the facts are the point of digesting).
    /// Only episodes that are <see cref="AiMemoryEntry.Consolidated"/> and not <see cref="AiMemoryEntry.Pinned"/> go; any other id is left alone.
    /// Privacy still holds: a fact carries the subjects of the episodes it came from, so <see cref="ForgetSubject"/> finds it without them.
    /// Returns the ids removed.
    /// </summary>
    public IReadOnlyList<string> Compact(params string[] episodeIds)
    {
        var set = new HashSet<string>(episodeIds, StringComparer.Ordinal);
        List<string> removed;
        lock (_lock)
        {
            removed = new List<string>();
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                var e = _entries[i];
                if (!set.Contains(e.Id) || e.Kind != AiMemoryKind.Episode || !e.Consolidated || e.Pinned) continue;
                _entries.RemoveAt(i); _vectors.RemoveAt(i);
                removed.Add(e.Id);
            }
            if (removed.Count > 0) Reindex();
        }
        if (removed.Count > 0) Removed?.Invoke(removed);
        return removed;
    }

    /// <summary>Forgets everything in one world (and what was derived from it).</summary>
    public IReadOnlyList<string> ForgetScope(string scope) => RemoveWhere(e => string.Equals(e.Scope, scope, StringComparison.Ordinal));

    /// <summary>
    /// Two subject ids turned out to be one person (a registry merged a guest into someone named): entries about <paramref name="fromId"/> are now about <paramref name="toId"/>,
    /// and a block on either applies to both.
    /// </summary>
    public int RemapSubject(string fromId, string toId)
    {
        var changed = new List<string>();
        lock (_lock)
        {
            foreach (var e in _entries)
            {
                var i = e.Subjects.IndexOf(fromId);
                if (i < 0) continue;
                e.Subjects.RemoveAt(i);
                if (!e.Subjects.Contains(toId)) e.Subjects.Add(toId);
                changed.Add(e.Id);
            }
            if (_blocked.Remove(fromId)) _blocked.Add(toId);
        }
        foreach (var id in changed) Changed?.Invoke(id);
        return changed.Count;
    }

    private IReadOnlyList<string> RemoveWhere(Func<AiMemoryEntry, bool> seed)
    {
        List<string> removed;
        var revived = new List<string>();
        lock (_lock)
        {
            var doomed = new HashSet<string>(_entries.Where(seed).Select(e => e.Id), StringComparer.Ordinal);
            // Everything derived from a removed entry goes too, to a fixed point (a fact derived from a fact derived from a forgotten episode).
            bool grew = doomed.Count > 0;
            while (grew)
            {
                grew = false;
                foreach (var e in _entries)
                    if (!doomed.Contains(e.Id) && e.DerivedFrom.Any(doomed.Contains)) { doomed.Add(e.Id); grew = true; }
            }
            if (doomed.Count == 0) return Array.Empty<string>();
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                if (!doomed.Contains(_entries[i].Id)) continue;
                _entries.RemoveAt(i); _vectors.RemoveAt(i);
            }
            // A surviving entry superseded by a removed one is current again.
            foreach (var e in _entries) if (e.SupersededBy != null && doomed.Contains(e.SupersededBy)) { e.SupersededBy = null; revived.Add(e.Id); }
            Reindex();
            removed = doomed.ToList();
        }
        Removed?.Invoke(removed);
        foreach (var id in revived) Changed?.Invoke(id);
        return removed;
    }

    private void Reindex()
    {
        _index.Clear();
        for (int i = 0; i < _entries.Count; i++) _index[_entries[i].Id] = i;
    }

    /// <summary>
    /// The <paramref name="k"/> best memories for a query embedding, best first: the reference search (exact, on the CPU). <paramref name="nowTicks"/> fixes "now" for the recency term.
    /// </summary>
    public IReadOnlyList<AiMemoryHit> Search(ReadOnlySpan<float> query, int k, AiMemoryFilter? filter = null, AiMemoryRanking? ranking = null, long? nowTicks = null)
    {
        if (query.Length != Dimension) throw new ArgumentException($"query has {query.Length} values; this store holds {Dimension}");
        if (k <= 0) return Array.Empty<AiMemoryHit>();
        // No filter = the DEFAULT filter, which leaves superseded entries out ("pink" must not answer once "purple" replaced it). Applying
        // no filter at all returned them (found by AiMemoryEngine's parity gate, which treated null as the default).
        filter ??= new AiMemoryFilter();
        ranking ??= new AiMemoryRanking();
        var q = SpeakerRegistry.Normalize(query.ToArray());
        var now = nowTicks ?? DateTime.UtcNow.Ticks;
        lock (_lock)
        {
            var hits = new List<(int i, float sim, float score)>();
            for (int i = 0; i < _entries.Count; i++)
            {
                var e = _entries[i];
                if (!filter.Matches(e)) continue;
                var v = _vectors[i];
                float dot = 0;
                for (int d = 0; d < v.Length; d++) dot += q[d] * v[d];
                hits.Add((i, dot, Score(ranking, dot, e, now)));
            }
            // Ties broken by newer first, then id, so the order is deterministic (the device search must agree).
            return hits.OrderByDescending(h => h.score).ThenByDescending(h => _entries[h.i].TimeTicks).ThenBy(h => _entries[h.i].Id, StringComparer.Ordinal)
                .Take(k).Select(h => new AiMemoryHit(_entries[h.i].Clone(), h.sim, h.score)).ToList();
        }
    }

    /// <summary>The ranked score of one entry (exposed so a device search can be checked against it).</summary>
    public static float Score(AiMemoryRanking ranking, float similarity, AiMemoryEntry e, long nowTicks)
    {
        double age = Math.Max(0, nowTicks - e.TimeTicks);
        double recency = Math.Pow(0.5, age / ranking.HalfLifeTicks);
        return (float)(ranking.Relevance * similarity + ranking.Recency * recency + ranking.Importance * e.Importance);
    }

    /// <summary>
    /// A copy of the store's vectors (row per entry, in <see cref="List"/>-independent store order) with the ids in the same order: what a device search uploads.
    /// </summary>
    public (IReadOnlyList<string> Ids, float[] Matrix) Snapshot()
    {
        lock (_lock)
        {
            var m = new float[_vectors.Count * Dimension];
            for (int i = 0; i < _vectors.Count; i++) _vectors[i].CopyTo(m, i * Dimension);
            return (_entries.Select(e => e.Id).ToList(), m);
        }
    }

    /// <summary>
    /// Switches the store to another embedding model in one step: <paramref name="embeddings"/> must hold a vector of <paramref name="dimension"/> for EVERY entry (embed them
    /// in the background from <see cref="List"/>, keep searching the old space meanwhile, then call this). Entries added since are rejected by the count check - embed them too.
    /// </summary>
    public void ReplaceEmbeddings(string modelId, int dimension, IReadOnlyDictionary<string, float[]> embeddings)
    {
        lock (_lock)
        {
            var missing = _entries.Where(e => !embeddings.ContainsKey(e.Id)).Select(e => e.Id).Take(3).ToList();
            if (missing.Count > 0) throw new ArgumentException($"no new embedding for {string.Join(", ", missing)}; a store never mixes two models' vectors");
            var fresh = new List<float[]>(_entries.Count);
            foreach (var e in _entries)
            {
                var v = embeddings[e.Id];
                if (v.Length != dimension) throw new ArgumentException($"embedding for {e.Id} has {v.Length} values, not {dimension}");
                fresh.Add(SpeakerRegistry.Normalize((float[])v.Clone()));
            }
            _vectors.Clear(); _vectors.AddRange(fresh);
            ModelId = modelId; Dimension = dimension;
        }
        Reembedded?.Invoke();
    }

    #region Persistence

    private const uint Magic = 0x4D454D53; // "SMEM"
    private const int FormatVersion = 1;

    private sealed record SavedMeta(int Version, string ModelId, int Dimension, bool LearningPaused, List<string> Blocked, List<AiMemoryEntry> Entries);

    /// <summary>
    /// Writes the entries (JSON, readable, for export) and the embeddings (binary: magic, version, model id, dimension, count, then per entry its id and floats) to two streams.
    /// Deleted entries are simply absent: a delete is gone from the files on the next save.
    /// </summary>
    public async Task SaveAsync(Stream meta, Stream embeddings, CancellationToken ct = default)
    {
        SavedMeta saved; List<(string id, float[] v)> rows;
        lock (_lock)
        {
            saved = new SavedMeta(FormatVersion, ModelId, Dimension, LearningPaused, _blocked.ToList(), _entries.Select(e => e.Clone()).ToList());
            rows = _entries.Select((e, i) => (e.Id, _vectors[i])).ToList();
        }
        await JsonSerializer.SerializeAsync(meta, saved, cancellationToken: ct).ConfigureAwait(false);
        using var w = new BinaryWriter(embeddings, Encoding.UTF8, leaveOpen: true);
        w.Write(Magic); w.Write(FormatVersion); w.Write(saved.ModelId); w.Write(saved.Dimension); w.Write(rows.Count);
        var buf = new byte[saved.Dimension * 4];
        foreach (var (id, v) in rows)
        {
            w.Write(id);
            for (int d = 0; d < v.Length; d++) BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(d * 4), v[d]);
            w.Write(buf);
        }
        w.Flush();
    }

    /// <summary>
    /// Reads what <see cref="SaveAsync"/> wrote. Throws when the two files disagree (different model, an entry without a vector) rather than guess.
    /// </summary>
    public static async Task<AiMemoryStore> LoadAsync(Stream meta, Stream embeddings, CancellationToken ct = default)
    {
        var saved = await JsonSerializer.DeserializeAsync<SavedMeta>(meta, cancellationToken: ct).ConfigureAwait(false)
            ?? throw new FormatException("not a memory store");
        if (saved.Version != FormatVersion) throw new FormatException($"memory store format {saved.Version}; this build reads {FormatVersion}");
        using var r = new BinaryReader(embeddings, Encoding.UTF8, leaveOpen: true);
        if (r.ReadUInt32() != Magic) throw new FormatException("not a memory embedding file");
        var version = r.ReadInt32();
        if (version != FormatVersion) throw new FormatException($"memory embedding format {version}; this build reads {FormatVersion}");
        var modelId = r.ReadString(); var dim = r.ReadInt32(); var count = r.ReadInt32();
        if (modelId != saved.ModelId || dim != saved.Dimension)
            throw new FormatException($"the entries were saved for {saved.ModelId} ({saved.Dimension}) but the embeddings are {modelId} ({dim})");
        var vectors = new Dictionary<string, float[]>(count, StringComparer.Ordinal);
        var buf = new byte[dim * 4];
        for (int n = 0; n < count; n++)
        {
            var id = r.ReadString();
            if (r.Read(buf, 0, buf.Length) != buf.Length) throw new FormatException("memory embedding file is truncated");
            var v = new float[dim];
            for (int d = 0; d < dim; d++) v[d] = BinaryPrimitives.ReadSingleLittleEndian(buf.AsSpan(d * 4));
            vectors[id] = v;
        }
        var store = new AiMemoryStore(saved.ModelId, saved.Dimension) { LearningPaused = saved.LearningPaused };
        foreach (var b in saved.Blocked) store._blocked.Add(b);
        foreach (var e in saved.Entries)
        {
            if (!vectors.TryGetValue(e.Id, out var v)) throw new FormatException($"memory '{e.Id}' has no embedding");
            store._index[e.Id] = store._entries.Count;
            store._entries.Add(e);
            store._vectors.Add(v);
        }
        if (vectors.Count != store._entries.Count) throw new FormatException("the embedding file holds vectors for entries that are not in the store");
        return store;
    }

    #endregion
}
