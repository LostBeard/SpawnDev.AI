using ILGPU.Runtime;
using SpawnDev.AI;
using SpawnDev.AI.Server;
using SpawnDev.ILGPU.ML;
using SpawnDev.ILGPU.ML.Pipelines;

/// <summary>
/// `chat-bench [model] [cuda|opencl|cpu] [contextWords]`: decode speed of AiChatEngine on an Ollama-cache model with each engine (the session engine, then Auto -
/// the batch decoder where supported), greedy and top-p + penalty, 128 new tokens, 5 runs each after one warm-up; and whether, and where, greedy
/// output differs between the engines. A difference is reported, not failed: two correct engines can flip a near-tie (see the NOTE below).
/// contextWords (default 0) puts that many words of filler text ahead of the question, to measure decode at a long context: every decode
/// step reads the whole KV cache, so a short-prompt rate says nothing about a 4,000-token agent prompt (Vertex on an RTX 2060 decoded
/// 17.7 tok/s at ~4,400 prompt tokens, 2026-11-02). Each line reports the engine-timed decode rate (AiChatResult.Timings) and the
/// wall-clock rate including the prompt.
/// </summary>
internal static class ChatBench
{
    public static async Task<int> RunAsync(string[] args)
    {
        var model = args.Length > 1 ? args[1] : "qwen3:1.7b";
        var which = args.Length > 2 ? args[2] : "cuda";
        int contextWords = args.Length > 3 && int.TryParse(args[3], out var cw) ? cw : 0;
        // suffixWords > 0 (needs contextWords): after the decode runs, 5 more turns that keep the filler as a cached prefix and append a
        // DIFFERENT suffix of that many words each time - the agent pattern (Vertex: ~95% of a 3-5k prompt reused, 100-350 new tokens per
        // turn, first word after 2-5 s on an RTX 2060). Reports the NEW-token prefill rate, (prompt - reused) / PromptEvalMs, and as the
        // reference the full-prompt prefill rate of the same total with nothing reused (a fresh engine per run would reload; instead the
        // reference run puts the varying words FIRST, so no prefix matches).
        int suffixWords = args.Length > 4 && int.TryParse(args[4], out var sx) ? sx : 0;
        string[] vocab = { "the", "river", "village", "stone", "builder", "north", "wall", "torch", "farm", "wheat", "chest", "diamond", "night", "zombie",
            "bridge", "canyon", "desert", "temple", "portal", "nether", "cat", "wolf", "castle", "moat", "redstone", "door", "glass", "dome", "ocean", "tower" };
        string Words(int count, int seed)
        {
            var rw = new Random(seed); var b = new System.Text.StringBuilder();
            for (int w = 0; w < count; w++) { b.Append(vocab[rw.Next(vocab.Length)]); b.Append(w % 12 == 11 ? ".\n" : " "); }
            return b.ToString();
        }
        int failures = 0;
        using var context = MLContext.Create().ToContext();
        using var acc = which switch
        {
            "opencl" => context.Devices.First(d => d.AcceleratorType == AcceleratorType.OpenCL).CreateAccelerator(context),
            "cpu" => MLContext.CreateCpuAccelerator(context),
            _ => context.Devices.First(d => d.AcceleratorType == AcceleratorType.Cuda).CreateAccelerator(context),
        };
        Console.WriteLine($"accelerator: {acc.Name} ({acc.AcceleratorType}), model {model}");
        const string Question = "Write a short paragraph about why GPUs are good at running neural networks.";
        string filler = "";
        if (contextWords > 0)
        {
            // Varied, natural text (a repeated sentence would let a prefix cache or the model's own copying skew the run).
            filler = "Here are some notes from the game log:\n" + Words(contextWords, 5) + "\n\n";
        }
        var messages = new[] { new AiChatMessage("user", filler + Question) };
        var greedy = new AiGenerationOptions { Strategy = "greedy", Temperature = 0, MaxOutputTokens = 128 };
        var topP = new AiGenerationOptions { Strategy = "top_p", Temperature = 0.7f, TopP = 0.9f, RepetitionPenalty = 1.1f, Seed = 7, MaxOutputTokens = 128 };
        var greedyText = new Dictionary<GgufEngine, string>();
        foreach (var engine in new[] { GgufEngine.Generator, GgufEngine.Auto })
        {
            var provider = new OllamaCacheModelProvider(new OllamaModelStore())
            {
                Engine = engine,
                // SPAWNDEV_AI_INT8_DECODE=1 benches the opt-in fast int8 decode (the same switch the host reads)
                FastInt8Decode = Environment.GetEnvironmentVariable("SPAWNDEV_AI_INT8_DECODE") == "1" ? true : null,
            };
            await using var registry = new ModelRegistry(provider, acc);
            var chat = new AiChatEngine(registry);
            string engineUsed = "?"; int reused = 0;
            chat.PerfLog = line =>
            {
                var i = line.IndexOf("engine=", StringComparison.Ordinal); if (i >= 0) engineUsed = line[(i + 7)..].Trim();
                var j = line.IndexOf("reused=", StringComparison.Ordinal);
                if (j >= 0) { var rest = line[(j + 7)..].TrimStart(); int k = 0; while (k < rest.Length && char.IsDigit(rest[k])) k++; int.TryParse(rest[..k], out reused); }
            };
            await chat.ChatAsync(new AiChatRequest { Model = model, Messages = messages, Options = greedy });   // load + warm-up
            foreach (var (name, opt) in new[] { ("greedy", greedy), ("top-p+penalty", topP) })
            {
                var rates = new List<double>(); var decodeRates = new List<double>(); int promptTokens = 0;
                for (int r = 0; r < 5; r++)
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    var res = await chat.ChatAsync(new AiChatRequest { Model = model, Messages = messages, Options = opt });
                    rates.Add(res.GeneratedTokens / sw.Elapsed.TotalSeconds);
                    promptTokens = res.PromptTokens;
                    if (res.Timings?.EvalMs is { } evalMs && evalMs > 0 && res.GeneratedTokens > 1) decodeRates.Add((res.GeneratedTokens - 1) / (evalMs / 1000));
                    if (name == "greedy" && r == 0) greedyText[engine] = res.Text;
                }
                rates.Sort(); decodeRates.Sort();
                string decode = decodeRates.Count == 5 ? $"decode median {decodeRates[2]:F1} tok/s (min {decodeRates[0]:F1}, max {decodeRates[4]:F1}), " : "";
                Console.WriteLine($"BENCH: chat {acc.AcceleratorType} {model} prompt={promptTokens}tok requested={engine} used={engineUsed} {name}: {decode}wall median {rates[2]:F1} tok/s incl. prompt");
            }
            if (suffixWords > 0 && contextWords > 0)
            {
                var shortGreedy = new AiGenerationOptions { Strategy = "greedy", Temperature = 0, MaxOutputTokens = 8 };
                foreach (var (label, suffixFirst) in new[] { ("suffix-prefill", false), ("full-prefill", true) })
                {
                    var prefillRates = new List<double>(); var firstMs = new List<double>(); int lastNew = 0, lastPrompt = 0, lastReused = 0;
                    for (int r = 0; r < 5; r++)
                    {
                        string fresh = "New notes:\n" + Words(suffixWords, 100 + r + (suffixFirst ? 50 : 0)) + "\n\n";
                        var turn = new[] { new AiChatMessage("user", suffixFirst ? fresh + filler + Question : filler + fresh + Question) };
                        var res = await chat.ChatAsync(new AiChatRequest { Model = model, Messages = turn, Options = shortGreedy });
                        int newTokens = res.PromptTokens - reused;
                        if (res.Timings?.PromptEvalMs is { } pe && pe > 0) { prefillRates.Add(newTokens / (pe / 1000)); firstMs.Add(pe); }
                        lastNew = newTokens; lastPrompt = res.PromptTokens; lastReused = reused;
                    }
                    prefillRates.Sort(); firstMs.Sort();
                    if (prefillRates.Count == 5)
                        Console.WriteLine($"BENCH: {label} {acc.AcceleratorType} {model} used={engineUsed} prompt={lastPrompt}tok reused={lastReused}tok new={lastNew}tok: " +
                            $"median {prefillRates[2]:F0} new tok/s (min {prefillRates[0]:F0}, max {prefillRates[4]:F0}), first token median {firstMs[2]:F0} ms");
                }
            }
        }
        // NOT a pass / fail. Two correct engines (different kernels, different accumulation order) may take different greedy paths at a
        // near-tie: smollm2:360m diverged at token 81 where the top-2 logit margin was 0.0024-0.015 while the engines' logits differ by up to
        // ~0.04 (Tuvok, 2026-11-02, ML Bench_OllamaGreedyDivergence_Margin). Whether a divergence is a tie or a fault needs the logits, which
        // this host does not see: that gate lives in SpawnDev.ILGPU.ML. Here: report whether and where the texts first differ.
        // CHATBENCH_GREEDY_OUT=<file>: write each engine's greedy text, so two runs (say two SpawnDev.ILGPU.ML versions) can be
        // compared byte for byte; the NOTE below only compares the engines within one run.
        if (Environment.GetEnvironmentVariable("CHATBENCH_GREEDY_OUT") is { Length: > 0 } greedyOut)
        {
            File.WriteAllText(greedyOut, System.Text.Json.JsonSerializer.Serialize(greedyText.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value)));
            Console.WriteLine($"NOTE: greedy texts written to {greedyOut}");
        }
        bool same = greedyText.Count == 2 && greedyText[GgufEngine.Generator] == greedyText[GgufEngine.Auto];
        Console.WriteLine($"NOTE: greedy output on both engines: {(same ? "identical" : "DIFFERS (check the top-2 margin at that step with ML's Bench_OllamaGreedyDivergence_Margin)")}");
        if (!same && greedyText.Count == 2)
        {
            string g = greedyText[GgufEngine.Generator], b = greedyText[GgufEngine.Auto];
            int at = 0; while (at < g.Length && at < b.Length && g[at] == b[at]) at++;
            string Around(string s) => s.Substring(Math.Max(0, at - 40), Math.Min(s.Length, at + 60) - Math.Max(0, at - 40)).Replace("\n", "\\n");
            Console.WriteLine($"  first difference at character {at} of {g.Length} / {b.Length}:");
            Console.WriteLine($"  Generator: ...{Around(g)}...");
            Console.WriteLine($"  Auto     : ...{Around(b)}...");
        }
        Console.WriteLine($"RESULTS: {failures} failed");
        return failures;
    }
}
