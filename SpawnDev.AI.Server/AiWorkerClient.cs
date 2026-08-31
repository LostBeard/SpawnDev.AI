using System.Text.Json;
using SpawnDev.SpawnJS.WebWorkers;

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

    /// <summary>False forces a DEDICATED worker even when SharedWorker is supported. Diagnostic +
    /// mitigation switch: the model-piece download loop reproduced ONLY under SharedWorker
    /// (2026-07-04, same client/OPFS store works on the main thread and desktop).</summary>
    public bool PreferSharedWorker { get; set; } = true;

    /// <summary>Attach the worker (shared preferred, dedicated fallback) and warm the server.</summary>
    public async Task<string> InitAsync()
    {
        await _initGate.WaitAsync();
        try
        {
            if (_worker == null)
            {
                if (PreferSharedWorker && _workers.SharedWebWorkerSupported)
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
    /// <summary>Fetch a tool artifact (generated image) by id: (mimeType, bytes, label).</summary>
    public async Task<(string Mime, byte[] Data, string? Label)> GetArtifactAsync(string id)
    {
        var json = await RequestJsonAsync("GET", $"/ai/artifacts/{id}");
        using var doc = JsonDocument.Parse(json);
        return (doc.RootElement.GetProperty("mime").GetString() ?? "application/octet-stream",
                Convert.FromBase64String(doc.RootElement.GetProperty("b64").GetString() ?? ""),
                doc.RootElement.TryGetProperty("label", out var l) ? l.GetString() : null);
    }

    /// <summary>List the worker's image models: (defaultName, [(name, note)]).</summary>
    public async Task<(string Default, List<(string Name, string Note)> Models)> ListImageModelsAsync()
    {
        var json = await RequestJsonAsync("GET", "/ai/image-models");
        using var doc = JsonDocument.Parse(json);
        var list = doc.RootElement.GetProperty("models").EnumerateArray()
            .Select(m => (m.GetProperty("name").GetString() ?? "", m.TryGetProperty("note", out var nn) ? nn.GetString() ?? "" : ""))
            .ToList();
        return (doc.RootElement.GetProperty("default").GetString() ?? "", list);
    }

    /// <summary>
    /// Transcribe mono PCM through the worker's speech engine.
    /// </summary>
    /// <remarks>
    /// ⚠️ Sends the samples as a JSON number array, which matches <c>/api/transcribe</c>'s first cut and is
    /// the WRONG shape for long audio: 30 s at 16 kHz is 480,000 numbers, and JSON-encoding that pulls bulk
    /// audio through the .NET heap. Fine for an utterance; the follow-up is a transferred Float32Array over
    /// the worker port. The signature does not change when that lands.
    /// </remarks>
    /// <param name="samples">Mono PCM in [-1, 1].</param>
    /// <param name="sampleRate">Sample rate of <paramref name="samples"/>.</param>
    /// <returns>The transcript, the model that produced it, and inference milliseconds.</returns>
    public async Task<(string Text, string Model, double InferenceMs)> TranscribeAsync(
        float[] samples, int sampleRate)
    {
        var body = JsonSerializer.Serialize(new { samples, sample_rate = sampleRate }, J);
        var json = await RequestJsonAsync("POST", "/api/transcribe", body);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        return (
            root.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "",
            root.TryGetProperty("model", out var m) ? m.GetString() ?? "" : "",
            root.TryGetProperty("inference_ms", out var ms) ? ms.GetDouble() : 0);
    }

    /// <summary>
    /// Speak <paramref name="text"/> in the voice of a reference clip. Returns mono PCM.
    /// </summary>
    /// <remarks>
    /// ⚠️ <paramref name="referenceSamples"/> is required - this voice is CLONED, so in a conversation the
    /// reference is the turn the user just spoke and the assistant answers in their own voice. There is no
    /// stock voice to fall back to.
    /// ⚠️ Same first-cut shape as <see cref="TranscribeAsync"/>: PCM crosses as a JSON number array, which
    /// is what works over both transports today and is the wrong shape for audio. A transferred
    /// Float32Array is the follow-up; this signature does not change when it lands.
    /// </remarks>
    public async Task<(float[] Samples, int SampleRate, string Model, double InferenceMs)> SpeakAsync(
        string text, string referenceText, float[] referenceSamples, int referenceSampleRate)
    {
        var body = JsonSerializer.Serialize(new
        {
            text,
            reference_text = referenceText,
            reference_samples = referenceSamples,
            sample_rate = referenceSampleRate,
        }, J);
        var json = await RequestJsonAsync("POST", "/api/speak", body);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.TryGetProperty("error", out var err))
            throw new Exception(err.GetString() ?? "speak failed");
        var arr = root.GetProperty("samples");
        var samples = new float[arr.GetArrayLength()];
        var i = 0;
        foreach (var v in arr.EnumerateArray()) samples[i++] = (float)v.GetDouble();
        return (
            samples,
            root.TryGetProperty("sample_rate", out var sr) ? sr.GetInt32() : 24000,
            root.TryGetProperty("model", out var m) ? m.GetString() ?? "" : "",
            root.TryGetProperty("inference_ms", out var ms) ? ms.GetDouble() : 0);
    }

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
                repeat_penalty = options.RepetitionPenalty != 1.0f ? options.RepetitionPenalty : (float?)null,
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
