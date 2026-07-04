namespace SpawnDev.AI;

/// <summary>One chat turn. <see cref="Role"/> is "system" / "user" / "assistant" (tool round-trips are
/// rendered INTO content by the serving layer - &lt;tool_call&gt;/&lt;tool_response&gt; blocks - so the
/// message list stays protocol-neutral).</summary>
public sealed record AiChatMessage(string Role, string Content);

/// <summary>Why a generation ended.</summary>
public enum AiStopKind
{
    /// <summary>Natural stop: EOS, a stop token, or a stop string.</summary>
    Stop,
    /// <summary>Truncated at the output-token cap (small models often never emit EOS - a UI should mark this).</summary>
    Length,
    /// <summary>Cancelled by the caller / client disconnect.</summary>
    Cancelled,
}

/// <summary>Protocol-neutral sampling + budget options (each protocol adapter maps its own fields here).</summary>
public sealed class AiGenerationOptions
{
    /// <summary>Output-token cap. Servers additionally clamp to their configured maximum.</summary>
    public int MaxOutputTokens { get; set; } = 512;
    /// <summary>"greedy" | "top_p" | "top_k". Temperature &lt;= 0 means greedy regardless.</summary>
    public string Strategy { get; set; } = "greedy";
    public float Temperature { get; set; } = 1.0f;
    public float TopP { get; set; } = 1.0f;
    public int TopK { get; set; } = 40;
    /// <summary>Repetition penalty (multiplicative; 1.0 = off). Small models want ~1.1-1.15: below
    /// that they loop verbatim; far above it, common grammatical tokens get punished into broken
    /// sentences (both measured on qwen2.5-0.5b).</summary>
    public float RepetitionPenalty { get; set; } = 1.0f;
    /// <summary>Seed for deterministic sampling, or null.</summary>
    public int? Seed { get; set; }
    /// <summary>Extra stop strings (protocol "stop" / "stop_sequences").</summary>
    public string[]? Stops { get; set; }
}

/// <summary>A parsed tool call the model emitted. <see cref="ArgumentsJson"/> is the raw JSON argument
/// object text (protocol adapters decide string-vs-object encoding).</summary>
public sealed record AiToolCall(string Name, string ArgumentsJson);

/// <summary>A chat request against a served model.</summary>
public sealed class AiChatRequest
{
    public required string Model { get; init; }
    public required IReadOnlyList<AiChatMessage> Messages { get; init; }
    public AiGenerationOptions Options { get; init; } = new();
    /// <summary>Tool definitions as raw JSON strings (forwarded verbatim into the chat template's tool
    /// block; the model answers with &lt;tool_call&gt; markup the server parses back out).</summary>
    public IReadOnlyList<string>? ToolsJson { get; init; }
}

/// <summary>A completed (non-streamed or fully-buffered) chat result.</summary>
public sealed record AiChatResult(
    string Text,
    int PromptTokens,
    int GeneratedTokens,
    AiStopKind Stop,
    IReadOnlyList<AiToolCall> ToolCalls)
{
    /// <summary><see cref="Text"/> with the tool-call markup removed (the natural-language preamble).</summary>
    public string TextWithoutToolCalls { get; init; } = Text;

    /// <summary>Binary artifacts produced by SERVER-side tool executions during this generation
    /// (generated images etc.). UIs render these inline; the text references them as
    /// "ai-artifact://{id}". Null when no server tool ran.</summary>
    public IReadOnlyList<AiToolArtifact>? Artifacts { get; init; }
}
