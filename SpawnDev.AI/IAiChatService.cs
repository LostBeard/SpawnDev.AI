namespace SpawnDev.AI;

/// <summary>A model available to the service (name as clients address it + display metadata).</summary>
public sealed record AiModelInfo(
    string Name,
    long SizeBytes,
    string Family,
    string QuantizationLevel,
    long ContextLength,
    IReadOnlyList<string> Capabilities);

/// <summary>
/// The transport-neutral chat service surface. <see cref="SpawnDev.AI"/>'s server implements it over
/// the SpawnDev.ILGPU.ML inference engine; protocol adapters (Ollama / OpenAI / Anthropic shapes over
/// HTTP or a browser-worker MessagePort) all drive this one interface.
/// </summary>
public interface IAiChatService
{
    /// <summary>Models this service can serve.</summary>
    Task<IReadOnlyList<AiModelInfo>> ListModelsAsync(CancellationToken ct = default);

    /// <summary>Detailed metadata for one model, or null if unknown.</summary>
    Task<AiModelInfo?> ShowModelAsync(string name, CancellationToken ct = default);

    /// <summary>Prompt token count for a message list against a model's tokenizer + chat template.</summary>
    Task<int> CountTokensAsync(string model, IReadOnlyList<AiChatMessage> messages, CancellationToken ct = default);

    /// <summary>Generate a complete response (tool calls parsed when the request carries tools).</summary>
    Task<AiChatResult> ChatAsync(AiChatRequest request, CancellationToken ct = default);

    /// <summary>Generate with streaming text deltas. <paramref name="onDelta"/> receives incremental text
    /// (UTF-8 safe; when the request carries tools, tool-call markup is HELD BACK and never streamed as
    /// visible text). Returns the complete result including parsed tool calls.</summary>
    Task<AiChatResult> ChatStreamAsync(AiChatRequest request, Func<string, Task> onDelta, CancellationToken ct = default);
}
