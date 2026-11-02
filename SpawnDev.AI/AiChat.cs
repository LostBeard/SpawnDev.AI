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

    /// <summary>Where the time of the (last) generation pass went, measured by the engine. Null when the engine did not time it.</summary>
    public AiChatTimings? Timings { get; init; }
}

/// <summary>
/// Where one generation pass spent its time, in milliseconds, measured by the engine around the decoder (the same split Ollama reports as
/// load_duration / prompt_eval_duration / eval_duration).
/// </summary>
/// <param name="LoadMs">Acquiring the model: 0-ish when it was resident, the whole download + GPU upload when it was not.</param>
/// <param name="PromptEvalMs">From the start of the decoder call to its first text delta: prefill plus the first sample (one token later
/// when the decoder held the first token back as a partial UTF-8 character or stop string). Null when no text was produced.</param>
/// <param name="EvalMs">From the first generated token to the end: the decode of the remaining tokens (llama.cpp's convention, so
/// GeneratedTokens / EvalMs is the rate Ollama and llama.cpp print; (GeneratedTokens - 1) / EvalMs is the strict per-step rate). Null as above.</param>
/// <param name="TotalMs">The whole pass, load included.</param>
public sealed record AiChatTimings(double LoadMs, double? PromptEvalMs, double? EvalMs, double TotalMs);

/// <summary>
/// What a finished chat turn cost, as the server reported it in Ollama's final frame (prompt_eval_count, eval_count and the *_duration
/// fields). <see cref="Timings"/> is null when the server sent no durations (an older server, or real Ollama without them).
/// </summary>
public sealed record AiChatUsage(int PromptTokens, int GeneratedTokens, AiChatTimings? Timings)
{
    /// <summary>Decode rate over the steps after the first token, (GeneratedTokens - 1) / EvalMs, in tokens per second; null when unknown.</summary>
    public double? DecodeTokensPerSecond =>
        Timings?.EvalMs is { } e && e > 0 && GeneratedTokens > 1 ? (GeneratedTokens - 1) / (e / 1000.0) : null;
}

/// <summary>
/// A model a user can choose, with everything needed to decide BEFORE committing to the download.
/// </summary>
/// <param name="Name">What to pass as the model name.</param>
/// <param name="SizeBytes">Approximate download size. 0 when unknown.</param>
/// <param name="Description">What it is good for, and its trade-off.</param>
/// <param name="CachedFraction">
/// How much of this model is already on the device, 0..1, or null when nothing has fetched it.
/// </param>
/// <remarks>
/// ⚠️ <paramref name="CachedFraction"/> IS PROGRESS, NOT A VERDICT. It never reliably reaches 1.0 for the
/// lazy-hash torrents the hub serves, because their reported length is piece-aligned and overstates the
/// file. Show it; do not decide anything on it. Whether the user AGREED to a download is a separate fact,
/// and one a host can actually persist.
/// </remarks>
public sealed record AiModelChoice(string Name, long SizeBytes, string Description, double? CachedFraction);
