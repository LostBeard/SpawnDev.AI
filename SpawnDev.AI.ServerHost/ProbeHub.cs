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

        (string Q, bool Expect)[] gh =
        {
            ("What is SpawnDev.BlazorJS?", true),
            ("who is on the SpawnDev crew?", true),
            ("list the SpawnDev libraries", true),
            ("tell me about SpawnDev.ILGPU", true),
            ("what does spawndev.webtorrent do", true),
            ("what is the capital of France?", false),
            ("draw a cat", false),
            ("write me a poem", false),
            ("who painted the Mona Lisa?", false),
        };
        int gfp = 0, gfn = 0;
        foreach (var (q, expect) in gh)
        {
            bool got = AiChatEngine.HasGitHubIntent(q);
            if (got && !expect) gfp++;
            if (!got && expect) gfn++;
            Console.WriteLine($"[{(got == expect ? "ok  " : got ? "FALSE-POS" : "FALSE-NEG"),-9}] github intent={got,-5} expect={expect,-5}  {q}");
        }
        Console.WriteLine($"[probe-intent] GITHUB false-positives={gfp} false-negatives={gfn} of {gh.Length}");
        return fp + fn + gfp + gfn;
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
            "You are a helpful assistant. You can look up the SpawnDev open-source libraries, their code and "
            + "docs, and the crew that builds them on GitHub - call github_lookup when the user asks about a "
            + "SpawnDev library, how it works, releases, or who made it.";
        string[] qs =
        {
            "What is SpawnDev.BlazorJS?",
            "List the SpawnDev libraries.",
            "Who is on the SpawnDev crew?",
            "Tell me about SpawnDev.ILGPU.",
            "What does SpawnDev.WebTorrent do?",
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
                Options = new AiGenerationOptions { MaxOutputTokens = 300, Temperature = 0.3f, Strategy = "top_p", TopP = 0.9f, RepetitionPenalty = 1.15f },
            };
            var res = await engine.ChatAsync(req);
            bool used = counter.Count > 0;
            if (used) called++;
            string ans = (res.Text ?? "").Replace("\n", " ").Trim();
            Console.WriteLine($"[{(used ? "CALLED " : "no-call")}] {q}");
            Console.WriteLine($"        -> {ans[..Math.Min(200, ans.Length)]}");
        }
        Console.WriteLine($"[probe-github] github_lookup called on {called}/{qs.Length} library questions");
    }

    sealed class CallCountingTool : IAiTool
    {
        private readonly IAiTool _inner;
        public int Count;
        public CallCountingTool(IAiTool inner) => _inner = inner;
        public string Name => _inner.Name;
        public string Description => _inner.Description;
        public string ParametersJsonSchema => _inner.ParametersJsonSchema;
        public Task<AiToolExecutionResult> ExecuteAsync(string argumentsJson, CancellationToken ct = default)
        { Count++; return _inner.ExecuteAsync(argumentsJson, ct); }
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
