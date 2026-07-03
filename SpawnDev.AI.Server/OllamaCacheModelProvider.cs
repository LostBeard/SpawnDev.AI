using ILGPU.Runtime;
using SpawnDev.ILGPU.ML;
using SpawnDev.ILGPU.ML.GGUF;
using SpawnDev.ILGPU.ML.Pipelines;
using SpawnDev.ILGPU.ML.Preprocessing;

namespace SpawnDev.AI.Server;

/// <summary>
/// The desktop model provider: serves models straight from Ollama's on-disk cache via
/// <see cref="OllamaModelStore"/> (zero-copy - sessions load the content-addressed blobs directly).
/// </summary>
public sealed class OllamaCacheModelProvider : IAiModelProvider
{
    private readonly OllamaModelStore _store;

    /// <summary>Create over an Ollama cache (default: ~/.ollama/models or $OLLAMA_MODELS).</summary>
    public OllamaCacheModelProvider(OllamaModelStore? store = null) => _store = store ?? new OllamaModelStore();

    /// <summary>The underlying cache reader.</summary>
    public OllamaModelStore Store => _store;

    public Task<IReadOnlyList<AiModelInfo>> ListAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<AiModelInfo>>(
            _store.List().Select(m => new AiModelInfo(m.Name, m.GgufSize, "gguf", QuantOf(m.Name), 0,
                Capabilities(m))).ToList());

    public async Task<AiModelInfo?> ShowAsync(string name, CancellationToken ct = default)
    {
        var m = _store.Resolve(name);
        if (m == null) return null;
        string arch = "gguf"; long ctxLen = 0;
        try
        {
            await using var hs = File.OpenRead(m.GgufPath);
            var gm = await GGUFParser.ParseHeaderAsync(hs, ct).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(gm.Architecture)) arch = gm.Architecture;
            ctxLen = gm.ContextLength;
        }
        catch { /* header unreadable - fall back to shallow metadata */ }
        return new AiModelInfo(m.Name, m.GgufSize, arch, QuantOf(m.Name), ctxLen, Capabilities(m));
    }

    public Task<string?> ResolveAsync(string name, CancellationToken ct = default)
        => Task.FromResult(_store.Resolve(name)?.Name);

    public async Task<LoadedModel> LoadAsync(string name, Accelerator accelerator, int maxSeqLen,
        bool enableWebGPUDecodeCapture, CancellationToken ct = default)
    {
        var meta = _store.Resolve(name)
            ?? throw new FileNotFoundException($"Model '{name}' is not in the Ollama cache.");
        await using var hs = File.OpenRead(meta.GgufPath);
        var gguf = await GGUFParser.ParseHeaderAsync(hs, ct).ConfigureAwait(false);
        var tok = SentencePieceTokenizer.FromGGUF(gguf)
            ?? throw new InvalidOperationException($"'{meta.Name}' has no SentencePiece tokenizer metadata.");
        var session = await InferenceSession.CreateFromGGUFFileAsync(accelerator, meta.GgufPath, ct: ct)
            .ConfigureAwait(false);
        int ctxCap = gguf.ContextLength > 0 ? Math.Min((int)gguf.ContextLength, maxSeqLen) : maxSeqLen;
        var gen = new GgufGenerator(session, accelerator, gguf, maxSeqLen: ctxCap)
        {
            EnableWebGPUDecodeCapture = enableWebGPUDecodeCapture,
        };
        return new LoadedModel
        {
            Info = new AiModelInfo(meta.Name, meta.GgufSize,
                string.IsNullOrEmpty(gguf.Architecture) ? "gguf" : gguf.Architecture,
                QuantOf(meta.Name), gguf.ContextLength, Capabilities(meta)),
            Gguf = gguf,
            Session = session,
            Generator = gen,
            Tokenizer = tok,
            Format = ChatTemplates.DetectChatFormat(gguf),
        };
    }

    private static IReadOnlyList<string> Capabilities(OllamaModel m)
    {
        var caps = new List<string> { "completion", "tools" };
        if (m.MmprojPath != null) caps.Add("vision");
        return caps;
    }

    internal static string QuantOf(string name)
    {
        var qm = System.Text.RegularExpressions.Regex.Match(name, @"[Qq]\d(?:_[A-Za-z0-9]+)*");
        return qm.Success ? qm.Value.ToUpperInvariant() : "";
    }
}
