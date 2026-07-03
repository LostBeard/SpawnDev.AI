using System.Text.Json;
using SpawnDev.BlazorJS.WebWorkers;

namespace SpawnDev.AI.Server;

/// <summary>
/// The window-side handle to the in-browser AI server: starts (or attaches to) the worker hosting
/// <see cref="AiWorkerServer"/> - SHARED worker when the browser supports it, so every tab talks to
/// the ONE resident model and the decode-capture warmup amortizes across the app - and speaks the
/// same protocol surface as an Ollama HTTP endpoint, over marshalled callback frames.
/// </summary>
public sealed class AiWorkerClient
{
    private static readonly JsonSerializerOptions J = new(JsonSerializerDefaults.Web);
    private readonly WebWorkerService _workers;
    private AsyncCallDispatcher? _worker;
    private readonly SemaphoreSlim _initGate = new(1, 1);

    /// <summary>Shared-worker name (tabs attaching the same name share the server instance).</summary>
    public string SharedWorkerName { get; set; } = "SpawnDevAI";

    public AiWorkerClient(WebWorkerService workers) => _workers = workers;

    /// <summary>True once the worker is attached and its GPU/registry reported ready.</summary>
    public bool Ready { get; private set; }

    /// <summary>Worker status line from the last <see cref="InitAsync"/>.</summary>
    public string Status { get; private set; } = "";

    /// <summary>Attach the worker (shared preferred, dedicated fallback) and warm the server.</summary>
    public async Task<string> InitAsync()
    {
        await _initGate.WaitAsync();
        try
        {
            if (_worker == null)
            {
                if (_workers.SharedWebWorkerSupported)
                {
                    var shared = await _workers.GetSharedWebWorker(SharedWorkerName);
                    _worker = shared;
                }
                else
                {
                    var dedicated = await _workers.GetWebWorker()
                        ?? throw new NotSupportedException("Web workers are not available in this browser.");
                    _worker = dedicated;
                }
            }
            Status = await _worker.Run<IAiWorkerApi, string>(s => s.GetStatusAsync());
            Ready = true;
            return Status;
        }
        finally { _initGate.Release(); }
    }

    /// <summary>
    /// Route one protocol request to the worker server (same method/path/body as the HTTP surface).
    /// <paramref name="onFrame"/> receives every <see cref="AiWireFrame"/>; returns after the
    /// terminal frame. Most callers want <see cref="RequestJsonAsync"/> or <see cref="ChatStreamAsync"/>.
    /// </summary>
    public async Task SendAsync(string method, string path, string? bodyJson, Action<AiWireFrame> onFrame)
    {
        if (_worker == null) await InitAsync();
        await _worker!.Run<IAiWorkerApi>(s => s.HandleRequestAsync(method, path, bodyJson,
            new Action<string>(frameJson => onFrame(AiWireFrame.FromJson(frameJson)))));
    }

    /// <summary>Buffered JSON request: returns the response body, throws on protocol error status.</summary>
    public async Task<string> RequestJsonAsync(string method, string path, string? bodyJson = null)
    {
        string? result = null; int status = 0;
        await SendAsync(method, path, bodyJson, f =>
        {
            if (f.T is "json" or "text" or "error") { result = f.Data; status = f.Status; }
        });
        if (status is not (>= 200 and < 300))
            throw new HttpRequestException($"{method} {path} -> {status}: {result}");
        return result ?? "";
    }

    /// <summary>
    /// Chat with streaming deltas over the Ollama-native surface (/api/chat NDJSON): builds the
    /// request, streams <c>message.content</c> deltas to <paramref name="onDelta"/>, returns the
    /// final done_reason ("stop" | "length").
    /// </summary>
    public async Task<string> ChatStreamAsync(string model, IReadOnlyList<AiChatMessage> messages,
        AiGenerationOptions? options = null, Action<string>? onDelta = null)
    {
        options ??= new AiGenerationOptions();
        var body = JsonSerializer.Serialize(new
        {
            model,
            messages = messages.Select(m => new { role = m.Role, content = m.Content }),
            stream = true,
            options = new
            {
                num_predict = options.MaxOutputTokens,
                temperature = options.Temperature,
                top_p = options.TopP,
                top_k = options.Strategy == "top_k" ? options.TopK : (int?)null,
                seed = options.Seed,
            },
        }, J);

        string doneReason = "stop"; string? error = null;
        await SendAsync("POST", "/api/chat", body, f =>
        {
            switch (f.T)
            {
                case "event" when f.Data != null:
                    using (var doc = JsonDocument.Parse(f.Data))
                    {
                        var root = doc.RootElement;
                        if (root.TryGetProperty("message", out var msg)
                            && msg.TryGetProperty("content", out var c)
                            && c.GetString() is { Length: > 0 } delta)
                            onDelta?.Invoke(delta);
                        if (root.TryGetProperty("done", out var d) && d.ValueKind == JsonValueKind.True
                            && root.TryGetProperty("done_reason", out var dr))
                            doneReason = dr.GetString() ?? "stop";
                    }
                    break;
                case "json" or "error" when f.Status is not (>= 200 and < 300) && f.Status != 0:
                    error = f.Data;
                    break;
            }
        });
        if (error != null) throw new HttpRequestException($"/api/chat: {error}");
        return doneReason;
    }
}
