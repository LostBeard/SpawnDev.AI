using ILGPU.Runtime;
using SpawnDev.AI;
using SpawnDev.AI.Server;
using SpawnDev.ILGPU.ML;
using SpawnDev.ILGPU.ML.Pipelines;

/// <summary>
/// `chat-bench [model] [cuda|opencl|cpu]`: decode speed of AiChatEngine on an Ollama-cache model with each engine (the session engine, then Auto -
/// the batch decoder where supported), greedy and top-p + penalty, 128 new tokens, 5 runs each after one warm-up; and whether, and where, greedy
/// output differs between the engines. A difference is reported, not failed: two correct engines can flip a near-tie (see the NOTE below).
/// </summary>
internal static class ChatBench
{
    public static async Task<int> RunAsync(string[] args)
    {
        var model = args.Length > 1 ? args[1] : "qwen3:1.7b";
        var which = args.Length > 2 ? args[2] : "cuda";
        int failures = 0;
        using var context = MLContext.Create().ToContext();
        using var acc = which switch
        {
            "opencl" => context.Devices.First(d => d.AcceleratorType == AcceleratorType.OpenCL).CreateAccelerator(context),
            "cpu" => MLContext.CreateCpuAccelerator(context),
            _ => context.Devices.First(d => d.AcceleratorType == AcceleratorType.Cuda).CreateAccelerator(context),
        };
        Console.WriteLine($"accelerator: {acc.Name} ({acc.AcceleratorType}), model {model}");
        var messages = new[] { new AiChatMessage("user", "Write a short paragraph about why GPUs are good at running neural networks.") };
        var greedy = new AiGenerationOptions { Strategy = "greedy", Temperature = 0, MaxOutputTokens = 128 };
        var topP = new AiGenerationOptions { Strategy = "top_p", Temperature = 0.7f, TopP = 0.9f, RepetitionPenalty = 1.1f, Seed = 7, MaxOutputTokens = 128 };
        var greedyText = new Dictionary<GgufEngine, string>();
        foreach (var engine in new[] { GgufEngine.Generator, GgufEngine.Auto })
        {
            var provider = new OllamaCacheModelProvider(new OllamaModelStore()) { Engine = engine };
            await using var registry = new ModelRegistry(provider, acc);
            var chat = new AiChatEngine(registry);
            string engineUsed = "?";
            chat.PerfLog = line => { var i = line.IndexOf("engine=", StringComparison.Ordinal); if (i >= 0) engineUsed = line[(i + 7)..].Trim(); };
            await chat.ChatAsync(new AiChatRequest { Model = model, Messages = messages, Options = greedy });   // load + warm-up
            foreach (var (name, opt) in new[] { ("greedy", greedy), ("top-p+penalty", topP) })
            {
                var rates = new List<double>();
                for (int r = 0; r < 5; r++)
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    var res = await chat.ChatAsync(new AiChatRequest { Model = model, Messages = messages, Options = opt });
                    rates.Add(res.GeneratedTokens / sw.Elapsed.TotalSeconds);
                    if (name == "greedy" && r == 0) greedyText[engine] = res.Text;
                }
                rates.Sort();
                Console.WriteLine($"BENCH: chat {acc.AcceleratorType} {model} requested={engine} used={engineUsed} {name}: median {rates[2]:F1} tok/s (min {rates[0]:F1}, max {rates[4]:F1}) incl. prompt");
            }
        }
        // NOT a pass / fail. Two correct engines (different kernels, different accumulation order) may take different greedy paths at a
        // near-tie: smollm2:360m diverged at token 81 where the top-2 logit margin was 0.0024-0.015 while the engines' logits differ by up to
        // ~0.04 (Tuvok, 2026-11-02, ML Bench_OllamaGreedyDivergence_Margin). Whether a divergence is a tie or a fault needs the logits, which
        // this host does not see: that gate lives in SpawnDev.ILGPU.ML. Here: report whether and where the texts first differ.
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
