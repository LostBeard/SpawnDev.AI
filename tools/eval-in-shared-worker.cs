// Evaluate an expression INSIDE a shared worker over CDP and print the result.
//
// 🔴 WHY: a shared worker is the configuration a normal visitor gets, and it is the one we cannot see
// into - no page.Console, no Playwright handle. But CDP lists shared workers as their own targets, so
// they can be both READ (tap-shared-worker.cs) and DRIVEN. That makes it possible to instrument the real
// thing without a rebuild: wrap Blob.prototype.arrayBuffer, count the calls and the bytes, and read the
// counters back a minute later. Measuring beats guessing about where ten minutes went.
//
//   dotnet run tools/eval-in-shared-worker.cs -- <endpoint> "<js expression>"
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

var endpoint = (args.FirstOrDefault(a => a.StartsWith("http")) ?? "http://localhost:9222").TrimEnd('/');
var expr = args.LastOrDefault(a => !a.StartsWith("http")) ?? "1+1";

using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
using var doc = JsonDocument.Parse(await http.GetStringAsync($"{endpoint}/json/list"));
var w = doc.RootElement.EnumerateArray()
    .Where(t => t.GetProperty("type").GetString() == "shared_worker")
    .Select(t => t.GetProperty("webSocketDebuggerUrl").GetString() ?? "")
    .FirstOrDefault(u => u.Length > 0);
if (w == null) { Console.WriteLine("no shared_worker target"); return 1; }

using var ws = new ClientWebSocket();
await ws.ConnectAsync(new Uri(w), CancellationToken.None);

// Escape the expression into a JSON string by hand - file-based apps run with reflection-based
// serialization disabled, and the frame shape is fixed.
// Base64 sidesteps JSON escaping entirely: the payload then contains no quote, backslash or control
// character, so the frame can be assembled as a plain string with nothing to get wrong.
var b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(expr));
var frame = "{" + Q("id") + ":1," + Q("method") + ":" + Q("Runtime.evaluate") + "," + Q("params") + ":{"
          + Q("expression") + ":" + Q("eval(atob(SQ" + b64 + "SQ))") + "," + Q("awaitPromise") + ":true,"
          + Q("returnByValue") + ":true}}";
frame = frame.Replace("SQ", "'");
static string Q(string s) => "\u0022" + s + "\u0022";
await ws.SendAsync(Encoding.UTF8.GetBytes(frame), WebSocketMessageType.Text, true, CancellationToken.None);

var buf = new byte[1 << 20];
var sb = new StringBuilder();
var deadline = DateTime.UtcNow.AddSeconds(30);
while (ws.State == WebSocketState.Open && DateTime.UtcNow < deadline)
{
    sb.Clear();
    while (true)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        WebSocketReceiveResult r;
        try { r = await ws.ReceiveAsync(buf, cts.Token); } catch (OperationCanceledException) { break; }
        sb.Append(Encoding.UTF8.GetString(buf, 0, r.Count));
        if (r.EndOfMessage) break;
    }
    if (sb.Length == 0) continue;
    using var ev = JsonDocument.Parse(sb.ToString());
    if (!ev.RootElement.TryGetProperty("id", out var idEl) || idEl.GetInt32() != 1) continue;
    var res = ev.RootElement.GetProperty("result");
    if (res.TryGetProperty("exceptionDetails", out var ex))
    { Console.WriteLine("EXCEPTION: " + ex.GetProperty("text").GetString()); return 1; }
    var val = res.GetProperty("result");
    Console.WriteLine(val.TryGetProperty("value", out var v) ? v.ToString()
        : val.TryGetProperty("description", out var d) ? d.GetString() : val.ToString());
    return 0;
}
Console.WriteLine("no reply");
return 1;
