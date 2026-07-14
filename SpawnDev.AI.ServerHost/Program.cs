// SpawnDev.AI.ServerHost - the thin desktop host: Kestrel on :11434 (Ollama drop-in), every request
// routed to the transport-free AiApiRouter. The SAME router runs in a browser worker over a
// MessagePort transport - this file is only the HTTP skin + accelerator selection.
using System.Text.Json;

using ILGPU.Runtime;
using SpawnDev.AI;
using SpawnDev.AI.Server;

int port = int.TryParse(Environment.GetEnvironmentVariable("SPAWNDEV_AI_PORT"), out var p) && p > 0 ? p : 11434;

// Diagnostic: `probe-intent` runs the model-free image-intent detector battery (false pos/neg). No GPU.
if (args.Length > 0 && args[0] == "probe-intent")
{
    Environment.ExitCode = ProbeHub.CheckIntent();
    return;
}

// Diagnostic: `probe-github-tool` exercises the GitHub tool directly (list/read/file/errors). No GPU.
if (args.Length > 0 && args[0] == "probe-github-tool")
{
    Environment.ExitCode = await ProbeHub.CheckGitHubToolAsync();
    return;
}

// Accelerator: CUDA > OpenCL > CPU (desktop host; the browser host uses WebGPU).
var context = SpawnDev.ILGPU.ML.MLContext.Create().ToContext();
Accelerator accelerator =
    context.Devices.FirstOrDefault(d => d.AcceleratorType == AcceleratorType.Cuda)?.CreateAccelerator(context)
    ?? context.Devices.FirstOrDefault(d => d.AcceleratorType == AcceleratorType.OpenCL)?.CreateAccelerator(context)
    ?? context.Devices.First(d => d.AcceleratorType == AcceleratorType.CPU).CreateAccelerator(context);
Console.WriteLine($"[SpawnDev.AI] accelerator: {accelerator.Name} ({accelerator.AcceleratorType})");

// ── Diagnostic: `probe-hub [modelName]` loads the EXACT HuggingFace GGUF the BROWSER worker uses (via
// HubModelProvider) and runs the image tool-calling probe, isolating tool-CALLING (does the model emit a
// <tool_call>?) from image generation with a stub tool. Answers whether the browser's HF GGUF refuses
// tool calls where the Ollama-cached GGUF (normal host path) succeeds. Kept as a repeatable diagnostic.
if (args.Length > 0 && args[0] == "probe-hub")
{
    await ProbeHub.RunAsync(accelerator, args.Length > 1 ? args[1] : "qwen2.5:0.5b-instruct-q8_0");
    return;
}

// Diagnostic: `probe-github [model]` measures whether the model CALLS github_lookup for library questions.
if (args.Length > 0 && args[0] == "probe-github")
{
    await ProbeHub.RunGitHubAsync(accelerator, args.Length > 1 ? args[1] : "qwen2.5:0.5b-instruct-q8_0");
    return;
}

// Diagnostic: `probe-gen <ollama-model>` loads a cached model through our pipeline + generates (arch check).
if (args.Length > 0 && args[0] == "probe-gen")
{
    await ProbeHub.RunGenAsync(accelerator, args.Length > 1 ? args[1] : "qwen3:14b-q4_K_M");
    return;
}

// Diagnostic: `probe-native <ollama-model>` measures NATIVE tool routing (forcing+grounding OFF) from the
// Ollama cache - sweep any cached model (qwen2.5, qwen3, ...) to see which tier can drop the compensations.
if (args.Length > 0 && args[0] == "probe-native")
{
    await ProbeHub.RunNativeAsync(accelerator, args.Length > 1 ? args[1] : "qwen2.5:1.5b-instruct-q4_K_M");
    return;
}

var store = new OllamaModelStore();
Console.WriteLine($"[SpawnDev.AI] Ollama cache: {OllamaModelStore.DefaultRoot()} (exists: {store.CacheExists}, models: {store.List().Count})");
await using var registry = new ModelRegistry(new OllamaCacheModelProvider(store), accelerator);
var engine = new AiChatEngine(registry)
{
    PerfLog = line => Console.WriteLine($"[perf] {line}"),
};

// Image generation: hub-streamed (WebTorrent + HF CDN), its own residency slot beside the LLM.
// The generate_image tool lets any chatting model produce images; /v1/images/generations serves
// DALL-E-compatible clients directly.
await using var webTorrent = new SpawnDev.WebTorrent.WebTorrentClient();
using var imageHttp = new HttpClient();
using var images = new AiImageEngine(webTorrent, imageHttp, accelerator)
{
    OnLoadProgress = (stage, pct) => { if (pct % 25 == 0) Console.WriteLine($"[image-load] {stage} {pct}%"); },
};
var tools = new AiToolRegistry();
tools.Register(new GenerateImageTool(images, tools));
tools.Register(new GitHubTool(imageHttp));   // SpawnDev library/crew Q&A via GitHub (allowlisted hosts)
engine.Tools = tools;

var router = new AiApiRouter(engine) { Images = images, Tools = tools };

var builder = WebApplication.CreateBuilder(args);
builder.Logging.SetMinimumLevel(LogLevel.Warning);
builder.WebHost.ConfigureKestrel(k => k.ListenAnyIP(port));
var app = builder.Build();

app.Run(async ctx =>
{
    JsonElement? body = null;
    if (ctx.Request.ContentLength is > 0 || ctx.Request.Headers.TransferEncoding.Count > 0)
    {
        try
        {
            using var doc = await JsonDocument.ParseAsync(ctx.Request.Body, default, ctx.RequestAborted);
            body = doc.RootElement.Clone();
        }
        catch (JsonException) { /* non-JSON body - routes treat it as empty */ }
    }
    var transport = new HttpAiServerTransport(ctx);
    bool handled;
    try
    {
        handled = await router.TryHandleAsync(ctx.Request.Method, ctx.Request.Path.Value ?? "/", body, transport);
    }
    catch (FileNotFoundException ex) { await transport.WriteJsonAsync(404, new { error = ex.Message }); return; }
    catch (OperationCanceledException) { return; }   // client went away - generation aborted, GPU freed
    if (!handled)
        await transport.WriteJsonAsync(404, new { error = $"unknown route {ctx.Request.Method} {ctx.Request.Path}" });
});

Console.WriteLine($"[SpawnDev.AI] Ollama-compatible server listening on http://localhost:{port}");
await app.RunAsync();

/// <summary>The HTTP skin of <see cref="IAiServerTransport"/>: JSON responses, SSE, and NDJSON over
/// an ASP.NET <see cref="HttpContext"/> - flushed per write (streaming clients time out on silence).</summary>
sealed class HttpAiServerTransport : IAiServerTransport
{
    private static readonly JsonSerializerOptions J = new(JsonSerializerDefaults.Web);
    private readonly HttpContext _ctx;
    public HttpAiServerTransport(HttpContext ctx) => _ctx = ctx;

    public CancellationToken Aborted => _ctx.RequestAborted;

    public async Task WriteJsonAsync(int statusCode, object payload)
    {
        _ctx.Response.StatusCode = statusCode;
        _ctx.Response.ContentType = "application/json";
        await _ctx.Response.WriteAsync(JsonSerializer.Serialize(payload, J), Aborted);
    }

    public async Task WriteTextAsync(int statusCode, string text)
    {
        _ctx.Response.StatusCode = statusCode;
        _ctx.Response.ContentType = "text/plain";
        await _ctx.Response.WriteAsync(text, Aborted);
    }

    public async Task StartEventStreamAsync(AiEventStreamKind kind)
    {
        _ctx.Response.ContentType = kind == AiEventStreamKind.Sse ? "text/event-stream" : "application/x-ndjson";
        if (kind == AiEventStreamKind.Sse) _ctx.Response.Headers.CacheControl = "no-cache";
        _kind = kind;
        await _ctx.Response.Body.FlushAsync(Aborted);
    }
    private AiEventStreamKind _kind;

    public async Task WriteEventAsync(string? eventName, object payload)
    {
        string json = JsonSerializer.Serialize(payload, J);
        string frame = _kind == AiEventStreamKind.Sse
            ? (eventName != null ? $"event: {eventName}\ndata: {json}\n\n" : $"data: {json}\n\n")
            : json + "\n";
        await _ctx.Response.WriteAsync(frame, Aborted);
        await _ctx.Response.Body.FlushAsync(Aborted);
    }

    public async Task WriteRawAsync(string text)
    {
        await _ctx.Response.WriteAsync(text, Aborted);
        await _ctx.Response.Body.FlushAsync(Aborted);
    }
}
