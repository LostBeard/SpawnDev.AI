using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using SpawnDev.AI;
using SpawnDev.AI.Server;
using SpawnDev.BlazorJS.JSObjects;

namespace SpawnDev.AI.Demo.Pages;

public partial class Home
{
    bool _ready, _starting, _busy;
    string _status = "", _busyNote = "";
    string _model = "qwen2.5:0.5b-instruct-q8_0";
    string _imageModel = "sd-turbo";
    readonly List<string> _models = new();
    List<(string Name, string Note)> _imageModels = new();

    sealed class ChatImage { public string Url = ""; public string Label = "image"; }
    sealed class Msg
    {
        public string Role = "user";
        public string Text = "";
        public List<ChatImage> Images = new();
        public double Ms; public double TokPerSec; public bool Truncated;
    }
    readonly List<Msg> _messages = new();
    string _input = "", _streaming = "";
    ElementReference _scrollRef;

    [Inject] NavigationManager Nav { get; set; } = default!;

    async Task StartAsync()
    {
        _starting = true;
        // ?worker=dedicated forces a dedicated worker (diagnostic: the piece-download loop
        // reproduced only under SharedWorker, 2026-07-04).
        if (Nav.Uri.Contains("worker=dedicated", StringComparison.OrdinalIgnoreCase))
            Ai.PreferSharedWorker = false;
        _status = Ai.PreferSharedWorker ? "Attaching shared worker, requesting WebGPU…" : "Attaching DEDICATED worker, requesting WebGPU…";
        StateHasChanged();
        try
        {
            _status = await Ai.InitAsync();
            var tags = await Ai.RequestJsonAsync("GET", "/api/tags");
            using (var doc = System.Text.Json.JsonDocument.Parse(tags))
            {
                _models.Clear();
                foreach (var m in doc.RootElement.GetProperty("models").EnumerateArray())
                    _models.Add(m.GetProperty("name").GetString()!);
            }
            if (_models.Count > 0 && !_models.Contains(_model)) _model = _models[0];
            try { var (def, list) = await Ai.ListImageModelsAsync(); _imageModels = list; _imageModel = def; }
            catch { _imageModels = new() { ("sd-turbo", "") }; }
            _ready = true;
            await RefreshStorageAsync();
        }
        catch (Exception ex) { _status = $"Failed: {ex.Message}"; }
        finally { _starting = false; StateHasChanged(); }
    }

    Task SendPreset(string text) { _input = text; return SendAsync(); }

    async Task OnKeyDown(KeyboardEventArgs e)
    { if (e.Key == "Enter" && !e.ShiftKey) await SendAsync(); }

    async Task SendAsync()
    {
        if (_busy || string.IsNullOrWhiteSpace(_input)) return;
        var text = _input.Trim();
        _input = "";

        if (text.StartsWith('/'))
        {
            HandleSlash(text);
            StateHasChanged();
            await ScrollToBottom();
            return;
        }

        _messages.Add(new Msg { Role = "user", Text = text });
        _busy = true; _streaming = "";
        _busyNote = _messages.Count(m => m.Role == "user") == 1
            ? "first message loads the model - downloads once, then cached" : "";
        StateHasChanged();
        await ScrollToBottom();

        // The hard-won render lessons as architecture: 10Hz text renders, 2Hz scroll (forced
        // layout), decode entirely in the worker - the UI thread only receives deltas.
        var renderClock = System.Diagnostics.Stopwatch.StartNew();
        var scrollClock = System.Diagnostics.Stopwatch.StartNew();
        var genClock = System.Diagnostics.Stopwatch.StartNew();
        int deltas = 0;
        try
        {
            // The model has a generate_image tool (injected server-side by the worker). A small model
            // WILL NOT reliably call a tool it is never told it has - the bare "helpful assistant"
            // prompt made qwen2.5-0.5b REFUSE image requests ~60% of the time ("I can't generate images
            // from text"), because nothing told it it could. Naming the tool + scoping it to explicit
            // image intent takes image requests from ~40% -> ~100% called, while the "(and only if)…
            // never call generate_image" clause keeps it from drawing on factual/creative prompts.
            var convo = new List<AiChatMessage> { new("system",
                "You are a helpful assistant running entirely on the user's own GPU in their browser. "
                + "You can both chat and create images. If (and only if) the user explicitly asks you to "
                + "draw, paint, generate, or show a picture, photo, or image, call generate_image with a "
                + "vivid caption. For every other message - questions, facts, math, explanations, stories, "
                + "poems - respond with plain text and never call generate_image.") };
            foreach (var m in _messages.Where(m => m.Role is "user" or "assistant"))
                convo.Add(new AiChatMessage(m.Role, m.Text));

            var doneReason = await Ai.ChatStreamAsync(_model, convo,
                // Temp 0.3 (was 0.7): the 0.5b's tool-routing is a sampling decision - 0.7 let a
                // "refuse" (on image requests) or a spurious "draw" (on factual ones) win off the tail.
                // 0.3 collapses toward the argmax (the correct route) without going fully greedy, keeping
                // some variety in ordinary chat. Image requests: ~100% called; false-draws: ~0.
                new AiGenerationOptions { MaxOutputTokens = 384, Strategy = "top_p", Temperature = 0.3f, TopP = 0.9f, RepetitionPenalty = 1.15f },
                onDelta: delta =>
                {
                    _streaming += delta; deltas++;
                    if (renderClock.ElapsedMilliseconds >= 100)
                    {
                        renderClock.Restart();
                        bool doScroll = scrollClock.ElapsedMilliseconds >= 500;
                        if (doScroll) scrollClock.Restart();
                        InvokeAsync(async () => { StateHasChanged(); if (doScroll) await ScrollToBottom(); });
                    }
                });
            genClock.Stop();

            var msg = new Msg
            {
                Role = "assistant",
                Ms = genClock.Elapsed.TotalMilliseconds,
                TokPerSec = deltas > 1 ? deltas / genClock.Elapsed.TotalSeconds : 0,
                Truncated = doneReason == "length",
            };
            msg.Text = await ResolveArtifactsAsync(_streaming, msg.Images);
            _messages.Add(msg);
            _status = $"last response: {msg.Ms / 1000.0:F1}s · {msg.TokPerSec:F1} tok/s · model {_model}";
            await RefreshStorageAsync();
        }
        catch (Exception ex) { _messages.Add(new Msg { Role = "system", Text = $"Error: {ex.Message}" }); }
        finally
        {
            _streaming = ""; _busy = false; _busyNote = "";
            StateHasChanged();
            await ScrollToBottom();
        }
    }

    // TEMP TEST HOOK (2026-07-05): drive SD-Turbo LOAD+GEN directly via /v1/images/generations, bypassing
    // the LLM (whose tool-call is intermittent on WebGPU). Logs timing to the PAGE console (capturable by
    // the Playwright gate, unlike the worker's WL SUMMARY). REMOVE once load-perf is fixed.
    async Task TestDirectImageAsync()
    {
        if (_busy) return;
        _busy = true; _busyNote = "IMGTEST: direct SD-Turbo (bypassing LLM)…"; StateHasChanged(); await ScrollToBottom();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var reqBody = System.Text.Json.JsonSerializer.Serialize(new { prompt = "a lighthouse in a storm", seed = 42 });
            var resp = await Ai.RequestJsonAsync("POST", "/v1/images/generations", reqBody);
            sw.Stop();
            Console.WriteLine($"IMGTEST: direct SD-Turbo load+gen = {sw.Elapsed.TotalSeconds:F1}s");
            using var doc = System.Text.Json.JsonDocument.Parse(resp);
            var b64 = doc.RootElement.GetProperty("data")[0].GetProperty("b64_json").GetString();
            var bytes = Convert.FromBase64String(b64!);
            using var blob = new Blob(new[] { bytes }, new BlobOptions { Type = "image/png" });
            var msg = new Msg { Role = "system", Text = $"IMGTEST {sw.Elapsed.TotalSeconds:F1}s" };
            msg.Images.Add(new ChatImage { Url = URL.CreateObjectURL(blob), Label = "imgtest" });
            _messages.Add(msg);
        }
        catch (Exception ex) { Console.WriteLine($"IMGTEST FAILED: {ex.GetType().Name}: {ex.Message}"); _messages.Add(new Msg { Role = "system", Text = $"IMGTEST error: {ex.Message}" }); }
        finally { _busy = false; _busyNote = ""; StateHasChanged(); await ScrollToBottom(); }
    }

    // Generate an image DIRECTLY from the user's typed prompt (bypass the LLM), via /v1/images/generations.
    // The 🔬 button above uses a fixed prompt for a deterministic smoke test; this uses whatever's typed.
    async Task DirectImageFromPromptAsync()
    {
        if (_busy) return;
        var prompt = (_input ?? "").Trim();
        if (prompt.Length == 0) return;
        _input = "";
        _messages.Add(new Msg { Role = "user", Text = prompt });
        _busy = true; _busyNote = "Generating image (direct SD-Turbo, bypassing LLM)…"; StateHasChanged(); await ScrollToBottom();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var reqBody = System.Text.Json.JsonSerializer.Serialize(new { prompt });
            var resp = await Ai.RequestJsonAsync("POST", "/v1/images/generations", reqBody);
            sw.Stop();
            Console.WriteLine($"Direct image gen = {sw.Elapsed.TotalSeconds:F1}s (prompt: {prompt})");
            using var doc = System.Text.Json.JsonDocument.Parse(resp);
            var b64 = doc.RootElement.GetProperty("data")[0].GetProperty("b64_json").GetString();
            var bytes = Convert.FromBase64String(b64!);
            using var blob = new Blob(new[] { bytes }, new BlobOptions { Type = "image/png" });
            var msg = new Msg { Role = "system", Text = $"image {sw.Elapsed.TotalSeconds:F1}s" };
            msg.Images.Add(new ChatImage { Url = URL.CreateObjectURL(blob), Label = prompt });
            _messages.Add(msg);
        }
        catch (Exception ex) { Console.WriteLine($"Image gen FAILED: {ex.GetType().Name}: {ex.Message}"); _messages.Add(new Msg { Role = "system", Text = $"Image gen error: {ex.Message}" }); }
        finally { _busy = false; _busyNote = ""; StateHasChanged(); await ScrollToBottom(); }
    }

    void HandleSlash(string text)
    {
        var parts = text.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        switch (parts[0].ToLowerInvariant())
        {
            case "/model":
                if (parts.Length == 1)
                    _messages.Add(new Msg { Role = "system", Text = "Chat models:\n" + string.Join("\n", _models.Select(m => (m == _model ? "► " : "· ") + m)) + "\n\nUse /model <name> to switch." });
                else if (_models.FirstOrDefault(m => m.Contains(parts[1], StringComparison.OrdinalIgnoreCase)) is string hit)
                { _model = hit; _messages.Add(new Msg { Role = "system", Text = $"Chat model → {hit}" }); }
                else _messages.Add(new Msg { Role = "system", Text = $"No chat model matching '{parts[1]}'. /model lists them." });
                break;
            case "/image-model":
                if (parts.Length == 1)
                    _messages.Add(new Msg { Role = "system", Text = "Image models:\n" + string.Join("\n", _imageModels.Select(m => (m.Name == _imageModel ? "► " : "· ") + m.Name + (m.Note.Length > 0 ? $" - {m.Note}" : ""))) + "\n\nUse /image-model <name> to switch." });
                else if (_imageModels.FirstOrDefault(m => m.Name.Contains(parts[1], StringComparison.OrdinalIgnoreCase)).Name is { Length: > 0 } ihit)
                { _imageModel = ihit; _messages.Add(new Msg { Role = "system", Text = $"Image model → {ihit}" }); }
                else _messages.Add(new Msg { Role = "system", Text = $"No image model matching '{parts[1]}'. /image-model lists them." });
                break;
            default:
                _messages.Add(new Msg { Role = "system", Text = $"Unknown command {parts[0]}. Commands: /model, /image-model." });
                break;
        }
    }

    // Replace ai-artifact:// markdown refs with resolved blob-URL images. The bytes never leave the
    // browser; the download anchor exports the same blob the <img> displays.
    async Task<string> ResolveArtifactsAsync(string text, List<ChatImage> images)
    {
        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(
            text, @"!\[([^\]]*)\]\(ai-artifact://([a-z0-9]+)\)"))
        {
            try
            {
                var (mime, data, label) = await Ai.GetArtifactAsync(m.Groups[2].Value);
                using var blob = new Blob(new[] { data }, new BlobOptions { Type = mime });
                images.Add(new ChatImage { Url = URL.CreateObjectURL(blob), Label = label ?? m.Groups[1].Value });
            }
            catch { /* evicted artifact - drop the ref silently */ }
            text = text.Replace(m.Value, "");
        }
        return text.Trim();
    }

    // Minimal SAFE rich rendering: HTML-escape everything first, then apply our own transforms
    // (code fences, inline code, bold). Model output never reaches the DOM unescaped.
    MarkupString RenderRich(string text)
    {
        var s = System.Net.WebUtility.HtmlEncode(text);
        s = System.Text.RegularExpressions.Regex.Replace(s, "```([a-zA-Z0-9]*)\\n([\\s\\S]*?)```", "<pre>$2</pre>");
        s = System.Text.RegularExpressions.Regex.Replace(s, "`([^`\\n]+)`", "<code>$1</code>");
        s = System.Text.RegularExpressions.Regex.Replace(s, "\\*\\*([^*\\n]+)\\*\\*", "<b>$1</b>");
        return new MarkupString(s);
    }

    static string Sanitize(string s)
        => string.Concat((s.Length > 40 ? s[..40] : s).Select(c => char.IsLetterOrDigit(c) ? c : '_'));

    Task ScrollToBottom()
    {
        try { using var el = new HTMLElement(_scrollRef); el.ScrollTop = el.ScrollHeight; }
        catch { }
        return Task.CompletedTask;
    }

    // ── Storage management: OPFS is INVISIBLE to Chrome DevTools ("Clear site data" doesn't touch
    // it, there is no viewer) - the app must be its own storage manager. Typed BlazorJS surface.
    string _storageLine = "";

    async Task RefreshStorageAsync()
    {
        try
        {
            using var storage = JS.Get<StorageManager>("navigator.storage");
            var est = await storage.Estimate();
            _storageLine = $"browser storage: {est.Usage / 1048576.0 / 1024.0:F2} GB used of {est.Quota / 1048576.0 / 1024.0:F0} GB quota (model cache lives here)";
        }
        catch { _storageLine = ""; }
    }

    async Task ClearStorageAsync()
    {
        try
        {
            using var storage = JS.Get<StorageManager>("navigator.storage");
            using var root = await storage.GetDirectory();
            var entries = await root.ValuesList();
            var names = entries.Select(e => e.Name).ToList();
            foreach (var e in entries) e.Dispose();
            foreach (var n in names)
                await root.RemoveEntry(n, recursive: true);
            _messages.Add(new Msg { Role = "system", Text = $"Cleared {names.Count} OPFS entries. Cached models will re-download on next use (a page reload is recommended)." });
            await RefreshStorageAsync();
        }
        catch (Exception ex)
        {
            _messages.Add(new Msg { Role = "system", Text = $"Storage clear failed: {ex.Message} (some entries may be locked by the active worker - reload and retry)" });
        }
        StateHasChanged();
    }
}
