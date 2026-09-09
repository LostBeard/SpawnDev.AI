using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using SpawnDev.ILGPU.ML.GGUF;
using SpawnDev.ILGPU.ML.Pipelines;
using SpawnDev.ILGPU.ML.Preprocessing;

namespace SpawnDev.AI.Server;

/// <summary>
/// The transport-neutral chat service over SpawnDev.ILGPU.ML: chat templating (ChatML / Llama3 /
/// gemma detected from the GGUF), the serialized-generation registry, tool-call parsing, and the
/// streaming tool-markup holdback. Protocol adapters (Ollama / OpenAI / Anthropic, over HTTP or a
/// browser-worker MessagePort) all drive this one class - extracted from the proven
/// Examples/06.OllamaServer.Console generation bridge.
/// </summary>
public sealed class AiChatEngine : IAiChatService
{
    private readonly ModelRegistry _registry;

    /// <summary>Server-wide cap on requested output tokens - agentic clients ask for huge values
    /// (Claude CLI: 32000) that a small local model would ramble into. Requests are clamped to this.</summary>
    public int MaxOutputTokens { get; set; } = 1024;

    /// <summary>Optional perf-log sink: one line per generation (prompt/reused/prefill/decode split).</summary>
    public Action<string>? PerfLog { get; set; }

    public AiChatEngine(ModelRegistry registry) => _registry = registry;

    /// <summary>The registry (model listing for protocol adapters).</summary>
    public ModelRegistry Registry => _registry;

    private string? _warmedModel;

    /// <summary>
    /// Make <paramref name="model"/> resident AND compiled, by running a real short generation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ WHY THIS EXISTS. The recogniser and the voice are warmed while the user is talking; the chat
    /// model was not warmed at all, so it loaded and compiled INSIDE the turn. MEASURED in the demo,
    /// 2026-09-03: a hands-free turn waited <b>22.9 s for the first token</b> and the app's own status text
    /// said why - "loading the model and compiling kernels". That is the whole first-token latency of a
    /// conversation, spent after the user has finished speaking and is waiting.
    /// </para>
    /// <para>
    /// ⚠️ LOADED IS NOT WARM - the lesson the VAD and the voice both taught. Kernels compile on FIRST
    /// EXECUTION, not at load, so acquiring a lease and returning would move the download and leave the
    /// compile exactly where it was. This runs a real generation through the real path.
    /// </para>
    /// <para>
    /// ⚠️ Several tokens, not one. One token is a prefill plus a single decode step, and the WebGPU decode
    /// capture needs repeated steps before it can record and replay - the same reason Silero needed several
    /// warm frames rather than one. A single-token warm would leave the capture to happen in the user's
    /// turn, which is the cost this is here to remove.
    /// </para>
    /// <para>
    /// Idempotent per model: warming is keyed on the model that was warmed, so switching models re-warms
    /// and repeat calls are free.
    /// </para>
    /// </remarks>
    public async Task EnsureReadyAsync(string model, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(model)) throw new ArgumentException("No model to warm.", nameof(model));
        if (_warmedModel == model) return;

        var clock = System.Diagnostics.Stopwatch.StartNew();
        await ChatAsync(new AiChatRequest
        {
            Model = model,
            Messages = new[] { new AiChatMessage("user", "Hi") },
            Options = new AiGenerationOptions { MaxOutputTokens = 8, Strategy = "greedy" },
        }, ct).ConfigureAwait(false);

        _warmedModel = model;
        PerfLog?.Invoke($"[AiChatEngine] warm decode of {model}: {clock.Elapsed.TotalSeconds:F1}s "
                      + "(load + kernel compilation + decode capture; a real turn after this should reach "
                      + "its first token quickly - if it does not, the cost is the graph, not the compile)");
    }

    public Task<IReadOnlyList<AiModelInfo>> ListModelsAsync(CancellationToken ct = default)
        => _registry.Provider.ListAsync(ct);

    public Task<AiModelInfo?> ShowModelAsync(string name, CancellationToken ct = default)
        => _registry.Provider.ShowAsync(name, ct);

    public async Task<int> CountTokensAsync(string model, IReadOnlyList<AiChatMessage> messages, CancellationToken ct = default)
    {
        using var lease = await _registry.AcquireAsync(FirstOrDefaultModel(model), ct).ConfigureAwait(false);
        var lm = lease.Model;
        var (promptIds, _) = ChatTemplates.BuildChatPrompt(lm.Gguf, lm.Tokenizer, ToTuples(messages));
        return promptIds.Length;
    }

    public Task<AiChatResult> ChatAsync(AiChatRequest request, CancellationToken ct = default)
        => GenerateAsync(request, onDelta: null, ct);

    public Task<AiChatResult> ChatStreamAsync(AiChatRequest request, Func<string, Task> onDelta, CancellationToken ct = default)
        => GenerateAsync(request, onDelta, ct);

    /// <summary>Server-side tools (generate_image etc.). When set AND the client sent no tools of
    /// its own, tool definitions are injected and the model's calls are EXECUTED here - the agentic
    /// loop: call → execute → tool_response → continue (bounded rounds). Client tools always win.</summary>
    public AiToolRegistry? Tools { get; set; }

    /// <summary>Max server-tool execution rounds per generation (loop guard).</summary>
    public int MaxToolRounds { get; set; } = 3;

    /// <summary>When true (default) and a <c>generate_image</c> server tool is available, a user message
    /// that clearly asks to CREATE a visual pre-emptively FORCES the image tool instead of trusting the
    /// model to emit the call. A small instruct model (qwen2.5-0.5b) REFUSES ~40% of plain image requests
    /// ("draw a cat" → "I'm sorry, I can't draw") because the tool call is its own stochastic decision, and
    /// the refusal is often the argmax (greedy doesn't help - measured 2026-07-13). Forcing writes only the
    /// CAPTION with the model (prefill-committed to the tool call), so there is no refusal path. Set false to
    /// restore pure model-driven routing.</summary>
    public bool ForceImageToolOnIntent { get; set; } = true;

    /// <summary>The image tool this engine force-calls on visual-creation intent (see
    /// <see cref="ForceImageToolOnIntent"/>).</summary>
    public string ImageToolName { get; set; } = "generate_image";

    /// <summary>When true (default) and a <c>github_lookup</c> tool is available, a user message about
    /// SpawnDev is GROUNDED: the engine pre-fetches the authoritative GitHub info and injects it as reference
    /// context, instead of trusting the model to call the tool. A small model never calls it (0/5 measured)
    /// and instead answers INACCURATELY from memory - reductive or invented (e.g. reducing the six-backend GPU
    /// compute library SpawnDev.ILGPU to "OpenCL on Linux", or getting WebTorrent wrong). Grounding makes the
    /// answer correct regardless of model. Set false for pure model-driven tool use.</summary>
    public bool GroundGitHubOnIntent { get; set; } = true;

    /// <summary>The GitHub lookup tool consulted for SpawnDev grounding (see <see cref="GroundGitHubOnIntent"/>).</summary>
    public string GitHubToolName { get; set; } = "github_lookup";

    private async Task<AiChatResult> GenerateAsync(AiChatRequest request, Func<string, Task>? onDelta, CancellationToken ct)
    {
        // Server-tool injection (client tools take precedence). Tools that ground (IAiGroundingProvider) are
        // NOT advertised as callable: grounding already injects their answer as context, and dangling the tool
        // in front of a small model just makes it emit malformed calls (or try to "look up" the capital of
        // France) instead of answering. They still ground below; only NON-grounding tools stay model-callable.
        var serverTools = request.ToolsJson == null ? Tools?.List() : null;
        IReadOnlyList<string>? toolsJson = request.ToolsJson;
        if (serverTools is { Count: > 0 })
        {
            // Exclude grounding tools from the callable list ONLY while grounding is on (it already injects
            // their answer). With grounding OFF - e.g. a capable model that reliably tool-calls - they become
            // model-callable again, so the Ground toggle coherently switches between grounding and native use.
            var callable = serverTools.Where(t =>
                // Grounding tools: excluded while grounding is on (it already injects their answer as context).
                (t is not IAiGroundingProvider || !GroundGitHubOnIntent)
                // The image tool: excluded while force-on-intent is on. ForceImageToolOnIntent CREATES images
                // without the model ever calling the tool (registry-driven, see TryForcedImageAsync), so dangling
                // its JSON schema in the chat is pure downside - a model not trained for tool-calling (LFM2)
                // drowns in the schema and emits garbage ("generate_image, tool_name, function...") instead of
                // answering "what is a chicken?". Symmetric to the grounding exclusion above; images still work.
                && !(ForceImageToolOnIntent && string.Equals(t.Name, ImageToolName, StringComparison.OrdinalIgnoreCase))
            ).ToList();
            toolsJson = callable.Count > 0 ? callable.Select(t =>
            {
                using var schema = System.Text.Json.JsonDocument.Parse(t.ParametersJsonSchema);
                return System.Text.Json.JsonSerializer.Serialize(new
                {
                    type = "function",
                    function = new { name = t.Name, description = t.Description, parameters = schema.RootElement.Clone() },
                });
            }).ToList() : null;
        }

        // Pre-emptive image-tool forcing: when the latest user turn clearly asks to CREATE a visual and the
        // generate_image tool is available, force it rather than gamble on the model choosing to call it. This
        // runs BEFORE the normal generation so a refusal is never produced or streamed. Falls through to the
        // normal path if the caption/execution fails.
        if (ForceImageToolOnIntent && serverTools is { Count: > 0 }
            && serverTools.Any(t => string.Equals(t.Name, ImageToolName, StringComparison.OrdinalIgnoreCase))
            && HasImageIntent(LastUserMessage(request.Messages)))
        {
            var forced = await TryForcedImageAsync(request, toolsJson, onDelta, ct).ConfigureAwait(false);
            if (forced is not null) return forced;
        }

        var messages = request.Messages.ToList();

        // Grounding: a small model won't call a lookup tool for questions it should (github_lookup: 0/5
        // measured) and answers inaccurately from memory instead. Any registered tool that implements IAiGroundingProvider gets
        // to inspect the latest user turn and return authoritative reference text, which we inject as context
        // so the answer is grounded, not invented. The GitHub tool grounds SpawnDev library/crew/app questions.
        if (GroundGitHubOnIntent && LastUserMessage(messages) is { Length: > 0 } lastForGrounding
            && serverTools?.FirstOrDefault(t => string.Equals(t.Name, GitHubToolName, StringComparison.OrdinalIgnoreCase)) is IAiGroundingProvider grounder)
        {
            var reference = await grounder.GetGroundingAsync(lastForGrounding, ct).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(reference)) messages = WithReference(messages, reference);
        }
        List<AiToolArtifact>? artifacts = null;
        AiChatResult result;
        int round = 0;
        while (true)
        {
            result = await GenerateOnePassAsync(request, messages, toolsJson, onDelta, ct).ConfigureAwait(false);
            if (serverTools is not { Count: > 0 } || result.ToolCalls.Count == 0 || ++round > MaxToolRounds)
                break;
            // Execute the model's calls server-side and continue the conversation with the results.
            messages.Add(new AiChatMessage("assistant", result.Text));
            foreach (var call in result.ToolCalls)
            {
                var tool = Tools!.Get(call.Name);
                var exec = tool != null
                    ? await tool.ExecuteAsync(call.ArgumentsJson, ct).ConfigureAwait(false)
                    : new AiToolExecutionResult($"Unknown tool '{call.Name}'.") { IsError = true };
                if (exec.Artifacts is { Count: > 0 })
                    (artifacts ??= new()).AddRange(exec.Artifacts);
                // qwen/ChatML convention (same as the protocol routers): tool results re-enter as a
                // user turn wrapped in <tool_response>.
                messages.Add(new AiChatMessage("user", $"<tool_response>\n{exec.TextForModel}\n</tool_response>"));
            }
        }
        if (artifacts == null) return result;
        // Deterministic artifact references: append markdown refs the ENGINE controls (models told
        // "don't repeat the id" won't reliably echo it). Every surface - typed clients, worker, and
        // plain HTTP protocol clients - can resolve ai-artifact://{id} via /ai/artifacts/{id}.
        var refs = string.Join("\n", artifacts.Select(a => $"![{a.Label ?? "generated image"}](ai-artifact://{a.Id})"));
        // STREAMING clients read only the delta stream, never this return value - so the refs must be
        // streamed too, or an image the model generated never reaches the browser. onDelta here is the
        // RAW protocol callback (not the tool-markup streamer), so refs pass through verbatim. Emitted
        // after the final round's prose = same order as the non-streamed Text below.
        if (onDelta != null)
            await onDelta("\n\n" + refs).ConfigureAwait(false);
        return result with
        {
            Text = result.Text.TrimEnd() + "\n\n" + refs,
            TextWithoutToolCalls = result.TextWithoutToolCalls.TrimEnd() + "\n\n" + refs,
            Artifacts = artifacts,
        };
    }

    private async Task<AiChatResult> GenerateOnePassAsync(AiChatRequest request, IReadOnlyList<AiChatMessage> messages,
        IReadOnlyList<string>? toolsJson, Func<string, Task>? onDelta, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        using var lease = await _registry.AcquireAsync(request.Model, ct).ConfigureAwait(false);
        var lm = lease.Model;
        // 🔴 `thinking` DEFAULTS TO TRUE in ChatTemplates, and not passing it is why gemma4:12b replied
        // "<|channel>thought <channel|>Jupiter is the largest planet..." - its reasoning channel arrived as
        // visible text, to be rendered in the bubble and read aloud by the TTS. For gemma4 the template
        // switch is the real control (it builds the prompt tokens itself); the prefill below covers
        // Qwen3-style models, and the filters cover anything that reasons anyway.
        var (promptIds, stopIds) = ChatTemplates.BuildChatPrompt(lm.Gguf, lm.Tokenizer, ToTuples(messages),
            thinking: !SuppressThinking, toolsJson: toolsJson);
        if (SuppressThinking)
        {
            var before = promptIds.Length;
            promptIds = WithThinkingSuppressed(promptIds, lm.Tokenizer);
            // ⚠️ ANNOUNCE IT. "The model reasoned anyway" and "my prefill never applied" produce the
            // identical reply, so without this line a failing run invites a theory about the model when
            // the honest reading is that the code did not run.
            PerfLog?.Invoke(promptIds.Length > before
                ? $"thinking suppressed: +{promptIds.Length - before} prefill tokens"
                : "thinking NOT suppressed: no <think> token in this vocabulary (not a reasoning model)");
        }
        long buildMs = sw.ElapsedMilliseconds;

        var cfg = ToConfig(request.Options);
        long firstMs = -1;

        Func<string, Task>? wrapped = null;
        ToolAwareStreamer? streamer = null;
        ThinkingStreamer? thinking = null;
        if (onDelta != null)
        {
            // Tool requests stream text but must never leak tool-call markup as visible text; the
            // holdback logic (extracted from the proven Anthropic streaming path) buffers the longest
            // possible partial "<tool_call>" suffix and stops text at the first full tag.
            streamer = toolsJson != null ? new ToolAwareStreamer(onDelta) : null;
            Func<string, Task> sink = streamer != null ? streamer.PushAsync : onDelta;

            // 🔴 THINKING IS FILTERED FIRST, AND ALWAYS - not only for tool requests. A hybrid reasoning
            // model (Qwen3) emits its chain of thought in <think>…</think> ahead of the answer, and nothing
            // downstream removes it: it renders in the bubble, it is what a room hands the next speaker as
            // something that was said out loud, and hands-free READS IT ALOUD. Filtering in the STREAM
            // rather than at the end matters - a final-only strip would let the whole monologue type itself
            // across the screen and then vanish.
            thinking = new ThinkingStreamer(sink);
            wrapped = async d =>
            {
                if (firstMs < 0) firstMs = sw.ElapsedMilliseconds;
                await thinking.PushAsync(d).ConfigureAwait(false);
            };
        }

        var res = await lm.Generator.GenerateAsync(promptIds, cfg, request.Options.Stops, stopIds, wrapped, ct)
            .ConfigureAwait(false);

        // ⚠️ Parse tool calls from the VISIBLE text, not the raw. A model that reasons about calling a
        // tool writes the call inside its think block ("maybe I should <tool_call>…"); parsing the raw
        // text would EXECUTE a call the model was only considering.
        var visible = StripThinking(res.Text);
        var calls = toolsJson != null
            ? ChatTemplates.ParseToolCalls(visible).Select(tc => new AiToolCall(tc.Name, tc.ArgumentsJson)).ToList()
            : new List<AiToolCall>();

        // Release any tail the thinking filter was holding (a trailing "<thi" that never became a tag)
        // BEFORE the tool streamer flushes, since it feeds into it.
        if (thinking != null) await thinking.FlushTailAsync().ConfigureAwait(false);
        if (streamer != null && calls.Count == 0)
            await streamer.FlushTailAsync().ConfigureAwait(false);   // no tool call - release the held-back tail

        // Thinking that never closed means the budget ran out mid-monologue: there IS no answer, and the
        // caller would otherwise get a silent empty reply with no idea why.
        if (visible.Trim().Length == 0 && res.Text.Contains("<think>", StringComparison.OrdinalIgnoreCase))
            PerfLog?.Invoke($"THINKING NEVER CLOSED on {request.Model}: the whole {res.GeneratedTokens}-token "
                + "budget went into reasoning, so the reply is empty. Raise MaxOutputTokens.");

        PerfLog?.Invoke(
            $"{(onDelta != null ? "stream" : "once"),-6} prompt={promptIds.Length,6}tok reused={lm.Generator.LastReusedPrefix,6}tok " +
            $"TTFT={(firstMs >= 0 ? firstMs : sw.ElapsedMilliseconds),7}ms total={sw.ElapsedMilliseconds,7}ms " +
            $"gen={res.GeneratedTokens,5}tok stop={res.Stop}");

        return new AiChatResult(visible, res.PromptTokens, res.GeneratedTokens, ToStopKind(res.Stop), calls)
        {
            TextWithoutToolCalls = calls.Count > 0 ? StripToolCalls(visible).Trim() : visible,
        };
    }

    // ── Pre-emptive image-tool forcing ──
    // The model REFUSES ~40% of plain image requests and the refusal is the greedy argmax, so no sampling or
    // prompt tweak makes it reliable (measured on qwen2.5-0.5b, both Ollama + HF GGUFs, 2026-07-13). When the
    // user clearly wants an image we bypass the routing decision entirely and run generate_image ourselves.
    //
    // CAPTION SOURCE — deterministic first, model only as fallback (PERF, 2026-07-13). Stripping the imperative
    // wrapper off the user text ("draw a cat" → "a cat") yields a caption ~identical to what the model writes,
    // and BETTER for detailed prompts (no 48-token truncation). Critically, deriving it touches NO model, so a
    // resident SD-Turbo stays resident across consecutive image requests (~10s warm, like the direct button).
    // The old always-run model caption pass thrashed VRAM: captioning loaded the LLM (evicting SD-Turbo), then
    // image-gen evicted the LLM to reload SD-Turbo — two big loads per image (~35s). We only fall back to a
    // model caption pass when the derived caption has no subject (e.g. bare "make a picture").
    private async Task<AiChatResult?> TryForcedImageAsync(AiChatRequest request, IReadOnlyList<string>? toolsJson,
        Func<string, Task>? onDelta, CancellationToken ct)
    {
        string userMsg = LastUserMessage(request.Messages) ?? "";
        string caption = DeriveCaption(userMsg);
        if (!IsUsableCaption(caption))
        {
            // Subject-less request - let the model invent one (accepts the model-load cost for this rare case).
            var modelCaption = await TryModelCaptionAsync(request, toolsJson, ct).ConfigureAwait(false);
            if (IsUsableCaption(modelCaption)) caption = modelCaption!;
        }
        if (!IsUsableCaption(caption)) return null;   // nothing usable - let the normal path answer

        var tool = Tools?.Get(ImageToolName);
        if (tool == null) return null;
        var argsJson = JsonSerializer.Serialize(new { prompt = caption });
        var exec = await tool.ExecuteAsync(argsJson, ct).ConfigureAwait(false);
        var call = new AiToolCall(ImageToolName, argsJson);

        if (exec.Artifacts is not { Count: > 0 })
        {
            // Image generation itself failed (e.g. the diffusion model could not load). Surface that plainly
            // rather than falling through to a confusing "I can't make images" model refusal.
            if (exec.IsError)
            {
                if (onDelta != null) await onDelta(exec.TextForModel).ConfigureAwait(false);
                return new AiChatResult(exec.TextForModel, 0, 0, AiStopKind.Stop, new[] { call })
                { TextWithoutToolCalls = exec.TextForModel };
            }
            return null;
        }

        var artifacts = exec.Artifacts.ToList();
        // Deterministic artifact refs the ENGINE controls (same as the agentic-loop path). The image is the
        // response; every surface resolves ai-artifact://{id}. The demo strips the ref markdown and paints the
        // image, so this stays image-only (no chatty preamble a forced call shouldn't invent).
        var refs = string.Join("\n", artifacts.Select(a => $"![{a.Label ?? caption}](ai-artifact://{a.Id})"));
        if (onDelta != null) await onDelta(refs).ConfigureAwait(false);
        return new AiChatResult(refs, 0, 0, AiStopKind.Stop, new[] { call })
        {
            TextWithoutToolCalls = "",
            Artifacts = artifacts,
        };
    }

    private static string? LastUserMessage(IReadOnlyList<AiChatMessage> messages)
    {
        for (int i = messages.Count - 1; i >= 0; i--)
            if (string.Equals(messages[i].Role, "user", StringComparison.OrdinalIgnoreCase))
                return messages[i].Content;
        return null;
    }

    // A visual-CREATION verb paired with a visual noun (or an imperative draw/paint/sketch opener). Conservative
    // on purpose: it must fire on real image requests ("draw a cat", "generate an image of a robot", "I want a
    // photo of a lake", "show me a picture of paris") without hijacking ordinary chat that merely mentions a
    // picture. Only consulted for the LATEST user turn.
    private static readonly Regex ImperativeDraw = new(
        @"^\s*(please\s+|can\s+you\s+|could\s+you\s+|would\s+you\s+)*(draw|sketch|paint|illustrate|render)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex CreateVisual = new(
        @"\b(draw|sketch|paint|illustrate|render|generate|create|make|produce|show|give|want|need|design|imagine)\b[\s\w,.'-]{0,40}?\b(image|images|picture|pictures|pic|pics|photo|photos|photograph|drawing|painting|illustration|art|artwork|wallpaper|logo|portrait|selfie|meme|icon|scene|render|visual)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>True when the message clearly asks the assistant to CREATE a visual (image-generation intent).</summary>
    public static bool HasImageIntent(string? message)
        => !string.IsNullOrWhiteSpace(message) && (ImperativeDraw.IsMatch(message) || CreateVisual.IsMatch(message));

    private static List<AiChatMessage> WithReference(IReadOnlyList<AiChatMessage> messages, string reference)
    {
        var block = "Reference information from the SpawnDev GitHub (authoritative - base your answer on this, "
            + "do not contradict it or invent details):\n\n" + reference;
        var list = messages.ToList();
        int sys = list.FindIndex(m => string.Equals(m.Role, "system", StringComparison.OrdinalIgnoreCase));
        if (sys >= 0) list[sys] = new AiChatMessage("system", list[sys].Content + "\n\n" + block);
        else list.Insert(0, new AiChatMessage("system", block));
        return list;
    }

    // Fallback caption path: ask the model for the caption by prefilling the assistant turn with the
    // generate_image tool-call opener so it can ONLY write the caption string; stop at the closing quote. Used
    // only when the deterministic DeriveCaption produced no subject - so it (and its LLM load) rarely runs.
    private async Task<string?> TryModelCaptionAsync(AiChatRequest request, IReadOnlyList<string>? toolsJson, CancellationToken ct)
    {
        try
        {
            using var lease = await _registry.AcquireAsync(request.Model, ct).ConfigureAwait(false);
            var lm = lease.Model;
            var (promptIds, stopIds) = ChatTemplates.BuildChatPrompt(lm.Gguf, lm.Tokenizer, ToTuples(request.Messages),
                toolsJson: toolsJson);
            // Ordinary tokens for ChatML/qwen (not specials), so plain Encode matches the template's tokenization.
            const string prefix = "<tool_call>\n{\"name\": \"generate_image\", \"arguments\": {\"prompt\": \"";
            var forcedIds = promptIds.Concat(lm.Tokenizer.Encode(prefix)).ToArray();
            var cfg = new GenerationConfig { MaxNewTokens = 48, Strategy = "greedy" };
            var res = await lm.Generator.GenerateAsync(forcedIds, cfg, new[] { "\"", "\n", "</tool_call>" }, stopIds, null, ct)
                .ConfigureAwait(false);
            return CleanCaption(res.Text);
        }
        catch { return null; }   // caption pass failed - caller keeps whatever it had
    }

    // A caption is usable when, after dropping filler words (articles, "of", stray verbs the strip left), at
    // least one real subject token of length >= 2 remains. Rejects empties and pure-filler like "a" / "of the".
    private static readonly HashSet<string> CaptionFiller = new(StringComparer.OrdinalIgnoreCase)
    { "a", "an", "the", "some", "of", "me", "it", "this", "that", "please", "draw", "paint", "sketch", "picture", "image", "photo" };
    private static bool IsUsableCaption(string? caption)
    {
        if (string.IsNullOrWhiteSpace(caption)) return false;
        var s = caption.Trim();
        if (s.Length is < 2 or >= 300) return false;
        foreach (var w in s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            var t = w.Trim('.', ',', '!', '?', '"', '\'', ':', ';');
            if (t.Length >= 2 && !CaptionFiller.Contains(t)) return true;
        }
        return false;
    }

    // Trim the model's caption to a clean single line (drop a trailing quote/brace/backslash and any tail after
    // a newline). Guards against the model running past the caption before the stop string bites.
    private static string CleanCaption(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return "";
        var s = raw.Trim();
        int nl = s.IndexOf('\n'); if (nl >= 0) s = s[..nl];
        s = s.TrimEnd('"', '}', '\\', ' ', '\t', ',');
        s = s.Trim().Trim('"').Trim();
        return s.Length is > 1 and < 300 ? s : "";
    }

    // Deterministic fallback caption: strip a leading polite/imperative wrapper ("please draw me a picture of ")
    // so the remaining subject phrase drives the diffusion model. Returns the whole message if nothing strips.
    // Each token is matched as a WHOLE word (it must be followed by whitespace, consumed with it) and
    // overlapping alternatives are ordered longest-first ("an" before "a", "photograph" before "photo"),
    // so we never chop a prefix off a word - e.g. "an image" must not strip to "n image".
    private static readonly Regex CaptionStrip = new(
        @"^\s*(?:(?:please|hey|can\s+you|could\s+you|would\s+you|i\s+(?:want|need|would\s+like)(?:\s+you\s+to)?)\s+)*"
        + @"(?:(?:draw|sketch|paint|illustrate|render|generate|create|make|produce|show|give|design|imagine)\s+)?"
        + @"(?:me\s+)?(?:(?:an|a|some|the)\s+)?"
        + @"(?:(?:photograph|photo|picture|pics|pic|images|image|drawings|drawing|paintings|painting|illustration|artwork|art|wallpaper|logo|portrait|render|visual)\s+)?"
        + @"(?:of\s+)?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static string DeriveCaption(string userMsg)
    {
        if (string.IsNullOrWhiteSpace(userMsg)) return "";
        var s = userMsg.Trim().TrimEnd('.', '!', '?', ' ');
        var stripped = CaptionStrip.Replace(s, "", 1).Trim();
        // Empty/too-thin strip (e.g. bare "make a picture") returns "" so the caller falls back to the model
        // caption pass; IsUsableCaption is the final gate either way.
        var result = stripped.Length >= 2 ? stripped : "";
        return result.Length < 300 ? result : result[..300];
    }

    // ── Streaming tool-markup holdback ──
    // A small model emits its tool call in several shapes: the instructed <tool_call>…</tool_call>, a
    // markdown ```json fence, or a bare {"name":…} object (qwen2.5-0.5b does ALL THREE). ParseToolCalls
    // catches every shape at the END of the round - but a naive stream flashes the raw JSON on screen
    // before that (TJ, 2026-07-05: the browser chat showed the generate_image JSON as text). This holds
    // text back at the first sign of ANY tool-call opener. If the round turns out NOT to be a tool call
    // (calls.Count == 0) the engine calls FlushTailAsync and the held text (e.g. a real code block) is
    // released intact; if it IS a tool call the engine skips the flush and the markup stays suppressed.
    private sealed class ToolAwareStreamer
    {
        private static readonly string[] Openers = { "<tool_call>", "```" };
        private readonly Func<string, Task> _onDelta;
        private readonly StringBuilder _sb = new();
        private int _emitted;
        private bool _stopText;
        public ToolAwareStreamer(Func<string, Task> onDelta) => _onDelta = onDelta;

        public async Task PushAsync(string delta)
        {
            _sb.Append(delta);
            if (_stopText) return;
            var s = _sb.ToString();

            // Earliest position at/after the emitted boundary where a tool call could begin.
            int stop = -1;
            foreach (var op in Openers)
            {
                int idx = s.IndexOf(op, Math.Max(0, _emitted - op.Length), StringComparison.Ordinal);
                if (idx >= 0 && (stop < 0 || idx < stop)) stop = idx;
            }
            // A response whose first non-whitespace char is '{' is a bare-JSON tool call from the top.
            int fnw = FirstNonWhitespace(s);
            if (fnw >= 0 && s[fnw] == '{' && (stop < 0 || fnw < stop)) stop = fnw;

            if (stop >= 0) { await FlushAsync(stop).ConfigureAwait(false); _stopText = true; return; }

            // Hold back the longest trailing run that could be the start of an opener (never emit half a fence).
            int hold = 0;
            foreach (var op in Openers)
            {
                int maxH = Math.Min(op.Length - 1, s.Length - _emitted);
                for (int h = maxH; h > hold; h--)
                    if (s.AsSpan(s.Length - h).SequenceEqual(op.AsSpan(0, h))) { hold = h; break; }
            }
            await FlushAsync(s.Length - hold).ConfigureAwait(false);
        }

        // Called only when the round produced NO tool call: whatever was held back is real content, so
        // release ALL of it (even if we had paused on a '{' or ``` that turned out to be legitimate).
        public Task FlushTailAsync() => FlushAsync(_sb.Length);

        private async Task FlushAsync(int upTo)
        {
            if (upTo > _emitted)
            {
                await _onDelta(_sb.ToString(_emitted, upTo - _emitted)).ConfigureAwait(false);
                _emitted = upTo;
            }
        }

        private static int FirstNonWhitespace(string s)
        {
            for (int i = 0; i < s.Length; i++) if (!char.IsWhiteSpace(s[i])) return i;
            return -1;
        }
    }

    /// <summary>
    /// Ask hybrid reasoning models NOT to think, rather than thinking and having it hidden. Default true.
    /// </summary>
    /// <remarks>
    /// 🔴 STRIPPING <c>&lt;think&gt;</c> IS NOT ENOUGH ON ITS OWN, and the bigger the model the worse it
    /// gets. MEASURED 2026-09-09: with a 384-token budget, <c>qwen3:0.6b</c> reasoned briefly and then
    /// answered, but <c>qwen3:1.7b</c> spent the ENTIRE budget reasoning and never reached an answer - so
    /// after filtering there was nothing left and the user got silence. Raising the budget is not the fix
    /// either: hundreds of tokens of deliberation before each line of dialogue is latency the voice pays
    /// for, per character, per turn.
    /// <para>
    /// So thinking is turned OFF at the prompt. Filtering stays as the backstop for a model that reasons
    /// anyway - the two are belt and braces, not alternatives.
    /// </para>
    /// </remarks>
    public bool SuppressThinking { get; set; } = true;

    /// <summary>
    /// Append the empty think block that tells a Qwen3-style model its reasoning is already done.
    /// </summary>
    /// <remarks>
    /// This is exactly what <c>enable_thinking=false</c> does in the model's own chat template: it prefills
    /// <c>&lt;think&gt;\n\n&lt;/think&gt;</c> into the assistant turn, so the model continues from a
    /// position where the reasoning section is already closed. That makes it STRUCTURAL rather than a
    /// request - unlike a "/no_think" hint in the prompt, which the model may simply not honour.
    /// <para>
    /// ⚠️ Uses the tokenizer's SPECIAL-token ids, never <c>Encode("&lt;think&gt;")</c>. If the tag is a
    /// single special token in the vocabulary (it is, for Qwen3) then encoding the text could split it into
    /// sub-words that merely LOOK like the tag and mean nothing to the model. A model with no such token is
    /// not a reasoning model, and is left completely alone.
    /// </para>
    /// </remarks>
    private static int[] WithThinkingSuppressed(int[] promptIds, SentencePieceTokenizer tokenizer)
    {
        if (!tokenizer.TryGetId("<think>", out var open) || !tokenizer.TryGetId("</think>", out var close))
            return promptIds;   // not a hybrid reasoning model - nothing to suppress

        // The template writes "<think>\n\n</think>\n\n"; the newlines are ordinary text.
        var gap = tokenizer.Encode("\n\n");
        var result = new List<int>(promptIds.Length + 2 + gap.Length * 2);
        result.AddRange(promptIds);
        result.Add(open);
        result.AddRange(gap);
        result.Add(close);
        result.AddRange(gap);
        return result.ToArray();
    }

    // ── Streaming thinking-markup suppression ──
    // A hybrid reasoning model emits <think>…</think> before its answer. That is private deliberation, not
    // a reply: it must never be rendered, never enter a group transcript as something a character said, and
    // above all never be spoken by the TTS. Mirrors ToolAwareStreamer deliberately - same holdback idea, so
    // a partial "<thi" at a delta boundary is never flashed on screen.
    /// <summary>Public so the delta-boundary behaviour can be tested at every possible split.</summary>
    public sealed class ThinkingStreamer
    {
        /// <summary>
        /// The open/close pairs that wrap a model's private reasoning rather than its answer.
        /// </summary>
        /// <remarks>
        /// ⚠️ ONE FILTER, SEVERAL FAMILIES. Qwen3 wraps reasoning in <c>&lt;think&gt;…&lt;/think&gt;</c>;
        /// gemma4 uses its own channel markup, <c>&lt;|channel&gt;thought … &lt;channel|&gt;</c>. Both are
        /// private deliberation that must never render or be spoken, and a filter that knew only the first
        /// let gemma4's straight through - MEASURED: "&lt;|channel&gt;thought &lt;channel|&gt;Jupiter is the
        /// largest planet...". A new model family means checking what ITS reasoning looks like, not
        /// assuming this list covers it.
        /// </remarks>
        private static readonly (string Open, string Close)[] Pairs =
        {
            ("<think>", "</think>"),
            ("<|channel>", "<channel|>"),
        };

        private readonly Func<string, Task> _onDelta;
        private readonly StringBuilder _sb = new();
        private int _cursor;      // everything before this is decided: emitted, or discarded as thinking
        private int _insidePair = -1;   // index into Pairs while inside a block, else -1

        public ThinkingStreamer(Func<string, Task> onDelta) => _onDelta = onDelta;

        public Task PushAsync(string delta)
        {
            _sb.Append(delta);
            return PumpAsync(final: false);
        }

        /// <summary>Release anything held back that turned out not to be a tag.</summary>
        public Task FlushTailAsync() => PumpAsync(final: true);

        private async Task PumpAsync(bool final)
        {
            while (true)
            {
                var s = _sb.ToString();
                if (_insidePair >= 0)
                {
                    var close = Pairs[_insidePair].Close;
                    int c = s.IndexOf(close, _cursor, StringComparison.Ordinal);
                    if (c < 0)
                    {
                        // Not closed yet. Advance the SEARCH position only as far as is safe for a closer
                        // that straddles this boundary - a closing tag needs close.Length chars, so one
                        // that is still incomplete must begin within the last close.Length-1.
                        _cursor = Math.Max(_cursor, s.Length - (close.Length - 1));
                        return;
                    }
                    _cursor = c + close.Length;
                    _insidePair = -1;
                    continue;
                }

                // The EARLIEST opener of any family wins, so one family's tag cannot hide inside another's.
                int bestAt = -1, bestPair = -1;
                for (int i = 0; i < Pairs.Length; i++)
                {
                    int at = s.IndexOf(Pairs[i].Open, _cursor, StringComparison.Ordinal);
                    if (at >= 0 && (bestAt < 0 || at < bestAt)) { bestAt = at; bestPair = i; }
                }
                if (bestAt >= 0)
                {
                    if (bestAt > _cursor) await Emit(s, _cursor, bestAt).ConfigureAwait(false);
                    _cursor = bestAt + Pairs[bestPair].Open.Length;
                    _insidePair = bestPair;
                    continue;
                }

                // No opener in sight: hold back the longest trailing run that could still BECOME one of
                // ANY family's openers, or a tag split across deltas leaks the block that follows it.
                int hold = 0;
                if (!final)
                    foreach (var (open, _) in Pairs)
                    {
                        int h = PartialSuffixLength(s, open);
                        if (h > hold) hold = h;
                    }
                int upTo = s.Length - hold;
                if (upTo > _cursor) { await Emit(s, _cursor, upTo).ConfigureAwait(false); _cursor = upTo; }
                return;
            }
        }

        private Task Emit(string s, int from, int to) => _onDelta(s[from..to]);

        /// <summary>Length of the longest proper prefix of <paramref name="tag"/> that ends the string.</summary>
        private static int PartialSuffixLength(string s, string tag)
        {
            int max = Math.Min(tag.Length - 1, s.Length);
            for (int h = max; h > 0; h--)
                if (s.AsSpan(s.Length - h).SequenceEqual(tag.AsSpan(0, h))) return h;
            return 0;
        }
    }

    /// <summary>
    /// Remove &lt;think&gt;…&lt;/think&gt; blocks, leaving only what the model meant to say.
    /// </summary>
    /// <remarks>
    /// ⚠️ An UNCLOSED block discards everything after it, deliberately. The model ran out of budget while
    /// still reasoning, so what follows is a severed half-thought, not an answer - showing it would put
    /// the model's private deliberation on screen, which is the whole thing this prevents.
    /// </remarks>
    public static string StripThinking(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        // Same pairs the streaming filter knows: Qwen3's <think> and gemma4's channel markup.
        (string Open, string Close)[] pairs = { ("<think>", "</think>"), ("<|channel>", "<channel|>") };
        if (!pairs.Any(p => text.Contains(p.Open, StringComparison.OrdinalIgnoreCase))) return text;

        var sb = new StringBuilder();
        int i = 0;
        while (i < text.Length)
        {
            int bestAt = -1, bestPair = -1;
            for (int p = 0; p < pairs.Length; p++)
            {
                int at = text.IndexOf(pairs[p].Open, i, StringComparison.OrdinalIgnoreCase);
                if (at >= 0 && (bestAt < 0 || at < bestAt)) { bestAt = at; bestPair = p; }
            }
            if (bestAt < 0) { sb.Append(text, i, text.Length - i); break; }

            sb.Append(text, i, bestAt - i);
            var (open, close) = pairs[bestPair];
            int c = text.IndexOf(close, bestAt + open.Length, StringComparison.OrdinalIgnoreCase);
            if (c < 0) break;                       // never closed - nothing after it is an answer
            i = c + close.Length;
        }
        return sb.ToString().TrimStart();
    }

    // ── Mapping helpers ──
    private GenerationConfig ToConfig(AiGenerationOptions o) => new()
    {
        MaxNewTokens = Math.Min(Math.Max(1, o.MaxOutputTokens), MaxOutputTokens),
        Strategy = o.Temperature > 0 ? o.Strategy : "greedy",
        Temperature = o.Temperature,
        TopP = o.TopP,
        TopK = o.TopK,
        Seed = o.Seed,
        RepetitionPenalty = o.RepetitionPenalty,
    };

    private static AiStopKind ToStopKind(StopReason r) => r switch
    {
        StopReason.Length => AiStopKind.Length,
        StopReason.Cancelled => AiStopKind.Cancelled,
        _ => AiStopKind.Stop,
    };

    private static List<(string, string)> ToTuples(IReadOnlyList<AiChatMessage> messages)
        => messages.Select(m => (m.Role, m.Content)).ToList();

    private string FirstOrDefaultModel(string model) => model; // resolution happens in the provider

    /// <summary>Remove &lt;tool_call&gt;…&lt;/tool_call&gt; blocks, leaving the natural-language preamble.</summary>
    public static string StripToolCalls(string text)
    {
        const string open = "<tool_call>", close = "</tool_call>";
        var sb = new StringBuilder();
        int i = 0;
        while (true)
        {
            int s = text.IndexOf(open, i, StringComparison.Ordinal);
            if (s < 0) { sb.Append(text, i, text.Length - i); break; }
            sb.Append(text, i, s - i);
            int e = text.IndexOf(close, s, StringComparison.Ordinal);
            if (e < 0) break;
            i = e + close.Length;
        }
        return sb.ToString();
    }
}
