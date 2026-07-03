using System.Text;
using System.Text.Json;

namespace SpawnDev.AI.Server;

/// <summary>
/// The Ollama-compatible API surface, transport-free: OpenAI-compat (/v1/chat/completions SSE,
/// /v1/models), Ollama native (/api/chat NDJSON, /api/generate, /api/tags, /api/show, /api/version),
/// and Anthropic Messages (/v1/messages SSE + count_tokens - Claude CLI). Extracted from the proven
/// Examples/06.OllamaServer.Console endpoints; every route drives one <see cref="AiChatEngine"/> and
/// writes through <see cref="IAiServerTransport"/> - the desktop HTTP host and the browser worker
/// host run THIS same code.
/// </summary>
public sealed class AiApiRouter
{
    private static readonly JsonSerializerOptions J = new(JsonSerializerDefaults.Web);
    private readonly AiChatEngine _engine;

    public AiApiRouter(AiChatEngine engine) => _engine = engine;

    /// <summary>Server version string reported by /api/version.</summary>
    public string Version { get; set; } = "0.1.0-spawndev";

    /// <summary>
    /// Route one request. Returns false when the (method, path) is not part of this API (the host
    /// decides what a miss means - 404 on HTTP, an error frame on a worker port).
    /// </summary>
    public async Task<bool> TryHandleAsync(string method, string path, JsonElement? body, IAiServerTransport t)
    {
        method = method.ToUpperInvariant();
        path = path.TrimEnd('/');
        if (path.Length == 0) path = "/";

        switch (method, path)
        {
            case ("GET", "/"): await t.WriteTextAsync(200, "Ollama is running (SpawnDev.AI)"); return true;
            case ("HEAD", "/"): await t.WriteTextAsync(200, ""); return true;
            case ("GET", "/api/version"): await t.WriteJsonAsync(200, new { version = Version }); return true;
            case ("GET", "/api/tags"): await ApiTags(t); return true;
            case ("GET", "/v1/models"): await V1Models(t); return true;
            case ("POST", "/api/show"): await ApiShow(Body(body), t); return true;
            case ("POST", "/v1/chat/completions"): await V1ChatCompletions(Body(body), t); return true;
            case ("POST", "/api/chat"): await ApiChat(Body(body), t); return true;
            case ("POST", "/api/generate"): await ApiGenerate(Body(body), t); return true;
            case ("POST", "/v1/messages"): await V1Messages(Body(body), t); return true;
            case ("POST", "/v1/messages/count_tokens"): await V1CountTokens(Body(body), t); return true;
            default: return false;
        }
    }

    private static JsonElement Body(JsonElement? body)
    {
        if (body is { } b) return b;
        using var d = JsonDocument.Parse("{}");
        return d.RootElement.Clone();
    }

    // ── Protocol-shaped errors ──
    private Task OpenAiError(IAiServerTransport t, string msg, int code) =>
        t.WriteJsonAsync(code, new { error = new { message = msg, type = "invalid_request_error", code = "model_not_found" } });
    private Task OllamaError(IAiServerTransport t, string msg, int code) =>
        t.WriteJsonAsync(code, new { error = msg });
    private Task AnthropicError(IAiServerTransport t, string msg, int code) =>
        t.WriteJsonAsync(code, new { type = "error", error = new { type = "not_found_error", message = msg } });

    // ── Listing / metadata ──
    private async Task ApiTags(IAiServerTransport t)
    {
        var models = (await _engine.ListModelsAsync(t.Aborted)).Select(m => new
        {
            name = m.Name,
            model = m.Name,
            size = m.SizeBytes,
            details = new { family = m.Family, parameter_size = "", quantization_level = m.QuantizationLevel },
        });
        await t.WriteJsonAsync(200, new { models });
    }

    private async Task V1Models(IAiServerTransport t)
    {
        var data = (await _engine.ListModelsAsync(t.Aborted)).Select(m => new
        { id = m.Name, @object = "model", created = 0, owned_by = "spawndev-ai" });
        await t.WriteJsonAsync(200, new { @object = "list", data });
    }

    private async Task ApiShow(JsonElement req, IAiServerTransport t)
    {
        string name = GetString(req, "model") ?? GetString(req, "name") ?? "";
        var m = await _engine.ShowModelAsync(name, t.Aborted);
        if (m == null) { await OllamaError(t, $"model '{name}' not found", 404); return; }

        var modelInfo = new Dictionary<string, object> { ["general.architecture"] = m.Family };
        if (m.ContextLength > 0) modelInfo[$"{m.Family}.context_length"] = m.ContextLength;

        await t.WriteJsonAsync(200, new
        {
            license = "",
            modelfile = $"# Modelfile for {m.Name}",
            parameters = "",
            template = "",
            details = new
            {
                parent_model = "",
                format = "gguf",
                family = m.Family,
                families = new[] { m.Family },
                parameter_size = "",
                quantization_level = m.QuantizationLevel,
            },
            model_info = modelInfo,
            capabilities = m.Capabilities,
        });
    }

    // ── OpenAI: /v1/chat/completions ──
    private async Task V1ChatCompletions(JsonElement req, IAiServerTransport t)
    {
        string model = GetString(req, "model") ?? "";
        if (await _engine.ShowModelAsync(model, t.Aborted) == null)
        { await OpenAiError(t, $"model '{model}' not found", 404); return; }
        var messages = ParseMessages(req, "messages");
        bool stream = GetBool(req, "stream") ?? false;
        var options = ReadOpenAiOptions(req);
        options.Stops = GetStringArray(req, "stop");
        var tools = GetTools(req);
        string id = "chatcmpl-" + Guid.NewGuid().ToString("N")[..24];
        long created = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var request = new AiChatRequest { Model = model, Messages = messages, Options = options, ToolsJson = tools };

        // Tools present → non-streaming (the tool_call only resolves at the end).
        if (tools != null || !stream)
        {
            var res = await _engine.ChatAsync(request, t.Aborted);
            object message = res.ToolCalls.Count > 0
                ? new
                {
                    role = "assistant",
                    content = (string?)null,
                    tool_calls = res.ToolCalls.Select((tc, ix) => new
                    { id = $"call_{ix}_{Guid.NewGuid().ToString("N")[..8]}", type = "function", function = new { name = tc.Name, arguments = tc.ArgumentsJson } }).ToArray(),
                }
                : new { role = "assistant", content = (string?)res.Text };
            string finish = res.ToolCalls.Count > 0 ? "tool_calls" : FinishReason(res);
            await t.WriteJsonAsync(200, new
            {
                id, @object = "chat.completion", created, model,
                choices = new[] { new { index = 0, message, finish_reason = finish } },
                usage = new { prompt_tokens = res.PromptTokens, completion_tokens = res.GeneratedTokens, total_tokens = res.PromptTokens + res.GeneratedTokens },
            });
            return;
        }

        await t.StartEventStreamAsync(AiEventStreamKind.Sse);
        bool first = true;
        var res2 = await _engine.ChatStreamAsync(request, async delta =>
        {
            var choice = first
                ? new { index = 0, delta = (object)new { role = "assistant", content = delta }, finish_reason = (string?)null }
                : new { index = 0, delta = (object)new { content = delta }, finish_reason = (string?)null };
            first = false;
            await t.WriteEventAsync(null, new { id, @object = "chat.completion.chunk", created, model, choices = new[] { choice } });
        }, t.Aborted);
        await t.WriteEventAsync(null, new { id, @object = "chat.completion.chunk", created, model, choices = new[] { new { index = 0, delta = new { }, finish_reason = FinishReason(res2) } } });
        await t.WriteRawAsync("data: [DONE]\n\n");
    }

    // ── Ollama native: /api/chat + /api/generate ──
    private async Task ApiChat(JsonElement req, IAiServerTransport t)
    {
        string model = GetString(req, "model") ?? "";
        if (await _engine.ShowModelAsync(model, t.Aborted) == null)
        { await OllamaError(t, $"model '{model}' not found", 404); return; }
        var messages = ParseMessages(req, "messages");
        bool stream = GetBool(req, "stream") ?? true; // Ollama defaults to streaming
        var options = ReadOllamaOptions(req);
        string created = DateTimeOffset.UtcNow.ToString("o");
        var tools = GetTools(req);
        var request = new AiChatRequest { Model = model, Messages = messages, Options = options, ToolsJson = tools };

        if (tools != null)
        {
            var res = await _engine.ChatAsync(request, t.Aborted);
            // A tool call empties content (Ollama shape): the raw markup must not leak as visible text.
            object toolMsg = res.ToolCalls.Count > 0
                ? new { role = "assistant", content = "", tool_calls = res.ToolCalls.Select(tc => new { function = new { name = tc.Name, arguments = ParseJsonOrEmpty(tc.ArgumentsJson) } }).ToArray() }
                : new { role = "assistant", content = res.Text };
            string doneReason = res.ToolCalls.Count > 0 ? "stop" : OllamaDone(res);
            if (!stream) { await t.WriteJsonAsync(200, new { model, created_at = created, message = toolMsg, done = true, done_reason = doneReason }); return; }
            await t.StartEventStreamAsync(AiEventStreamKind.Ndjson);
            await t.WriteEventAsync(null, new { model, created_at = created, message = toolMsg, done = false });
            await t.WriteEventAsync(null, new { model, created_at = created, message = new { role = "assistant", content = "" }, done = true, done_reason = doneReason });
            return;
        }

        if (!stream)
        {
            var res = await _engine.ChatAsync(request, t.Aborted);
            await t.WriteJsonAsync(200, new { model, created_at = created, message = new { role = "assistant", content = res.Text }, done = true, done_reason = OllamaDone(res) });
            return;
        }

        await t.StartEventStreamAsync(AiEventStreamKind.Ndjson);
        var res2 = await _engine.ChatStreamAsync(request, async delta =>
            await t.WriteEventAsync(null, new { model, created_at = created, message = new { role = "assistant", content = delta }, done = false }),
            t.Aborted);
        await t.WriteEventAsync(null, new { model, created_at = created, message = new { role = "assistant", content = "" }, done = true, done_reason = OllamaDone(res2) });
    }

    private async Task ApiGenerate(JsonElement req, IAiServerTransport t)
    {
        string model = GetString(req, "model") ?? "";
        if (await _engine.ShowModelAsync(model, t.Aborted) == null)
        { await OllamaError(t, $"model '{model}' not found", 404); return; }
        string prompt = GetString(req, "prompt") ?? "";
        bool stream = GetBool(req, "stream") ?? true;
        var options = ReadOllamaOptions(req);
        string created = DateTimeOffset.UtcNow.ToString("o");
        var request = new AiChatRequest
        { Model = model, Messages = new[] { new AiChatMessage("user", prompt) }, Options = options };

        if (!stream)
        {
            var res = await _engine.ChatAsync(request, t.Aborted);
            await t.WriteJsonAsync(200, new { model, created_at = created, response = res.Text, done = true, done_reason = OllamaDone(res) });
            return;
        }
        await t.StartEventStreamAsync(AiEventStreamKind.Ndjson);
        var res2 = await _engine.ChatStreamAsync(request, async delta =>
            await t.WriteEventAsync(null, new { model, created_at = created, response = delta, done = false }),
            t.Aborted);
        await t.WriteEventAsync(null, new { model, created_at = created, response = "", done = true, done_reason = OllamaDone(res2) });
    }

    // ── Anthropic Messages: /v1/messages (+count_tokens) — Claude CLI ──
    private async Task V1CountTokens(JsonElement req, IAiServerTransport t)
    {
        string model = GetString(req, "model") ?? "";
        var messages = ParseMessages(req, "messages");
        int n = await _engine.CountTokensAsync(model, messages, t.Aborted);
        await t.WriteJsonAsync(200, new { input_tokens = n });
    }

    private async Task V1Messages(JsonElement req, IAiServerTransport t)
    {
        string model = GetString(req, "model") ?? "";
        if (await _engine.ShowModelAsync(model, t.Aborted) == null)
        { await AnthropicError(t, $"model '{model}' not found", 404); return; }
        var messages = ParseMessages(req, "messages", anthropicSystem: GetString(req, "system"));
        bool stream = GetBool(req, "stream") ?? false;
        var options = ReadAnthropicOptions(req);
        options.Stops = GetStringArray(req, "stop_sequences");
        var tools = GetTools(req);
        string id = "msg_" + Guid.NewGuid().ToString("N")[..24];
        var request = new AiChatRequest { Model = model, Messages = messages, Options = options, ToolsJson = tools };

        if (!stream)
        {
            var res = await _engine.ChatAsync(request, t.Aborted);
            bool hasTool = res.ToolCalls.Count > 0;
            var content = new List<object>();
            if (!hasTool) content.Add(new { type = "text", text = res.Text });
            else if (res.TextWithoutToolCalls.Length > 0) content.Add(new { type = "text", text = res.TextWithoutToolCalls });
            foreach (var tc in res.ToolCalls)
                content.Add(new { type = "tool_use", id = "toolu_" + Guid.NewGuid().ToString("N")[..20], name = tc.Name, input = ParseJsonOrEmpty(tc.ArgumentsJson) });
            await t.WriteJsonAsync(200, new
            {
                id, type = "message", role = "assistant", model,
                content, stop_reason = hasTool ? "tool_use" : AnthropicStop(res), stop_sequence = (string?)null,
                usage = new { input_tokens = res.PromptTokens, output_tokens = res.GeneratedTokens },
            });
            return;
        }

        // Streaming (Claude CLI times out on buffered silence): text deltas stream live - the engine
        // holds back tool markup when tools are present - and tool_use blocks emit at the end.
        // Streaming also makes a client disconnect ABORT generation (the event write throws).
        await t.StartEventStreamAsync(AiEventStreamKind.Sse);
        await t.WriteEventAsync("message_start", new { type = "message_start", message = new { id, type = "message", role = "assistant", model, content = Array.Empty<object>(), stop_reason = (string?)null, stop_sequence = (string?)null, usage = new { input_tokens = 0, output_tokens = 1 } } });
        await t.WriteEventAsync("content_block_start", new { type = "content_block_start", index = 0, content_block = new { type = "text", text = "" } });

        var sres = await _engine.ChatStreamAsync(request, async delta =>
            await t.WriteEventAsync("content_block_delta", new { type = "content_block_delta", index = 0, delta = new { type = "text_delta", text = delta } }),
            t.Aborted);

        await t.WriteEventAsync("content_block_stop", new { type = "content_block_stop", index = 0 });
        int bi = 1;
        foreach (var tc in sres.ToolCalls)
        {
            await t.WriteEventAsync("content_block_start", new { type = "content_block_start", index = bi, content_block = new { type = "tool_use", id = "toolu_" + Guid.NewGuid().ToString("N")[..20], name = tc.Name, input = new { } } });
            await t.WriteEventAsync("content_block_delta", new { type = "content_block_delta", index = bi, delta = new { type = "input_json_delta", partial_json = tc.ArgumentsJson } });
            await t.WriteEventAsync("content_block_stop", new { type = "content_block_stop", index = bi });
            bi++;
        }
        await t.WriteEventAsync("message_delta", new { type = "message_delta", delta = new { stop_reason = sres.ToolCalls.Count > 0 ? "tool_use" : AnthropicStop(sres), stop_sequence = (string?)null }, usage = new { output_tokens = sres.GeneratedTokens } });
        await t.WriteEventAsync("message_stop", new { type = "message_stop" });
    }

    // ── Request parsing (protocol JSON → contracts) ──
    private static string? GetString(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    private static bool? GetBool(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && (v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False) ? v.GetBoolean() : null;
    private static int? GetInt(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : null;
    private static float? GetFloat(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetSingle() : null;
    private static string[]? GetStringArray(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v)) return null;
        if (v.ValueKind == JsonValueKind.String) return new[] { v.GetString()! };
        if (v.ValueKind == JsonValueKind.Array) return v.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToArray();
        return null;
    }

    private static List<string>? GetTools(JsonElement req)
    {
        if (!req.TryGetProperty("tools", out var tl) || tl.ValueKind != JsonValueKind.Array || tl.GetArrayLength() == 0) return null;
        var list = new List<string>();
        foreach (var tool in tl.EnumerateArray()) list.Add(tool.GetRawText());
        return list;
    }

    private static JsonElement ParseJsonOrEmpty(string json)
    {
        try { using var d = JsonDocument.Parse(json); return d.RootElement.Clone(); }
        catch { using var d = JsonDocument.Parse("{}"); return d.RootElement.Clone(); }
    }

    // messages[] → AiChatMessage list. Anthropic content can be block arrays; assistant tool_calls and
    // role:"tool" results are rendered back into <tool_call>/<tool_response> markup (ChatML round-trip).
    private static List<AiChatMessage> ParseMessages(JsonElement req, string name, string? anthropicSystem = null)
    {
        var list = new List<AiChatMessage>();
        if (!string.IsNullOrEmpty(anthropicSystem)) list.Add(new AiChatMessage("system", anthropicSystem));
        if (req.TryGetProperty(name, out var msgs) && msgs.ValueKind == JsonValueKind.Array)
        {
            foreach (var m in msgs.EnumerateArray())
            {
                string role = m.TryGetProperty("role", out var r) ? r.GetString() ?? "user" : "user";
                string content = m.TryGetProperty("content", out var c) ? ExtractContent(c) : "";

                if (role == "assistant" && m.TryGetProperty("tool_calls", out var tcs) && tcs.ValueKind == JsonValueKind.Array)
                {
                    var sb = new StringBuilder(content);
                    foreach (var tc in tcs.EnumerateArray())
                    {
                        if (!tc.TryGetProperty("function", out var fn)) continue;
                        string fname = fn.TryGetProperty("name", out var nn) ? nn.GetString() ?? "" : "";
                        string fargs = fn.TryGetProperty("arguments", out var aa)
                            ? (aa.ValueKind == JsonValueKind.String ? aa.GetString() ?? "{}" : aa.GetRawText())
                            : "{}";
                        sb.Append($"\n<tool_call>\n{{\"name\": \"{fname}\", \"arguments\": {fargs}}}\n</tool_call>");
                    }
                    content = sb.ToString();
                }
                if (role == "tool")
                {
                    content = $"<tool_response>\n{content}\n</tool_response>";
                    role = "user";
                }
                list.Add(new AiChatMessage(role, content));
            }
        }
        return list;
    }

    private static string ExtractContent(JsonElement c)
    {
        if (c.ValueKind == JsonValueKind.String) return c.GetString() ?? "";
        if (c.ValueKind == JsonValueKind.Array) // OpenAI/Anthropic content blocks
        {
            var sb = new StringBuilder();
            foreach (var block in c.EnumerateArray())
            {
                string btype = block.TryGetProperty("type", out var bt) ? bt.GetString() ?? "" : "";
                if (btype == "tool_use")
                {
                    string nm = block.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                    string inp = block.TryGetProperty("input", out var ip) ? ip.GetRawText() : "{}";
                    sb.Append($"\n<tool_call>\n{{\"name\": \"{nm}\", \"arguments\": {inp}}}\n</tool_call>");
                }
                else if (btype == "tool_result")
                {
                    string rc = block.TryGetProperty("content", out var cc)
                        ? (cc.ValueKind == JsonValueKind.String ? cc.GetString() ?? "" : ExtractContent(cc)) : "";
                    sb.Append($"<tool_response>\n{rc}\n</tool_response>");
                }
                else if (block.TryGetProperty("text", out var tx) && tx.ValueKind == JsonValueKind.String)
                    sb.Append(tx.GetString());
            }
            return sb.ToString();
        }
        return "";
    }

    private AiGenerationOptions ReadOpenAiOptions(JsonElement req)
    {
        var o = new AiGenerationOptions { MaxOutputTokens = GetInt(req, "max_tokens") ?? GetInt(req, "max_completion_tokens") ?? 512 };
        ApplySampling(o, GetFloat(req, "temperature"), GetFloat(req, "top_p"), null, GetInt(req, "seed"));
        return o;
    }
    private AiGenerationOptions ReadAnthropicOptions(JsonElement req)
    {
        var o = new AiGenerationOptions { MaxOutputTokens = GetInt(req, "max_tokens") ?? 512 };
        ApplySampling(o, GetFloat(req, "temperature"), GetFloat(req, "top_p"), GetInt(req, "top_k"), null);
        return o;
    }
    private AiGenerationOptions ReadOllamaOptions(JsonElement req)
    {
        var o = new AiGenerationOptions { MaxOutputTokens = 512 };
        if (req.TryGetProperty("options", out var opt) && opt.ValueKind == JsonValueKind.Object)
        {
            o.MaxOutputTokens = GetInt(opt, "num_predict") ?? 512;
            ApplySampling(o, GetFloat(opt, "temperature"), GetFloat(opt, "top_p"), GetInt(opt, "top_k"), GetInt(opt, "seed"));
            if (GetFloat(opt, "repeat_penalty") is float rp && rp > 0) o.RepetitionPenalty = rp;
        }
        return o;
    }
    private static void ApplySampling(AiGenerationOptions o, float? temp, float? topP, int? topK, int? seed)
    {
        if (seed is int s) o.Seed = s;
        if (temp is float tv && tv > 0)
        {
            o.Temperature = tv;
            if (topK is int k && k > 0) { o.Strategy = "top_k"; o.TopK = k; }
            else { o.Strategy = "top_p"; o.TopP = topP ?? 1.0f; }
        }
        else o.Strategy = "greedy";
    }

    private static string FinishReason(AiChatResult r) => r.Stop == AiStopKind.Length ? "length" : "stop";
    private static string OllamaDone(AiChatResult r) => r.Stop == AiStopKind.Length ? "length" : "stop";
    private static string AnthropicStop(AiChatResult r) => r.Stop == AiStopKind.Length ? "max_tokens" : "end_turn";
}
