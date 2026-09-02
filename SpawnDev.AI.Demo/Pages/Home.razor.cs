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
        // ⚠️ A turn that has produced no token yet renders an EMPTY in-progress bubble. On a cold turn
        // that state can last minutes (GGUF load + first-execution kernel compilation) and it is
        // indistinguishable from a hung page - Captain reported exactly that: "transcribed fine then
        // produced NO assistant reply in 15 minutes". The speak path already learned this lesson and grew
        // a moving counter; the chat path never did. A number that MOVES is the whole difference between
        // "slow" and "broken".
        var turnStarted = DateTime.UtcNow;
        DateTime? firstDeltaAt = null;
        using var waitTicker = new CancellationTokenSource();
        var waitTickerTask = Task.Run(async () =>
        {
            try
            {
                while (!waitTicker.IsCancellationRequested)
                {
                    await Task.Delay(500, waitTicker.Token);
                    if (waitTicker.IsCancellationRequested) break;
                    // Once tokens flow the streaming text is itself the progress indicator; the caption
                    // only has a job while there is nothing else on screen.
                    if (firstDeltaAt != null) break;
                    var secs = (DateTime.UtcNow - turnStarted).TotalSeconds;
                    _busyNote = $"waiting for the first token… {secs:F0}s"
                              + (secs > 20 ? " (loading the model and compiling kernels)" : "");
                    await InvokeAsync(StateHasChanged);
                }
            }
            catch (OperationCanceledException) { /* expected once a token arrives or the turn ends */ }
            catch (Exception ex) { Console.WriteLine($"[HF-CHAT] ticker stopped: {ex.Message}"); }
        });
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
                    // ⚠️ TIME TO FIRST TOKEN is a SEPARATE measurement from decode rate, and conflating
                    // them is what made last night's "0.4 tok/s" unexplainable: genClock starts before the
                    // model is loaded, so a cold turn divides the token count by load + compile + decode
                    // and reports a decode rate that was never measured. This model has run at 34 tok/s in
                    // the browser; a number an order of magnitude off was describing a different quantity.
                    firstDeltaAt ??= DateTime.UtcNow;
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
            waitTicker.Cancel();
            try { await waitTickerTask; } catch { /* the ticker reports its own failures */ }

            // Decode rate is measured from the FIRST TOKEN onward. Everything before it is load and
            // kernel compilation, which is a real cost and is reported separately rather than smeared
            // into a per-token number that then describes nothing.
            double ttftSeconds = firstDeltaAt is { } t ? (t - turnStarted).TotalSeconds : 0;
            double decodeSeconds = firstDeltaAt is { } f ? (DateTime.UtcNow - f).TotalSeconds : 0;

            var msg = new Msg
            {
                Role = "assistant",
                Ms = genClock.Elapsed.TotalMilliseconds,
                TokPerSec = deltas > 1 && decodeSeconds > 0 ? deltas / decodeSeconds : 0,
                Truncated = doneReason == "length",
            };
            Console.WriteLine($"[HF-CHAT] {deltas} deltas: first token after {ttftSeconds:F1}s, "
                            + $"then {msg.TokPerSec:F1} tok/s over {decodeSeconds:F1}s "
                            + $"(total {genClock.Elapsed.TotalSeconds:F1}s)");
            msg.Text = await ResolveArtifactsAsync(_streaming, msg.Images);
            _messages.Add(msg);
            _status = $"last response: {msg.Ms / 1000.0:F1}s · {ttftSeconds:F1}s to first token · "
                    + $"{msg.TokPerSec:F1} tok/s · model {_model}";
            await RefreshStorageAsync();
            spokenReply = msg.Text;
        }
        catch (Exception ex) { _messages.Add(new Msg { Role = "system", Text = $"Error: {ex.Message}" }); }
        finally
        {
            // Belt and braces: the ticker is also cancelled on the success path, but an exception thrown
            // before that leaves a background loop writing captions over the error message.
            waitTicker.Cancel();
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
    // Capture runs at the microphone's NATIVE rate (48 kHz on most hardware) and is converted to 16 kHz as
    // it arrives, by a STREAMING resampler. Calling AudioPreprocessor.Resample per ~10 ms chunk instead
    // would hand the filter no signal either side of a chunk boundary, stitching in a discontinuity 100
    // times a second; StreamingResampler carries the tail across chunks and is gated to produce output
    // bit-identical to a whole-buffer call (ILGPU.ML Streaming_MatchesWholeBufferResample).
    //
    // Converting on the way IN rather than once at the end is what makes live endpointing possible at all:
    // the detector needs a continuous 16 kHz stream while the microphone is still open. It also cuts what
    // crosses to the worker by 3x, which matters here - AiWorkerClient JSON-encodes samples, so 9 s at
    // 48 kHz would be 432,000 numbers.
    //
    // ⚠️ Requires an ILGPU.ML whose AudioPreprocessor.Resample band-limits before decimating. Up to and
    // including 5.2.2 it was bare linear interpolation, which aliased 8-24 kHz back onto the speech and
    // made Whisper return fluent, confident, unrelated text.
    const int WhisperRate = 16000;

    // ⚠️ This is a SAFETY CEILING now, not the way a turn ends. It used to be the ONLY way a turn ended:
    // the loop recorded for a flat 30 s no matter what was said, so four words meant sitting through 26 s
    // of silence, every turn, before anything happened. Silero decides the end now (VadOptions
    // .MinSilenceDuration); this only bounds a microphone left open in a noisy room.
    const double MaxUtteranceSeconds = 30.0;

    /// <summary>Audio kept behind the live edge while nothing is being said, as a lead-in guard.</summary>
    /// <remarks>
    /// The detector opens a segment slightly BEFORE the frame that crossed the threshold (VadOptions
    /// .SpeechPad plus one frame), so the window cannot discard right up to the live edge or the first
    /// consonant of every utterance is already gone when the span naming it arrives. Two seconds is far
    /// more than the detector can reach back for and costs 128 KB.
    /// </remarks>
    const double SilentTailKeepSeconds = 2.0;

    MediaStreamCapture? _mic;

    /// <summary>The canonical capture buffer: mono, 16 kHz, what the recogniser and the cloner both use.</summary>
    readonly List<float> _micSamples = new();

    /// <summary>Absolute index, in the 16 kHz stream, of <c>_micSamples[0]</c>.</summary>
    /// <remarks>
    /// The endpointer answers in offsets from the start of the stream, and quiet audio is dropped from the
    /// front of the buffer while nobody is talking - so a span cannot be indexed into the list directly.
    /// Without this a hands-free session that waited a while before you spoke would slice the wrong audio,
    /// which does not throw: it transcribes and CLONES A VOICE from the wrong seconds.
    /// </remarks>
    long _micBufferStart;

    StreamingResampler? _micResampler;
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
            // ⚠️ Warm the three models BEFORE the first turn, and say so while it happens. Loaded lazily
            // they load INSIDE the turn that needs them, so the user's first sentence is followed by a
            // recogniser download and the first reply by a voice download - MEASURED at 88.7 s for that
            // first spoken reply, with the text answer already on screen and no indication of why. The
            // work is identical; only its position in the conversation changes.
            //
            // Warming is best-effort. A kind that fails here is still attempted lazily by its own route,
            // so a preload failure must not end the conversation before it starts.
            _status = "Getting ready — loading the endpointer, recogniser and voice…";
            StateHasChanged();
            // ⚠️ ONLY the endpointer is waited for. It is the one model needed before the microphone can
            // usefully open, and it is by far the smallest (643 KB). Waiting for all three here would keep
            // the microphone shut for minutes on a cold cache - trading "the first turn is slow" for "the
            // button does nothing for a while", which is not an improvement.
            string? warmNote = null;
            try
            {
                var (_, failed) = await Ai.WarmAsync("vad");
                if (failed.Length > 0)
                    warmNote = $"{string.Join(", ", failed.Select(f => $"{f.Kind} ({f.Error})"))} did not "
                             + "preload; it will be loaded when first needed.";
            }
            catch (Exception ex)
            {
                warmNote = $"Could not preload the endpointer ({ex.Message}); loading it as needed.";
            }

            // ⚠️ Do NOT overwrite a warning with the cheerful line. Reporting a problem and then erasing it
            // one statement later is the same defect that hid every spoken-reply failure until now.
            _status = warmNote == null
                ? "Hands-free on — listening. Say something."
                : $"Hands-free on — listening. {warmNote}";
            if (warmNote != null) _messages.Add(new Msg { Role = "system", Text = warmNote });
            StateHasChanged();
            await StartListeningAsync();

            // The recogniser and the voice load WHILE the user speaks their first sentence. That is the
            // whole point: the work is unavoidable, its position in the conversation is not. Transcription
            // cannot begin until they stop talking anyway, so these seconds are otherwise dead.
            _ = WarmInBackgroundAsync();
        }
        else
        {
            _speaker?.Stop();
            if (_listening) await StopListeningAsync();
            _status = "Hands-free off.";
            StateHasChanged();
        }
    }

    /// <summary>
    /// Load the recogniser and the voice while the user is talking.
    /// </summary>
    /// <remarks>
    /// ⚠️ Nothing may escape this method - it is fire-and-forget, and an unhandled exception on a runtime
    /// callback EXITS the .NET WASM runtime and takes the page with it. A warm failure is reported and
    /// otherwise ignored: the lazy path in each engine still works, so failing to PRELOAD must never end
    /// a conversation that has just started.
    /// </remarks>
    async Task WarmInBackgroundAsync()
    {
        try
        {
            var (_, failed) = await Ai.WarmAsync("speech", "voice");
            if (failed.Length == 0) return;
            var note = $"{string.Join(", ", failed.Select(f => $"{f.Kind} ({f.Error})"))} did not preload; "
                     + "it will be loaded when first needed.";
            await InvokeAsync(() =>
            {
                _messages.Add(new Msg { Role = "system", Text = note });
                StateHasChanged();
            });
        }
        catch (Exception ex)
        {
            try
            {
                JS.LogError($"[hands-free] background warm failed: {ex.Message}");
                await InvokeAsync(() =>
                {
                    _messages.Add(new Msg
                    {
                        Role = "system",
                        Text = $"Could not preload the recogniser/voice ({ex.Message}); they will load when "
                             + "first needed, which makes the first reply slower.",
                    });
                    StateHasChanged();
                });
            }
            catch { /* nothing may escape */ }
        }
    }

    /// <summary>Speak one reply, then hand the microphone back.</summary>
    async Task SpeakReplyAsync(string text)
    {
        if (_lastHeardSamples == null || _lastHeardSamples.Length == 0)
        {
            // Nothing to clone from. Say so rather than falling silent: a hands-free loop that stops
            // talking for no stated reason is indistinguishable from one that crashed.
            SpeechFailed("Nothing to speak with — the voice is cloned from what you said, and I have no "
                       + "audio for this turn.");
            return;
        }

        try
        {
            // ⚠️ _speaking, not _busyNote. Speaking deliberately happens AFTER the turn's `finally`, so the
            // composer is usable while it talks - which also means `_busy` is false and the in-progress
            // bubble that renders `_busyNote` is not in the DOM at all. The note was being set into a
            // element nobody displays, so the first spoken reply (a cold ZipVoice load: two int8 graphs,
            // a token table and a 54 MB vocoder) showed the user a finished text answer and then nothing
            // whatsoever for minutes. Indistinguishable from "it just doesn't speak".
            _speaking = true;
            _status = "Preparing the voice…";
            StateHasChanged();

            // ⚠️ A STATIC string held for minutes is the same defect as no string at all. The comment above
            // records that a finished answer followed by silence was "indistinguishable from 'it just
            // doesn't speak'" - but a caption that never changes for two minutes is equally
            // indistinguishable from a hung page, and Captain read it exactly that way on the first cold
            // synthesis. A number that MOVES is the whole difference between "slow" and "broken", and it
            // costs one timer. The elapsed count keeps running until the samples come back.
            var speakStarted = DateTime.UtcNow;
            using var speakTicker = new CancellationTokenSource();
            var ticker = Task.Run(async () =>
            {
                try
                {
                    while (!speakTicker.IsCancellationRequested)
                    {
                        await Task.Delay(500, speakTicker.Token);
                        if (speakTicker.IsCancellationRequested) break;
                        var secs = (DateTime.UtcNow - speakStarted).TotalSeconds;
                        // Only ever overwrite our OWN caption. Clobbering a failure message that arrived
                        // while this was ticking would hide it, which is the bug this file keeps re-learning.
                        if (_speaking && _status.StartsWith("Preparing the voice"))
                        {
                            _status = $"Preparing the voice… {secs:F0}s" +
                                      (secs > 20 ? " (first synthesis compiles kernels and loads the voice)" : "");
                            await InvokeAsync(StateHasChanged);
                        }
                    }
                }
                catch (OperationCanceledException) { /* expected on completion */ }
                catch (Exception ex) { Console.WriteLine($"[HF-SPEAK] ticker stopped: {ex.Message}"); }
            });

            var (samples, rate, _, ms) = await Ai.SpeakAsync(text, _lastHeardText, _lastHeardSamples,
                WhisperRate);
            speakTicker.Cancel();
            try { await ticker; } catch { /* already reported by the ticker itself */ }
            Console.WriteLine($"[HF-SPEAK] synthesis returned after " +
                              $"{(DateTime.UtcNow - speakStarted).TotalSeconds:F1}s ({ms:F0} ms reported), " +
                              $"{samples.Length} samples @ {rate} Hz");

            _speaker ??= new AudioPlayback(JS);
            var seconds = await _speaker.PlayAsync(samples, rate);
            _status = $"Spoke {seconds:F1}s in {ms:F0} ms";
            StateHasChanged();

            await _speaker.WaitForEndAsync();
        }
        catch (Exception ex)
        {
            SpeechFailed($"Speaking failed: {ex.Message}");
        }
        finally
        {
            _speaking = false;
            _busyNote = "";
            StateHasChanged();
        }

        // Back to listening for the next turn - only now, with the speakers quiet.
        if (_handsFree && !_listening) await StartListeningAsync();
    }

    /// <summary>True while a reply is being synthesised or played.</summary>
    bool _speaking;

    /// <summary>
    /// Record a failure to speak somewhere it will still be there a second later.
    /// </summary>
    /// <remarks>
    /// ⚠️ THE REASON THIS IS NOT JUST <c>_status</c>. The hands-free loop goes straight back to listening
    /// after a turn, and <see cref="StartListeningAsync"/> overwrites the status line with "Listening…" -
    /// so a spoken reply that failed reported itself for a fraction of a second and then erased the
    /// evidence. From the outside that is indistinguishable from an assistant that simply answers in text,
    /// which is exactly how "it never speaks" went unexplained: the app WAS saying why, into a field it
    /// then cleared. A chat bubble persists, and the console line survives for a gate to read.
    /// </remarks>
    void SpeechFailed(string message)
    {
        _status = message;
        _messages.Add(new Msg { Role = "system", Text = message });
        // JS.LogError, never Console.Error.WriteLine - the latter raises Blazor's error UI, which makes a
        // gate report the whole turn FAILED even when everything else worked.
        JS.LogError($"[hands-free] {message}");
        StateHasChanged();
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
        lock (_vadQueue) _vadQueue.Clear();
        _micBufferStart = 0;
        _micResampler = null;
        _listenSeconds = 0;
        _speechActive = false;
        _speechProbability = 0;
        _micChunks = 0;
        _micRawPeak = 0; _micRawRms = 0; _micPeak = 0; _micRms = 0;
        _vadBatches = 0;
        _vadPeakProbability = 0;
        _micTurnPeak = 0;
        _micTurnRms = 0;
        _micLoggedAt = DateTime.MinValue;

        // \u26a0\ufe0f Reset the endpointer's stream BEFORE the first sample of the new one. It answers in offsets
        // counted from its own clock, so a clock carried over from the previous turn returns spans that
        // point past the end of this recording - which does not throw, it slices the wrong audio.
        // Skipped when the endpointer has already failed; the loop is being torn down in that case.
        if (!_vadFailed)
        {
            try { await Ai.VadAsync(System.Array.Empty<float>(), reset: true); }
            catch (Exception ex)
            {
                _vadFailed = true;
                _status = $"Endpointing unavailable, so hands-free cannot tell when you stop talking: {ex.Message}";
                _handsFree = false;
                StateHasChanged();
                return;
            }
        }

        if (!await _mic.StartMicrophoneAsync())
        {
            _status = $"Microphone unavailable. {_mic.LastAudioError?.Message}";
            StateHasChanged();
            return;
        }

        _listening = true;
        _status = _handsFree ? "Listening \u2014 say something, and stop when you're done." : "Listening\u2026";
        StateHasChanged();
    }

    /// <summary>
    /// Stop capturing and transcribe what was said.
    /// </summary>
    /// <param name="spanStart">
    /// Start of the utterance the endpointer closed, as an absolute offset in the 16 kHz stream. Null
    /// transcribes the whole buffer, which is what the push-to-talk button wants: the user decided the
    /// bounds by pressing stop.
    /// </param>
    /// <param name="spanLength">Length of that utterance in samples.</param>
    async Task StopListeningAsync(long? spanStart = null, int? spanLength = null)
    {
        if (!_listening) return;
        _mic?.StopMicrophone();
        _listening = false;

        // Whatever the resampler still holds is the tail of the last word - take it before it is dropped.
        if (_micResampler != null)
        {
            var tail = _micResampler.Flush();
            if (tail.Length > 0) lock (_micSamples) _micSamples.AddRange(tail);
            _micResampler = null;
        }

        float[] captured;
        lock (_micSamples)
        {
            if (spanStart is long s && spanLength is int n)
            {
                // \u26a0\ufe0f CLAMP. Silero's clock advances in whole 512-sample frames, so a span can name up to
                // 511 samples past what has been appended, and quiet audio has been trimmed off the
                // front - so neither end of the span can be trusted to sit inside the list.
                int from = (int)Math.Max(0, s - _micBufferStart);
                int to = (int)Math.Min(_micSamples.Count, from + (long)n);
                captured = from < to ? _micSamples.GetRange(from, to - from).ToArray() : System.Array.Empty<float>();
            }
            else captured = _micSamples.ToArray();
        }

        if (captured.Length < WhisperRate / 2)
        {
            _status = "That was too short to transcribe.";
            StateHasChanged();
            // Hands-free must go back to listening rather than ending the conversation on a cough.
            if (_handsFree && !_vadFailed) await StartListeningAsync();
            return;
        }

        _busy = true;
        _busyNote = "Transcribing\u2026";
        StateHasChanged();
        try
        {
            // Already 16 kHz: the stream was converted on the way in, by a resampler whose output is
            // gated to equal a whole-buffer conversion exactly.
            var samples = captured;

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
                // ⚠️ WhisperRate, not _micRate: `captured` is the CONVERTED stream. Dividing 16 kHz
                // samples by the microphone's native 48 kHz reports a 4.0 s utterance as 1.3 s, which
                // reads as dropped audio and sends you hunting a capture bug that is not there.
                _status = $"Transcribed {captured.Length / (double)WhisperRate:F1}s in {ms:F0} ms";
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
        if (!_handsFree) return;

        if (!string.IsNullOrWhiteSpace(_input)) await SendAsync();
        // ⚠️ Nothing to send - Whisper returned "[BLANK_AUDIO]" for a segment the detector opened, which a
        // door or a cough will do. Go back to listening. Returning here instead left the conversation
        // silently OVER, with the microphone shut and the button still reading "hands-free on".
        else if (!_listening && !_vadFailed) await StartListeningAsync();
    }

    void OnMicAudio(float[] chunk, int rate)
    {
        // The browser can hand over a different rate than it promised, and a resampler pinned to the wrong
        // source rate produces confident, wrong audio rather than an error.
        if (_micResampler == null || _micResampler.SourceRate != rate)
        {
            _micResampler = new StreamingResampler(rate, WhisperRate);
            // Print the ratio once per device. 48000 -> 16000 is an exact 3:1 decimation; 44100 -> 16000
            // is not, and a rate the resampler handles badly is invisible in every other number here.
            Console.WriteLine($"[HF-MIC] capture opened: device {rate} Hz -> {WhisperRate} Hz " +
                              $"(ratio {(double)rate / WhisperRate:F4}), first chunk {chunk.Length} samples");
        }
        _micRate = rate;

        // Level of the RAW chunk, before any conversion. No LINQ - it silently fails in WASM logging paths.
        float rawPeak = 0f;
        double rawSum = 0;
        for (int i = 0; i < chunk.Length; i++)
        {
            var v = chunk[i];
            var a = v < 0 ? -v : v;
            if (a > rawPeak) rawPeak = a;
            rawSum += (double)v * v;
        }
        _micRawPeak = rawPeak;
        _micRawRms = chunk.Length > 0 ? (float)Math.Sqrt(rawSum / chunk.Length) : 0f;
        if (_micRawPeak > _micTurnPeak) _micTurnPeak = _micRawPeak;
        if (_micRawRms > _micTurnRms) _micTurnRms = _micRawRms;
        _micChunks++;
        _micChunkSamples = chunk.Length;

        var converted = _micResampler.Process(chunk);
        if (converted.Length == 0) return;   // the resampler is holding a partial kernel window

        // Level AFTER conversion - this is the signal the detector is actually given.
        float peak = 0f;
        double sum = 0;
        for (int i = 0; i < converted.Length; i++)
        {
            var v = converted[i];
            var a = v < 0 ? -v : v;
            if (a > peak) peak = a;
            sum += (double)v * v;
        }
        _micPeak = peak;
        _micRms = converted.Length > 0 ? (float)Math.Sqrt(sum / converted.Length) : 0f;

        var now = DateTime.UtcNow;
        if ((now - _micLoggedAt).TotalMilliseconds >= 1000)
        {
            _micLoggedAt = now;
            Console.WriteLine($"[HF-MIC] chunks={_micChunks} in={_micChunkSamples}@{rate}Hz " +
                              $"raw peak={_micRawPeak:F4} rms={_micRawRms:F4} | " +
                              $"16k peak={_micPeak:F4} rms={_micRms:F4} out={converted.Length} | " +
                              $"TURN MAX raw peak={_micTurnPeak:F4} rms={_micTurnRms:F4} | " +
                              $"vad batches={_vadBatches} p={_speechProbability:F3} peakP={_vadPeakProbability:F3} " +
                              $"active={_speechActive} {_vadFrameMs:F1}ms/frame");
        }

        double seconds;
        lock (_micSamples)
        {
            _micSamples.AddRange(converted);
            seconds = (_micBufferStart + _micSamples.Count) / (double)WhisperRate;
        }

        // Hand the same audio to the endpointer. Not awaited: this runs on the capture callback, and an
        // unhandled exception on a runtime callback EXITS the .NET WASM runtime and takes the page with
        // it - so the pump owns its own error handling and this only enqueues.
        lock (_vadQueue) _vadQueue.Enqueue(converted);
        PumpVad();

        // The safety ceiling, not the endpoint. See MaxUtteranceSeconds.
        //
        // ⚠️ Only while SPEECH IS OPEN. `seconds` counts every sample since the microphone opened, and a
        // hands-free microphone is meant to sit open indefinitely waiting for someone to talk - so an
        // unconditional ceiling fires in a silent room and transcribes the two seconds of nothing that
        // TrimQuietAudio has kept, roughly every 30 s, forever. Waiting quietly is correct behaviour, not a
        // condition to recover from; the ceiling is here for a talker who never pauses (and the detector's
        // own VadOptions.MaxSpeechDuration already covers that from the other side).
        if (_speechActive && seconds >= MaxUtteranceSeconds)
        {
            _ = InvokeAsync(() => StopListeningAsync());
            return;
        }

        // The sample count IS the clock. Repaint about four times a second, not once per chunk.
        if (seconds - _listenSeconds >= 0.25)
        {
            _listenSeconds = seconds;
            _ = InvokeAsync(StateHasChanged);
        }
    }

    // ── Endpointing ───────────────────────────────────────────────────────────────────────────────────
    // Silero in the worker decides when you have stopped talking. Everything below is the plumbing that
    // keeps ONE request in flight at a time and turns the spans it returns into a slice of _micSamples.
    //
    // ⚠️ The worker holds the GPU, so the detector cannot run here. Audio goes ACROSS per batch and only
    // (start, length) comes back - never the utterance's samples, which the window already has.
    readonly Queue<float[]> _vadQueue = new();
    Task? _vadPump;
    bool _vadFailed;

    /// <summary>Speech probability of the last frame, for the level meter.</summary>
    float _speechProbability;

    /// <summary>Whether the endpointer currently believes someone is talking.</summary>
    bool _speechActive;

    /// <summary>Mean ms per 512-sample frame in the worker. The realtime budget is 32 ms.</summary>
    double _vadFrameMs;

    // ── Capture instrumentation ───────────────────────────────────────────────────────────────────────
    // WHY BOTH SIDES OF THE RESAMPLER ARE MEASURED. "The detector never reported speech" has three very
    // different causes that look identical from the status line: nothing is reaching the page at all, the
    // rate conversion is destroying it, or it arrives intact and simply never crosses VadOptions.Threshold.
    // Peak/RMS taken BEFORE and AFTER StreamingResampler separates all three in one line: loud in and
    // silent out is the resampler, silent in is capture, loud in and loud out with a low probability is
    // the threshold. Logging only the VAD probability - which is what the status line does - cannot tell
    // them apart, and that is exactly the hole this fell into.

    /// <summary>Peak absolute sample of the last raw capture chunk, before rate conversion.</summary>
    float _micRawPeak;

    /// <summary>RMS of the last raw capture chunk, before rate conversion.</summary>
    float _micRawRms;

    /// <summary>Peak absolute sample after conversion to 16 kHz - what the detector actually sees.</summary>
    float _micPeak;

    /// <summary>RMS after conversion to 16 kHz, driving the on-screen level meter.</summary>
    float _micRms;

    /// <summary>Chunks seen since the microphone opened, so a dead callback is distinguishable from a quiet one.</summary>
    int _micChunks;

    /// <summary>Sample count of the last raw chunk, to show the device's cadence.</summary>
    int _micChunkSamples;

    /// <summary>Wall clock of the last capture-path console line, so it prints about once a second.</summary>
    DateTime _micLoggedAt = DateTime.MinValue;

    /// <summary>VAD batches completed since listening started.</summary>
    int _vadBatches;

    /// <summary>Microphone level as 0-100 for the meter.</summary>
    /// <remarks>
    /// SQRT-scaled, deliberately. Speech RMS sits around 0.02-0.15 while the scale runs to 1.0, so a
    /// linear bar for normal talking is a bar that never visibly leaves zero - which is precisely the
    /// reading ("it is not hearing me") this is here to disprove or confirm. 0.2 RMS is taken as a loud
    /// talker and pinned to full scale.
    /// </remarks>
    int MicLevelPercent
    {
        get
        {
            if (_micRms <= 0) return 0;
            var v = Math.Sqrt(_micRms / 0.2);
            if (v > 1) v = 1;
            return (int)(v * 100);
        }
    }

    /// <summary>Highest speech probability seen this turn - the single most useful number after a miss.</summary>
    float _vadPeakProbability;

    // ⚠️ TURN maxima, not last-chunk values. _micRawPeak is the peak of ONE 10 ms chunk and the line is
    // printed once a second, so comparing two of those across turns compares two arbitrary instants -
    // which is exactly the wrong conclusion I drew from the first capture of this log. The detector's
    // peakP is a running maximum, so the input side has to be one too or the two halves of "did it get
    // quieter, or did the detector go deaf?" are not comparable at all.

    /// <summary>Loudest raw sample seen since listening started.</summary>
    float _micTurnPeak;

    /// <summary>Loudest raw chunk RMS seen since listening started.</summary>
    float _micTurnRms;

    /// <summary>Start one pump if none is running. Cheap and safe to call per chunk.</summary>
    void PumpVad()
    {
        lock (_vadQueue)
        {
            if (_vadPump != null && !_vadPump.IsCompleted) return;
            _vadPump = Task.Run(VadPumpAsync);
        }
    }

    async Task VadPumpAsync()
    {
        // ⚠️ Nothing may escape this method - it is started from a capture callback.
        try
        {
            while (_listening && !_vadFailed)
            {
                float[] batch;
                lock (_vadQueue)
                {
                    if (_vadQueue.Count == 0) return;
                    // Coalesce whatever piled up while the last request was in flight. One crossing with
                    // 1600 numbers beats ten with 160, and falling behind is what a fixed timer looked
                    // like from the outside.
                    int total = 0;
                    foreach (var q in _vadQueue) total += q.Length;
                    batch = new float[total];
                    int at = 0;
                    while (_vadQueue.Count > 0)
                    {
                        var q = _vadQueue.Dequeue();
                        System.Array.Copy(q, 0, batch, at, q.Length);
                        at += q.Length;
                    }
                }

                var (active, probability, spans, meanFrameMs) = await Ai.VadAsync(batch);
                _speechActive = active;
                _speechProbability = probability;
                _vadFrameMs = meanFrameMs;
                _vadBatches++;
                if (probability > _vadPeakProbability) _vadPeakProbability = probability;
                // ⚠️ The PEAK probability of the turn is the number that settles a miss. An instantaneous
                // reading sampled while nobody happens to be talking is 0.00 whether the detector is
                // healthy or dead; the running maximum is not, and it is the difference between
                // "never crossed the threshold" and "never saw a signal at all".
                Console.WriteLine($"[HF-VAD] batch {_vadBatches}: {batch.Length} samples, p={probability:F3}, " +
                                  $"peakP={_vadPeakProbability:F3}, active={active}, spans={spans.Length}, " +
                                  $"{meanFrameMs:F1} ms/frame");

                if (spans.Length > 0)
                {
                    // The turn is over. Take the first closed utterance and stop; anything the detector
                    // emitted after it belongs to the next turn, which starts with a fresh stream anyway.
                    var (start, length) = spans[0];
                    await InvokeAsync(() => StopListeningAsync(start, length));
                    return;
                }

                TrimQuietAudio();
                // Show whether it can actually hear you, and what the endpointer costs. The realtime
                // budget is 32 ms per 512-sample frame; above that the detector falls behind the
                // microphone and the turn ends late, which looks exactly like the fixed timer this
                // replaced. Printing the number is how that gets noticed instead of assumed.
                // ⚠️ The PROBABILITY is shown, not just the on/off state, and it is the first thing to look
                // at when someone says "it didn't hear me". A number that moves when you speak but never
                // reaches VadOptions.Threshold is a gain/threshold problem; a number pinned at zero is a
                // dead capture path. Those two look identical if all you print is "Listening…".
                if (_listening)
                    _status = _speechActive
                        ? $"Hearing you… (speech {_speechProbability:F2}, {_vadFrameMs:F1} ms/frame)"
                        : $"Listening… (speech {_speechProbability:F2}, {_vadFrameMs:F1} ms/frame)";
                await InvokeAsync(StateHasChanged);
            }
        }
        catch (Exception ex)
        {
            // ⚠️ Say it out loud and STOP the loop. An endpointer that has silently died is
            // indistinguishable from a room nobody is talking in - the microphone would stay open until
            // the safety ceiling and the user would conclude the feature is broken without ever being
            // told what broke. Falling back to the fixed timer quietly would be the same defect again.
            _vadFailed = true;
            _status = $"Endpointing failed, so hands-free cannot tell when you stop talking: {ex.Message}";
            _handsFree = false;
            await InvokeAsync(async () =>
            {
                if (_listening) await StopListeningAsync();
                StateHasChanged();
            });
        }
    }

    /// <summary>
    /// Drop audio from the front of the buffer that no utterance can still need.
    /// </summary>
    /// <remarks>
    /// A hands-free microphone stays open indefinitely waiting for someone to speak, so without this the
    /// buffer grows for as long as the conversation lasts. Only quiet audio is dropped: while speech is
    /// open the segment's start is not known yet, and that is bounded by VadOptions.MaxSpeechDuration.
    /// </remarks>
    void TrimQuietAudio()
    {
        if (_speechActive) return;
        int keep = (int)(SilentTailKeepSeconds * WhisperRate);
        lock (_micSamples)
        {
            int drop = _micSamples.Count - keep;
            if (drop <= 0) return;
            _micSamples.RemoveRange(0, drop);
            _micBufferStart += drop;
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
