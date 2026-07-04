using ILGPU.Runtime;
using SpawnDev.ILGPU.ML;
using SpawnDev.ILGPU.ML.GGUF;
using SpawnDev.ILGPU.ML.Hub;
using SpawnDev.ILGPU.ML.Pipelines;
using SpawnDev.ILGPU.ML.Preprocessing;
using SpawnDev.WebTorrent;

namespace SpawnDev.AI.Server;

/// <summary>A model the hub provider can serve: the name clients address it by + its Hugging Face
/// coordinates (streamed via the SpawnDev hub - WebTorrent-seeded, HF CDN fallback, browser-cached).</summary>
public sealed record HubModelOption(string Name, string Repo, string File, long ApproxSizeBytes = 0);

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

    public async Task<LoadedModel> LoadAsync(string name, Accelerator accelerator, int maxSeqLen,
        bool enableWebGPUDecodeCapture, CancellationToken ct = default)
    {
        var opt = Find(name)
            ?? throw new FileNotFoundException($"Model '{name}' is not in the hub model list.");
        var hub = new HubModelStream(_webTorrent, _http) { PrepareTimeout = PrepareTimeout };
        // deselect:true - fetch ONLY the pieces the weight-stream reads. deselect:false let the torrent
        // background-download EVERY file in the repo (all quants, 10-15GB - Captain caught it live
        // 2026-07-04) while the stream read its one file with priority.
        var model = await hub.OpenAsync(opt.Repo, opt.File, deselect: true, ct).ConfigureAwait(false);
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
