using ILGPU.Runtime;
using SpawnDev.ILGPU.ML;
using SpawnDev.ILGPU.ML.GGUF;
using SpawnDev.ILGPU.ML.Hub;
using SpawnDev.ILGPU.ML.Pipelines;
using SpawnDev.ILGPU.ML.Preprocessing;
using SpawnDev.WebTorrent;

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
    private readonly WebTorrentClient _webTorrent;
    private readonly HttpClient _http;
    private readonly List<HubModelOption> _models;

    /// <summary>Progress callback while weights stream ((stage, percent) per hub events).</summary>
    public Action<string, int>? OnLoadProgress { get; set; }

    /// <summary>Hub preparation timeout (cold hub cache can take minutes for multi-GB models).</summary>
    public TimeSpan PrepareTimeout { get; set; } = TimeSpan.FromMinutes(8);

    public HubModelProvider(WebTorrentClient webTorrent, HttpClient http, IEnumerable<HubModelOption> models)
    {
        _webTorrent = webTorrent;
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
    /// Is this model's file already complete on this device?
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 ASKS THE FILE, NOT THE TORRENT. Every model arrives through the WebTorrent client - that is what
    /// gives lazy-hash torrents for URL-backed files and random-access streams, and it is why a 7 GB model
    /// can load in a browser tab at all (the bytes stay JS-side over IJSReadStream and never touch the
    /// small WASM managed heap). But the loader opens with <c>deselect: true</c> so only the pieces the
    /// weight stream reads are fetched, which means <c>Torrent.Progress</c> NEVER reaches 1.0 for a
    /// multi-file repo, by design.
    /// </para>
    /// <para>
    /// ⚠️ An earlier version checked <c>Torrent.Progress >= 0.999</c> and therefore reported "not
    /// downloaded" for the model the demo had been running on all session - the browser gate caught it
    /// blocking its own default model. <c>TorrentFileInfo.Done</c> is the per-file answer and is the right
    /// question: the file this model needs, complete or not.
    /// </para>
    /// </remarks>
    /// <summary>
    /// How much of this model is cached on this device, 0..1 - or null when nothing has fetched it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ PROGRESS ONLY. This deliberately does NOT answer "is it fully downloaded", because for the
    /// lazy-hash torrents the hub hands out that question currently has no reliable answer. MEASURED, on a
    /// model that had just loaded and generated a reply:
    /// <c>torrent 'qwen2.5-0.5b-instruct-q8_0.gguf' progress=27.9% bitfield=162 pieces=162,
    /// file '' len=675710816 downloaded=188743680 done=False</c>.
    /// </para>
    /// <para>
    /// <c>TorrentFileInfo.Name</c> is EMPTY there - the name lives on the torrent for a single-file
    /// torrent, so matching on the file name never matches anything. Match on <c>Torrent.Name</c>.
    /// </para>
    /// <para>
    /// ⚠️ I PREVIOUSLY WROTE, IN THIS COMMENT, THAT THE REPORTED <c>Length</c> WAS PIECE-ALIGNED AND
    /// OVERSTATED THE FILE. That was wrong, and it is retracted. The hub's own cache holds
    /// <c>qwen2.5-0.5b-instruct-q8_0.gguf</c> at exactly 675,710,816 bytes - the same figure the torrent
    /// reports - so <c>Length</c> is the true size. What was actually wrong was the hand-entered
    /// <c>ApproxSizeBytes</c> in the demo (531,067,136), and the sizes there now come from the hub cache.
    /// </para>
    /// <para>
    /// 🔴 THE GUARD IS STILL BUILT ON CONSENT, NOT ON THIS - but for a narrower reason than I first gave.
    /// <c>Done</c> read false on a model that had loaded and answered, at 27.9%, and I do not yet know why
    /// (lazy-hash piece verification lagging behind cached data is the likeliest explanation, since a
    /// lazy-hash torrent computes its hashes as it goes). Until that is understood, whether the user
    /// AGREED is the fact the app can actually answer. This value is fine for showing progress.
    /// </para>
    /// </remarks>
    public double? CachedFraction(string name)
    {
        var hint = Find(name)?.CacheFileHint;
        if (string.IsNullOrEmpty(hint)) return null;

        foreach (var torrent in _webTorrent.Torrents)
        {
            if (torrent.Name?.Contains(hint, StringComparison.OrdinalIgnoreCase) != true) continue;
            return torrent.Progress;
        }
        return null;
    }

    /// <summary>Name, size, purpose and how much is cached - what a picker needs before asking.</summary>
    /// <remarks>
    /// The size is the CONFIGURED figure. It is hand-entered and therefore worth checking against the
    /// published file, but it is still the best number available: the torrent's own reported length is
    /// piece-aligned and overstated (see <see cref="CachedFraction"/>).
    /// </remarks>
    public IReadOnlyList<(string Name, long SizeBytes, string Description, double? CachedFraction)> Catalogue()
        => _models.Select(m => (m.Name, m.ApproxSizeBytes, m.Description, CachedFraction(m.Name))).ToList();

    public async Task<LoadedModel> LoadAsync(string name, Accelerator accelerator, int maxSeqLen,
        bool enableWebGPUDecodeCapture, CancellationToken ct = default)
    {
        var opt = Find(name)
            ?? throw new FileNotFoundException($"Model '{name}' is not in the hub model list.");
        var hub = new HubModelStream(_webTorrent, _http) { PrepareTimeout = PrepareTimeout };
        // deselect:true - fetch ONLY the pieces the weight-stream reads. deselect:false let the torrent
        // background-download EVERY file in the repo (all quants, 10-15GB - Captain caught it live
        // 2026-07-04) while the stream read its one file with priority.
        var model = opt.IsOllama
            ? await hub.OpenOllamaAsync(opt.OllamaModel!, opt.OllamaTag!, "model", deselect: true, ct)
                .ConfigureAwait(false)
            : await hub.OpenAsync(opt.Repo, opt.File, deselect: true, ct).ConfigureAwait(false);
        try
        {
            var stream = model.Stream;
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
                OwnedStream = model.Stream,   // hub stream lives (and dies) with the loaded model
            };
        }
        catch
        {
            model.Stream.Dispose();
            throw;
        }
    }

    private HubModelOption? Find(string name)
        => _models.FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));
}
