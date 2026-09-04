// Minimal static server for a PUBLISHED Blazor WASM app, with the SAME headers
// PlaywrightMultiTest.StaticFileServer sets - so a number measured here and a number measured in PMT
// are comparable. COEP/COOP are required for SharedArrayBuffer (WebWorkers/threads).
//   dotnet run serve-static.cs -- <wwwroot> <port>
using System.Net;

var root = args.Length > 0 ? args[0] : ".";
var port = args.Length > 1 ? int.Parse(args[1]) : 5299;
var listener = new HttpListener();
listener.Prefixes.Add($"http://localhost:{port}/");
listener.Start();
Console.WriteLine($"serving {root} on http://localhost:{port}/");

var mime = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
{
    [".html"] = "text/html", [".htm"] = "text/html", [".js"] = "text/javascript",
    [".mjs"] = "text/javascript", [".css"] = "text/css", [".json"] = "application/json",
    [".wasm"] = "application/wasm", [".dll"] = "application/octet-stream",
    [".pdb"] = "application/octet-stream", [".dat"] = "application/octet-stream",
    [".blat"] = "application/octet-stream", [".woff"] = "font/woff", [".woff2"] = "font/woff2",
    [".png"] = "image/png", [".jpg"] = "image/jpeg", [".svg"] = "image/svg+xml",
    [".ico"] = "image/x-icon", [".wav"] = "audio/wav", [".mp3"] = "audio/mpeg",
    [".onnx"] = "application/octet-stream", [".txt"] = "text/plain", [".md"] = "text/markdown",
    [".br"] = "application/octet-stream", [".gz"] = "application/octet-stream",
};

while (true)
{
    HttpListenerContext ctx;
    try { ctx = await listener.GetContextAsync(); } catch { break; }
    _ = Task.Run(async () =>
    {
        try
        {
            var rel = Uri.UnescapeDataString(ctx.Request.Url!.AbsolutePath).TrimStart('/');
            if (rel.Length == 0) rel = "index.html";
            var path = Path.GetFullPath(Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar)));
            // SPA fallback: an unknown path with no extension is a route, not a file.
            if (!File.Exists(path) && !Path.HasExtension(path))
                path = Path.GetFullPath(Path.Combine(root, "index.html"));
            ctx.Response.Headers["Cross-Origin-Embedder-Policy"] = "credentialless";
            ctx.Response.Headers["Cross-Origin-Opener-Policy"] = "same-origin";
            ctx.Response.Headers["Cache-Control"] = "no-cache";
            if (!File.Exists(path)) { ctx.Response.StatusCode = 404; ctx.Response.Close(); return; }
            ctx.Response.ContentType = mime.TryGetValue(Path.GetExtension(path), out var m) ? m : "application/octet-stream";
            var bytes = await File.ReadAllBytesAsync(path);
            ctx.Response.ContentLength64 = bytes.Length;
            await ctx.Response.OutputStream.WriteAsync(bytes);
            ctx.Response.Close();
        }
        catch { try { ctx.Response.Abort(); } catch { } }
    });
}
