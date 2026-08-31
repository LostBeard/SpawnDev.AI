using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using SpawnDev.SpawnJS.JSObjects;
using SpawnDev.AI;
using SpawnDev.AI.Server;
using SpawnDev.SpawnJS.Blazor;
using SpawnDev.SpawnJS;
using SpawnDev.ILGPU.ML.Preprocessing;

namespace SpawnDev.AI.Demo.Pages;

public partial class Home : IDisposable
{
    bool _ready, _starting, _busy;
    string _status = "", _busyNote = "";
    string _model = "qwen2.5:0.5b-instruct-q8_0";
    string _imageModel = "sd-turbo";
    readonly List<string> _models = new();
    List<(string Name, string Note)> _imageModels = new();

    // ── Agent settings (user-editable via the ⚙️ panel) ──
    // The system prompt shapes how the model behaves. Image REQUESTS no longer depend on this text - the
    // server forces the generate_image tool on clear visual intent (AiChatEngine.ForceImageToolOnIntent) - but
    // the prompt still steers tone, refusal behavior, and when the model volunteers an image on its own.
    const string DefaultSystemPrompt =
        "You are a helpful assistant running entirely on the user's own GPU in their browser. Answer "
        + "questions, facts, math, explanations, stories, and poems clearly in plain text. When the user asks "
        + "about the SpawnDev open-source libraries, the apps built with them, or the crew, authoritative "
        + "reference information from GitHub is added to the conversation automatically - answer from it and "
        + "do not say you need a repository name. When the user asks for a picture, photo, or drawing, the app "
        + "generates the image automatically - you don't need to do anything, so never say you can't make images.";
    bool _showSettings;
    string _systemPrompt = DefaultSystemPrompt;
    float _temperature = 0.3f;
    int _maxTokens = 384;

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
        // ?worker=dedicated forces a dedicated worker (diagnostic: the piece-download loop
        // reproduced only under SharedWorker, 2026-07-04).
        var location = JS.Get<string>("location.href");
        if (location.Contains("worker=dedicated", StringComparison.OrdinalIgnoreCase))
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

    void ToggleSettings() { _showSettings = !_showSettings; StateHasChanged(); }
    void ResetSystemPrompt() { _systemPrompt = DefaultSystemPrompt; StateHasChanged(); }

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
        string? spokenReply = null;
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
            // The system prompt is user-editable via the ⚙️ settings panel (_systemPrompt). Image requests
            // no longer depend on it: the server pre-emptively forces the generate_image tool on clear visual
            // intent (AiChatEngine.ForceImageToolOnIntent) because a 0.5b REFUSES ~40% of plain image requests
            // and the refusal is the greedy argmax, so no prompt/sampling tweak makes it reliable. An empty
            // prompt is allowed (some users want a bare model); we just skip the system turn then.
            var convo = new List<AiChatMessage>();
            if (!string.IsNullOrWhiteSpace(_systemPrompt)) convo.Add(new AiChatMessage("system", _systemPrompt));
            foreach (var m in _messages.Where(m => m.Role is "user" or "assistant"))
                convo.Add(new AiChatMessage(m.Role, m.Text));

            var doneReason = await Ai.ChatStreamAsync(_model, convo,
                new AiGenerationOptions { MaxOutputTokens = _maxTokens, Strategy = "top_p", Temperature = _temperature, TopP = 0.9f, RepetitionPenalty = 1.15f },
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
            spokenReply = msg.Text;
        }
        catch (Exception ex) { _messages.Add(new Msg { Role = "system", Text = $"Error: {ex.Message}" }); }
        finally
        {
            _streaming = ""; _busy = false; _busyNote = "";
            StateHasChanged();
            await ScrollToBottom();
        }

        // Speaking happens AFTER the finally, so the reply is on screen and the composer is usable while
        // it talks. Doing it inside the turn would leave the UI "busy" for the whole utterance.
        if (_handsFree && !string.IsNullOrWhiteSpace(spokenReply))
            await SpeakReplyAsync(spokenReply!);
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
        try { using var el = _scrollRef.As<HTMLElement>(); el.ScrollTop = el.ScrollHeight; }
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

    // ── Voice input ───────────────────────────────────────────────────────────────────────────────────
    // Speak instead of typing: the microphone feeds Whisper in the AI worker and the transcript lands in
    // the composer, where it can be edited before sending rather than fired off blind.
    //
    // Capture runs at the microphone's NATIVE rate (48 kHz on most hardware) and the whole utterance is
    // converted ONCE at the end. Resampling each ~10 ms chunk instead would hand the filter no signal
    // either side of a chunk boundary, stitching in a discontinuity 100 times a second. Converting once
    // also cuts what crosses to the worker by 3x, which matters here: AiWorkerClient.TranscribeAsync
    // JSON-encodes the samples, so 9 s at 48 kHz would be 432,000 numbers.
    //
    // ⚠️ Requires an ILGPU.ML whose AudioPreprocessor.Resample band-limits before decimating. Up to and
    // including 5.2.2 it was bare linear interpolation, which aliased 8-24 kHz back onto the speech and
    // made Whisper return fluent, confident, unrelated text.
    const int WhisperRate = 16000;
    const double MaxUtteranceSeconds = 30.0;

    MediaStreamCapture? _mic;
    readonly List<float> _micSamples = new();
    int _micRate = WhisperRate;
    bool _listening;
    double _listenSeconds;

    // ── Hands-free conversation ───────────────────────────────────────────────────────────────────────
    // Listen, transcribe, send, speak the reply in the USER'S OWN VOICE, listen again.
    //
    // ⚠️ The reply is spoken with the turn just heard as the voice reference - ZipVoice clones, so the
    // assistant answers in the voice that asked. That is the product, not a shortcut; there is no stock
    // voice, and an engine that substituted one would be a different thing wearing this one's clothes.
    //
    // ⚠️ The microphone is NOT reopened until playback finishes. A loop that listens while it talks hears
    // itself, transcribes its own reply, and answers it - a feedback loop that looks like a hang and is
    // not one. WaitForEndAsync is what keeps the two halves apart.
    bool _handsFree;
    AudioPlayback? _speaker;
    float[]? _lastHeardSamples;
    string _lastHeardText = "";

    /// <summary>Turn the hands-free conversation on or off.</summary>
    async Task ToggleHandsFreeAsync()
    {
        _handsFree = !_handsFree;
        if (_handsFree)
        {
            _status = "Hands-free on — listening. Say something.";
            StateHasChanged();
            await StartListeningAsync();
        }
        else
        {
            _speaker?.Stop();
            if (_listening) await StopListeningAsync();
            _status = "Hands-free off.";
            StateHasChanged();
        }
    }

    /// <summary>Speak one reply, then hand the microphone back.</summary>
    async Task SpeakReplyAsync(string text)
    {
        if (_lastHeardSamples == null || _lastHeardSamples.Length == 0)
        {
            // Nothing to clone from. Say so rather than falling silent: a hands-free loop that stops
            // talking for no stated reason is indistinguishable from one that crashed.
            _status = "Nothing to speak with — the voice is cloned from what you said, and I have no "
                    + "audio for this turn.";
            StateHasChanged();
            return;
        }

        try
        {
            _busyNote = "Speaking…";
            StateHasChanged();

            var (samples, rate, _, ms) = await Ai.SpeakAsync(text, _lastHeardText, _lastHeardSamples,
                WhisperRate);

            _speaker ??= new AudioPlayback(JS);
            var seconds = await _speaker.PlayAsync(samples, rate);
            _status = $"Spoke {seconds:F1}s in {ms:F0} ms";
            StateHasChanged();

            await _speaker.WaitForEndAsync();
        }
        catch (Exception ex)
        {
            _status = $"Speaking failed: {ex.Message}";
        }
        finally
        {
            _busyNote = "";
            StateHasChanged();
        }

        // Back to listening for the next turn - only now, with the speakers quiet.
        if (_handsFree && !_listening) await StartListeningAsync();
    }

    async Task ToggleMicAsync()
    {
        if (_listening) await StopListeningAsync();
        else await StartListeningAsync();
    }

    async Task StartListeningAsync()
    {
        if (_busy || _listening) return;

        _mic ??= new MediaStreamCapture(JS);
        // Re-subscribing on every start would fire the handler N times per chunk.
        _mic.OnAudioReady -= OnMicAudio;
        _mic.OnAudioReady += OnMicAudio;
        _mic.OnAudioError -= OnMicError;
        _mic.OnAudioError += OnMicError;

        lock (_micSamples) _micSamples.Clear();
        _listenSeconds = 0;

        if (!await _mic.StartMicrophoneAsync())
        {
            _status = $"Microphone unavailable. {_mic.LastAudioError?.Message}";
            StateHasChanged();
            return;
        }

        _listening = true;
        _status = "Listening\u2026";
        StateHasChanged();
    }

    async Task StopListeningAsync()
    {
        if (!_listening) return;
        _mic?.StopMicrophone();
        _listening = false;

        float[] captured;
        lock (_micSamples) captured = _micSamples.ToArray();

        if (captured.Length < _micRate / 2)
        {
            _status = "That was too short to transcribe.";
            StateHasChanged();
            return;
        }

        _busy = true;
        _busyNote = "Transcribing\u2026";
        StateHasChanged();
        try
        {
            var samples = _micRate == WhisperRate
                ? captured
                : AudioPreprocessor.Resample(captured, _micRate, WhisperRate);

            var (text, _, ms) = await Ai.TranscribeAsync(samples, WhisperRate);
            text = (text ?? "").Trim();

            if (text.Length == 0)
            {
                // Whisper answers silence with "[BLANK_AUDIO]" or nothing at all. Say so plainly rather
                // than dropping an empty string into the composer.
                _status = "Heard nothing to transcribe.";
            }
            else
            {
                _input = string.IsNullOrWhiteSpace(_input) ? text : $"{_input.TrimEnd()} {text}";
                _status = $"Transcribed {captured.Length / (double)_micRate:F1}s in {ms:F0} ms";
                // Keep the resampled audio and its transcript: they are the voice reference for the reply.
                // ⚠️ The 16 kHz version, not the raw capture - it is what the recogniser heard, so the
                // transcript describes exactly these samples. Handing the cloner a different rendering of
                // the utterance than the text describes degrades the clone invisibly.
                _lastHeardSamples = samples;
                _lastHeardText = text;
            }
        }
        catch (Exception ex)
        {
            _status = $"Transcription failed: {ex.Message}";
        }
        finally
        {
            _busy = false;
            _busyNote = "";
            StateHasChanged();
        }

        // Hands-free sends what it heard instead of parking it in the composer. Typing mode deliberately
        // does NOT: a transcript you can read and correct before it goes is the safer default, and the
        // whole point of hands-free is that there is nobody at the keyboard to do that.
        if (_handsFree && !string.IsNullOrWhiteSpace(_input)) await SendAsync();
    }

    void OnMicAudio(float[] chunk, int rate)
    {
        _micRate = rate;
        double seconds;
        lock (_micSamples)
        {
            _micSamples.AddRange(chunk);
            seconds = _micSamples.Count / (double)rate;
        }

        if (seconds >= MaxUtteranceSeconds)
        {
            _ = InvokeAsync(StopListeningAsync);
            return;
        }

        // The sample count IS the clock. Repaint about four times a second, not once per chunk.
        if (seconds - _listenSeconds >= 0.25)
        {
            _listenSeconds = seconds;
            _ = InvokeAsync(StateHasChanged);
        }
    }

    void OnMicError(Exception ex)
    {
        // A capture that dies silently is indistinguishable from a quiet room. Say it out loud.
        _listening = false;
        _status = $"Microphone stopped: {ex.Message}";
        _ = InvokeAsync(StateHasChanged);
    }

    public void Dispose()
    {
        _mic?.Dispose();
        _mic = null;
    }

}
