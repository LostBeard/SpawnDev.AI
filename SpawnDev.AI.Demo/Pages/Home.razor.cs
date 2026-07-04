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

    async Task StartAsync()
    {
        _starting = true;
        _status = "Attaching shared worker, requesting WebGPU…";
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
            var convo = new List<AiChatMessage> { new("system",
                "You are a helpful assistant running entirely on the user's own GPU in their browser.") };
            foreach (var m in _messages.Where(m => m.Role is "user" or "assistant"))
                convo.Add(new AiChatMessage(m.Role, m.Text));

            var doneReason = await Ai.ChatStreamAsync(_model, convo,
                new AiGenerationOptions { MaxOutputTokens = 384, Strategy = "top_p", Temperature = 0.7f, TopP = 0.9f, RepetitionPenalty = 1.15f },
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
        }
        catch (Exception ex) { _messages.Add(new Msg { Role = "system", Text = $"Error: {ex.Message}" }); }
        finally
        {
            _streaming = ""; _busy = false; _busyNote = "";
            StateHasChanged();
            await ScrollToBottom();
        }
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
}
