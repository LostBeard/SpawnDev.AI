using System.Text;
using SpawnDev.ILGPU.ML.GGUF;
using SpawnDev.ILGPU.ML.Pipelines;
using SpawnDev.ILGPU.ML.Preprocessing;

namespace SpawnDev.AI.Server;

/// <summary>
/// The transport-neutral chat service over SpawnDev.ILGPU.ML: chat templating (ChatML / Llama3 /
/// gemma detected from the GGUF), the serialized-generation registry, tool-call parsing, and the
/// streaming tool-markup holdback. Protocol adapters (Ollama / OpenAI / Anthropic, over HTTP or a
/// browser-worker MessagePort) all drive this one class - extracted from the proven
/// Examples/06.OllamaServer.Console generation bridge.
/// </summary>
public sealed class AiChatEngine : IAiChatService
{
    private readonly ModelRegistry _registry;

    /// <summary>Server-wide cap on requested output tokens - agentic clients ask for huge values
    /// (Claude CLI: 32000) that a small local model would ramble into. Requests are clamped to this.</summary>
    public int MaxOutputTokens { get; set; } = 1024;

    /// <summary>Optional perf-log sink: one line per generation (prompt/reused/prefill/decode split).</summary>
    public Action<string>? PerfLog { get; set; }

    public AiChatEngine(ModelRegistry registry) => _registry = registry;

    /// <summary>The registry (model listing for protocol adapters).</summary>
    public ModelRegistry Registry => _registry;

    public Task<IReadOnlyList<AiModelInfo>> ListModelsAsync(CancellationToken ct = default)
        => _registry.Provider.ListAsync(ct);

    public Task<AiModelInfo?> ShowModelAsync(string name, CancellationToken ct = default)
        => _registry.Provider.ShowAsync(name, ct);

    public async Task<int> CountTokensAsync(string model, IReadOnlyList<AiChatMessage> messages, CancellationToken ct = default)
    {
        using var lease = await _registry.AcquireAsync(FirstOrDefaultModel(model), ct).ConfigureAwait(false);
        var lm = lease.Model;
        var (promptIds, _) = ChatTemplates.BuildChatPrompt(lm.Gguf, lm.Tokenizer, ToTuples(messages));
        return promptIds.Length;
    }

    public Task<AiChatResult> ChatAsync(AiChatRequest request, CancellationToken ct = default)
        => GenerateAsync(request, onDelta: null, ct);

    public Task<AiChatResult> ChatStreamAsync(AiChatRequest request, Func<string, Task> onDelta, CancellationToken ct = default)
        => GenerateAsync(request, onDelta, ct);

    /// <summary>Server-side tools (generate_image etc.). When set AND the client sent no tools of
    /// its own, tool definitions are injected and the model's calls are EXECUTED here - the agentic
    /// loop: call → execute → tool_response → continue (bounded rounds). Client tools always win.</summary>
    public AiToolRegistry? Tools { get; set; }

    /// <summary>Max server-tool execution rounds per generation (loop guard).</summary>
    public int MaxToolRounds { get; set; } = 3;

    private async Task<AiChatResult> GenerateAsync(AiChatRequest request, Func<string, Task>? onDelta, CancellationToken ct)
    {
        // Server-tool injection (client tools take precedence).
        var serverTools = request.ToolsJson == null ? Tools?.List() : null;
        IReadOnlyList<string>? toolsJson = request.ToolsJson;
        if (serverTools is { Count: > 0 })
            toolsJson = serverTools.Select(t =>
            {
                using var schema = System.Text.Json.JsonDocument.Parse(t.ParametersJsonSchema);
                return System.Text.Json.JsonSerializer.Serialize(new
                {
                    type = "function",
                    function = new { name = t.Name, description = t.Description, parameters = schema.RootElement.Clone() },
                });
            }).ToList();

        var messages = request.Messages.ToList();
        List<AiToolArtifact>? artifacts = null;
        AiChatResult result;
        int round = 0;
        while (true)
        {
            result = await GenerateOnePassAsync(request, messages, toolsJson, onDelta, ct).ConfigureAwait(false);
            if (serverTools is not { Count: > 0 } || result.ToolCalls.Count == 0 || ++round > MaxToolRounds)
                break;
            // Execute the model's calls server-side and continue the conversation with the results.
            messages.Add(new AiChatMessage("assistant", result.Text));
            foreach (var call in result.ToolCalls)
            {
                var tool = Tools!.Get(call.Name);
                var exec = tool != null
                    ? await tool.ExecuteAsync(call.ArgumentsJson, ct).ConfigureAwait(false)
                    : new AiToolExecutionResult($"Unknown tool '{call.Name}'.") { IsError = true };
                if (exec.Artifacts is { Count: > 0 })
                    (artifacts ??= new()).AddRange(exec.Artifacts);
                // qwen/ChatML convention (same as the protocol routers): tool results re-enter as a
                // user turn wrapped in <tool_response>.
                messages.Add(new AiChatMessage("user", $"<tool_response>\n{exec.TextForModel}\n</tool_response>"));
            }
        }
        if (artifacts == null) return result;
        // Deterministic artifact references: append markdown refs the ENGINE controls (models told
        // "don't repeat the id" won't reliably echo it). Every surface - typed clients, worker, and
        // plain HTTP protocol clients - can resolve ai-artifact://{id} via /ai/artifacts/{id}.
        var refs = string.Join("\n", artifacts.Select(a => $"![{a.Label ?? "generated image"}](ai-artifact://{a.Id})"));
        return result with
        {
            Text = result.Text.TrimEnd() + "\n\n" + refs,
            TextWithoutToolCalls = result.TextWithoutToolCalls.TrimEnd() + "\n\n" + refs,
            Artifacts = artifacts,
        };
    }

    private async Task<AiChatResult> GenerateOnePassAsync(AiChatRequest request, IReadOnlyList<AiChatMessage> messages,
        IReadOnlyList<string>? toolsJson, Func<string, Task>? onDelta, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        using var lease = await _registry.AcquireAsync(request.Model, ct).ConfigureAwait(false);
        var lm = lease.Model;
        var (promptIds, stopIds) = ChatTemplates.BuildChatPrompt(lm.Gguf, lm.Tokenizer, ToTuples(messages),
            toolsJson: toolsJson);
        long buildMs = sw.ElapsedMilliseconds;

        var cfg = ToConfig(request.Options);
        long firstMs = -1;

        Func<string, Task>? wrapped = null;
        ToolAwareStreamer? streamer = null;
        if (onDelta != null)
        {
            // Tool requests stream text but must never leak tool-call markup as visible text; the
            // holdback logic (extracted from the proven Anthropic streaming path) buffers the longest
            // possible partial "<tool_call>" suffix and stops text at the first full tag.
            streamer = toolsJson != null ? new ToolAwareStreamer(onDelta) : null;
            wrapped = async d =>
            {
                if (firstMs < 0) firstMs = sw.ElapsedMilliseconds;
                if (streamer != null) await streamer.PushAsync(d).ConfigureAwait(false);
                else await onDelta(d).ConfigureAwait(false);
            };
        }

        var res = await lm.Generator.GenerateAsync(promptIds, cfg, request.Options.Stops, stopIds, wrapped, ct)
            .ConfigureAwait(false);

        var calls = toolsJson != null
            ? ChatTemplates.ParseToolCalls(res.Text).Select(tc => new AiToolCall(tc.Name, tc.ArgumentsJson)).ToList()
            : new List<AiToolCall>();
        if (streamer != null && calls.Count == 0)
            await streamer.FlushTailAsync().ConfigureAwait(false);   // no tool call - release the held-back tail

        PerfLog?.Invoke(
            $"{(onDelta != null ? "stream" : "once"),-6} prompt={promptIds.Length,6}tok reused={lm.Generator.LastReusedPrefix,6}tok " +
            $"TTFT={(firstMs >= 0 ? firstMs : sw.ElapsedMilliseconds),7}ms total={sw.ElapsedMilliseconds,7}ms " +
            $"gen={res.GeneratedTokens,5}tok stop={res.Stop}");

        return new AiChatResult(res.Text, res.PromptTokens, res.GeneratedTokens, ToStopKind(res.Stop), calls)
        {
            TextWithoutToolCalls = calls.Count > 0 ? StripToolCalls(res.Text).Trim() : res.Text,
        };
    }

    // ── Streaming tool-markup holdback (verbatim logic from the proven /v1/messages SSE path) ──
    private sealed class ToolAwareStreamer
    {
        private const string TC = "<tool_call>";
        private readonly Func<string, Task> _onDelta;
        private readonly StringBuilder _sb = new();
        private int _emitted;
        private bool _stopText;
        public ToolAwareStreamer(Func<string, Task> onDelta) => _onDelta = onDelta;

        public async Task PushAsync(string delta)
        {
            _sb.Append(delta);
            if (_stopText) return;
            var s = _sb.ToString();
            int tc = s.IndexOf(TC, Math.Max(0, _emitted - TC.Length), StringComparison.Ordinal);
            if (tc >= 0) { await FlushAsync(tc).ConfigureAwait(false); _stopText = true; return; }
            int hold = 0, maxH = Math.Min(TC.Length - 1, s.Length - _emitted);
            for (int h = maxH; h > 0; h--)
                if (s.AsSpan(s.Length - h).SequenceEqual(TC.AsSpan(0, h))) { hold = h; break; }
            await FlushAsync(s.Length - hold).ConfigureAwait(false);
        }

        public Task FlushTailAsync() => _stopText ? Task.CompletedTask : FlushAsync(_sb.Length);

        private async Task FlushAsync(int upTo)
        {
            if (upTo > _emitted)
            {
                await _onDelta(_sb.ToString(_emitted, upTo - _emitted)).ConfigureAwait(false);
                _emitted = upTo;
            }
        }
    }

    // ── Mapping helpers ──
    private GenerationConfig ToConfig(AiGenerationOptions o) => new()
    {
        MaxNewTokens = Math.Min(Math.Max(1, o.MaxOutputTokens), MaxOutputTokens),
        Strategy = o.Temperature > 0 ? o.Strategy : "greedy",
        Temperature = o.Temperature,
        TopP = o.TopP,
        TopK = o.TopK,
        Seed = o.Seed,
        RepetitionPenalty = o.RepetitionPenalty,
    };

    private static AiStopKind ToStopKind(StopReason r) => r switch
    {
        StopReason.Length => AiStopKind.Length,
        StopReason.Cancelled => AiStopKind.Cancelled,
        _ => AiStopKind.Stop,
    };

    private static List<(string, string)> ToTuples(IReadOnlyList<AiChatMessage> messages)
        => messages.Select(m => (m.Role, m.Content)).ToList();

    private string FirstOrDefaultModel(string model) => model; // resolution happens in the provider

    /// <summary>Remove &lt;tool_call&gt;…&lt;/tool_call&gt; blocks, leaving the natural-language preamble.</summary>
    public static string StripToolCalls(string text)
    {
        const string open = "<tool_call>", close = "</tool_call>";
        var sb = new StringBuilder();
        int i = 0;
        while (true)
        {
            int s = text.IndexOf(open, i, StringComparison.Ordinal);
            if (s < 0) { sb.Append(text, i, text.Length - i); break; }
            sb.Append(text, i, s - i);
            int e = text.IndexOf(close, s, StringComparison.Ordinal);
            if (e < 0) break;
            i = e + close.Length;
        }
        return sb.ToString();
    }
}
