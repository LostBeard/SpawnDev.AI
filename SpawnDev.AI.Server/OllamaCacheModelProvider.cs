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

    /// <summary>
    /// Which decoder a loaded model uses. <see cref="GgufEngine.Auto"/> (default): the batch decoder for qwen2 / qwen3 / llama (without rope_freqs)
    /// on CUDA, OpenCL and WebGPU (MEASURED by SpawnDev.ILGPU.ML 5.8.1-local.4, Qwen2.5-0.5B: WebGPU 64.0 -> 254.6 tok/s, CUDA 97.7 -> 166.7, OpenCL
    /// 48.5 -> 194.3), the session engine otherwise. Same generation either way; set Generator to compare.
    /// </summary>
    public GgufEngine Engine { get; set; } = GgufEngine.Auto;

    /// <summary>
    /// OPT-IN faster decode at a small accuracy cost (off by default): the one-row Q4_K / Q6_K decode GEMVs read int8 activations
    /// (llama.cpp / Ollama's MMVQ scheme: each 32-value block of the activations quantized to int8, dotted with dp4a) instead of
    /// fp32. MEASURED by SpawnDev.ILGPU.ML 5.8.12 (RTX 4070, qwen3:1.7b Q4_K_M): decode 237 / 222 -> 261 / 242 tok/s (short /
    /// long prompt), at KL 5.5e-3 against fp32, top-1 agreement 96.4%, perplexity +1.1%. Greedy text follows fp32 for a while
    /// and then continues as a coherent alternative (ServerHost chat-bench, same model, 26-token prompt: identical for the first
    /// 134 characters; batch decoder 233.7 / 234.7 -> 259.2 / 259.8 tok/s, interleaved, 2026-11-10). CUDA only (dp4a + 32-lane shuffles); no effect on WebGPU / OpenCL /
    /// CPU. Covers the Q4_K GEMVs and the Q6_K ones of the split route (ffn_down, v); NOT the Q6_K LM head (kept on fp32
    /// activations on purpose), Q8_0, F16 or MXFP4 weights; and only single-sequence decode steps (a batched step of 2+
    /// sequences keeps fp32 activations, so the gain shrinks under concurrent load). SpawnDev.ILGPU.ML plans an int8 prefill
    /// GEMM under the same switch, which will then change prefill numerics too. null (the default) leaves SpawnDev.ILGPU.ML's
    /// own setting alone (off, or env GGUF_INT8_ACT=1); true / false set it. The switch is process-wide in SpawnDev.ILGPU.ML
    /// and is applied when a model loads, so set it before loading: a model already resident keeps the path its captured
    /// decode steps were recorded with until it is reloaded.
    /// </summary>
    public bool? FastInt8Decode { get; set; }

    public Task<LoadedModel> LoadAsync(string name, Accelerator accelerator, int maxSeqLen,
        bool enableWebGPUDecodeCapture, CancellationToken ct = default)
        => LoadAsync(name, accelerator, maxSeqLen, enableWebGPUDecodeCapture, null, ct);

    /// <inheritdoc />
    public async Task<LoadedModel> LoadAsync(string name, Accelerator accelerator, int maxSeqLen,
        bool enableWebGPUDecodeCapture, SpawnDev.ILGPU.ML.Training.LoraAdapterSet? lora, CancellationToken ct = default)
    {
        var meta = _store.Resolve(name)
            ?? throw new FileNotFoundException($"Model '{name}' is not in the Ollama cache.");
        // The generator owns this stream on either engine (ownsStream): the session engine reads weights from it on demand.
        var fs = new FileStream(meta.GgufPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, useAsync: true);
        GgufGenerator gen; GGUFModel gguf; SentencePieceTokenizer tok;
        try
        {
            gguf = await GGUFParser.ParseHeaderAsync(fs, ct).ConfigureAwait(false);
            tok = SentencePieceTokenizer.FromGGUF(gguf)
                ?? throw new InvalidOperationException($"'{meta.Name}' has no SentencePiece tokenizer metadata.");
            int ctxCap = gguf.ContextLength > 0 ? Math.Min((int)gguf.ContextLength, maxSeqLen) : maxSeqLen;
            if (FastInt8Decode is bool int8)
                SpawnDev.ILGPU.ML.Kernels.FusedDequantMatMul.Int8ActGemv = int8;
            gen = await GgufGenerator.CreateAsync(accelerator, fs, ctxCap, model: gguf, engine: Engine, ownsStream: true, ct: ct, lora: lora).ConfigureAwait(false);
        }
        catch { fs.Dispose(); throw; }
        gen.EnableWebGPUDecodeCapture = enableWebGPUDecodeCapture;
        // SpawnDev.ILGPU.ML 5.8.11 / 5.8.12 BUG (Tuvok, 2026-11-10; fixed in ML 66de8873, release 5.8.13): on CUDA the overlapped
        // v projection (GgufBatchDecoder.OverlapV, a second stream) shared q|k's FusedDequantMatMul, whose prefill of 512+ rows
        // stages dequantized weights in scratch - v's weights could overwrite q|k's mid-GEMM. Prompts over 512 tokens on models
        // whose v block type differs from q|k (common in Q4_K_M: llama3, qwen2.5) got logits off by up to 27. Off until the ML
        // version with the fix: costs the overlap's ~4% decode. REMOVE this when moving to ML 5.8.13+.
        if (gen.BatchDecoder is { } batchDecoder)
            batchDecoder.OverlapV = false;
        Console.WriteLine($"[SpawnDev.AI] {meta.Name}: decoding with {gen.Engine}{(gen.EngineReason.Length > 0 ? " (" + gen.EngineReason + ")" : "")}");
        return new LoadedModel
        {
            Info = new AiModelInfo(meta.Name, meta.GgufSize,
                string.IsNullOrEmpty(gguf.Architecture) ? "gguf" : gguf.Architecture,
                QuantOf(meta.Name), gguf.ContextLength, Capabilities(meta)),
            Gguf = gguf,
            Session = gen.Engine == GgufEngine.Generator ? gen.Session : null,
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
