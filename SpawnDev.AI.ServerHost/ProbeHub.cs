using ILGPU.Runtime;
using SpawnDev.AI;
using SpawnDev.AI.Server;

/// <summary>
/// Diagnostic probe (invoked via `dotnet run ... probe-hub [modelName]`): loads the EXACT HuggingFace
/// GGUF the browser worker streams (via <see cref="HubModelProvider"/>), then runs a battery of image
/// requests to measure how reliably the small model TOOL-CALLS. A stub generate_image tool isolates
/// tool-calling from actual image generation. Compare its IMAGE/REFUSE counts against the normal host
/// (Ollama-cached GGUF) to tell whether a browser refusal is a model/GGUF difference or a runtime one.
/// </summary>
internal static class ProbeHub
{
    const string Sys =
        "You are a helpful assistant running entirely on the user's own GPU in their browser, with no "
        + "internet access. Answer questions, facts, math, explanations, stories, and poems clearly in plain "
        + "text. When the user asks for a picture, photo, or drawing, the app generates the image "
        + "automatically - you don't need to do anything, so never say you are unable to make images.";

    static readonly string[] Questions =
    {
        // Image requests - want IMAGE
        "draw a cat",
        "can you generate an image of a sunset?",
        "make me a picture of a dog",
        "I want a photo of a mountain lake",
        "generate an image of a robot",
        "show me a picture of paris",
        "create an image of a dragon",
        "draw a majestic dragon breathing fire over a medieval castle at sunset",  // detailed - keep detail
        "make a picture",                                                          // subjectless - model fallback
        // Control (non-image) - want TEXT, must NOT spuriously draw
        "what is the capital of France?",
        "write me a short poem about the sea",
        "tell me about the Mona Lisa painting",
        "what's 2 + 2?",
    };

    /// <summary>Model-free check of the GitHub tool itself: list repos, read a repo overview, read a file,
    /// and exercise the error/allowlist paths. No GPU.</summary>
    public static async Task<int> CheckGitHubToolAsync()
    {
        using var http = new HttpClient();
        var tool = new GitHubTool(http);
        (string Label, string Args)[] calls =
        {
            ("list repos", "{}"),
            ("read repo (bare name)", "{\"repo\":\"SpawnDev.BlazorJS\"}"),
            ("read repo (owner/name)", "{\"repo\":\"LostBeard/SpawnDev.AI\"}"),
            ("read a file", "{\"repo\":\"SpawnDev.AI\",\"path\":\"CHANGELOG.md\"}"),
            ("crew question source", "{\"repo\":\"SpawnDev.ILGPU\"}"),
            ("bad repo (404)", "{\"repo\":\"LostBeard/does-not-exist-xyz\"}"),
            ("path traversal blocked", "{\"repo\":\"SpawnDev.AI\",\"path\":\"../../etc/passwd\"}"),
        };
        int fails = 0;
        foreach (var (label, args) in calls)
        {
            var res = await tool.ExecuteAsync(args);
            string first = (res.TextForModel ?? "").Replace("\n", " ");
            Console.WriteLine($"[{(res.IsError ? "ERR " : "ok  ")}] {label,-24} -> {first[..Math.Min(120, first.Length)]}");
            // Sanity: the two intentional-failure cases SHOULD be errors; the rest should NOT be.
            bool expectErr = label.Contains("404") || label.Contains("traversal");
            if (res.IsError != expectErr) { fails++; Console.WriteLine($"      ^^ UNEXPECTED (expectErr={expectErr})"); }
        }
        Console.WriteLine($"[probe-github-tool] unexpected outcomes={fails} of {calls.Length}");
        return fails;
    }

    /// <summary>Model-free check of the image-intent detector: labeled battery, reports false pos/neg.</summary>
    public static int CheckIntent()
    {
        (string Q, bool Expect)[] cases =
        {
            // Image requests - MUST detect (true)
            ("draw a cat", true),
            ("can you generate an image of a sunset?", true),
            ("make me a picture of a dog", true),
            ("I want a photo of a mountain lake", true),
            ("generate an image of a robot", true),
            ("show me a picture of paris", true),
            ("create an image of a dragon", true),
            ("paint a portrait of a queen", true),
            ("please draw me a dragon", true),
            ("sketch a house on a hill", true),
            ("render a scene of a forest at dawn", true),
            ("design a logo for my coffee shop", true),
            ("Draw me a picture of a lighthouse in a storm.", true),
            // Ordinary chat - MUST NOT detect (false)
            ("what is the capital of France?", false),
            ("tell me about the Mona Lisa painting", false),
            ("explain how you run inside my browser", false),
            ("write me a poem about the sea", false),
            ("who painted the Sistine Chapel?", false),
            ("describe a beautiful sunset for me", false),
            ("what does a golden retriever look like?", false),
            ("can you show me the code for a for loop?", false),
            ("give me a recipe for pancakes", false),
            ("summarize this article", false),
            ("what's 2 + 2?", false),
            ("how are you today?", false),
        };
        int fp = 0, fn = 0;
        foreach (var (q, expect) in cases)
        {
            bool got = AiChatEngine.HasImageIntent(q);
            string mark = got == expect ? "ok  " : (got ? "FALSE-POS" : "FALSE-NEG");
            if (got && !expect) fp++;
            if (!got && expect) fn++;
            Console.WriteLine($"[{mark,-9}] intent={got,-5} expect={expect,-5}  {q}");
        }
        Console.WriteLine($"[probe-intent] IMAGE false-positives={fp} false-negatives={fn} of {cases.Length}");
        // GitHub grounding intent is now index-aware and lives in GitHubTool (any repo name, not a fixed
        // regex) - it's exercised end-to-end by `probe-github`, not here.
        return fp + fn;
    }

    public static async Task RunAsync(Accelerator accelerator, string modelName)
    {
        await using var webTorrent = new SpawnDev.WebTorrent.WebTorrentClient();
        using var http = new HttpClient();
        var provider = new HubModelProvider(webTorrent, http, new[]
        {
            new HubModelOption("qwen2.5:0.5b-instruct-q8_0", "Qwen/Qwen2.5-0.5B-Instruct-GGUF", "qwen2.5-0.5b-instruct-q8_0.gguf", 531_067_136),
            new HubModelOption("qwen2.5:1.5b-instruct-q4_k_m", "Qwen/Qwen2.5-1.5B-Instruct-GGUF", "qwen2.5-1.5b-instruct-q4_k_m.gguf", 1_117_320_000),
        })
        { OnLoadProgress = (stage, pct) => { if (pct % 25 == 0) Console.WriteLine($"[load] {stage} {pct}%"); } };

        await using var registry = new ModelRegistry(provider, accelerator, 4096);
        var engine = new AiChatEngine(registry);
        var tools = new AiToolRegistry();
        tools.Register(new StubImageTool());
        engine.Tools = tools;

        Console.WriteLine($"[probe-hub] model: {modelName} (HF hub GGUF; first run downloads)");
        // Compare sampling strategies: current demo (top_p @0.3) vs greedy (temp 0) vs the forced-tool
        // recovery. Same loaded model, so all passes are cheap after the one-time download.
        var variants = new (string Label, AiGenerationOptions Opts)[]
        {
            ("top_p@0.3 (current)", new AiGenerationOptions { MaxOutputTokens = 200, Temperature = 0.3f, Strategy = "top_p", TopP = 0.9f, RepetitionPenalty = 1.15f }),
            ("greedy (temp 0)",     new AiGenerationOptions { MaxOutputTokens = 200, Temperature = 0f, Strategy = "greedy", RepetitionPenalty = 1.15f }),
        };
        foreach (var (label, opts) in variants)
        {
            Console.WriteLine($"\n=== variant: {label} ===");
            int imgs = 0, refuses = 0, texts = 0;
            foreach (var q in Questions)
            {
                var req = new AiChatRequest
                {
                    Model = modelName,
                    Messages = new[] { new AiChatMessage("system", Sys), new AiChatMessage("user", q) },
                    Options = opts,
                };
                var res = await engine.ChatAsync(req);
                string c = res.Text ?? "";
                bool img = c.Contains("ai-artifact://") || res.ToolCalls.Any(t => t.Name == "generate_image") || (res.Artifacts?.Count > 0);
                bool refuse = c.Contains("can't", StringComparison.OrdinalIgnoreCase) || c.Contains("cannot", StringComparison.OrdinalIgnoreCase)
                    || c.Contains("unable", StringComparison.OrdinalIgnoreCase) || c.Contains("not within", StringComparison.OrdinalIgnoreCase) || c.Contains("not able", StringComparison.OrdinalIgnoreCase);
                string tag = img ? "IMAGE " : refuse ? "REFUSE" : "TEXT  ";
                if (img) imgs++; else if (refuse) refuses++; else texts++;
                string caption = res.ToolCalls.FirstOrDefault(t => t.Name == "generate_image")?.ArgumentsJson ?? "";
                Console.WriteLine($"[{tag}] {q}");
                Console.WriteLine($"        -> caption: {caption}");
            }
            Console.WriteLine($"[probe-hub] {label}: IMAGE={imgs} REFUSE={refuses} TEXT={texts} of {Questions.Length}");
        }
    }

    /// <summary>Does the model actually CALL github_lookup for SpawnDev library/crew questions, and does the
    /// answer use the fetched info? Runs the full agentic loop with the REAL GitHub tool (call-counted).</summary>
    public static async Task RunGitHubAsync(Accelerator accelerator, string modelName)
    {
        await using var webTorrent = new SpawnDev.WebTorrent.WebTorrentClient();
        using var http = new HttpClient();
        var provider = new HubModelProvider(webTorrent, http, new[]
        {
            new HubModelOption("qwen2.5:0.5b-instruct-q8_0", "Qwen/Qwen2.5-0.5B-Instruct-GGUF", "qwen2.5-0.5b-instruct-q8_0.gguf", 531_067_136),
            new HubModelOption("qwen2.5:1.5b-instruct-q4_k_m", "Qwen/Qwen2.5-1.5B-Instruct-GGUF", "qwen2.5-1.5b-instruct-q4_k_m.gguf", 1_117_320_000),
        });
        await using var registry = new ModelRegistry(provider, accelerator, 4096);
        var engine = new AiChatEngine(registry);
        var tools = new AiToolRegistry();
        var counter = new CallCountingTool(new GitHubTool(http));
        tools.Register(counter);
        engine.Tools = tools;

        const string sys =
            "You are a helpful assistant. When the user asks about the SpawnDev open-source libraries, the apps "
            + "built with them, or the crew, authoritative reference information from GitHub is added to the "
            + "conversation automatically - answer from it and do not say you need a repository name.";
        string[] qs =
        {
            "What is SpawnDev.BlazorJS?",
            "List the SpawnDev libraries.",
            "Who is on the SpawnDev crew?",
            "Tell me about SpawnDev.ILGPU.",
            "What does SpawnDev.WebTorrent do?",
            "What is Anaglyphohol?",                 // non-SpawnDev app - tests index any-repo matching
            "What is SpawnDev.BlazorJS and who is on the crew?",  // compound repo + crew - needs BOTH sections
            "What is the capital of France?",        // control - must NOT ground (no repo/spawndev intent)
        };
        Console.WriteLine($"[probe-github] model: {modelName}");
        int called = 0;
        foreach (var q in qs)
        {
            counter.Count = 0;
            var req = new AiChatRequest
            {
                Model = modelName,
                Messages = new[] { new AiChatMessage("system", sys), new AiChatMessage("user", q) },
                Options = new AiGenerationOptions { MaxOutputTokens = 500, Temperature = 0.3f, Strategy = "top_p", TopP = 0.9f, RepetitionPenalty = 1.15f },
            };
            var res = await engine.ChatAsync(req);
            bool used = counter.Count > 0;
            if (used) called++;
            string ans = (res.Text ?? "").Replace("\n", " ").Trim();
            Console.WriteLine($"[{(used ? "CALLED " : "no-call")}] {q}");
            Console.WriteLine($"        -> {ans[..Math.Min(450, ans.Length)]}");
        }
        Console.WriteLine($"[probe-github] github_lookup called on {called}/{qs.Length} library questions");
    }

    /// <summary>The demo's EXACT chat path on the desktop GPU: the same HF hub GGUF the browser streams, the
    /// same two registered tools, the same system prompt, the same top_p sampling + RepetitionPenalty, and the
    /// same STREAMING call the composer makes (Home.razor.cs:125). Prior LFM2 verification used greedy +
    /// no-tools + non-streaming and passed while the demo produced garbage - this reproduces what TJ sees.
    /// `probe-demo [model] [prompt]`.</summary>
    public static async Task RunDemoAsync(Accelerator accelerator, string modelName, string prompt)
    {
        // Verbatim copy of SpawnDev.AI.Demo/Pages/Home.razor.cs DefaultSystemPrompt.
        const string demoSys =
            "You are a helpful assistant running entirely on the user's own GPU in their browser. Answer "
            + "questions, facts, math, explanations, stories, and poems clearly in plain text. When the user asks "
            + "about the SpawnDev open-source libraries, the apps built with them, or the crew, authoritative "
            + "reference information from GitHub is added to the conversation automatically - answer from it and "
            + "do not say you need a repository name. When the user asks for a picture, photo, or drawing, the app "
            + "generates the image automatically - you don't need to do anything, so never say you can't make images.";

        await using var webTorrent = new SpawnDev.WebTorrent.WebTorrentClient();
        using var http = new HttpClient();
        // Same model list the demo registers (SpawnDev.AI.Demo/Program.cs).
        var provider = new HubModelProvider(webTorrent, http, new[]
        {
            new HubModelOption("qwen2.5:0.5b-instruct-q8_0", "Qwen/Qwen2.5-0.5B-Instruct-GGUF", "qwen2.5-0.5b-instruct-q8_0.gguf", 531_067_136),
            new HubModelOption("smollm2:360m-instruct-q8_0", "HuggingFaceTB/SmolLM2-360M-Instruct-GGUF", "smollm2-360m-instruct-q8_0.gguf", 386_404_352),
            new HubModelOption("qwen2.5:1.5b-instruct-q4_k_m", "Qwen/Qwen2.5-1.5B-Instruct-GGUF", "qwen2.5-1.5b-instruct-q4_k_m.gguf", 1_117_320_000),
            new HubModelOption("qwen3:0.6b-q8_0", "Qwen/Qwen3-0.6B-GGUF", "Qwen3-0.6B-Q8_0.gguf", 639_446_688),
            new HubModelOption("lfm2:1.2b-q4_k_m", "LiquidAI/LFM2-1.2B-GGUF", "LFM2-1.2B-Q4_K_M.gguf", 730_893_248),
        });
        await using var registry = new ModelRegistry(provider, accelerator, 4096);
        // Engine + tools exactly as AiWorkerServer wires them (both tools registered; the demo's defaults
        // for ForceImageToolOnIntent/GroundGitHubOnIntent left untouched).
        var engine = new AiChatEngine(registry);
        using var images = new AiImageEngine(webTorrent, http, accelerator);
        var tools = new AiToolRegistry();
        tools.Register(new GenerateImageTool(images, tools));
        tools.Register(new GitHubTool(http));
        engine.Tools = tools;

        var req = new AiChatRequest
        {
            Model = modelName,
            Messages = new[] { new AiChatMessage("system", demoSys), new AiChatMessage("user", prompt) },
            // Verbatim from Home.razor.cs:125 (the composer's options).
            Options = new AiGenerationOptions { MaxOutputTokens = 384, Strategy = "top_p", Temperature = 0.3f, TopP = 0.9f, RepetitionPenalty = 1.15f },
        };
        Console.WriteLine($"[probe-demo] model={modelName}");
        Console.WriteLine($"[probe-demo] prompt={prompt}");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var res = await engine.ChatStreamAsync(req, delta => { Console.Write(delta); return Task.CompletedTask; });
        sw.Stop();
        Console.WriteLine();
        Console.WriteLine($"────── [probe-demo] {sw.Elapsed.TotalSeconds:F1}s, {res.GeneratedTokens} tok, stop={res.Stop}");
    }

    /// <summary>Minimal arch-support diagnostic: load an Ollama-cached model through our GGUF pipeline and
    /// generate from a fixed prompt (greedy). Coherent output => the architecture is wired correctly;
    /// garbage/loop => a graph gap (e.g. missing QK-norm); a throw => metadata/arch not handled at all.</summary>
    public static async Task RunGenAsync(Accelerator accelerator, string modelName)
    {
        var store = new OllamaModelStore();
        await using var registry = new ModelRegistry(new OllamaCacheModelProvider(store), accelerator, 4096);
        var engine = new AiChatEngine(registry) { ForceImageToolOnIntent = false, GroundGitHubOnIntent = false };
        var req = new AiChatRequest
        {
            Model = modelName,
            Messages = new[]
            {
                new AiChatMessage("system", "You are a helpful, concise assistant."),
                new AiChatMessage("user", "In one or two sentences: what is the capital of France, and name two famous landmarks there?"),
            },
            Options = new AiGenerationOptions { MaxOutputTokens = 200, Strategy = "greedy" },
        };
        Console.WriteLine($"[probe-gen] loading + generating: {modelName}");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var res = await engine.ChatAsync(req);
        sw.Stop();
        Console.WriteLine($"[probe-gen] {modelName} ({sw.Elapsed.TotalSeconds:F1}s, {res.GeneratedTokens} tok, stop={res.Stop}):");
        Console.WriteLine("──────");
        Console.WriteLine(res.Text);
        Console.WriteLine("──────");
    }

    /// <summary>Measures NATIVE tool-calling with forcing + grounding OFF and both tools model-callable: does
    /// the model itself call generate_image for image requests and github_lookup for library questions, and
    /// nothing for plain chat? Uses the Ollama cache (no downloads) so any cached model can be swept. Answers
    /// "does a newer/bigger model let us drop the forcing/grounding compensations?"</summary>
    public static async Task RunNativeAsync(Accelerator accelerator, string modelName)
    {
        using var http = new HttpClient();
        var store = new OllamaModelStore();
        await using var registry = new ModelRegistry(new OllamaCacheModelProvider(store), accelerator, 4096);
        var engine = new AiChatEngine(registry)
        {
            ForceImageToolOnIntent = false,   // NO image forcing - the model must choose to call it
            GroundGitHubOnIntent = false,     // NO grounding - github_lookup becomes model-callable
        };
        var tools = new AiToolRegistry();
        var image = new CallCountingTool(new StubImageTool());
        var github = new CallCountingTool(new GitHubTool(http));
        tools.Register(image);
        tools.Register(github);
        engine.Tools = tools;

        const string sys =
            "You are a helpful assistant. You have two tools: generate_image (make a picture from a prompt) "
            + "and github_lookup (look up the SpawnDev libraries and crew). Call the right tool when it helps; "
            + "answer directly otherwise.";
        // (question, expected tool: "image" | "github" | "none")
        (string Q, string Want)[] qs =
        {
            ("Draw a cat.", "image"),
            ("Generate an image of a sunset over mountains.", "image"),
            ("Make me a picture of a robot.", "image"),
            ("What is SpawnDev.BlazorJS?", "github"),
            ("Who is on the SpawnDev crew?", "github"),
            ("Tell me about SpawnDev.ILGPU.", "github"),
            ("What is the capital of France?", "none"),
            ("Write a two-line poem about rain.", "none"),
        };
        Console.WriteLine($"[probe-native] model: {modelName} (forcing OFF, grounding OFF, tools model-callable)");
        int correct = 0;
        foreach (var (q, want) in qs)
        {
            image.Count = 0; github.Count = 0;
            var req = new AiChatRequest
            {
                Model = modelName,
                Messages = new[] { new AiChatMessage("system", sys), new AiChatMessage("user", q) },
                Options = new AiGenerationOptions { MaxOutputTokens = 250, Temperature = 0.3f, Strategy = "top_p", TopP = 0.9f, RepetitionPenalty = 1.15f },
            };
            AiChatResult res;
            try { res = await engine.ChatAsync(req); }
            catch (Exception ex) { Console.WriteLine($"[ERR    ] {q} -> {ex.Message}"); continue; }
            string got = image.Count > 0 ? "image" : github.Count > 0 ? "github" : "none";
            bool ok = got == want;
            if (ok) correct++;
            Console.WriteLine($"[{(ok ? "ok  " : "MISS"),-6}] want={want,-6} got={got,-6}  {q}");
        }
        Console.WriteLine($"[probe-native] {modelName}: {correct}/{qs.Length} correct native tool routing");
    }

    sealed class CallCountingTool : IAiTool, IAiGroundingProvider
    {
        private readonly IAiTool _inner;
        public int Count;   // counts both direct model calls (ExecuteAsync) and grounding (GetGroundingAsync)
        public CallCountingTool(IAiTool inner) => _inner = inner;
        public string Name => _inner.Name;
        public string Description => _inner.Description;
        public string ParametersJsonSchema => _inner.ParametersJsonSchema;
        public Task<AiToolExecutionResult> ExecuteAsync(string argumentsJson, CancellationToken ct = default)
        { Count++; return _inner.ExecuteAsync(argumentsJson, ct); }
        public async Task<string?> GetGroundingAsync(string userMessage, CancellationToken ct = default)
        {
            if (_inner is not IAiGroundingProvider gp) return null;
            var r = await gp.GetGroundingAsync(userMessage, ct);
            if (r != null) Count++;   // count only when grounding actually fired (non-null reference)
            return r;
        }
    }

    sealed class StubImageTool : IAiTool
    {
        public string Name => "generate_image";
        public string Description =>
            "Generate an image from a text prompt using the local on-device diffusion model. "
            + "Use when the user asks for a picture, drawing, photo, or any visual.";
        public string ParametersJsonSchema => """
            {
              "type": "object",
              "properties": {
                "prompt": { "type": "string", "description": "What the image should depict, phrased as a caption (e.g. 'a photo of a red fox in snow')." },
                "seed": { "type": "integer", "description": "Optional seed for reproducibility." }
              },
              "required": ["prompt"]
            }
            """;
        public Task<AiToolExecutionResult> ExecuteAsync(string argumentsJson, CancellationToken ct = default)
        {
            string id = Guid.NewGuid().ToString("N")[..12];
            return Task.FromResult(new AiToolExecutionResult(
                $"Image generated (512x512, stub). Displayed as ai-artifact://{id} - describe it briefly; do not repeat the id.",
                new[] { new AiToolArtifact(id, "image/png", new byte[] { 1 }, "stub") }));
        }
    }
}
