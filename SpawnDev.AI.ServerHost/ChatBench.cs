using ILGPU.Runtime;
using SpawnDev.AI;
using SpawnDev.AI.Server;
using SpawnDev.ILGPU.ML;
using SpawnDev.ILGPU.ML.Pipelines;

/// <summary>
/// `chat-bench [model] [cuda|opencl|cpu]`: decode speed of AiChatEngine on an Ollama-cache model with each engine (the session engine, then Auto -
/// the batch decoder where supported), greedy and top-p + penalty, 128 new tokens, 5 runs each after one warm-up; and whether greedy output is the
/// same text on both engines (it must be: greedy is deterministic on each). Exit code = failures.
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
        bool same = greedyText.Count == 2 && greedyText[GgufEngine.Generator] == greedyText[GgufEngine.Auto];
        Console.WriteLine($"TEST: greedy output is the same text on both engines|{(same ? "PASS" : "FAIL")}");
        if (!same) { failures++; foreach (var kv in greedyText) Console.WriteLine($"  {kv.Key}: {kv.Value[..Math.Min(160, kv.Value.Length)]}"); }
        Console.WriteLine($"RESULTS: {failures} failed");
        return failures;
    }
}
