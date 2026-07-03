// SpawnDev.AI.ServerHost - the thin desktop host: Kestrel on :11434 (Ollama drop-in), every request
// routed to the transport-free AiApiRouter. The SAME router runs in a browser worker over a
// MessagePort transport - this file is only the HTTP skin + accelerator selection.
using System.Text.Json;

using ILGPU.Runtime;
using SpawnDev.AI;
using SpawnDev.AI.Server;

int port = int.TryParse(Environment.GetEnvironmentVariable("SPAWNDEV_AI_PORT"), out var p) && p > 0 ? p : 11434;

// Accelerator: CUDA > OpenCL > CPU (desktop host; the browser host uses WebGPU).
var context = SpawnDev.ILGPU.ML.MLContext.Create().ToContext();
Accelerator accelerator =
    context.Devices.FirstOrDefault(d => d.AcceleratorType == AcceleratorType.Cuda)?.CreateAccelerator(context)
    ?? context.Devices.FirstOrDefault(d => d.AcceleratorType == AcceleratorType.OpenCL)?.CreateAccelerator(context)
    ?? context.Devices.First(d => d.AcceleratorType == AcceleratorType.CPU).CreateAccelerator(context);
Console.WriteLine($"[SpawnDev.AI] accelerator: {accelerator.Name} ({accelerator.AcceleratorType})");

var store = new OllamaModelStore();
Console.WriteLine($"[SpawnDev.AI] Ollama cache: {OllamaModelStore.DefaultRoot()} (exists: {store.CacheExists}, models: {store.List().Count})");
await using var registry = new ModelRegistry(store, accelerator);
var engine = new AiChatEngine(registry)
{
    PerfLog = line => Console.WriteLine($"[perf] {line}"),
};
var router = new AiApiRouter(engine);

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
