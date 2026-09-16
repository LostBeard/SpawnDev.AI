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

    /// <summary>
    /// Cancels the in-flight generation. Non-null exactly while a turn is generating, which is what the
    /// Stop button binds its enabled state to.
    /// </summary>
    /// <remarks>
    /// ⚠️ This token reaches the WORKER. <c>AiWorkerClient.ChatStreamAsync</c> passes it as a real
    /// parameter, SpawnJS.WebWorkers marshals it, and it becomes the request's
    /// <c>IAiServerTransport.Aborted</c> - so pressing Stop halts decoding rather than just abandoning a
    /// local await while the worker keeps burning the GPU. Before 2026-09-08 the worker transport
    /// hardcoded <c>Aborted</c> to <c>CancellationToken.None</c>, so a stalled turn could only be waited
    /// out or reloaded.
    /// </remarks>
    CancellationTokenSource? _generationCts;

    /// <summary>True when the user stopped the current turn, so it can be labelled instead of read as a
    /// natural finish. The wire cannot tell us: the Ollama surface reports a cancelled generation as
    /// <c>done_reason "stop"</c> (see AiWorkerClient.ChatStreamAsync).</summary>
    bool _stoppedByUser;
    string _status = "", _busyNote = "";
    // The default is the model the demo is ABOUT, not the smallest one that runs. MEASURED here:
    // the only model in the catalogue that held a persona through a room round rather than
    // restating the scene back at the user. It costs a 1.8 GB first fetch, which the picker
    // states before anything downloads - see the catalogue note in Program.cs.
    string _model = "qwen3:1.7b-q8_0";
    string _imageModel = "sd-turbo";
    readonly List<string> _models = new();
    List<(string Name, string Note)> _imageModels = new();

    // ── Agent settings (user-editable via the ⚙️ panel) ──
    // The system prompt shapes how the model behaves. Image REQUESTS no longer depend on this text - the
    // server forces the generate_image tool on clear visual intent (AiChatEngine.ForceImageToolOnIntent) - but
    // the prompt still steers tone, refusal behavior, and when the model volunteers an image on its own.
    /// <summary>
    /// What the app calls itself when nobody has made a character yet.
    /// </summary>
    /// <remarks>
    /// 🔴 THE DEFAULT AI IS A CHARACTER TOO (Captain, 2026-09-10: "the default ai should be a default
    /// persona right and have a default avatar also"). Before this, a first visit showed a nameless
    /// "Assistant" and an empty stage, so the two things the demo is FOR - personas and avatars - were
    /// invisible until the user went and built one. The default now has a name, a personality, and a body
    /// that reacts while it talks, which is the demo demonstrating itself.
    /// <para>
    /// ⚠️ It is NOT put in the room. The room is the multi-character feature and starts empty on purpose;
    /// this is the ordinary one-on-one chat, wearing the same clothes.
    /// </para>
    /// </remarks>
    const string DefaultAgentName = "Nova";

    /// <summary>The default AI's personality, appended to its instructions.</summary>
    const string DefaultPersona =
        "Your name is " + DefaultAgentName + ". You are warm, direct and a little curious; you keep "
        + "answers short unless asked for depth, and you say plainly when you do not know something "
        + "rather than inventing it. ";

    /// <summary>
    /// What the assistant is told about its body.
    /// </summary>
    /// <remarks>
    /// 🔴 WITHOUT THIS THE AVATAR CANNOT MOVE, no matter what the page does. Captain: "the avatar seem to
    /// be 100% static, never changes. does the ai know to use it?" It did not: the body was on screen and
    /// nothing in the prompt mentioned it, so the model never wrote an action and there was never anything
    /// to perform. The room's characters are told; the default assistant was not.
    /// <para>
    /// ⚠️ It names the parts it ACTUALLY HAS. A model told it has a body writes what bodies usually do -
    /// waves, smiles, folds its arms - and this one is a head and two antennae, so unnamed parts produce
    /// directions nothing can perform. The examples are drawn from the gestures
    /// <c>SpawnDev.Reachy.GestureClassifier</c> recognises, which is the vocabulary the screen avatar and
    /// the physical Reachy Mini both perform.
    /// </para>
    /// <para>
    /// ⚠️ "NEVER for emphasis" is load-bearing. Asterisks around ordinary words are markdown emphasis, and
    /// telling the model to use them for actions makes that ambiguous - see
    /// <see cref="StageDirections.SplitForBody"/> for the guard that keeps a stray "*not*" spoken.
    /// </para>
    /// </remarks>
    /// <remarks>
    /// 🔴 "ALWAYS ANSWER IN WORDS" IS THE MOST IMPORTANT SENTENCE HERE, and it was missing. Captain, on
    /// the running demo: <c>"tilts head" not replying, not listenikng, hands free enabled</c> - the model
    /// had been told it could act and replied with an action and NOTHING ELSE. That is not an answer to
    /// anything, and in a hands-free turn it is worse than useless: there are no words to speak, so the
    /// utterance is empty and the conversation has nothing to continue from.
    /// <para>
    /// ⚠️ A prompt is guidance, not a guarantee - the speak path must survive an action-only reply
    /// whatever this says, which is why <see cref="SpeakReplyAsync"/> reopens the microphone from a
    /// finally. Both halves are needed: this makes it rare, that makes it harmless.
    /// </para>
    /// </remarks>
    const string BodyInstructions =
        "You have a small robot body on screen: a head that nods, shakes, tilts, looks up and down, leans "
        + "in and turns, and two antennae that perk up, wiggle or droop. You may show what you mean with it "
        + "by writing one physical action between asterisks, like *tilts head* or *antennae perk up*, and it "
        + "is performed rather than spoken. ALWAYS ANSWER IN WORDS AS WELL - an action is never a reply on "
        + "its own, and a reply that contains only an action has not answered at all. Use at most one per "
        + "reply, only when it genuinely adds something (curiosity, agreement, delight, sympathy), and not "
        + "on most replies. Never use asterisks for emphasis. ";

    const string DefaultSystemPrompt =
        DefaultPersona
        + BodyInstructions
        + "You are a helpful assistant running entirely on the user's own GPU in their browser. Answer "
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
        /// <summary>The user pressed Stop during this turn, so the text is deliberately partial.
        /// Distinct from <see cref="Truncated"/>, which is the model hitting the output-token cap.</summary>
        public bool Stopped;
        /// <summary>
        /// Display name of the speaker. Empty falls back to the role label ("Assistant").
        /// </summary>
        /// <remarks>
        /// ⚠️ In a room EVERY member is role "assistant", so the role alone cannot say who spoke and a
        /// three-way conversation renders as one assistant talking to itself. The name is carried on the
        /// message rather than looked up later because a character can be renamed or removed from the room
        /// afterwards, and the transcript must still say who said it at the time.
        /// </remarks>
        public string Who = "";
    }
    readonly List<Msg> _messages = new();
    string _input = "", _streaming = "";
    /// <summary>Who the in-progress bubble belongs to. Empty = the solo assistant.</summary>
    string _streamingWho = "";
    ElementReference _scrollRef;

    async Task StartAsync()
    {
        _starting = true;
        // The SHARED worker is the default again (see AiWorkerClient.PreferSharedWorker) - one AI server
        // for every tab, one copy of the model in VRAM.
        // ?worker=dedicated forces one worker per tab. Two reasons to want it: a dedicated worker's console
        // reaches the page (a shared worker's does not, so a slow load there reads as a hang), and
        // OPFSStream takes the sync access handle there, which a shared worker cannot.
        var location = JS.Get<string>("location.href");
        if (location.Contains("worker=dedicated", StringComparison.OrdinalIgnoreCase))
            Ai.PreferSharedWorker = false;
        // ?worker=shared is now a no-op against the default, kept so the choice is explicit either way.
        if (location.Contains("worker=shared", StringComparison.OrdinalIgnoreCase))
            Ai.PreferSharedWorker = true;
        // ?bench=1 runs the window-vs-worker cost benchmarks once, before anything is loaded.
        if (location.Contains("bench=1", StringComparison.OrdinalIgnoreCase))
            Ai.RunStartupBenchmarks = true;
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
            // 🔴 RESTORE THE USER'S CHOICES BEFORE ANYTHING DEPENDS ON THEM - the consent below is recorded
            // against _model, so reading the stored one afterwards would approve the wrong model.
            // ⚠️ Only honoured if the server still offers it: a model that has been removed from the
            // catalogue must not leave the picker pointing at something that cannot load.
            await Prefs.LoadAsync();
            if (Prefs.Get(AppPreferences.ModelKey) is { Length: > 0 } savedModel
                && _models.Contains(savedModel))
                _model = savedModel;
            if (_models.Count > 0 && !_models.Contains(_model)) _model = _models[0];
            try { var (def, list) = await Ai.ListImageModelsAsync(); _imageModels = list; _imageModel = def; }
            catch { _imageModels = new() { ("sd-turbo", "") }; }
            _ready = true;
            await RefreshStorageAsync();
            // Metadata only - no clip is read and nothing is prepared here. Preparing happens when a voice
            // is actually chosen, so a page load never pays for voices the user may not use.
            await LoadSavedVoicesAsync();
            // ⚠️ AFTER the saved voices are listed, because a stored id is only honoured when the voice it
            // names still exists - a voice the user deleted must not leave the picker pointing at it.
            // ⚠️ An EMPTY stored value is a real choice here ("clone me each turn"), so it is restored as
            // faithfully as any other; only a MISSING one falls back to the default.
            if (Prefs.Get(AppPreferences.VoiceKey) is { } savedVoice
                && (savedVoice.Length == 0 || VoiceExists(savedVoice)))
            {
                _voiceId = savedVoice;
                _voiceName = VoiceDisplayName(savedVoice);
            }
            // Characters are metadata only - no audio, no model - so listing them costs a directory read.
            await LoadCharactersAsync();
            // The catalogue is metadata too, and it is what lets the picker state a size before asking
            // anyone to commit to a download.
            await LoadCatalogueAsync();
            // Tool names, so a character can be granted one by name in the editor.
            await LoadToolNamesAsync();
            // Pressing "Start the AI server" is the agreement for the model that server will run - its
            // size is on the button. Every other model is agreed to separately in the model panel.
            await Consent.ApproveAsync(_model);
        }
        catch (Exception ex) { _status = $"Failed: {ex.Message}"; }
        finally { _starting = false; StateHasChanged(); }
    }

    Task SendPreset(string text) { _input = text; return SendAsync(); }

    void ToggleSettings() { _showSettings = !_showSettings; StateHasChanged(); }
    void ResetSystemPrompt() { _systemPrompt = DefaultSystemPrompt; StateHasChanged(); }

    async Task OnKeyDown(KeyboardEventArgs e)
    { if (e.Key == "Enter" && !e.ShiftKey) await SendAsync(); }

    /// <summary>
    /// Stops the in-flight generation. Safe to press at any time - a no-op when nothing is generating.
    /// </summary>
    /// <remarks>
    /// The turn does NOT throw or unwind: the generator returns what it has produced, so the partial
    /// reply is kept and rendered with a "stopped" marker. That is why this only cancels and lets
    /// <see cref="SendAsync"/>'s normal completion path run.
    /// </remarks>
    void StopGeneration()
    {
        if (_generationCts is not { IsCancellationRequested: false } cts) return;
        _stoppedByUser = true;
        _busyNote = "stopping…";
        cts.Cancel();
    }

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

        // 🔴 Never let a turn be the thing that starts a multi-GB download. The weights are fetched deep
        // inside the first generation, where nothing has asked the user anything, so the check belongs
        // here - before either path commits.
        if (!EnsureModelIsHere()) { _input = text; return; }

        // 🔴 Never let a turn be the thing that starts a multi-GB download. The weights are fetched deep
        // inside the first generation, where nothing has asked the user anything, so the check belongs
        // here - before either path commits. The typed text goes back in the box, not in the bin.
        if (!EnsureModelIsHere()) { _input = text; return; }

        // With characters in the room the turn belongs to THEM: each replies in order, hearing the ones
        // before it. The solo path below sends one message to one model with the page's system prompt,
        // which is a different conversation entirely - not a special case of the same one.
        if (RoomActive) { await RunRoomRoundAsync(text); return; }

        // ⚠️ REMEMBERED HERE rather than on the picker's change event. The model <select> uses @bind, which
        // gives no hook to run after the value lands, and _model is also set by the /model command and by
        // the model panel - three call sites to keep in step. The start of a turn is where every one of
        // them has already happened, and SetAsync is a no-op when nothing changed.
        _ = Prefs.SetAsync(AppPreferences.ModelKey, _model);
        _messages.Add(new Msg { Role = "user", Text = text });
        _busy = true; _streaming = ""; ResetSpeculativeChunk();
        _stoppedByUser = false;
        _generationCts = new CancellationTokenSource();
        string? spokenReply = null;
        _busyNote = _messages.Count(m => m.Role == "user") == 1
            ? "first message loads the model - downloads once, then cached" : "";
        // ⚠️ A turn that has produced no token yet renders an EMPTY in-progress bubble. On a cold turn
        // that state can last minutes (GGUF load + first-execution kernel compilation) and it is
        // indistinguishable from a hung page - Captain reported exactly that: "transcribed fine then
        // produced NO assistant reply in 15 minutes". The speak path already learned this lesson and grew
        // a moving counter; the chat path never did. A number that MOVES is the whole difference between
        // "slow" and "broken".
        // ⚠️ AND A MOVING NUMBER IS NOT ENOUGH EITHER. Captain, on the deployed build: "it takes it
        // roughly 1 minute to respond to the first message and the user has no idea what is going on or
        // how long it will take." The counter says a wait is happening; it cannot say that 1.71 GB is
        // coming down at 41 MB/s with 30 s to go. TrackProgressAsync asks the worker and falls back to
        // this same counter when the worker cannot answer.
        var turnStarted = DateTime.UtcNow;
        DateTime? firstDeltaAt = null;
        using var waitTicker = new CancellationTokenSource();
        // ⚠️ The fallback verb differs for the FIRST message, because what is happening differs. The first
        // message loads a model; every message after it is waiting on decoding. When the worker cannot
        // answer a progress poll - a dedicated worker blocks its own message loop while it reads OPFS
        // synchronously - this verb is all the caption has, so it has to be the right one.
        var firstOfSession = _messages.Count(m => m.Role == "user") == 1;
        var waitTickerTask = Task.Run(() => TrackProgressAsync(turnStarted, () => firstDeltaAt == null,
            firstOfSession ? "Loading the model" : "waiting for the first token", waitTicker.Token));
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
                    // Start rendering the first spoken chunk as soon as the stream has settled it, so
                    // hands-free does not sit in silence for ~9 s after the text lands. See
                    // MaybeStartSpeculativeChunk.
                    MaybeStartSpeculativeChunk();
                    if (renderClock.ElapsedMilliseconds >= 100)
                    {
                        renderClock.Restart();
                        bool doScroll = scrollClock.ElapsedMilliseconds >= 500;
                        if (doScroll) scrollClock.Restart();
                        InvokeAsync(async () => { StateHasChanged(); if (doScroll) await ScrollToBottom(); });
                    }
                },
                ct: _generationCts.Token);
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
                // ⚠️ Read from OUR flag, not from doneReason: a cancelled generation comes back as
                // "stop" on the Ollama-compatible surface, so the wire cannot distinguish it.
                Stopped = _stoppedByUser,
            };
            Console.WriteLine($"[HF-CHAT] {deltas} deltas: first token after {ttftSeconds:F1}s, "
                            + $"then {msg.TokPerSec:F1} tok/s over {decodeSeconds:F1}s "
                            + $"(total {genClock.Elapsed.TotalSeconds:F1}s)");
            msg.Text = await ResolveArtifactsAsync(_streaming, msg.Images);
            _messages.Add(msg);
            // The assistant has a body - act out whatever the reply described. Text turn or spoken turn,
            // the same reply drives it; see Home.razor.Body.cs.
            PerformReply(msg.Text);
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
            _generationCts?.Dispose();
            _generationCts = null;
            // ⚠️ Cleared HERE and not only in the ticker's own finally. Cancelling the ticker does not
            // synchronously end it, so the bubble could render one more frame carrying a progress bar for
            // work that has already finished.
            _streaming = ""; _busy = false; _busyNote = ""; _progressInfo = null; _progressPending = false;
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
        // 🔴 SINGLE ASTERISKS, which used to render as literal asterisks. Captain: "the stage direction is
        // visible in the chat (not sure if that is intentional)." It was not intentional - the assistant
        // is now asked to write actions that way, so what used to be rare model noise is on most replies.
        // ⚠️ The SAME predicate the voice uses decides which is which (StageDirections.IsStageDirection),
        // so the page and the speaker never disagree: what is drawn as an action is exactly what the voice
        // skipped and the body performed, and emphasis stays emphasis in both.
        s = System.Text.RegularExpressions.Regex.Replace(s, "\\*([^*\\n]+)\\*", m =>
            StageDirections.IsStageDirection(m.Groups[1].Value)
                ? $"<span class=\"act\">{m.Groups[1].Value}</span>"
                : $"<em>{m.Groups[1].Value}</em>");
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

    // ── SAVED VOICES ───────────────────────────────────────────────────────────────────────────────────
    // Empty = the original behaviour: clone from whatever the user last said, every turn. Set = speak from
    // a voice whose reference was turned into features ONCE.
    //
    // 🔴 WHY THIS IS A CHOICE AND NOT THE DEFAULT PATH. Cloning per turn costs on every reply: the
    // reference PCM crosses the worker as a JSON number array (a six-figure array for a few seconds at
    // 24 kHz), the engine re-trims it and re-runs the mel to rebuild prompt features that never change for
    // a voice, and the clone is taken from the RECOGNISER'S transcript of the user - which must be verbatim
    // or it bleeds into the start of every generated line. Saving a voice pays all of that once.
    /// <summary>
    /// The voice replies are spoken in. Defaults to a BUNDLED voice, never to cloning.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 THIS DEFAULTED TO EMPTY, AND EMPTY USED TO MEAN "CLONE THE USER". Captain: "it still seem to
    /// clone voice of the user every time ... when it should only clone when 'add a voice' as selected
    /// manually", and then, on the fix: "nothing should ever mean 'clone me each turn' because that is
    /// just asinine."
    /// </para>
    /// <para>
    /// He is right twice. Making the default a named voice stopped it happening by accident; deleting the
    /// MODE stopped it being reachable by accident at all. A magic empty value that starts copying a
    /// person's voice is not a setting anyone chose - it is what you get for leaving a control alone, and
    /// no amount of relabelling fixes that.
    /// </para>
    /// <para>
    /// ⚠️ EMPTY NOW MEANS NOTHING. Every id here names a real voice, bundled or saved; an empty one falls
    /// back to <see cref="BundledVoices.DefaultId"/>. Cloning is what 💾🗣️ does - once, deliberately,
    /// producing a voice with a name that is then selectable like any other, including by a persona.
    /// </para>
    /// </remarks>
    string _voiceId = BundledVoices.DefaultId;
    string _voiceName = "";
    bool _savingVoice;

    /// <summary>Name typed for the voice about to be saved. Defaults to something usable.</summary>
    string _newVoiceName = "My voice";

    /// <summary>Saved voices from OPFS - metadata only, so listing never reads a clip.</summary>
    List<SavedVoice> _savedVoices = new();

    /// <summary>
    /// Voice ids already prepared in the worker this session. Preparation is per SESSION (the engine holds
    /// features in memory), while the library is per DEVICE - so a saved voice is prepared lazily, once,
    /// the first time it is chosen after a reload.
    /// </summary>
    readonly HashSet<string> _preparedVoices = new(StringComparer.OrdinalIgnoreCase);

    async Task LoadSavedVoicesAsync()
    {
        try { _savedVoices = await Voices.ListAsync(); }
        catch (Exception ex) { Console.WriteLine($"[voices] could not list saved voices: {ex.Message}"); }
    }

    /// <summary>
    /// Choose a saved voice, preparing it in the worker if this session has not already.
    /// </summary>
    /// <remarks>
    /// ⚠️ Preparation is what costs - it derives the prompt features from the clip - so it happens HERE, on
    /// a deliberate click, and exactly once per session. Doing it at startup would make every page load pay
    /// for voices the user may not use; doing it per reply is the defect this whole feature removes.
    /// </remarks>
    async Task SelectVoiceAsync(string id)
    {
        // ⭐ A built-in voice is selected INSTANTLY - there is no clip to fetch and no features to
        // derive - so it skips the whole "Preparing ... (once)" path below. Falling through would find no
        // saved voice and silently reset the picker to the default.
        if (BundledVoices.IsBuiltIn(id))
        {
            _voiceId = id;
            _voiceName = BundledVoices.BuiltInDisplayName(id);
            _preparedVoices.Add(id);
            _status = $"Speaking as \u201c{_voiceName}\u201d.";
            await RememberVoiceAsync();
            StateHasChanged();
            return;
        }

        if (BundledVoices.Find(id) is { } bundled) { await SelectBundledVoiceAsync(bundled); return; }

        var saved = _savedVoices.FirstOrDefault(v => v.Id == id);
        if (saved == null) { await ClearSavedVoice(); return; }

        _savingVoice = true;
        _status = _preparedVoices.Contains(id) ? $"Switching to “{saved.DisplayName}”…"
                                               : $"Preparing “{saved.DisplayName}” (once)…";
        StateHasChanged();
        try
        {
            if (!_preparedVoices.Contains(id))
            {
                var samples = await Voices.ReadSamplesAsync(saved);
                if (samples == null)
                {
                    // Say which voice, and that its audio is the missing part - a picker that silently does
                    // nothing is indistinguishable from one that is broken.
                    SpeechFailed($"“{saved.DisplayName}” could not be loaded — its saved audio is missing or "
                               + "truncated. Save it again.");
                    return;
                }
                // Preparing a voice loads the ZipVoice models the first time - a real download and a real
                // GPU load, reported like every other one. Captain: "progress bars for the voice model(s)
                // and 'Preparing a voice'".
                await WithProgressAsync($"Preparing “{saved.DisplayName}”", () =>
                    Ai.PrepareVoiceAsync(saved.Id, saved.DisplayName, saved.ReferenceText,
                        samples, saved.SampleRate));
                _preparedVoices.Add(id);
            }
            _voiceId = saved.Id;
            _voiceName = saved.DisplayName;
            _status = $"Speaking as “{_voiceName}”.";
            await RememberVoiceAsync();
        }
        catch (Exception ex)
        {
            SpeechFailed($"Could not use “{saved.DisplayName}”: {ex.Message}");
        }
        finally
        {
            _savingVoice = false;
            StateHasChanged();
        }
    }

    /// <summary>
    /// Choose one of the voices included with the app, fetching and preparing it the first time.
    /// </summary>
    /// <remarks>
    /// The point of these is that a user has something to speak with BEFORE recording anyone: the per-turn
    /// cloning path needs the user to have just spoken, and a saved voice needs someone to have trained one.
    /// A bundled clip needs neither.
    /// <para>
    /// ⚠️ Prepared from <see cref="BundledVoice.Transcript"/>, which must be verbatim. A transcript
    /// containing words the clip does not say makes ZipVoice speak those words at the start of every line
    /// it generates - see the remarks on <see cref="BundledVoices"/>.
    /// </para>
    /// </remarks>
    async Task SelectBundledVoiceAsync(BundledVoice bundled)
    {
        _savingVoice = true;
        _status = _preparedVoices.Contains(bundled.Id) ? $"Switching to “{bundled.DisplayName}”…"
                                                       : $"Preparing “{bundled.DisplayName}” (once)…";
        StateHasChanged();
        try
        {
            if (!_preparedVoices.Contains(bundled.Id))
            {
                var wav = await Http.GetByteArrayAsync(bundled.Url);
                var (samples, rate) = WavCodec.Decode(wav);
                if (samples.Length == 0)
                    throw new Exception($"{bundled.Url} decoded to zero samples");
                await WithProgressAsync($"Preparing “{bundled.DisplayName}”", () =>
                    Ai.PrepareVoiceAsync(bundled.Id, bundled.DisplayName, bundled.Transcript, samples, rate));
                _preparedVoices.Add(bundled.Id);
                Console.WriteLine($"[voices] prepared bundled voice {bundled.DisplayName} "
                                + $"({samples.Length / (double)rate:F1}s, {bundled.Licence})");
            }
            _voiceId = bundled.Id;
            _voiceName = bundled.DisplayName;
            _status = $"Speaking as “{_voiceName}”.";
            await RememberVoiceAsync();
        }
        catch (Exception ex)
        {
            SpeechFailed($"Could not use “{bundled.DisplayName}”: {ex.Message}");
        }
        finally
        {
            _savingVoice = false;
            StateHasChanged();
        }
    }

    /// <summary>Delete a saved voice from this device.</summary>
    async Task DeleteVoiceAsync(string id)
    {
        try
        {
            await Voices.DeleteAsync(id);
            _preparedVoices.Remove(id);
            // ⚠️ Fall back to the DEFAULT voice, not to "". Empty means "clone me each turn", so the old
            // line quietly turned deleting a voice into opting INTO cloning - the one thing the user of a
            // delete button is least likely to have wanted.
            if (_voiceId == id)
            {
                _voiceId = BundledVoices.DefaultId;
                _voiceName = VoiceDisplayName(_voiceId);
                await RememberVoiceAsync();
            }
            await LoadSavedVoicesAsync();
            _status = "Voice deleted.";
        }
        catch (Exception ex) { SpeechFailed($"Deleting the voice failed: {ex.Message}"); }
        StateHasChanged();
    }

    /// <summary>
    /// Keep the voice just heard, so every later reply speaks in it without re-deriving anything.
    /// </summary>
    /// <remarks>
    /// ⚠️ Uses the turn's OWN transcript as the reference text, which is the best available and still only
    /// as good as the recogniser. That is a reason to let a person save a voice deliberately from a clean
    /// utterance rather than re-cloning silently from whatever the last turn happened to be.
    /// </remarks>
    async Task SaveCurrentVoiceAsync(string displayName)
    {
        if (_lastHeardSamples == null || _lastHeardSamples.Length == 0)
        {
            SpeechFailed("No audio to save a voice from yet — say something first.");
            return;
        }
        _savingVoice = true;
        _status = $"Saving the voice “{displayName}”…";
        StateHasChanged();
        try
        {
            var id = VoiceLibrary.MakeId(displayName);
            // Persist FIRST, then prepare. A voice that is prepared but not saved would work until the next
            // reload and then vanish with no way to get it back - the clip is only in memory for this turn.
            await Voices.SaveAsync(id, displayName, _lastHeardText, _lastHeardSamples, WhisperRate);
            (string VoiceId, string DisplayName, int PromptFrames, double ReferenceSeconds) prepared = default;
            await WithProgressAsync($"Preparing “{displayName}”", async () =>
                prepared = await Ai.PrepareVoiceAsync(id, displayName, _lastHeardText,
                    _lastHeardSamples, WhisperRate));
            _preparedVoices.Add(id);
            _voiceId = prepared.VoiceId;
            _voiceName = string.IsNullOrWhiteSpace(prepared.DisplayName) ? displayName : prepared.DisplayName;
            await RememberVoiceAsync();
            await LoadSavedVoicesAsync();
            _status = $"Saved “{_voiceName}” ({prepared.ReferenceSeconds:F1}s reference). "
                    + "Replies speak in it without re-cloning, and it survives a reload.";
        }
        catch (Exception ex)
        {
            SpeechFailed($"Saving the voice failed: {ex.Message}");
        }
        finally
        {
            _savingVoice = false;
            StateHasChanged();
        }
    }

    /// <summary>
    /// Fall back to the default named voice - used when the selected one has gone.
    /// </summary>
    /// <remarks>
    /// ⚠️ This used to set the voice to the empty string, which MEANT "clone the user on every reply".
    /// Losing a voice is not consent to copy somebody, and an empty value is not a setting anyone chose.
    /// </remarks>
    async Task ClearSavedVoice()
    {
        _voiceId = BundledVoices.DefaultId;
        _voiceName = VoiceDisplayName(_voiceId);
        _status = string.IsNullOrEmpty(_voiceName)
            ? "That voice is gone; no voice is available."
            : $"That voice is gone — speaking as “{_voiceName}”.";
        await RememberVoiceAsync();
        StateHasChanged();
    }

    /// <summary>
    /// Remember which voice is selected, so a reload does not silently change who is speaking.
    /// </summary>
    /// <remarks>
    /// ⚠️ Stores the empty string FAITHFULLY. Empty is a real choice here ("clone me each turn"), so
    /// treating it as "nothing stored" would put the user back on the default voice at every reload and
    /// look exactly like the app ignoring them.
    /// </remarks>
    Task RememberVoiceAsync() => Prefs.SetAsync(AppPreferences.VoiceKey, _voiceId);

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
            // ⚠️ "chat" belongs here as much as the other two. It was missing, and the chat model
            // therefore loaded and compiled INSIDE the turn: MEASURED 22.9 s waiting for the first token,
            // after the user had finished speaking. The recogniser and the voice were already being warmed
            // during the seconds the user is talking; the model that answers was not.
            // ⚠️ THE ORDER IS THE POINT, not just the contents. The server warms these SEQUENTIALLY on one
            // GPU, so this list is a schedule: whatever is late in it may not be ready when the turn wants
            // it, and whatever is early delays everything after it.
            //
            // A turn needs them in exactly this order - recognise what was said, generate the reply, then
            // speak it. MEASURED 2026-09-03: with "voice" ahead of "chat", first-token latency got WORSE
            // than warming nothing at all (22.9 s -> 28.0 s), because the chat warm sat behind the voice's
            // model downloads and never finished in time; the turn then queued behind the warm it was
            // waiting on. Voice goes last because it is both the biggest download and the last thing the
            // turn needs.
            var (_, failed) = await Ai.WarmAsync(new[] { "speech", "chat", "voice" }, _model);
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


    /// <summary>
    /// Characters per spoken chunk. Sentence ends are never crossed, so this is a target and a long
    /// sentence is spoken whole.
    /// </summary>
    /// <remarks>
    /// Smaller means the voice starts sooner and the render/play overlap has less slack; larger means fewer
    /// seams. One or two sentences is the useful range - this is not the old brevity cap, nothing is
    /// truncated at this length.
    /// </remarks>
    const int SpeakChunkCharacters = 200;

    /// <summary>
    /// Floor for every chunk but the last - the size below which a chunk cannot pay for itself.
    /// </summary>
    /// <remarks>
    /// 🔴 DERIVED FROM A MEASUREMENT, NOT PICKED. In the demo's WebGPU worker (RTX 4070, Kokoro) a
    /// synthesis costs <c>render = 5.04s + 0.22 x audio</c> and audio runs 0.0635 s per character, so:
    /// <list type="bullet">
    /// <item>a chunk only GROWS the playback lead past <c>5.04/(1-0.22)</c> = 6.4 s of audio = ~102 chars;</item>
    /// <item>the first chunk must cover the worst following render, <c>5.04 + 0.22 x (320 chars = 20.3s)</c>
    /// = 9.5 s, so it needs &gt;= 9.5 s of audio = ~150 chars.</item>
    /// </list>
    /// 200 clears both with margin. MEASURED before this floor existed: the first chunk of a 900-character
    /// reply came out at <b>82 characters</b> (5.30 s of audio) because the splitter stopped at the last
    /// sentence end under the ceiling - and chunk 2 then arrived <b>3,498 ms late</b>, an audible gap, even
    /// though the reply as a whole rendered at 0.57x realtime. A whole-reply average cannot see a stall.
    /// <para>
    /// ⚠️ THE COST IS TIME-TO-FIRST-AUDIO, and it is the right trade. The 82-char chunk started speaking at
    /// 6,189 ms; a 200-char one starts at ~7.8 s. ~1.6 s more silence once, against a gap in the middle of
    /// every reply. The real lever for first-audio is the 5.04 s FIXED per-pass cost, not the chunk size.
    /// </para>
    /// <para>
    /// ⚠️ Device-specific. A slower GPU has a larger fixed cost and needs a larger floor; past some point it
    /// cannot stream at all. Gate: <c>AiVoiceStreamingTests.ChunkedReplyStreamsWithoutAPause</c>.
    /// </para>
    /// </remarks>
    const int SpeakChunkMinimumCharacters = 200;

    /// <summary>
    /// Characters per chunk AFTER the first one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 EQUAL CHUNKS WERE THE WRONG SHAPE, and it is the mechanism behind Captain's "the pause between
    /// when it pauses reading and starts again is very large". A synthesis is not proportional to its
    /// text: MEASURED, a chunk cost ~31 s to render **1.9 s of speech**, of which the first Euler step
    /// alone was 8.7 s of 20 s - a FIXED cost paid once per synthesis. Four equal chunks therefore pay
    /// that toll four times, and since rendering is ~15x slower than realtime, no amount of pipelining can
    /// hide it: the voice speaks 1.9 s and then stalls for half a minute, every time.
    /// </para>
    /// <para>
    /// So the first chunk stays SHORT - that one is about time-to-first-audio, which chunking really does
    /// fix - and everything after it is merged into far fewer, larger renders. Same audio, same order, a
    /// fraction of the fixed cost.
    /// </para>
    /// <para>
    /// ⚠️ 320 is not arbitrary: it is the length ZipVoice is MEASURED clean to (the old
    /// <c>MaxSpokenCharacters</c>, and `AiVoiceTests` gates intelligibility at five lengths up to 343).
    /// Raising it further is a question for that gate, not for this file.
    /// </para>
    /// <para>
    /// 🔴 MEASURED AGAIN 2026-09-14, and the conclusion above is confirmed with a number:
    /// <c>chunk 2/3: silence 79828 ms (waited 79826 ms for synthesis, synth took 72980 ms), plays 20.4s</c>
    /// - 73 s of rendering for 20.4 s of audio, so <b>3.6x slower than realtime at this length</b>, and
    /// 79.826 of the 79.828 s pause was waiting for the renderer. Nothing about chunk size, playback
    /// scheduling or the 25 ms end-of-clip poll can fix that: while the renderer is slower than realtime,
    /// every extra chunk boundary is another stall, and merging them only moves the same total wait around.
    /// </para>
    /// <para>
    /// ⚠️ SO THE REMAINING LEVER IS THE VOICE ENGINE, NOT THIS FILE. A reply longer than
    /// 160 + 320 characters becomes three or more renders and stalls twice or more; the page now says so
    /// while it waits (see the render ticker in <c>SpeakCoreAsync</c>) rather than freezing on
    /// "Speaking 2/3…", which is what made it read as hung.
    /// </para>
    /// </remarks>
    const int SpeakChunkCharactersAfterFirst = 320;

    /// <summary>
    /// Split for speech, then merge everything after the first chunk into larger renders.
    /// </summary>
    /// <remarks>
    /// Sentence boundaries come from the engine's own splitter; this only decides how many of those pieces
    /// share one synthesis. See <see cref="SpeakChunkCharactersAfterFirst"/> for why that is not uniform.
    /// </remarks>
    /// <summary>
    /// Reply text -> the words actually spoken, plus the stage directions lifted out of them.
    /// </summary>
    /// <remarks>
    /// 🔴 ONE FUNCTION, BOTH CALLERS, ON PURPOSE. This is used by <c>SpeakCoreAsync</c> and by the
    /// speculative pre-render that starts during generation. If they each did their own asterisk
    /// handling they would drift, and the pre-render would produce audio for text the reply never says -
    /// the one failure mode of speculating that a listener cannot detect.
    /// </remarks>
    static (string Speakable, IReadOnlyList<string> Actions) ToSpeakableText(string text, bool inScene)
    {
        var (marked, markedActions) = StageDirections.SplitForBody(text);
        var speakable = marked;
        var actionList = new List<string>(markedActions);
        if (inScene)
        {
            var prose = SpawnDev.Reachy.SpokenText.Split(marked);
            speakable = prose.Spoken;
            actionList.AddRange(prose.Actions);
        }
        return (speakable, actionList);
    }

    // ── Speculative first chunk ──────────────────────────────────────────────────────────────────
    //
    // 🔴 THE NINE SECONDS TJ REPORTED. A reply's first chunk takes ~9.8 s to synthesise on WebGPU
    // (MEASURED 2026-09-15), and today that whole 9.8 s happens AFTER the text has finished streaming -
    // so hands-free has a nine-second hole between the answer appearing and the voice starting.
    //
    // ⭐ The first chunk is decidable long before the reply ends. SplitIntoSpeakableChunks closes a chunk
    // at a sentence end using only text already seen, so once a PREFIX already yields two chunks, chunk 0
    // can never change - appending more text cannot move a boundary that is already behind it. That is
    // the licence to start rendering it while the model is still writing.
    //
    // ⚠️ HANDS-FREE ONLY. The renderer and the LLM share one GPU in one worker, so this does not create
    // free time - it MOVES the synthesis earlier, and the text finishes slightly later for it. In
    // hands-free nobody is reading the text, so that trade is all upside; with the keyboard it would slow
    // the thing the user is actually watching.
    //
    // ⚠️ VALIDATED, NOT TRUSTED. The audio is used only if the finished reply's chunk 0 is byte-identical
    // to what was speculated. `spokenReply` is ResolveArtifactsAsync(_streaming), not the raw stream, so a
    // late artifact rewrite can change the text - in which case this is discarded and the chunk rendered
    // normally. Wrong audio is far worse than a slow start.
    string? _specChunkText;
    Task<(float[] Samples, int Rate, double Ms)>? _specChunkTask;

    void ResetSpeculativeChunk()
    {
        _specChunkText = null;
        _specChunkTask = null;
    }

    /// <summary>Starts rendering chunk 0 if the stream has settled it and nothing is rendering yet.</summary>
    void MaybeStartSpeculativeChunk()
    {
        if (!_handsFree || _specChunkTask != null || string.IsNullOrWhiteSpace(_voiceId)) return;
        var (speakable, _) = ToSpeakableText(_streaming, _room.RolePlay);
        if (string.IsNullOrWhiteSpace(speakable)) return;
        var chunks = SpeakableChunks(speakable);
        if (chunks.Count < 2) return;          // chunk 0 not settled yet - see the remarks above
        _specChunkText = chunks[0];
        _specChunkTask = SynthesizeChunkAsync(_specChunkText, _voiceId);
    }

    internal static List<string> SpeakableChunks(string speakable)
    {
        var fine = AiVoiceEngine.SplitIntoSpeakableChunks(
            speakable, SpeakChunkCharacters, SpeakChunkMinimumCharacters);
        if (fine.Count <= 1) return fine;

        var merged = new List<string> { fine[0] };
        var sb = new System.Text.StringBuilder();
        for (var i = 1; i < fine.Count; i++)
        {
            // Start a new render only when adding this piece would exceed the measured-clean length, so a
            // long reply becomes a couple of renders instead of one per sentence.
            if (sb.Length > 0 && sb.Length + 1 + fine[i].Length > SpeakChunkCharactersAfterFirst)
            {
                merged.Add(sb.ToString());
                sb.Clear();
            }
            if (sb.Length > 0) sb.Append(' ');
            sb.Append(fine[i]);
        }
        if (sb.Length > 0) merged.Add(sb.ToString());
        return merged;
    }

    /// <summary>Cancels the current spoken reply - both the chunk loop and the audio.</summary>
    CancellationTokenSource? _speakCts;

    /// <summary>
    /// Synthesise one chunk. <paramref name="voiceId"/> null falls back to the page's selected voice, so a
    /// room member speaks in ITS voice rather than whoever the page last picked.
    /// </summary>
    async Task<(float[] Samples, int Rate, double Ms)> SynthesizeChunkAsync(string chunk, string? voiceId = null)
    {
        // 🔴 ONE PATH. Captain: "nothing should ever mean 'clone me each turn' because that is just
        // asinine." He is right, and it was worse than a naming problem: an EMPTY id used to fall through
        // to cloning the user, so the absence of a choice silently started copying a person's voice on
        // every reply - the one behaviour in this app that most deserves to be asked for out loud, reached
        // by nobody doing anything. Cloning is now what the SAVE button does, once, producing a named
        // voice; there is no mode in which it happens by itself.
        var useVoice = voiceId ?? _voiceId;
        if (string.IsNullOrEmpty(useVoice)) useVoice = BundledVoices.DefaultId;
        var (samples, rate, _, ms, _) = await Ai.SpeakInVoiceAsync(chunk, useVoice);
        return (samples, rate, ms);
    }

    /// <summary>
    /// Make sure the voice we are about to speak in is prepared in the worker, preparing it if not.
    /// </summary>
    /// <remarks>
    /// 🔴 NEEDED THE MOMENT THE DEFAULT STOPPED BEING "CLONE THE USER". A selected voice used to be
    /// prepared by the PICKER, because the only way to have one selected was to pick it. Now
    /// <see cref="_voiceId"/> starts on a bundled voice that nobody has touched, so the first reply would
    /// ask the worker to speak in a voice it has never been given - and the failure would read as the
    /// voice being broken rather than as never having been loaded.
    /// <para>
    /// ⚠️ NO FALLBACK TO CLONING. Falling back would quietly restore the behaviour this change exists to
    /// remove, and it would do it exactly when the user is least likely to notice: on a failure path. A
    /// voice that cannot be prepared is reported.
    /// </para>
    /// </remarks>
    /// <param name="voice">Voice id. Empty is not a mode - it falls back to the default named voice.</param>
    /// <returns>The id that will be spoken in, prepared where possible.</returns>
    /// <summary>What to call a voice id, whichever of the three kinds it is.</summary>
    /// <remarks>
    /// ⚠️ ONE answer, in one place. Every call site used to write
    /// <c>BundledVoices.Find(id)?.DisplayName ?? saved?.DisplayName ?? ""</c>, and adding a third kind of
    /// voice meant finding all of them - the ones that were missed do not throw, they just leave the
    /// voice label blank, which reads as "no voice" while the app speaks perfectly well.
    /// </remarks>
    string VoiceDisplayName(string? id)
        => string.IsNullOrEmpty(id) ? ""
         : BundledVoices.IsBuiltIn(id) ? BundledVoices.BuiltInDisplayName(id)
         : BundledVoices.Find(id)?.DisplayName
           ?? _savedVoices.FirstOrDefault(v => v.Id == id)?.DisplayName ?? "";

    /// <summary>Whether this id names a voice that still exists and can be spoken.</summary>
    bool VoiceExists(string? id)
        => !string.IsNullOrEmpty(id)
           && (BundledVoices.IsBuiltIn(id) || BundledVoices.IsBundled(id)
               || _savedVoices.Any(v => v.Id == id));

    async Task<string> EnsureVoiceReadyAsync(string voice)
    {
        // ⚠️ Empty MEANS NOTHING here, deliberately. It used to mean "clone the user every turn", which is
        // a thing nobody asks for by leaving a control alone.
        if (string.IsNullOrEmpty(voice)) voice = BundledVoices.DefaultId;
        if (string.IsNullOrEmpty(voice) || _preparedVoices.Contains(voice)) return voice;

        // ⭐ A BUILT-IN VOICE HAS NOTHING TO PREPARE. There is no clip to fetch, no silence to trim, no
        // mel to compute and no prompt to build - the voice is a name the model already knows - so the
        // whole "Preparing ..." step that a clone needs simply does not exist for it. Falling through to
        // the clone path below would find no bundled clip and no saved voice, return quietly, and leave
        // the voice label wrong; this says so instead.
        if (BundledVoices.IsBuiltIn(voice))
        {
            _preparedVoices.Add(voice);
            _voiceName = BundledVoices.BuiltInDisplayName(voice);
            return voice;
        }

        try
        {
            if (BundledVoices.Find(voice) is { } bundled)
            {
                var wav = await Http.GetByteArrayAsync(bundled.Url);
                var (samples, rate) = WavCodec.Decode(wav);
                if (samples.Length == 0) throw new Exception($"{bundled.Url} decoded to zero samples");
                await WithProgressAsync($"Preparing “{bundled.DisplayName}”", () =>
                    Ai.PrepareVoiceAsync(bundled.Id, bundled.DisplayName, bundled.Transcript, samples, rate));
                _preparedVoices.Add(voice);
                _voiceName = bundled.DisplayName;
                return voice;
            }
            if (_savedVoices.FirstOrDefault(v => v.Id == voice) is { } saved)
            {
                var samples = await Voices.ReadSamplesAsync(saved);
                if (samples == null) throw new Exception("its saved audio is missing or truncated");
                await WithProgressAsync($"Preparing “{saved.DisplayName}”", () =>
                    Ai.PrepareVoiceAsync(saved.Id, saved.DisplayName, saved.ReferenceText,
                        samples, saved.SampleRate));
                _preparedVoices.Add(voice);
                _voiceName = saved.DisplayName;
                return voice;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[HF-SPEAK] could not prepare voice '{voice}': {ex.Message}");
        }
        return voice;
    }

    /// <summary>
    /// Stop speaking now, so the user can answer instead of waiting the reply out.
    /// </summary>
    /// <remarks>
    /// ⚠️ Cancels the CHUNK LOOP as well as the audio. Stopping only the audio would leave the next
    /// sentence rendering and then play it, so the voice would resume by itself a moment after being
    /// told to stop.
    /// </remarks>
    void StopSpeaking()
    {
        try { _speakCts?.Cancel(); } catch { /* already disposed - nothing left to stop */ }
        try { _speaker?.Stop(); } catch (Exception ex) { Console.WriteLine($"[HF-SPEAK] stop: {ex.Message}"); }
        _status = "Stopped speaking — go ahead.";
        StateHasChanged();
    }

    /// <summary>Speak one reply, then hand the microphone back.</summary>
    /// <param name="text">What to say.</param>
    /// <param name="voiceId">
    /// The voice to say it in. Null uses the page's selected voice; a room member passes its OWN, which is
    /// what makes several characters distinguishable by ear rather than all sounding like the last voice
    /// the page happened to pick.
    /// </param>
    /// <param name="resumeListening">
    /// Whether to reopen the microphone when this utterance ends. A solo reply IS the turn, so it does.
    /// </param>
    /// <remarks>
    /// 🔴 <paramref name="resumeListening"/> exists because a GROUP round speaks several times in one turn.
    /// Reopening the mic after each member would start recording the user while the next character is still
    /// to speak - so the recording captures that character's synthesised voice, and the room is talking to
    /// itself through the microphone. The round reopens it ONCE, at the end.
    /// </remarks>
    async Task SpeakReplyAsync(string text, string? voiceId = null, bool resumeListening = true)
    {
        // 🔴 THE MICROPHONE COMES BACK ON EVERY PATH. Captain, watching a hands-free session: "it is hung.
        // it should be replying or listening as hands free is enabled. that is the definition of hung."
        //
        // It was. Reopening the microphone was the LAST STATEMENT of this method, after the try/finally -
        // so every `return` inside it skipped the thing that keeps the conversation alive, and the loop
        // stopped dead with no error and no listening. Two such returns existed: no voice to clone from,
        // and nothing to say aloud.
        //
        // ⚠️ THE SECOND ONE IS NEW-ISH, and that is why this surfaced now. The assistant is now asked to
        // write physical actions, so a reply CAN be all action and no dialogue ("*nods*") - `speakable` is
        // then empty, which was a rare model quirk before and is a designed-for case today. Adding a
        // feature made a latent dead end reachable.
        try
        {
            await SpeakCoreAsync(text, voiceId);
        }
        finally
        {
            // Only now, with the speakers quiet, and only when this utterance was the whole turn - a group
            // round speaks several times and reopens the mic once, at the end (see resumeListening).
            if (resumeListening && _handsFree && !_listening)
                await ResumeListeningAsync("after speaking the reply");
        }
    }

    /// <summary>Speak one reply. Returning early is safe - <see cref="SpeakReplyAsync"/> owns the microphone.</summary>
    async Task SpeakCoreAsync(string text, string? voiceId)
    {
        _speakCts?.Dispose();
        _speakCts = new CancellationTokenSource();
        // ⚠️ THE "NOTHING TO CLONE FROM" GUARD IS GONE WITH THE MODE IT GUARDED. Speaking no longer
        // depends on having just heard the user: every voice is a named one, prepared from a clip that is
        // already on disk. There is nothing left that can fail for want of a reference.
        // The selected voice may never have been prepared - the default is a bundled one nobody picked.
        var voice = await EnsureVoiceReadyAsync(voiceId ?? _voiceId);

        try
        {
            // ⚠️ _speaking, not _busyNote. Speaking deliberately happens AFTER the turn's `finally`, so the
            // composer is usable while it talks - which also means `_busy` is false and the in-progress
            // bubble that renders `_busyNote` is not in the DOM at all. The note was being set into a
            // element nobody displays, so the first spoken reply (a cold ZipVoice load: two int8 graphs,
            // a token table and a 54 MB vocoder) showed the user a finished text answer and then nothing
            // whatsoever for minutes. Indistinguishable from "it just doesn't speak".
            _speaking = true;
            // 🔴 IT SAYS WHAT IS ACTUALLY HAPPENING. This read "Preparing the voice…" and was WRONG every
            // time: EnsureVoiceReadyAsync is awaited ABOVE, so by the time this runs the voice is already
            // prepared - and a built-in voice (the default, Kokoro) has nothing to prepare at all and
            // returns instantly. What the user was actually waiting through is the FIRST CHUNK BEING
            // SYNTHESISED, which is ~9 s on WebGPU (MEASURED 2026-09-15: time-to-first-audio 9,818 ms for
            // a 180-token chunk).
            //
            // TJ, on the GH Pages build: "there is always a roughly 9 second 'Preparing voice' before tts
            // starts after the response finishes." Nine seconds is real and is being worked; telling him
            // it was voice preparation sent him looking at the wrong thing, which is what a wrong label
            // costs. A label that names the wrong stage is worse than no label - it misdirects whoever
            // tries to fix it, including me.
            _status = "Generating speech…";
            StateHasChanged();

            // ⚠️ A STATIC string held for minutes is the same defect as no string at all. The comment above
            // records that a finished answer followed by silence was "indistinguishable from 'it just
            // doesn't speak'" - but a caption that never changes for two minutes is equally
            // indistinguishable from a hung page, and Captain read it exactly that way on the first cold
            // synthesis.
            // 🔴 AND A COUNTER IS NOT ENOUGH EITHER. Captain: "progress bars for the voice model(s) and
            // 'Preparing a voice'". The voice is a MODEL - two int8 graphs plus a 54 MB vocoder out of a
            // remote archive, MEASURED 88.7 s cold - and it downloads and loads through exactly the same
            // source and engine hooks the chat model does, so the same tracker reports it. What was
            // missing was never the numbers; it was a route from the worker to this page.
            var speakStarted = DateTime.UtcNow;
            bool firstAudioPlayed = false;
            using var speakTicker = new CancellationTokenSource();
            // Same correction as the status above - this counts up while the FIRST CHUNK RENDERS, not
            // while a voice is prepared.
            var ticker = Task.Run(() => TrackProgressAsync(speakStarted, () => !firstAudioPlayed,
                "Generating speech", speakTicker.Token));

            // ── STREAM IT: say sentence N while sentence N+1 renders ────────────────────────────────────
            //
            // 🔴 This replaces "wait for the whole reply, then start talking". Time-to-first-audio was the
            // length of the ENTIRE reply; now it is the length of the first sentence. It also retires the
            // brevity cap as a truncation: the cap made the voice stop early while the page showed text it
            // never read, and chunking says all of it.
            //
            // ⚠️ PLAYBACK IS STRICTLY SERIALISED - render ahead, play one at a time, await the end of each.
            // Firing playback per chunk without waiting makes a reply interrupt ITSELF a word or two in,
            // which is a defect this codebase has already paid for once on the robot.
            // 🔴 STAGE DIRECTIONS ARE NOT SPEECH. A character in a scene writes "*tilts head* Fine."
            // and the synthesiser, given the raw text, reads the asterisks out loud. Handled HERE rather
            // than at each call site because this is the only place text becomes audio - one guard that
            // cannot be forgotten by the next path that wants to speak something.
            //
            // ⚠️ WHICH treatment depends on whether we are in a scene, and the difference matters. In a
            // scene the model was ASKED to mark actions with asterisks, so they are lifted out. Outside
            // one, a single asterisk is ordinary emphasis - and removing the words in "I'm *not* doing
            // that" would have the voice say the OPPOSITE of what is on screen. There we keep the words
            // and drop only the markers.
            // Role-play: the SDK splitter, which also lifts out un-asterisked third-person prose that is
            // really a stage direction. Otherwise Unmark, which keeps every word - see StageDirections.
            // 🔴 THREE CASES NOW, not two. Captain: "the avatar seem to be 100% static, never changes. does
            // the ai know to use it? every ai should have and use an avatar by default". The solo
            // assistant HAS a body, so its replies carry stage directions too - but it also holds ordinary
            // question-and-answer conversations, where the SDK splitter would lift markdown emphasis out
            // of the speech and invert sentences. SplitForBody is the embodied-but-not-in-a-scene rule;
            // see StageDirections for why neither existing half is right on its own.
            // 🔴 ONE ASTERISK RULE EVERYWHERE, and that is a change. It used to branch: the SDK splitter in
            // a scene, Unmark outside one. Every character now has a body by default (Captain: "every ai
            // should have and use an avatar by default"), so RolePlay is true for essentially every room -
            // which would have put the SDK splitter, which lifts EVERY marked span, on ordinary
            // conversation. "I'm *not* doing that" spoken as "I'm doing that" is the one failure here that
            // a listener cannot detect. SplitForBody lifts real stage directions and keeps emphasis.
            //
            // ⚠️ A SCENE STILL GETS THE SDK'S PROSE EXTRACTION, which is the half SplitForBody does not do:
            // models write "Her head tilts to one side" with no markers at all, and left alone it is read
            // aloud. Running it over the ALREADY-split text is safe - SplitForBody emits no asterisks, so
            // the second pass can only do the prose work.
            var (speakable, actions) = ToSpeakableText(text, _room.RolePlay);
            if (actions.Count > 0)
                Console.WriteLine($"[HF-SPEAK] {actions.Count} stage direction(s) not spoken: "
                    + string.Join(", ", actions));
            // ⚠️ The BODY is not driven from here. Speaking is optional - the demo answers in text unless a
            // voice is chosen or hands-free is on - and an avatar that only moves when the app happens to
            // be talking is static for most users, which is exactly the report. The solo body is driven
            // from the turn itself, in SendAsync, so it acts whether or not the reply is spoken.
            if (string.IsNullOrWhiteSpace(speakable))
            {
                // The whole reply was action and no dialogue. Silence is correct - there is nothing to
                // say - but say WHY, or it reads as the voice having failed.
                // ⚠️ A SYSTEM BUBBLE, not just _status. Hands-free reopens the microphone immediately
                // after this and StartListeningAsync overwrites the status line with "Listening…", so a
                // status-only explanation is erased within a fraction of a second - the exact defect this
                // file has already paid for twice. A bubble survives, and this one names a real problem:
                // the model answered with a gesture instead of an answer.
                _status = actions.Count > 0 ? "(action only - nothing said aloud)" : "Nothing to speak.";
                if (actions.Count > 0)
                    _messages.Add(new Msg
                    {
                        Role = "system",
                        Text = $"That reply was only an action (*{string.Join("*, *", actions)}*) with "
                             + "nothing said, so there was nothing to speak. Small models do this; a "
                             + "larger one from the 📦 model panel holds a conversation better.",
                    });
                StateHasChanged();
                return;
            }
            var chunks = SpeakableChunks(speakable);
            _speaker ??= new AudioPlayback(JS);
            double spokenSeconds = 0;
            int spokenChunks = 0;

            // Chunk 0 is synthesised up front; from then on the NEXT one renders while the current plays.
            // ⭐ Reuse the chunk rendered DURING generation, but only if the finished reply asks for
            // byte-identical text. `spokenReply` is ResolveArtifactsAsync(_streaming), so a late artifact
            // rewrite can change it - and speaking audio the reply does not say is the one failure a
            // listener cannot detect. On any mismatch the speculation is dropped and this renders
            // normally, which costs exactly what it cost before.
            Task<(float[] Samples, int Rate, double Ms)> pending;
            if (_specChunkTask != null && _specChunkText == chunks[0])
            {
                pending = _specChunkTask;
                // ⚠️ "REUSED", not "without waiting". The task may still be running - whether it saved
                // anything is the `waited` figure on the first-audio line below, not this message.
                Console.WriteLine($"[HF-SPEAK] first chunk reusing the render started during generation "
                                + $"({chunks[0].Length} chars)");
            }
            else
            {
                if (_specChunkTask != null)
                    Console.WriteLine("[HF-SPEAK] pre-rendered chunk DISCARDED - the finished reply's first "
                                    + "chunk differs from what was speculated; rendering it properly");
                pending = SynthesizeChunkAsync(chunks[0], voice);
            }
            ResetSpeculativeChunk();
            DateTime lastClipEndedAt = default;
            for (int i = 0; i < chunks.Count; i++)
            {
                if (_speakCts?.IsCancellationRequested ?? false) break;

                // 🔴 SAY THAT IT IS RENDERING. MEASURED 2026-09-14 on a three-chunk reply:
                // "chunk 2/3: silence 79828 ms (waited 79826 ms for synthesis, synth took 72980 ms),
                // plays 20.4s" - 73 s to render 20.4 s of audio, 3.6x slower than realtime. Every
                // millisecond of that 79.8 s pause was waiting for the renderer, and the status line sat
                // frozen on "Speaking 2/3… (23.5s)" throughout. Captain read the result as the app being
                // hung, which is exactly what a caption that does not move for eighty seconds looks like.
                // ⚠️ A renderer slower than realtime cannot be pipelined out of existence - rendering
                // chunk N+1 while chunk N plays only buys the length of chunk N. That is a voice-engine
                // problem; this is the part the page owes the user meanwhile, which is the truth.
                var waitStarted = DateTime.UtcNow;
                using var renderTicker = new CancellationTokenSource();
                Task? renderTick = null;
                if (i > 0)
                {
                    _progressPending = true;
                    var chunkNo = i + 1;
                    var total = chunks.Count;
                    renderTick = Task.Run(async () =>
                    {
                        try
                        {
                            while (!renderTicker.IsCancellationRequested)
                            {
                                await Task.Delay(500, renderTicker.Token);
                                if (renderTicker.IsCancellationRequested) break;
                                var secs = (DateTime.UtcNow - waitStarted).TotalSeconds;
                                _busyNote = $"Rendering sentence {chunkNo} of {total}… {secs:F0}s "
                                          + "(this voice renders slower than it speaks)";
                                _status = _busyNote;
                                await InvokeAsync(StateHasChanged);
                            }
                        }
                        catch (OperationCanceledException) { /* the chunk arrived */ }
                        catch (Exception ex) { Console.WriteLine($"[HF-SPEAK] render ticker: {ex.Message}"); }
                    });
                }
                var (samples, rate, ms) = await pending;
                renderTicker.Cancel();
                if (renderTick != null)
                {
                    try { await renderTick; } catch { /* reports itself */ }
                    _progressPending = false;
                    _busyNote = "";
                }
                var waitedMs = (DateTime.UtcNow - waitStarted).TotalMilliseconds;
                // Kick the next synthesis BEFORE playing this one - that overlap is the whole point.
                pending = i + 1 < chunks.Count ? SynthesizeChunkAsync(chunks[i + 1], voice) : null!;

                if (_speakCts?.IsCancellationRequested ?? false) break;
                if (i == 0)
                {
                    firstAudioPlayed = true;
                    speakTicker.Cancel();
                    try { await ticker; } catch { /* already reported by the ticker itself */ }
                    _progressInfo = null; _progressPending = false;
                    // ⚠️ `waitedMs` IS THE NUMBER THAT SAYS WHETHER PRE-RENDERING HELPED, and it was
                    // missing here while chunk 2+ has always reported it. Reusing a speculative task is
                    // NOT the same as the audio being ready - the task may still be running - so
                    // "pre-rendered" on its own proves nothing. A small waited means the pre-render
                    // finished ahead of the reply; a waited close to the synthesis time means it started
                    // too late or was starved by the LLM sharing the GPU, and the speculation bought
                    // nothing.
                    Console.WriteLine($"[HF-SPEAK] first audio after "
                        + $"{(DateTime.UtcNow - speakStarted).TotalSeconds:F1}s (waited {waitedMs:F0} ms "
                        + $"for synthesis, synth took {ms:F0} ms)");
                }

                // 🔴 THE GAP BETWEEN CHUNKS, MEASURED rather than guessed at. Captain: "there is still a
                // long pause between tts streamed 'chunks'". Three different things could cause it and
                // they need opposite fixes: the next chunk's synthesis not being ready (render is slower
                // than playback), the wait for the previous clip to end overshooting, or the cost of
                // starting a new clip. One line separates them - silence is the interval between the last
                // clip ending and this one starting, and `waited` is how much of it was synthesis.
                var silenceMs = lastClipEndedAt == default ? 0
                    : (DateTime.UtcNow - lastClipEndedAt).TotalMilliseconds;
                var seconds = await _speaker.PlayAsync(samples, rate);
                if (i > 0)
                    Console.WriteLine($"[HF-SPEAK] chunk {i + 1}/{chunks.Count}: silence {silenceMs:F0} ms "
                        + $"(waited {waitedMs:F0} ms for synthesis, synth took {ms:F0} ms), plays {seconds:F1}s");
                spokenSeconds += seconds;
                spokenChunks++;
                _status = chunks.Count > 1
                    ? $"Speaking {i + 1}/{chunks.Count}… ({spokenSeconds:F1}s)"
                    : $"Spoke {seconds:F1}s in {ms:F0} ms";
                StateHasChanged();

                // ⚠️ Cancellable wait. Stop() ends playback; without passing the token this would sit here
                // until the audio finished on its own and the Stop button would do nothing visible.
                try { await _speaker.WaitForEndAsync(_speakCts?.Token ?? default); }
                catch (OperationCanceledException) { break; }
                lastClipEndedAt = DateTime.UtcNow;
            }

            if (_speakCts?.IsCancellationRequested ?? false)
            {
                _speaker.Stop();
                _status = $"Stopped after {spokenChunks} of {chunks.Count} — go ahead.";
            }
            else
            {
                _status = $"Spoke {spokenSeconds:F1}s";
            }
            StateHasChanged();
        }
        catch (Exception ex)
        {
            SpeechFailed($"Speaking failed: {ex.Message}");
        }
        finally
        {
            _speaking = false;
            _busyNote = "";
            _progressInfo = null;
            _progressPending = false;
            StateHasChanged();
        }
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

    /// <summary>Resume listening for the next hands-free turn, and SAY SO when that cannot happen.</summary>
    /// <remarks>
    /// 🔴 <see cref="StartListeningAsync"/> opens with <c>if (_busy || _listening) return;</c>. That is
    /// correct for a button press - a second click while transcribing should do nothing - and fatal for
    /// the hands-free loop, where it is the ONLY thing standing between one turn and the next. A resume
    /// that arrives while the previous turn is still winding down returns silently: the microphone never
    /// reopens, no message is written anywhere, and the conversation is simply over with the 💬🔊 button
    /// still lit.
    /// <para>
    /// MEASURED 2026-09-04: the Captain's session ended in exactly that state - "after it finished talking
    /// it did not start listening again" - with the page showing a stale caption and no error. A silent
    /// early return is indistinguishable from a feature that does not work.
    /// </para>
    /// <para>
    /// So: give the previous turn a moment to settle, retry, and if listening still cannot start, report
    /// it where it survives (status + a system bubble + the console), the same contract
    /// <see cref="SpeechFailed"/> follows.
    /// </para>
    /// </remarks>
    async Task ResumeListeningAsync(string why)
    {
        for (int attempt = 0; attempt < 15; attempt++)
        {
            if (!_handsFree || _vadFailed) return;   // deliberately off, or already reported elsewhere
            if (_listening) return;                  // already back on
            if (!_busy)
            {
                await StartListeningAsync();
                if (_listening) return;
            }
            await Task.Delay(200);
        }

        var msg = $"Hands-free could not resume listening {why} "
                + $"(busy={_busy}, listening={_listening}, vadFailed={_vadFailed}). Press 💬🔊 to restart.";
        _status = msg;
        _messages.Add(new Msg { Role = "system", Text = msg });
        JS.LogError($"[hands-free] {msg}");
        StateHasChanged();
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
        // 🔴 PRINT BOTH CLOCKS ON EVERY CAPTURE, not only when the result is too short to use.
        //
        // The detector answers in offsets counted from the first sample it was fed since its reset;
        // `_micBufferStart` counts what TrimQuietAudio has dropped off the front of our list. They agree
        // only while every appended sample is also fed to the detector, and when they drift the slice is
        // still a plausible length - it is simply the WRONG AUDIO, which reads as a recogniser that has
        // got worse rather than as a bookkeeping bug.
        //
        // ⚠️ MEASURED 2026-09-08 with tools/drive-hands-free.cs --turns 4, injecting the IDENTICAL 4.0 s
        // clip every turn: captured 4.0s / 4.1s / 4.6s / 4.9s and word overlap 88% / 75% / 62% / 50% -
        // monotonic in lockstep, so the window and the utterance are drifting apart per turn. The
        // too-short diagnostic could never see it because the capture is never too short. This line is
        // what makes the drift visible.
        long dbgSpanS = spanStart is long sd ? sd : -1;
        int dbgSpanN = spanLength is int nd ? nd : -1;
        int dbgCount; long dbgStart;
        lock (_micSamples) { dbgCount = _micSamples.Count; dbgStart = _micBufferStart; }
        Console.WriteLine($"[HF-CAPTURE] span start={dbgSpanS} length={dbgSpanN} "
            + $"({(dbgSpanN > 0 ? dbgSpanN / (double)WhisperRate : 0):F2}s) | _micBufferStart={dbgStart} "
            + $"_micSamples={dbgCount} (buffer covers {dbgStart}..{dbgStart + dbgCount}) | "
            + $"clamp from={(dbgSpanS < 0 ? 0 : Math.Max(0, dbgSpanS - dbgStart))} "
            + $"| vadBatches={_vadBatches}");
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

        // ⚠️ PROFILE THE SLICE, not just its bounds. A length and an offset cannot tell "the clip is in
        // there, cleanly" from "the clip is in there, mangled" - and those have completely different fixes.
        // Per-second peak/RMS is the same instrument the voice gate uses, and it separates leading quiet,
        // a truncated utterance and a level problem at a glance.
        if (captured.Length > 0)
        {
            var prof = new System.Text.StringBuilder();
            for (int sec = 0; sec * WhisperRate < captured.Length; sec++)
            {
                int from2 = sec * WhisperRate, to2 = Math.Min(captured.Length, from2 + WhisperRate);
                double sum2 = 0; float pk = 0;
                for (int i = from2; i < to2; i++)
                {
                    var v = captured[i]; var a = v < 0 ? -v : v;
                    if (a > pk) pk = a;
                    sum2 += (double)v * v;
                }
                prof.Append($"{Math.Sqrt(sum2 / Math.Max(1, to2 - from2)):F3}/{pk:F2} ");
            }
            Console.WriteLine($"[HF-CAPTURE] captured {captured.Length} samples "
                + $"({captured.Length / (double)WhisperRate:F2}s) rms/peak per second: {prof}");
        }

        if (captured.Length < WhisperRate / 2)
        {
            // 🔴 SAY WHY IT WAS SHORT. "That was too short to transcribe" is what a cough looks like AND
            // what a span pointing outside the buffer looks like, and those have opposite fixes. MEASURED
            // 2026-09-04: on the Captain's second hands-free turn the page sat on exactly this message with
            // the microphone shut, and the message could not distinguish "you said nothing" from "the
            // endpointer answered in a clock this buffer does not share".
            //
            // The two clocks: a span's `start` counts from the first sample fed to the detector since its
            // last reset; `_micBufferStart` counts samples TrimQuietAudio has dropped off the front of our
            // list. They agree only while every appended sample is also fed to the detector. Print both,
            // plus what the clamp produced, so a real cough (from<to, just brief) is instantly separable
            // from a desynchronised clock (from >= to, empty).
            long spanS = spanStart is long ss ? ss : -1;
            int spanN = spanLength is int nn ? nn : -1;
            int micCount; long micStart;
            lock (_micSamples) { micCount = _micSamples.Count; micStart = _micBufferStart; }
            long fromDbg = spanS < 0 ? 0 : Math.Max(0, spanS - micStart);
            long toDbg = spanS < 0 ? micCount : Math.Min(micCount, fromDbg + spanN);
            bool outsideBuffer = spanS >= 0 && fromDbg >= toDbg;
            Console.WriteLine($"[HF-SHORT] captured {captured.Length} samples (<{WhisperRate / 2} needed) | "
                + $"span start={spanS} length={spanN} | _micBufferStart={micStart} _micSamples={micCount} | "
                + $"clamp from={fromDbg} to={toDbg} | vadBatches={_vadBatches} "
                + $"| handsFree={_handsFree} vadFailed={_vadFailed} busy={_busy} "
                + $"=> {(_vadFailed ? "THE ENDPOINTER DIED - this stop is a consequence, see [HF-VAD-FAILED]"
                        : outsideBuffer ? "SPAN POINTS OUTSIDE THE BUFFER - the detector's clock and this buffer disagree"
                        : "genuinely brief audio")}");
            // 🔴 DO NOT OVERWRITE A FAILURE MESSAGE WITH THE COSMETIC ONE.
            // MEASURED 2026-09-04: the endpointer threw, its catch block wrote the real diagnosis
            // ("Endpointing failed, so hands-free cannot tell when you stop talking: <ex>") and then
            // called StopListeningAsync() to shut the microphone. This branch ran a moment later and
            // replaced that sentence with "That was too short to transcribe." - so the Captain saw a
            // cough message for a dead detector, hands-free silently off, and no way to tell the two
            // apart. The turn had captured 800 samples because the detector died ~50 ms in, not
            // because anybody coughed.
            // The same rule the hands-free toggle already documents: reporting a problem and then
            // erasing it one statement later is the defect, not the reporting.
            if (!_vadFailed)
                _status = outsideBuffer
                    ? "The endpointer reported an utterance this recording does not contain "
                      + $"(span {spanS}+{spanN}, buffer {micStart}..{micStart + micCount}). See [HF-SHORT] in the console."
                    : "That was too short to transcribe.";
            StateHasChanged();
            // Hands-free must go back to listening rather than ending the conversation on a cough.
            if (_handsFree && !_vadFailed) await ResumeListeningAsync("after a too-short capture");
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
        else if (!_listening && !_vadFailed) await ResumeListeningAsync("after a blank transcript");
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

            // 🔴 PUT THE EXCEPTION SOMEWHERE IT CANNOT BE ERASED.
            // MEASURED 2026-09-04: _status was the ONLY record of why hands-free died, and
            // StopListeningAsync's "too short to transcribe" branch overwrote it microseconds later.
            // The result was a conversation that ended itself, blamed the Captain's microphone, and
            // threw away the only sentence naming the real cause. The console line and the system
            // bubble both outlive _status; the bubble is what survives into a screenshot.
            // Type AND stack, not just Message: "Object reference not set" names nothing on its own.
            Console.WriteLine($"[HF-VAD-FAILED] batch {_vadBatches}: {ex.GetType().FullName}: {ex.Message}\n{ex.StackTrace}");
            JS.LogError($"[hands-free] endpointer died on batch {_vadBatches}", ex.ToString());
            _messages.Add(new Msg
            {
                Role = "system",
                Text = $"Hands-free stopped: the endpointer failed on batch {_vadBatches} "
                     + $"({ex.GetType().Name}: {ex.Message}). See [HF-VAD-FAILED] in the console."
            });

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
