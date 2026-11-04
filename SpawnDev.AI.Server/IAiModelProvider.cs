using ILGPU.Runtime;

namespace SpawnDev.AI.Server;

/// <summary>
/// Where models come from. Desktop: <see cref="OllamaCacheModelProvider"/> reads Ollama's on-disk
/// content-addressed cache (zero-copy blob paths). Browser: <see cref="HubModelProvider"/> streams
/// GGUF weights from the SpawnDev hub (HTTP / HuggingFace) straight to the GPU. The
/// <see cref="ModelRegistry"/>'s gate/resident-swap logic is provider-independent.
/// </summary>
public interface IAiModelProvider
{
    /// <summary>Models this provider can serve (shallow metadata - no weights touched).</summary>
    Task<IReadOnlyList<AiModelInfo>> ListAsync(CancellationToken ct = default);

    /// <summary>Detailed metadata for one model (may read the GGUF header), or null if unknown.</summary>
    Task<AiModelInfo?> ShowAsync(string name, CancellationToken ct = default);

    /// <summary>Resolve a requested name to its canonical form ("qwen2.5" → "qwen2.5:latest"), or null
    /// if the model isn't servable. Cheap - called per request; must not touch weights.</summary>
    Task<string?> ResolveAsync(string name, CancellationToken ct = default);

    /// <summary>Load a model onto the accelerator (session + generator + tokenizer + chat format).</summary>
    Task<LoadedModel> LoadAsync(string name, Accelerator accelerator, int maxSeqLen,
        bool enableWebGPUDecodeCapture, CancellationToken ct = default);

    /// <summary>
    /// Load a model with LoRA adapters applied (SpawnDev.ILGPU.ML <c>GgufGenerator.CreateAsync(..., lora:)</c>, either engine), or the base
    /// model when <paramref name="lora"/> is null. The default implementation serves only the base model; the built-in providers implement it.
    /// </summary>
    Task<LoadedModel> LoadAsync(string name, Accelerator accelerator, int maxSeqLen, bool enableWebGPUDecodeCapture,
        SpawnDev.ILGPU.ML.Training.LoraAdapterSet? lora, CancellationToken ct = default)
        => lora == null
            ? LoadAsync(name, accelerator, maxSeqLen, enableWebGPUDecodeCapture, ct)
            : throw new NotSupportedException($"{GetType().Name} does not load LoRA adapters.");
}
