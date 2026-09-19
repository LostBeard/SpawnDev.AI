using ILGPU.Runtime;
using SpawnDev.ILGPU.ML;
using SpawnDev.ILGPU.ML.GGUF;
using SpawnDev.ILGPU.ML.Hub;
using SpawnDev.ILGPU.ML.Pipelines;
using SpawnDev.ILGPU.ML.Preprocessing;

namespace SpawnDev.AI.Server;

/// <summary>
/// A model the hub provider can serve: the name clients address it by, plus where to get it.
/// </summary>
/// <param name="Name">What clients ask for, e.g. <c>qwen3:1.7b-q8_0</c>.</param>
/// <param name="Repo">Hugging Face repo id. Empty for an ollama-registry model.</param>
/// <param name="File">File within the repo. Empty for an ollama-registry model.</param>
/// <param name="ApproxSizeBytes">
/// Download size. NOT decoration - the UI shows it before asking the user to commit, so a wrong value
/// here misinforms exactly the consent this is meant to obtain.
/// </param>
/// <param name="Description">One line the picker shows: what this model is good for, and its trade-off.</param>
/// <param name="OllamaModel">Ollama registry model name (e.g. <c>gemma4</c>), or null for the HF path.</param>
/// <param name="OllamaTag">Ollama tag (e.g. <c>12b</c>).</param>
/// <remarks>
/// Two sources because the hub proxies both. HF coordinates cover most GGUF quants; the ollama registry is
/// how multi-layer models arrive (gemma4 ships its weights and its vision/audio projector as separate
/// layers), and it is the path the ILGPU.ML demo already uses for gemma4:12b.
/// </remarks>
public sealed record HubModelOption(string Name, string Repo, string File, long ApproxSizeBytes = 0,
    string Description = "", string? OllamaModel = null, string? OllamaTag = null)
{
    /// <summary>True when this model comes from the ollama registry rather than Hugging Face.</summary>
    public bool IsOllama => !string.IsNullOrEmpty(OllamaModel);

    /// <summary>An ollama-registry model, e.g. <c>FromOllama("gemma4:12b", "gemma4", "12b", ...)</c>.</summary>
    public static HubModelOption FromOllama(string name, string model, string tag, long approxSizeBytes = 0,
        string description = "")
        => new(name, "", "", approxSizeBytes, description, model, tag);

    /// <summary>The file name this model lands under, used to recognise it in the torrent cache.</summary>
    public string CacheFileHint => IsOllama ? $"{OllamaModel}-{OllamaTag}" : File;
}

/// <summary>
/// The browser model provider: streams GGUF weights from the SpawnDev hub straight onto the GPU
/// (weights load AS they download; later loads hit the browser cache). This is what the in-browser
/// worker server uses in place of Ollama's on-disk cache. Also works on desktop for hub-served models.
/// </summary>
public sealed class HubModelProvider : IAiModelProvider
{
    private readonly IModelSource _source;
    private readonly HttpClient _http;
    private readonly List<HubModelOption> _models;

    /// <summary>Progress callback while weights stream ((stage, percent) per hub events).</summary>
    public Action<string, int>? OnLoadProgress { get; set; }

    /// <summary>
    /// The model <see cref="LoadAsync"/> is working on right now, or empty between loads.
    /// </summary>
    /// <remarks>
    /// 🔴 Needed because <see cref="OnLoadProgress"/> carries a stage and a percent and NOT a name, so a
    /// subscriber could report "uploading weights 43%" without being able to say of what. With several
    /// model kinds sharing one GPU that is genuinely ambiguous to a user watching a page - a chat model
    /// and an image model report the same stage names.
    /// </remarks>
    public string LoadingModel { get; private set; } = "";

    /// <summary>Hub preparation timeout (cold hub cache can take minutes for multi-GB models).</summary>
    public TimeSpan PrepareTimeout { get; set; } = TimeSpan.FromMinutes(8);

    /// <param name="source">
    /// Model delivery. <see cref="HubModelSource"/> is plain HTTP through the hub cached in OPFS.
    /// </param>
    public HubModelProvider(IModelSource source, HttpClient http, IEnumerable<HubModelOption> models)
    {
        _source = source;
        _http = http;
        _models = models.ToList();
    }

    /// <summary>The configured model list (mutable - add options at runtime before they're requested).</summary>
    public List<HubModelOption> Models => _models;

    public Task<IReadOnlyList<AiModelInfo>> ListAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<AiModelInfo>>(
            _models.Select(m => new AiModelInfo(m.Name, m.ApproxSizeBytes, "gguf",
                OllamaCacheModelProvider.QuantOf(m.File), 0, new[] { "completion", "tools" })).ToList());

    public async Task<AiModelInfo?> ShowAsync(string name, CancellationToken ct = default)
    {
        var m = Find(name);
        if (m == null) return null;
        return (await ListAsync(ct).ConfigureAwait(false)).First(i => i.Name == m.Name);
    }

    public Task<string?> ResolveAsync(string name, CancellationToken ct = default)
        => Task.FromResult(Find(name)?.Name);

    /// <summary>
    /// How much of this model is cached on this device, 0..1 - or null when nothing has fetched it.
    /// </summary>
    /// <remarks>
    /// Reports progress from <see cref="ICachingModelSource"/> (active downloads and the resumable store).
    /// Consent - whether the user agreed to download - remains the guard for starting a fetch; this value
    /// is for showing progress only.
    /// </remarks>
    public async Task<double?> CachedFractionAsync(string name, CancellationToken ct = default)
    {
        var opt = Find(name);
        if (opt == null) return null;

        // Cache state is a CAPABILITY, not a guarantee: a source that does not cache (a ranged reader, a
        // local-file source) has nothing to report, and null already means "nothing has fetched it".
        if (_source is not ICachingModelSource caching) return null;

        var key = opt.IsOllama
            ? HubModelSource.OllamaCacheKey(opt.OllamaModel!, opt.OllamaTag!, "model")
            : caching.CacheKey(opt.Repo, opt.File);

        // In flight: the downloader knows exactly how far it is.
        foreach (var d in caching.ActiveDownloads)
            if (d.Key == key) return d.Fraction ?? 0d;

        // Otherwise ask the store. A COMPLETE entry is 1.0; a partial reports its real fraction of the
        // expected total; absent is null.
        if (caching.Store is not IResumableModelStore resumable)
            return await caching.Store.ExistsAsync(key, ct).ConfigureAwait(false) ? 1d : null;

        var state = await resumable.GetStateAsync(key, ct).ConfigureAwait(false);
        if (!state.Exists) return null;
        if (state.Complete) return 1d;
        return state.TotalBytes > 0 ? Math.Clamp((double)state.BytesWritten / state.TotalBytes, 0d, 1d) : 0d;
    }

    /// <summary>Name, size, purpose and how much is cached - what a picker needs before asking.</summary>
    /// <remarks>
    /// The size is the CONFIGURED figure (<c>ApproxSizeBytes</c>), which is hand-entered and worth checking
    /// against the published file.
    /// </remarks>
    public async Task<IReadOnlyList<(string Name, long SizeBytes, string Description, double? CachedFraction)>>
        CatalogueAsync(CancellationToken ct = default)
    {
        var rows = new List<(string, long, string, double?)>(_models.Count);
        foreach (var m in _models)
            rows.Add((m.Name, m.ApproxSizeBytes, m.Description,
                await CachedFractionAsync(m.Name, ct).ConfigureAwait(false)));
        return rows;
    }
    public async Task<LoadedModel> LoadAsync(string name, Accelerator accelerator, int maxSeqLen,
        bool enableWebGPUDecodeCapture, CancellationToken ct = default)
    {
        var opt = Find(name)
            ?? throw new FileNotFoundException($"Model '{name}' is not in the hub model list.");
        LoadingModel = opt.Name;
        try
        {
            return await LoadCoreAsync(opt, accelerator, maxSeqLen, enableWebGPUDecodeCapture, ct)
                .ConfigureAwait(false);
        }
        finally
        {
            // 🔴 In a finally, always. A load that throws in "upload" would otherwise leave a progress UI
            // reporting a stage that stopped running, which is a frozen bar over a failed request.
            LoadingModel = "";
            OnLoadProgress?.Invoke("idle", 100);
        }
    }

    private async Task<LoadedModel> LoadCoreAsync(HubModelOption opt, Accelerator accelerator, int maxSeqLen,
        bool enableWebGPUDecodeCapture, CancellationToken ct)
    {
        // ⚠️ REPORTED BEFORE THE AWAIT, not after. Opening the stream is where a cold model spends its
        // download - MEASURED 44.4 s for 1.83 GB - and a stage announced only on the way out would leave
        // exactly that stretch unreported, which is the stretch the user is staring at.
        OnLoadProgress?.Invoke("fetch", 0);
        // Plain HTTP into OPFS. A range request fetches only what is read.
        // Ollama addressing (model:tag/layer) is deliberately outside IModelSource - it is not repo/path, and
        // pretending otherwise would force every implementer to honour a shape it does not have. So it is a
        // capability test, with a message that names the actual limitation rather than a cast failure.
        var stream = opt.IsOllama
            ? _source is HubModelSource hub
                ? await hub.OpenOllamaAsync(opt.OllamaModel!, opt.OllamaTag!, "model", cancellationToken: ct)
                    .ConfigureAwait(false)
                : throw new NotSupportedException(
                    $"'{opt.Name}' comes from the ollama registry, which this model source does not serve " +
                    $"({_source.GetType().Name}). Use HubModelSource, or configure the model by its " +
                    "HuggingFace repo/file coordinates instead.")
            : await _source.OpenAsync(opt.Repo, opt.File, ct).ConfigureAwait(false);
        try
        {
            stream.Seek(0, SeekOrigin.Begin);
            var gguf = await GGUFParser.ParseHeaderAsync(stream, ct).ConfigureAwait(false);
            var tok = SentencePieceTokenizer.FromGGUF(gguf)
                ?? throw new InvalidOperationException($"'{opt.Name}' has no SentencePiece tokenizer metadata.");
            stream.Seek(0, SeekOrigin.Begin);
            var session = await InferenceSession.CreateFromGGUFStreamAsync(accelerator, stream,
                OnLoadProgress, ct).ConfigureAwait(false);
            int ctxCap = gguf.ContextLength > 0 ? Math.Min((int)gguf.ContextLength, maxSeqLen) : maxSeqLen;
            var gen = new GgufGenerator(session, accelerator, gguf, maxSeqLen: ctxCap)
            {
                EnableWebGPUDecodeCapture = enableWebGPUDecodeCapture,
            };
            return new LoadedModel
            {
                Info = new AiModelInfo(opt.Name, opt.ApproxSizeBytes > 0 ? opt.ApproxSizeBytes : stream.Length,
                    string.IsNullOrEmpty(gguf.Architecture) ? "gguf" : gguf.Architecture,
                    OllamaCacheModelProvider.QuantOf(opt.File), gguf.ContextLength,
                    new[] { "completion", "tools" }),
                Gguf = gguf,
                Session = session,
                Generator = gen,
                Tokenizer = tok,
                Format = ChatTemplates.DetectChatFormat(gguf),
                OwnedStream = stream,   // delivery stream lives (and dies) with the loaded model
            };
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    private HubModelOption? Find(string name)
        => _models.FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));
}
