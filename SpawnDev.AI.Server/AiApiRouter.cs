using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

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

    /// <summary>Optional image engine - enables /v1/images/generations (OpenAI-compatible).</summary>
    public AiImageEngine? Images { get; set; }

    /// <summary>Optional tool registry - enables /ai/artifacts/{id} (base64 fetch of tool outputs).</summary>
    public AiToolRegistry? Tools { get; set; }

    /// <summary>Optional voice engine - enables /api/speak (text to speech).</summary>
    /// <remarks>
    /// Separate from <see cref="Speech"/> on purpose: an app can want to LISTEN without talking back (a
    /// dictation box) or to talk without listening (a read-aloud button), and each engine holds a different
    /// model resident on a shared GPU. Coupling them would force both loads on anyone who wanted either.
    /// </remarks>
    public AiVoiceEngine? Voice { get; set; }

    /// <summary>Optional speech engine - enables /api/transcribe (speech to text).</summary>
    public AiSpeechEngine? Speech { get; set; }

    /// <summary>Optional endpointer - enables /api/vad (where an utterance starts and stops).</summary>
    /// <remarks>
    /// Separate from <see cref="Speech"/> for the same reason <see cref="Voice"/> is: a caller that already
    /// knows the bounds of its audio (a file, a push-to-talk button) needs the recogniser and not this,
    /// while a hands-free loop needs this BEFORE it has anything to recognise.
    /// </remarks>
    public AiVadEngine? Vad { get; set; }

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
            case ("POST", "/v1/images/generations") when Images != null: await V1ImagesGenerations(Body(body), t); return true;
            case ("POST", "/mcp") when Tools != null: await Mcp(Body(body), t); return true;
            case ("POST", "/api/transcribe") when Speech != null: await ApiTranscribe(Body(body), t); return true;
            case ("POST", "/api/vad") when Vad != null: await ApiVad(Body(body), t); return true;
            case ("POST", "/api/warm"): await ApiWarm(Body(body), t); return true;
            case ("POST", "/api/speak") when Voice != null: await ApiSpeak(Body(body), t); return true;
            case ("GET", _) when Tools != null && path.StartsWith("/ai/artifacts/", StringComparison.Ordinal):
                await GetArtifact(path["/ai/artifacts/".Length..], t); return true;
            case ("GET", "/ai/image-models") when Images != null:
                await t.WriteJsonAsync(200, new
                {
                    @default = Images.DefaultModel,
                    models = Images.Models.Select(m => new { name = m.Name, note = m.Note }),
                });
                return true;
            default: return false;
        }
    }

    // ── OpenAI: POST /v1/images/generations (DALL-E-compatible; b64_json response) ──
    private async Task V1ImagesGenerations(JsonElement req, IAiServerTransport t)
    {
        string prompt = GetString(req, "prompt") ?? "";
        if (string.IsNullOrWhiteSpace(prompt)) { await OpenAiError(t, "'prompt' is required", 400); return; }
        string? model = GetString(req, "model");
        int n = Math.Clamp(GetInt(req, "n") ?? 1, 1, 4);
        int? seed = GetInt(req, "seed");

        var data = new List<object>();
        for (int i = 0; i < n; i++)
        {
            AiGeneratedImage img;
            try { img = await Images!.GenerateAsync(prompt, model, seed is int s ? s + i : null, ct: t.Aborted); }
            catch (FileNotFoundException ex) { await OpenAiError(t, ex.Message, 404); return; }
            var png = PngEncoder.EncodeRgba(img.Rgba, img.Width, img.Height);
            data.Add(new { b64_json = Convert.ToBase64String(png), revised_prompt = prompt, seed = img.Seed });
        }
        await t.WriteJsonAsync(200, new { created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), data });
    }

    // ── GET /ai/artifacts/{id}: base64 fetch of a stored tool artifact (generated image). ──
    private async Task GetArtifact(string id, IAiServerTransport t)
    {
        var a = Tools!.GetArtifact(id);
        if (a == null) { await t.WriteJsonAsync(404, new { error = $"artifact '{id}' not found (evicted or never existed)" }); return; }
        await t.WriteJsonAsync(200, new { id = a.Id, mime = a.MimeType, label = a.Label, b64 = Convert.ToBase64String(a.Data) });
    }

    // ── MCP (Model Context Protocol) surface: JSON-RPC 2.0 over POST /mcp. Exposes the AiToolRegistry so
    // Claude CLI / any MCP agent can list + call the server's tools (generate_image, ...). Request-response
    // only - initialize / tools/list / tools/call / ping (server-initiated streaming isn't needed for tools),
    // so a single JSON response is returned (valid under Streamable HTTP). Same registry the internal agentic
    // loop and the /v1 tool surfaces read - one registration, three surfaces. ──
    private const string McpProtocolVersion = "2024-11-05";

    private async Task Mcp(JsonElement req, IAiServerTransport t)
    {
        string? method = req.TryGetProperty("method", out var mEl) && mEl.ValueKind == JsonValueKind.String ? mEl.GetString() : null;
        // A JSON-RPC message with no "id" is a NOTIFICATION (e.g. notifications/initialized) - acknowledge with
        // 202 and no JSON-RPC response body, ever.
        bool isNotification = !req.TryGetProperty("id", out var idEl);
        if (isNotification) { await t.WriteTextAsync(202, ""); return; }
        object? id = JsonRpcId(idEl);

        if (string.IsNullOrEmpty(method)) { await McpError(t, id, -32600, "Invalid Request: missing 'method'"); return; }
        req.TryGetProperty("params", out var prm);

        switch (method)
        {
            case "initialize":
                // Echo the client's requested protocol version when present (signals we speak it); else our default.
                string proto = prm.ValueKind == JsonValueKind.Object
                    && prm.TryGetProperty("protocolVersion", out var vEl) && vEl.ValueKind == JsonValueKind.String
                    ? vEl.GetString()! : McpProtocolVersion;
                await McpResult(t, id, new
                {
                    protocolVersion = proto,
                    capabilities = new { tools = new { } },
                    serverInfo = new { name = "SpawnDev.AI", version = Version },
                });
                return;

            case "ping":
                await McpResult(t, id, new { });
                return;

            case "tools/list":
                var list = (Tools?.List() ?? (IReadOnlyList<IAiTool>)Array.Empty<IAiTool>()).Select(x => new
                {
                    name = x.Name,
                    description = x.Description,
                    inputSchema = ParseSchema(x.ParametersJsonSchema),
                }).ToArray();
                await McpResult(t, id, new { tools = list });
                return;

            case "tools/call":
                if (prm.ValueKind != JsonValueKind.Object || !prm.TryGetProperty("name", out var nameEl) || nameEl.ValueKind != JsonValueKind.String)
                { await McpError(t, id, -32602, "Invalid params: 'name' is required"); return; }
                var tool = Tools?.Get(nameEl.GetString()!);
                if (tool == null) { await McpError(t, id, -32602, $"Unknown tool '{nameEl.GetString()}'"); return; }

                // MCP passes arguments as a JSON object; the tool contract takes the arguments JSON as a string.
                string argsJson = prm.TryGetProperty("arguments", out var argsEl) && argsEl.ValueKind == JsonValueKind.Object
                    ? argsEl.GetRawText() : "{}";
                AiToolExecutionResult exec;
                try { exec = await tool.ExecuteAsync(argsJson, t.Aborted).ConfigureAwait(false); }
                catch (Exception ex) { exec = new AiToolExecutionResult($"Tool '{tool.Name}' threw: {ex.Message}") { IsError = true }; }

                // Content = the text the caller reads, plus any image artifacts inline (base64). A tool error is
                // reported via isError=true with the message as text (per MCP, tool errors are in-band, not JSON-RPC
                // errors - those are reserved for protocol failures).
                var content = new List<object> { new { type = "text", text = exec.TextForModel } };
                if (exec.Artifacts != null)
                    foreach (var a in exec.Artifacts)
                        if (a.MimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                            content.Add(new { type = "image", data = Convert.ToBase64String(a.Data), mimeType = a.MimeType });
                await McpResult(t, id, new { content, isError = exec.IsError });
                return;

            default:
                await McpError(t, id, -32601, $"Method not found: {method}");
                return;
        }
    }

    private Task McpResult(IAiServerTransport t, object? id, object result) =>
        t.WriteJsonAsync(200, new { jsonrpc = "2.0", id, result });

    private Task McpError(IAiServerTransport t, object? id, int code, string message) =>
        t.WriteJsonAsync(200, new { jsonrpc = "2.0", id, error = new { code, message } });

    // A tool's ParametersJsonSchema is a JSON string; MCP tools/list needs it as a JSON object. Fall back to a
    // permissive empty-object schema if a tool ships malformed schema text (never break the whole list).
    private static JsonNode ParseSchema(string schemaJson)
    {
        try { return JsonNode.Parse(schemaJson) ?? new JsonObject { ["type"] = "object" }; }
        catch { return new JsonObject { ["type"] = "object" }; }
    }

    // JSON-RPC id echoes back with its original type (string | number | null).
    private static object? JsonRpcId(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.String => e.GetString(),
        JsonValueKind.Number => e.TryGetInt64(out var l) ? l : e.GetDouble(),
        _ => null,
    };

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

    /// <summary>
    /// POST /api/transcribe - speech to text. Body: <c>{ samples: number[], sample_rate: int }</c>, mono
    /// PCM in [-1, 1]. Responds <c>{ text, model, inference_ms }</c>.
    /// </summary>
    /// <remarks>
    /// ⚠️ PCM as a JSON number array is deliberately the FIRST cut, not the end state: it is the simplest
    /// thing that works over both transports (HTTP and the worker MessagePort) and lets the speech loop be
    /// gated end to end. It is also the WRONG shape for real audio - 30 s at 16 kHz is 480,000 numbers, and
    /// JSON-encoding that violates the standing "bulk bytes stay out of the .NET heap" rule. The follow-up
    /// is a binary frame (or a transferred Float32Array on the worker port); the route and the engine do not
    /// change when that lands.
    /// </remarks>
    private async Task ApiTranscribe(JsonElement body, IAiServerTransport t)
    {
        if (!body.TryGetProperty("samples", out var samplesEl) || samplesEl.ValueKind != JsonValueKind.Array)
        {
            await t.WriteJsonAsync(400, new { error = "body needs a 'samples' array of mono PCM in [-1,1]" });
            return;
        }
        var sampleRate = body.TryGetProperty("sample_rate", out var srEl) && srEl.TryGetInt32(out var sr)
            ? sr : 16000;

        var samples = new float[samplesEl.GetArrayLength()];
        var i = 0;
        foreach (var v in samplesEl.EnumerateArray()) samples[i++] = (float)v.GetDouble();

        if (samples.Length == 0)
        {
            await t.WriteJsonAsync(400, new { error = "'samples' was empty" });
            return;
        }

        try
        {
            var result = await Speech!.TranscribeAsync(samples, sampleRate).ConfigureAwait(false);
            await t.WriteJsonAsync(200, new
            {
                text = result.Text,
                model = result.Model,
                inference_ms = result.InferenceMs,
                // ⚠️ The engine runs in a shared worker, whose console is NOT the page console - so the
                // split it prints there is invisible to the window, to DevTools on the page, and to the UI
                // gate. Carrying it in the response is what makes it readable at all.
                timing = result.Split == null ? null : new
                {
                    graph_runs = result.Split.GraphRuns,
                    executor_ms = result.Split.ExecutorMs,
                    readback_count = result.Split.ReadbackCount,
                    readback_ms = result.Split.ReadbackMs,
                    drain_count = result.Split.DrainCount,
                    drain_ms = result.Split.DrainMs,
                    residual_ms = result.Split.ResidualMs,
                    outside_executor_ms = result.Split.OutsideExecutorMs,
                    mel_ms = result.Split.MelMs,
                    encoder_capture = result.Split.EncoderCaptureStatus,
                    encoder_ms = result.Split.EncoderMs,
                    prefill_ms = result.Split.PrefillMs,
                    decode_steps_ms = result.Split.DecodeStepsMs,
                    decode_steps = result.Split.DecodeSteps,
                    encoder_nodes = result.Split.EncoderNodeCount,
                    decoder_nodes = result.Split.DecoderNodeCount,
                    decode_setup_ms = result.Split.DecodeSetupMs,
                    decode_graph_ms = result.Split.DecodeGraphMs,
                    decode_argmax_ms = result.Split.DecodeArgmaxMs,
                },
            });
        }
        catch (Exception ex)
        {
            // Report the failure rather than an empty transcript: "" is indistinguishable from silence, and
            // a caller cannot tell a broken model from a quiet microphone.
            // ⚠️ Include the STACK. A bare "NullReferenceException: Arg_NullReferenceException" names nothing
            // - it cost a round trip of guessing before this was added. The frames are what identify the
            // failing call, and this runs in a worker whose exceptions are not otherwise visible.
            await t.WriteJsonAsync(500, new
            {
                error = $"{ex.GetType().Name}: {ex.Message}",
                detail = ex.ToString().Length > 2000 ? ex.ToString()[..2000] : ex.ToString(),
            });
        }
    }

    /// <summary>
    /// POST /api/warm - make model kinds resident NOW. Body: <c>{ kinds: ["vad","speech","voice"] }</c>.
    /// Responds <c>{ warmed: [...], failed: [{ kind, error }] }</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ WHY. A hands-free conversation loads three models, and lazily it loads each one INSIDE the turn
    /// that needs it - so the user's first sentence is followed by a recogniser download, and the first
    /// reply by a voice download. MEASURED in the demo: the first spoken reply cost <b>88.7 s</b>, nearly
    /// all of it a cold ZipVoice load (two int8 graphs plus a 54 MB vocoder pulled out of a remote
    /// archive), and it happened after the text answer was already on screen. The work is the same either
    /// way; what changes is WHEN. Warming while the microphone is open spends it during the seconds the
    /// user is talking anyway.
    /// </para>
    /// <para>
    /// ⚠️ Partial success is REPORTED, not thrown. Warming is an optimisation - a kind that fails to warm
    /// must still be attempted lazily by its own route later, and turning the conversation off because a
    /// preload failed would be worse than the delay it avoids.
    /// </para>
    /// </remarks>
    private async Task ApiWarm(JsonElement body, IAiServerTransport t)
    {
        var kinds = GetStringArray(body, "kinds") ?? System.Array.Empty<string>();
        if (kinds.Length == 0) kinds = new[] { "vad", "speech", "voice" };

        var warmed = new List<string>();
        var failed = new List<object>();
        foreach (var kind in kinds)
        {
            try
            {
                switch (kind.ToLowerInvariant())
                {
                    case "vad" when Vad != null: await Vad.EnsureReadyAsync(t.Aborted); warmed.Add("vad"); break;
                    case "speech" when Speech != null: await Speech.EnsureReadyAsync(t.Aborted); warmed.Add("speech"); break;
                    case "voice" when Voice != null: await Voice.EnsureReadyAsync(t.Aborted); warmed.Add("voice"); break;
                    // ⚠️ "chat" needs a MODEL, unlike the other three - each of those owns exactly one.
                    // Body: { kinds: ["chat"], model: "..." }. Without a model there is nothing to warm and
                    // saying so beats silently warming whatever happens to be resident.
                    case "chat":
                    {
                        var model = GetString(body, "model");
                        if (string.IsNullOrWhiteSpace(model))
                            failed.Add(new { kind, error = "warming \"chat\" requires a \"model\" in the body" });
                        else { await _engine.EnsureReadyAsync(model, t.Aborted); warmed.Add("chat"); }
                        break;
                    }
                    default: failed.Add(new { kind, error = "no such engine on this server" }); break;
                }
            }
            catch (Exception ex)
            {
                failed.Add(new { kind, error = $"{ex.GetType().Name}: {ex.Message}" });
            }
        }

        await t.WriteJsonAsync(200, new { warmed = warmed.ToArray(), failed = failed.ToArray() });
    }

    /// <summary>
    /// POST /api/vad - where an utterance starts and stops. Body:
    /// <c>{ samples: number[], reset?: bool, flush?: bool }</c>, mono PCM at 16 kHz continuing the stream
    /// fed so far. Responds
    /// <c>{ speech_active, probability, spans: [{ start, length }], frame_ms, mean_frame_ms, model }</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>Spans, not samples.</b> A closed utterance comes back as offsets into the CALLER'S stream, not
    /// as audio. The caller already holds every sample it sent; returning a 20 s utterance as a JSON number
    /// array would be 320,000 numbers to express what two integers say. This is the one audio route that
    /// does not have the "wrong shape for bulk audio" caveat the others carry, and it is deliberate.
    /// </para>
    /// <para>
    /// ⚠️ <c>reset</c> is not cosmetic: the offsets are counted from the first sample after the last reset,
    /// so a caller reopening its microphone must reset or it gets offsets pointing past its new buffer.
    /// <c>flush</c> closes speech still in progress - without it, the last thing said before the microphone
    /// closed is never emitted, because the detector waits for trailing silence it will now never see.
    /// </para>
    /// </remarks>
    private async Task ApiVad(JsonElement body, IAiServerTransport t)
    {
        var reset = body.TryGetProperty("reset", out var rEl)
                    && rEl.ValueKind == JsonValueKind.True;
        var flush = body.TryGetProperty("flush", out var fEl)
                    && fEl.ValueKind == JsonValueKind.True;

        float[] samples = Array.Empty<float>();
        if (body.TryGetProperty("samples", out var samplesEl) && samplesEl.ValueKind == JsonValueKind.Array)
        {
            samples = new float[samplesEl.GetArrayLength()];
            var i = 0;
            foreach (var v in samplesEl.EnumerateArray()) samples[i++] = (float)v.GetDouble();
        }

        try
        {
            // ⚠️ A reset LOADS the model if it is not resident yet, and that is the point: callers reset
            // just before opening the microphone, so this is the one moment when paying for the load costs
            // nobody anything. Leaving it lazy put the load on the first audio frame instead, with the
            // microphone already running and audio queueing behind it - MEASURED at 17.6 s to endpoint a
            // 4.0 s utterance.
            if (reset)
            {
                await Vad!.EnsureReadyAsync(t.Aborted).ConfigureAwait(false);
                await Vad!.ResetStreamAsync(t.Aborted).ConfigureAwait(false);
            }

            var update = samples.Length > 0
                ? await Vad!.AcceptAsync(samples, t.Aborted).ConfigureAwait(false)
                : new AiVadUpdate(false, Vad!.LastProbability, Array.Empty<AiSpeechSpan>(), 0);

            if (flush)
            {
                var tail = await Vad!.FlushAsync(t.Aborted).ConfigureAwait(false);
                if (tail.Spans.Count > 0)
                    update = update with { Spans = update.Spans.Concat(tail.Spans).ToArray() };
            }

            await t.WriteJsonAsync(200, new
            {
                speech_active = update.SpeechActive,
                probability = update.Probability,
                spans = update.Spans.Select(s => new { start = s.StartSample, length = s.Length }).ToArray(),
                frame_ms = update.FrameMs,
                mean_frame_ms = Vad!.MeanFrameMs,
                model = Vad!.ModelName,
            });
        }
        catch (Exception ex)
        {
            // As /api/transcribe: report it with the STACK. A silent endpointer is indistinguishable from
            // a quiet room, and the loop would wait forever on one.
            await t.WriteJsonAsync(500, new
            {
                error = $"{ex.GetType().Name}: {ex.Message}",
                detail = ex.ToString().Length > 2000 ? ex.ToString()[..2000] : ex.ToString(),
            });
        }
    }

    /// <summary>
    /// POST /api/speak - text to speech, in a cloned voice. Body:
    /// <c>{ text, reference_samples: number[], reference_text, sample_rate }</c>.
    /// Responds <c>{ samples, sample_rate, model, inference_ms, duration_seconds }</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <c>reference_samples</c> is REQUIRED and there is no default voice. ZipVoice clones - it speaks in
    /// the voice of a clip you give it - so in a conversation the reference is the turn the user just spoke,
    /// and the assistant answers in their voice. An engine that silently substituted some stock voice when
    /// the reference was missing would be a different product quietly pretending to be this one.
    /// </para>
    /// <para>
    /// ⚠️ Same first-cut caveat as /api/transcribe: PCM as a JSON number array is the simplest thing that
    /// works over BOTH transports (HTTP and the worker MessagePort), and it is the wrong shape for real
    /// audio - a few seconds at 24 kHz is a six-figure array of numbers, which violates the standing "bulk
    /// bytes stay out of the .NET heap" rule. The follow-up is a binary frame or a transferred
    /// Float32Array; neither the route nor the engine changes when that lands.
    /// </para>
    /// </remarks>
    private async Task ApiSpeak(JsonElement body, IAiServerTransport t)
    {
        var text = body.TryGetProperty("text", out var textEl) ? textEl.GetString() ?? "" : "";
        if (string.IsNullOrWhiteSpace(text))
        {
            await t.WriteJsonAsync(400, new { error = "body needs a non-empty 'text'" });
            return;
        }

        if (!body.TryGetProperty("reference_samples", out var refEl) || refEl.ValueKind != JsonValueKind.Array
            || refEl.GetArrayLength() == 0)
        {
            await t.WriteJsonAsync(400, new
            {
                error = "body needs a non-empty 'reference_samples' array - this voice is CLONED from a "
                      + "reference clip, so there is no default voice to fall back to",
            });
            return;
        }

        var referenceText = body.TryGetProperty("reference_text", out var rtEl) ? rtEl.GetString() ?? "" : "";
        var sampleRate = body.TryGetProperty("sample_rate", out var srEl) && srEl.TryGetInt32(out var sr)
            ? sr : 16000;

        var reference = new float[refEl.GetArrayLength()];
        var i = 0;
        foreach (var v in refEl.EnumerateArray()) reference[i++] = (float)v.GetDouble();

        try
        {
            // Optional: a caller can ask for a longer read-out than the default brevity cap. Honoured
            // because the cap is a product choice, not an engine limit (the long-utterance defect it used
            // to hide was fixed in ILGPU.ML 5.2.7-local.11).
            // ⚠️ ValueKind FIRST. TryGetInt32 THROWS on a non-number element instead of returning false, and
            // the client serialises `max_spoken_characters: null` whenever no override is given - so the
            // property EXISTS and is Null, and the "Try" name reads as safe when it is not. That 500'd every
            // ordinary speak request the moment this field was added.
            int? maxSpoken = body.TryGetProperty("max_spoken_characters", out var msEl)
                && msEl.ValueKind == JsonValueKind.Number
                && msEl.TryGetInt32(out var ms) && ms > 0 ? ms : null;
            // Hand the voice a recogniser. ZipVoice draws fresh noise per synthesis and some draws come
            // out as fluent speech that is not the requested sentence at all - a defect invisible to
            // amplitude, duration and every other check, and the direct cause of the "high pitch weird
            // noises" the Captain reported on 2026-09-04 (read-back scored 0% word overlap, "[INAUDIBLE]").
            // This router is the one place that holds BOTH engines, so it is where the loop closes.
            // ⚠️ OFF BY DEFAULT, AND THE REASON MATTERS. This was wired in on 2026-09-04 because spoken
            // replies came back garbled, on the theory that ZipVoice had drawn bad noise. It had not: the
            // garbling was a SHAPE defect in ILGPU.ML (a compile-time output shape used as runtime truth),
            // fixed in 5.2.9 - after which the same lines read back at 100%, verbatim, on the FIRST draw on
            // both CUDA and WebGPU. Re-rolling is a mitigation for a cause that no longer exists, and it
            // costs a full Whisper transcription per synthesis.
            // It also currently trips a SEPARATE, pre-existing defect: transcribing inside a synthesis
            // replays Whisper's captured encoder plan and raises "[Buffer (unlabeled)] used in submit while
            // destroyed". That bug is real and open - it is NOT the reason this defaults off, and turning
            // this on is the fastest way to reproduce it.
            Func<float[], int, Task<string>>? readBack = !Voice!.VerifyByReadBack || Speech == null
                ? null
                : async (audio, rate) =>
                    (await Speech.TranscribeAsync(audio, rate).ConfigureAwait(false)).Text ?? "";

            // Optional pinned noise draw, so a caller can make a synthesis REPRODUCIBLE. Same ValueKind-first
            // guard as max_spoken_characters above: TryGetInt32 THROWS on a non-number element rather than
            // returning false, and clients serialise an absent value as null.
            int? noiseSeed = body.TryGetProperty("noise_seed", out var nsEl)
                && nsEl.ValueKind == JsonValueKind.Number
                && nsEl.TryGetInt32(out var ns) ? ns : null;

            var result = await Voice!.SpeakAsync(text, referenceText, reference, sampleRate, maxSpoken,
                    readBack, noiseSeed)
                .ConfigureAwait(false);
            await t.WriteJsonAsync(200, new
            {
                samples = result.Samples,
                sample_rate = result.SampleRate,
                model = result.Model,
                inference_ms = result.InferenceMs,
                duration_seconds = result.DurationSeconds,
                // ⚠️ Carried back because the engine runs in a SHARED WORKER, whose console is not the page
                // console - a Console.WriteLine there is invisible to the window, to DevTools on the page,
                // and to a Playwright gate. The transcribe route already does this for the same reason.
                decoder_ms = result.DecoderMs,
                decoder_first_step_ms = result.DecoderFirstStepMs,
                capture_status = result.CaptureStatus,
            });
        }
        catch (Exception ex)
        {
            // Same reasoning as ApiTranscribe: report the failure WITH frames. Silence is
            // indistinguishable from a working model that produced nothing, and this runs in a worker
            // whose exceptions are not otherwise visible.
            await t.WriteJsonAsync(500, new
            {
                error = $"{ex.GetType().Name}: {ex.Message}",
                detail = ex.ToString().Length > 2000 ? ex.ToString()[..2000] : ex.ToString(),
            });
        }
    }

    private static string FinishReason(AiChatResult r) => r.Stop == AiStopKind.Length ? "length" : "stop";
    private static string OllamaDone(AiChatResult r) => r.Stop == AiStopKind.Length ? "length" : "stop";
    private static string AnthropicStop(AiChatResult r) => r.Stop == AiStopKind.Length ? "max_tokens" : "end_turn";
}
