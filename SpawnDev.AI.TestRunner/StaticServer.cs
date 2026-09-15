using System.Net;

/// <summary>
/// A static file server for the published app, with the SAME headers PlaywrightMultiTest sets.
/// </summary>
/// <remarks>
/// ⚠️ COOP/COEP are not optional: SharedArrayBuffer is gated behind cross-origin isolation, and the
/// WebWorkers transport needs it. Serve without them and the app boots and then fails in a way that looks
/// nothing like a missing header.
/// </remarks>
internal static class StaticServer
{
    private static readonly Dictionary<string, string> Mime = new(StringComparer.OrdinalIgnoreCase)
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

    public static void Start(string root, int port)
    {
        var listener = new HttpListener();
        listener.Prefixes.Add($"http://localhost:{port}/");
        listener.Start();
        _ = Task.Run(async () =>
        {
            while (listener.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = await listener.GetContextAsync(); } catch { break; }
                _ = Task.Run(() => Serve(ctx, root));
            }
        });
    }

    private static void Serve(HttpListenerContext ctx, string root)
    {
        try
        {
            var rel = Uri.UnescapeDataString(ctx.Request.Url?.AbsolutePath ?? "/").TrimStart('/');
            if (rel.Length == 0) rel = "index.html";
            var path = Path.GetFullPath(Path.Combine(root, rel));
            // Never serve outside the published root, whatever the request says.
            if (!path.StartsWith(Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase)
                || !File.Exists(path))
            {
                // A Blazor SPA route is not a missing file - fall back to the document.
                path = Path.Combine(root, "index.html");
                if (!File.Exists(path)) { ctx.Response.StatusCode = 404; ctx.Response.Close(); return; }
            }
            ctx.Response.Headers["Cross-Origin-Opener-Policy"] = "same-origin";
            ctx.Response.Headers["Cross-Origin-Embedder-Policy"] = "require-corp";
            ctx.Response.Headers["Cross-Origin-Resource-Policy"] = "cross-origin";
            ctx.Response.ContentType = Mime.TryGetValue(Path.GetExtension(path), out var m)
                ? m : "application/octet-stream";
            var bytes = File.ReadAllBytes(path);
            ctx.Response.ContentLength64 = bytes.Length;
            ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
        }
        catch { try { ctx.Response.StatusCode = 500; } catch { } }
        finally { try { ctx.Response.Close(); } catch { } }
    }
}
