using System.Collections.Concurrent;
using ILGPU;
using ILGPU.Runtime;
using SpawnDev.ILGPU;
using SpawnDev.ILGPU.ML;
using SpawnDev.ILGPU.ML.Hub;
using SpawnDev.ILGPU.ML.Pipelines;
using SpawnDev.ILGPU.ML.Preprocessing;
using SpawnDev.ILGPU.ML.Retrieval;
using FV = ILGPU.Runtime.ArrayView1D<float, ILGPU.Stride1D.Dense>;
using IV = ILGPU.Runtime.ArrayView1D<int, ILGPU.Stride1D.Dense>;
using LV = ILGPU.Runtime.ArrayView1D<long, ILGPU.Stride1D.Dense>;

namespace SpawnDev.AI.Server;

/// <summary>
/// Long-term memory on the accelerator: embeds text with a sentence-embedding model (all-MiniLM-L6-v2 by default) and searches an
/// <see cref="AiMemoryStore"/> by meaning, recency and importance, with only the k best results ever leaving the device.
/// </summary>
/// <remarks>
/// <para>
/// The <see cref="Store"/> owns the memories (and their persistence and privacy rules); this engine keeps a device copy of every vector in a
/// <see cref="DeviceVectorIndex"/> and of the per-entry facts a search filters and ranks on (kind, scope, superseded, time, importance) in
/// slot-indexed device arrays. A query is embedded on the device, one kernel turns those arrays into the allow mask and the
/// recency + importance addend, and the index ranks: score = Relevance x cosine + Recency x 0.5^(age / half-life) + Importance x importance
/// (<see cref="AiMemoryRanking"/>), the same formula as <see cref="AiMemoryStore.Search"/>, which is the reference this engine is gated
/// against. Filters on subjects or tags are rarer and set-valued: they are resolved on the host into a mask, only for the queries that use them.
/// </para>
/// <para>
/// The store can be changed directly (deletes, supersedes, a person forgotten through a registry event): the engine follows its events.
/// Those events are QUEUED and applied before the next device operation, never run from the event itself - on a browser's single thread a
/// handler that waited for a search in flight would deadlock.
/// </para>
/// </remarks>
public sealed partial class AiMemoryEngine : IDisposable
{
    private readonly IModelSource _source;
    private readonly Accelerator _acc;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ConcurrentQueue<Action> _pending = new();
    private FeatureExtractionPipeline? _embedder;
    private LoadedTokenizer? _tokenizer;
    private DeviceVectorIndex? _index;
    private readonly Dictionary<string, int> _slotOf = new(StringComparer.Ordinal);
    private readonly Dictionary<int, string> _idOf = new();
    private readonly Dictionary<string, int> _scopeIds = new(StringComparer.Ordinal) { [""] = 0 };
    private MemoryBuffer1D<int, Stride1D.Dense>? _kind, _scope, _superseded, _allow, _hostAllow;
    private MemoryBuffer1D<long, Stride1D.Dense>? _time, _params;
    private MemoryBuffer1D<float, Stride1D.Dense>? _importance, _addend, _query, _scratch;
    private Action<Index1D, IV, IV, IV, LV, FV, IV, IV, FV, LV, float, float>? _filterRank;
    private bool _subscribed;

    /// <param name="source">Where the embedding model comes from (the hub, a local folder).</param>
    /// <param name="accelerator">The shared accelerator. Never disposed here - the app owns it.</param>
    /// <param name="store">The memories. Its <see cref="AiMemoryStore.ModelId"/> must be <see cref="ModelRepo"/> (a store made for another model is re-embedded by <see cref="ReembedAsync"/>).</param>
    public AiMemoryEngine(IModelSource source, Accelerator accelerator, AiMemoryStore store)
    {
        _source = source; _acc = accelerator; Store = store;
    }

    /// <summary>The memories this engine searches.</summary>
    public AiMemoryStore Store { get; }

    /// <summary>Hugging Face repo of the sentence-embedding model (Apache-2.0).</summary>
    public string ModelRepo { get; set; } = ModelHub.KnownModels.AllMiniLmL6V2;
    /// <summary>The ONNX file in <see cref="ModelRepo"/>.</summary>
    public string ModelFile { get; set; } = "onnx/model.onnx";
    /// <summary>The tokenizer file in <see cref="ModelRepo"/>.</summary>
    public string TokenizerFile { get; set; } = "tokenizer.json";
    /// <summary>The embedding length the model produces (384 for MiniLM-L6).</summary>
    public int Dimension { get; set; } = 384;
    /// <summary>Tokens per text the model sees (longer text is cut). 128 covers a memory of a few sentences.</summary>
    public int MaxTokens { get; set; } = 128;

    /// <summary>Whether the model is resident.</summary>
    public bool IsLoaded => _embedder != null;

    /// <summary>The most results one search returns.</summary>
    public int MaxK => DeviceVectorIndex.MaxK;

    // Times on the device are UTC ticks (long: exact filters on every backend, emulated where 64-bit is not native).

    /// <summary>
    /// Loads the model and uploads the store's vectors and facts (once). Throws when the store's embeddings come from another model than
    /// <see cref="ModelRepo"/>: searching them with this model's queries would compare two different spaces. <see cref="ReembedAsync"/> fixes that.
    /// </summary>
    public async Task EnsureReadyAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await LoadModelLockedAsync(ct).ConfigureAwait(false);
            if (_index == null)
            {
                if (!string.Equals(Store.ModelId, ModelRepo, StringComparison.Ordinal) || Store.Dimension != Dimension)
                    throw new InvalidOperationException($"the store holds embeddings of {Store.ModelId} ({Store.Dimension} numbers); this engine makes {ModelRepo} ({Dimension}). Call ReembedAsync first.");
                Rebuild();
                if (!_subscribed)
                {
                    Store.Added += (e, v) => _pending.Enqueue(() => AddToDevice(e, v));
                    Store.Removed += ids => _pending.Enqueue(() => { foreach (var id in ids) RemoveFromDevice(id); });
                    Store.Changed += id => _pending.Enqueue(() => RefreshFacts(id));
                    Store.Reembedded += () => _pending.Enqueue(Rebuild);
                    _subscribed = true;
                }
            }
            ApplyPending();
        }
        finally { _gate.Release(); }
    }

    /// <summary>
    /// Re-embeds every memory with THIS engine's model and switches the store to it in one step (<see cref="AiMemoryStore.ReplaceEmbeddings"/>):
    /// for a model change. Until the switch, an engine on the old model keeps answering from the old vectors; after it, that engine refuses to
    /// search (it would compare two spaces) and this one serves. Memories added meanwhile are embedded too. Reports (done, total).
    /// </summary>
    public async Task ReembedAsync(IProgress<(int Done, int Total)>? progress = null, CancellationToken ct = default)
    {
        var vectors = new Dictionary<string, float[]>(StringComparer.Ordinal);
        var all = new AiMemoryFilter { IncludeSuperseded = true };
        while (true)
        {
            var todo = Store.List(all).Where(e => !vectors.ContainsKey(e.Id)).ToList();
            int total = vectors.Count + todo.Count;
            foreach (var e in todo)
            {
                vectors[e.Id] = await EmbedCoreAsync(e.Text, ct).ConfigureAwait(false);
                progress?.Report((vectors.Count, total));
            }
            try { Store.ReplaceEmbeddings(ModelRepo, Dimension, vectors); return; }
            catch (ArgumentException) when (Store.List(all).Any(e => !vectors.ContainsKey(e.Id))) { /* added while embedding: embed those and switch */ }
        }
    }

    private async Task LoadModelLockedAsync(CancellationToken ct)
    {
        if (_embedder != null) return;
        await using (var t = await _source.OpenAsync(ModelRepo, TokenizerFile, ct).ConfigureAwait(false))
        using (var reader = new StreamReader(t))
            _tokenizer = TokenizerLoader.FromTokenizerJson(await reader.ReadToEndAsync(ct).ConfigureAwait(false));
        InferenceSession session;
        await using (var m = await _source.OpenAsync(ModelRepo, ModelFile, ct).ConfigureAwait(false))
            session = await InferenceSession.CreateFromOnnxStreamAsync(_acc, m, inputShapes: new Dictionary<string, int[]>
            {
                // all three: the model declares token_type_ids too, and an input the session never pins is an unwritten buffer
                ["input_ids"] = new[] { 1, MaxTokens },
                ["attention_mask"] = new[] { 1, MaxTokens },
                ["token_type_ids"] = new[] { 1, MaxTokens },
            }, ct: ct).ConfigureAwait(false);
        _embedder = new FeatureExtractionPipeline(session, _acc, maxLength: MaxTokens, hiddenSize: Dimension);
        _query = _acc.Allocate1D<float>(Dimension);
        _scratch = _acc.Allocate1D<float>(Dimension);
        _params = _acc.Allocate1D<long>(8);
        _filterRank = _acc.LoadAutoGroupedStreamKernel<Index1D, IV, IV, IV, LV, FV, IV, IV, FV, LV, float, float>(FilterRankKernel);
    }

    /// <summary>
    /// Remembers something: embeds <see cref="AiMemoryEntry.Text"/> on the device and adds the entry to the <see cref="Store"/> (which keeps
    /// the vector for its files; 384 numbers come back for that, nothing else). Returns the id, or null when the store refused it
    /// (learning paused, a subject who must not be learned from).
    /// </summary>
    public async Task<string?> RememberAsync(AiMemoryEntry entry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var v = await EmbedAsync(entry.Text, ct).ConfigureAwait(false);
        var id = Store.Add(entry, v);
        await EnsureReadyAsync(ct).ConfigureAwait(false);   // applies the Added event now
        return id;
    }

    /// <summary>The embedding of <paramref name="text"/> (unit length), read back to the host. For storage; a search keeps its query on the device.</summary>
    public Task<float[]> EmbedAsync(string text, CancellationToken ct = default) => EmbedCoreAsync(text, ct);

    private async Task<float[]> EmbedCoreAsync(string text, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await LoadModelLockedAsync(ct).ConfigureAwait(false);
            await _embedder!.EmbedAsync(text ?? "", _tokenizer!, _scratch!.View).ConfigureAwait(false);
            return await _scratch.View.CopyToHostAsync<float, Stride1D.Dense>().ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    /// <summary>
    /// The <paramref name="k"/> best memories for <paramref name="query"/>, best first, by <paramref name="ranking"/> (default
    /// <see cref="AiMemoryRanking"/>) among those passing <paramref name="filter"/> (superseded facts left out unless it says otherwise).
    /// <paramref name="nowTicks"/> fixes "now" for recency (default: the clock).
    /// </summary>
    public async Task<IReadOnlyList<AiMemoryHit>> SearchAsync(string query, int k, AiMemoryFilter? filter = null, AiMemoryRanking? ranking = null,
        long? nowTicks = null, CancellationToken ct = default)
    {
        await EnsureReadyAsync(ct).ConfigureAwait(false);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ApplyPending();
            await _embedder!.EmbedAsync(query ?? "", _tokenizer!, _query!.View).ConfigureAwait(false);
            return await SearchLockedAsync(_query.View, k, filter, ranking, nowTicks).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    /// <summary><see cref="SearchAsync(string, int, AiMemoryFilter?, AiMemoryRanking?, long?, CancellationToken)"/> for a query embedding already on the device (<see cref="Dimension"/> floats, unit length).</summary>
    public async Task<IReadOnlyList<AiMemoryHit>> SearchAsync(FV queryEmbedding, int k, AiMemoryFilter? filter = null, AiMemoryRanking? ranking = null,
        long? nowTicks = null, CancellationToken ct = default)
    {
        await EnsureReadyAsync(ct).ConfigureAwait(false);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try { ApplyPending(); return await SearchLockedAsync(queryEmbedding, k, filter, ranking, nowTicks).ConfigureAwait(false); }
        finally { _gate.Release(); }
    }

    /// <summary><see cref="SearchAsync(string, int, AiMemoryFilter?, AiMemoryRanking?, long?, CancellationToken)"/> for a query embedding on the host.</summary>
    public async Task<IReadOnlyList<AiMemoryHit>> SearchAsync(float[] queryEmbedding, int k, AiMemoryFilter? filter = null, AiMemoryRanking? ranking = null,
        long? nowTicks = null, CancellationToken ct = default)
    {
        if (queryEmbedding.Length != Dimension) throw new ArgumentException($"a query is {Dimension} numbers", nameof(queryEmbedding));
        await EnsureReadyAsync(ct).ConfigureAwait(false);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ApplyPending();
            _query!.View.CopyFromCPU(SpeakerRegistry.Normalize((float[])queryEmbedding.Clone()));
            return await SearchLockedAsync(_query.View, k, filter, ranking, nowTicks).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    private async Task<IReadOnlyList<AiMemoryHit>> SearchLockedAsync(FV query, int k, AiMemoryFilter? filter, AiMemoryRanking? ranking, long? nowTicks)
    {
        if (!string.Equals(Store.ModelId, ModelRepo, StringComparison.Ordinal))
            throw new InvalidOperationException($"the store now holds embeddings of {Store.ModelId}; this engine embeds queries with {ModelRepo}. Use an engine on {Store.ModelId}.");
        if (k <= 0 || _index!.Count == 0) return Array.Empty<AiMemoryHit>();
        if (k > MaxK) throw new ArgumentOutOfRangeException(nameof(k), $"at most {MaxK} results per search");
        ranking ??= new AiMemoryRanking();
        filter ??= new AiMemoryFilter();
        long now = nowTicks ?? DateTime.UtcNow.Ticks;
        int cap = _index.Capacity;

        // Set-valued filters (subjects, tags) on the host, only when asked for.
        bool useHost = filter.AnySubject != null || filter.AllTags != null;
        if (useHost)
        {
            var mask = new int[cap];
            foreach (var e in Store.List(filter)) if (_slotOf.TryGetValue(e.Id, out var slot)) mask[slot] = 1;
            _hostAllow!.View.SubView(0, cap).CopyFromCPU(mask);
        }
        int scopeId = filter.Scope == null ? -1 : (_scopeIds.TryGetValue(filter.Scope, out var sid) ? sid : int.MaxValue);   // an unknown scope matches only the every-scope entries
        int kindMask = filter.Kinds == null ? -1 : filter.Kinds.Aggregate(0, (m, kd) => m | (1 << (int)kd));
        // The search's scalars in one tiny upload (8 longs): a kernel loader takes at most 15 parameters, and 64-bit values do not ride in a
        // struct uniform on WebGL.
        _params!.View.CopyFromCPU(new long[] { filter.FromTicks ?? long.MinValue, filter.ToTicks ?? long.MaxValue, now, (long)ranking.HalfLifeTicks,
            scopeId, kindMask, filter.IncludeSuperseded ? 1 : 0, useHost ? 1 : 0 });
        _filterRank!(cap, _kind!.View, _scope!.View, _superseded!.View, _time!.View, _importance!.View, _hostAllow!.View, _allow!.View, _addend!.View,
            _params.View, ranking.Recency, ranking.Importance);
        var top = await _index.SearchAsync(query, k, _allow.View, _addend.View, ranking.Relevance).ConfigureAwait(false);

        // The plain cosine of each hit, for the caller (one read of the query: Dimension floats).
        var q = top.Length > 0 ? await query.SubView(0, Dimension).CopyToHostAsync<float, Stride1D.Dense>().ConfigureAwait(false) : Array.Empty<float>();
        var hits = new List<AiMemoryHit>(top.Length);
        foreach (var (slot, score) in top)
        {
            if (!_idOf.TryGetValue(slot, out var id)) continue;
            var e = Store.Get(id); var v = Store.GetEmbedding(id);
            if (e == null || v == null) continue;   // removed after this search was queued
            float dot = 0; for (int d = 0; d < Dimension; d++) dot += q[d] * v[d];
            hits.Add(new AiMemoryHit(e, dot, score));
        }
        return hits;
    }

    // allow[s] = live slot passing scope / kind / superseded / time (and the host mask when used); addend[s] = recency + importance terms.
    // p: [0] fromTicks, [1] toTicks (exclusive), [2] nowTicks, [3] halfLifeTicks, [4] scopeId (-1 any), [5] kindMask, [6] includeSuperseded, [7] useHost
    private static void FilterRankKernel(Index1D i, IV kind, IV scope, IV superseded, LV time, FV importance, IV hostAllow, IV allow, FV addend,
        LV p, float wRec, float wImp)
    {
        long fromTicks = p[0], toTicks = p[1], nowTicks = p[2], halfLifeTicks = p[3];
        int scopeId = (int)p[4], kindMask = (int)p[5], includeSuperseded = (int)p[6], useHost = (int)p[7];
        int s = i;
        int k = kind[s];
        bool ok = k >= 0;                                                     // -1 = empty slot
        if (scopeId >= 0) ok = ok & (scope[s] == 0 | scope[s] == scopeId);
        ok = ok & ((kindMask >> (k & 31) & 1) != 0);
        if (includeSuperseded == 0) ok = ok & (superseded[s] == 0);
        long t = time[s];
        ok = ok & (t >= fromTicks) & (t < toTicks);
        if (useHost != 0) ok = ok & (hostAllow[s] != 0);
        allow[s] = ok ? 1 : 0;
        long age = nowTicks - t;
        if (age < 0) age = 0;
        float halfLives = (float)((double)age / (double)halfLifeTicks);
        addend[s] = wRec * MathF.Exp(-0.6931472f * halfLives) + wImp * importance[s];
    }

    // ---- keeping the device copy in step with the store (always under _gate) ----

    private void ApplyPending()
    {
        while (_pending.TryDequeue(out var a)) a();
    }

    private void Rebuild()
    {
        // After a re-embed to ANOTHER model this engine's queries no longer match the store: keep the old index (searches refuse, see above).
        if (_index != null && !string.Equals(Store.ModelId, ModelRepo, StringComparison.Ordinal)) return;
        _index?.Dispose();
        _slotOf.Clear(); _idOf.Clear();
        var (ids, matrix) = Store.Snapshot();
        _index = new DeviceVectorIndex(_acc, Dimension, Math.Max(1024, ids.Count));
        _index.CapacityChanged += GrowFacts;
        AllocateFacts(_index.Capacity);
        if (ids.Count > 0)
        {
            var slots = _index.AddRange(matrix, ids.Count);
            for (int n = 0; n < ids.Count; n++) { _slotOf[ids[n]] = slots[n]; _idOf[slots[n]] = ids[n]; }
            // ONE upload per fact array. Per-entry RefreshFacts made 5 tiny uploads per memory: 50,000 entries took 10.8 s to load on an
            // RTX 2060 and 3.6 s on a 4070 (Seven's measurement, 2026-11-01), all of it paid at every start of the app.
            int cap = _index.Capacity;
            var kind = Enumerable.Repeat(-1, cap).ToArray();
            var scope = new int[cap]; var sup = new int[cap]; var time = new long[cap]; var imp = new float[cap];
            var byId = Store.List(new AiMemoryFilter { IncludeSuperseded = true }).ToDictionary(e => e.Id, StringComparer.Ordinal);
            for (int n = 0; n < ids.Count; n++)
            {
                if (!byId.TryGetValue(ids[n], out var e)) continue;
                int slot = slots[n];
                if (!_scopeIds.TryGetValue(e.Scope, out var sc)) { sc = _scopeIds.Count; _scopeIds[e.Scope] = sc; }
                kind[slot] = (int)e.Kind; scope[slot] = sc; sup[slot] = e.SupersededBy != null ? 1 : 0; time[slot] = e.TimeTicks; imp[slot] = e.Importance;
            }
            _kind!.View.CopyFromCPU(kind); _scope!.View.CopyFromCPU(scope); _superseded!.View.CopyFromCPU(sup);
            _time!.View.CopyFromCPU(time); _importance!.View.CopyFromCPU(imp);
        }
    }

    private void AllocateFacts(int cap)
    {
        foreach (var b in new IDisposable?[] { _kind, _scope, _superseded, _allow, _hostAllow, _time, _importance, _addend }) b?.Dispose();
        _kind = _acc.Allocate1D<int>(cap); _kind.View.CopyFromCPU(Enumerable.Repeat(-1, cap).ToArray());
        _scope = _acc.Allocate1D<int>(cap); _scope.MemSetToZero();
        _superseded = _acc.Allocate1D<int>(cap); _superseded.MemSetToZero();
        _allow = _acc.Allocate1D<int>(cap); _allow.MemSetToZero();
        _hostAllow = _acc.Allocate1D<int>(cap); _hostAllow.MemSetToZero();
        _time = _acc.Allocate1D<long>(cap); _time.MemSetToZero();
        _importance = _acc.Allocate1D<float>(cap); _importance.MemSetToZero();
        _addend = _acc.Allocate1D<float>(cap); _addend.MemSetToZero();
    }

    private void GrowFacts(int cap)
    {
        var old = (_kind!, _scope!, _superseded!, _time!, _importance!);
        int was = (int)old.Item1.Length;
        _kind = null; _scope = null; _superseded = null; _time = null; _importance = null;   // AllocateFacts must not dispose the ones being copied
        AllocateFacts(cap);
        _kind!.View.SubView(0, was).CopyFrom(old.Item1.View);
        _scope!.View.SubView(0, was).CopyFrom(old.Item2.View);
        _superseded!.View.SubView(0, was).CopyFrom(old.Item3.View);
        _time!.View.SubView(0, was).CopyFrom(old.Item4.View);
        _importance!.View.SubView(0, was).CopyFrom(old.Item5.View);
        _retired.AddRange(new IDisposable[] { old.Item1, old.Item2, old.Item3, old.Item4, old.Item5 });   // a pending copy may still read them
    }
    private readonly List<IDisposable> _retired = new();

    private void AddToDevice(AiMemoryEntry e, float[] v)
    {
        if (_index == null || _slotOf.ContainsKey(e.Id)) return;
        var slot = _index.Add(v);
        _slotOf[e.Id] = slot; _idOf[slot] = e.Id;
        RefreshFacts(e.Id);
    }

    private void RemoveFromDevice(string id)
    {
        if (_index == null || !_slotOf.Remove(id, out var slot)) return;
        _idOf.Remove(slot);
        _index.Remove(slot);                                       // zeroes the vector on the device
        _kind!.View.SubView(slot, 1).CopyFromCPU(new[] { -1 });
    }

    private void RefreshFacts(string id)
    {
        if (!_slotOf.TryGetValue(id, out var slot)) return;
        var e = Store.Get(id);
        if (e == null) return;
        if (!_scopeIds.TryGetValue(e.Scope, out var sc)) { sc = _scopeIds.Count; _scopeIds[e.Scope] = sc; }
        _kind!.View.SubView(slot, 1).CopyFromCPU(new[] { (int)e.Kind });
        _scope!.View.SubView(slot, 1).CopyFromCPU(new[] { sc });
        _superseded!.View.SubView(slot, 1).CopyFromCPU(new[] { e.SupersededBy != null ? 1 : 0 });
        _time!.View.SubView(slot, 1).CopyFromCPU(new[] { e.TimeTicks });
        _importance!.View.SubView(slot, 1).CopyFromCPU(new[] { e.Importance });
    }

    /// <summary>Disposes the model and the device copies. Never the accelerator, never the store.</summary>
    public void Dispose()
    {
        _embedder?.Dispose(); _index?.Dispose();
        foreach (var b in new IDisposable?[] { _kind, _scope, _superseded, _allow, _hostAllow, _time, _importance, _addend, _query, _scratch, _params }) b?.Dispose();
        foreach (var b in _retired) b.Dispose();
        _gate.Dispose();
    }
}
