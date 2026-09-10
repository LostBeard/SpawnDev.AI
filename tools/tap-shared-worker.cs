// Stream a SHARED WORKER's console and exceptions - the logs that never reach the page.
//
// 🔴 WHY THIS EXISTS. Sync access handles are DEDICATED-worker only, so SpawnDev.WebTorrent's
// createWritable/Blob fallback runs exactly where a SHARED worker runs - and a shared worker's console
// does not reach page.Console, so every Playwright gate we have passes `?worker=dedicated` simply to be
// able to read anything. That means the gates systematically select the configuration that HIDES any
// shared-worker-only defect. Two real ones lived there undetected (a content file sized by how much had
// arrived rather than its true length, and a cached Blob snapshot going stale mid-read) while every gate
// was green.
//
// CDP lists shared workers as their own targets, each with a webSocketDebuggerUrl, so their console is
// readable after all - it just needs asking for directly.
//
//   1. start Chrome with --remote-debugging-port=9222 and open the app
//   2. dotnet run tools/tap-shared-worker.cs [-- http://localhost:9222] [seconds]
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

var endpoint = (args.FirstOrDefault(a => a.StartsWith("http")) ?? "http://localhost:9222").TrimEnd('/');
var seconds = args.Where(a => int.TryParse(a, out _)).Select(int.Parse).FirstOrDefault();
if (seconds <= 0) seconds = 180;

using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
var listJson = await http.GetStringAsync($"{endpoint}/json/list");
using var doc = JsonDocument.Parse(listJson);
var workers = doc.RootElement.EnumerateArray()
    .Where(t => t.GetProperty("type").GetString() == "shared_worker")
    .Select(t => (Url: t.GetProperty("url").GetString() ?? "",
                  Ws: t.GetProperty("webSocketDebuggerUrl").GetString() ?? ""))
    .Where(t => t.Ws.Length > 0).ToList();

if (workers.Count == 0)
{
    Console.WriteLine("no shared_worker target found. Is the app open, and started (the worker is created "
        + "by 'Start the AI server')? Targets seen:");
    foreach (var t in doc.RootElement.EnumerateArray())
        Console.WriteLine($"  {t.GetProperty("type").GetString()}  {t.GetProperty("url").GetString()}");
    return 1;
}

foreach (var w in workers) Console.WriteLine($"[tap] shared worker: {w.Url}");
var target = workers[0];

using var ws = new ClientWebSocket();
await ws.ConnectAsync(new Uri(target.Ws), CancellationToken.None);
Console.WriteLine("[tap] attached; streaming console + exceptions. Ctrl-C to stop.");

var id = 0;
async Task Send(string method)
{
    var msg = "{\"id\":" + (++id) + ",\"method\":\"" + method + "\"}";
    await ws.SendAsync(Encoding.UTF8.GetBytes(msg), WebSocketMessageType.Text, true, CancellationToken.None);
}
// Runtime carries console.* ; Log carries browser-level entries (network failures, worker errors) that
// Runtime does not - a stalled download shows up in the second, not the first.
await Send("Runtime.enable");
await Send("Log.enable");

using var life = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
var deadline = DateTime.UtcNow.AddSeconds(seconds);
var buf = new byte[1 << 20];
var sb = new StringBuilder();
while (ws.State == WebSocketState.Open && DateTime.UtcNow < deadline)
{
    // ⚠️ ONE token for the whole session, cancelled at the deadline. Cancelling an individual
    // ReceiveAsync ABORTS the WebSocket - it does not merely time out - so a per-read timeout killed the
    // tap on the first quiet moment, which is exactly when watching a stalled worker matters most. That
    // cost two runs before I noticed the tool was the thing failing, not the subject.
    sb.Clear();
    var closed = false;
    while (true)
    {
        WebSocketReceiveResult r;
        try { r = await ws.ReceiveAsync(buf, life.Token); }
        catch (OperationCanceledException) { closed = true; break; }
        catch (WebSocketException) { closed = true; break; }
        if (r.MessageType == WebSocketMessageType.Close) { closed = true; break; }
        sb.Append(Encoding.UTF8.GetString(buf, 0, r.Count));
        if (r.EndOfMessage) break;
    }
    if (closed) break;
    if (sb.Length == 0) continue;

    using var ev = JsonDocument.Parse(sb.ToString());
    if (!ev.RootElement.TryGetProperty("method", out var m)) continue;
    var method = m.GetString();
    var p = ev.RootElement.GetProperty("params");
    var stamp = DateTime.Now.ToString("HH:mm:ss");
    if (method == "Runtime.consoleAPICalled")
    {
        var type = p.GetProperty("type").GetString();
        var parts = p.TryGetProperty("args", out var a)
            ? a.EnumerateArray().Select(x =>
                x.TryGetProperty("value", out var v) ? v.ToString()
                : x.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "")
            : Enumerable.Empty<string>();
        Console.WriteLine($"  {stamp} [{type}] {string.Join(" ", parts)}");
    }
    else if (method == "Runtime.exceptionThrown")
    {
        var det = p.GetProperty("exceptionDetails");
        var text = det.TryGetProperty("exception", out var ex) && ex.TryGetProperty("description", out var d2)
            ? d2.GetString() : det.GetProperty("text").GetString();
        Console.WriteLine($"  {stamp} 🔴 EXCEPTION {text}");
    }
    else if (method == "Log.entryAdded")
    {
        var e = p.GetProperty("entry");
        Console.WriteLine($"  {stamp} [log:{e.GetProperty("level").GetString()}] "
            + $"{e.GetProperty("text").GetString()} {(e.TryGetProperty("url", out var u) ? u.GetString() : "")}");
    }
}
Console.WriteLine("[tap] done");
return 0;
