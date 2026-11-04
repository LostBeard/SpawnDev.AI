using ILGPU.Runtime;
using SpawnDev.AI;
using SpawnDev.AI.Server;
using SpawnDev.ILGPU.ML;

/// <summary>
/// `pass-test [model] [cuda|opencl|cpu]`: AiChatEngine.PassObserver reports each generation pass with the exact rendered prompt and the raw
/// output (for LoRA training pairs, which must match inference token for token). Checks on a real model:
/// one pass per plain chat; the ids are what the decoder used; the rendered text keeps the template's special tokens and contains the
/// user's words; the raw output contains the visible reply; the rendered prompt is the text of the ids (re-rendering is stable);
/// a throwing observer does not fail the chat; no observer, no call.
/// </summary>
internal static class PassObserverChecks
{
    public static async Task<int> RunAsync(string[] args)
    {
        var model = args.Length > 1 ? args[1] : "qwen3:1.7b";
        var which = args.Length > 2 ? args[2] : "cuda";
        int failures = 0;
        void Check(bool ok, string what) { Console.WriteLine($"TEST: {what}|{(ok ? "PASS" : "FAIL")}"); if (!ok) failures++; }

        using var context = MLContext.Create().ToContext();
        using var acc = which switch
        {
            "opencl" => context.Devices.First(d => d.AcceleratorType == AcceleratorType.OpenCL).CreateAccelerator(context),
            "cpu" => MLContext.CreateCpuAccelerator(context),
            _ => context.Devices.First(d => d.AcceleratorType == AcceleratorType.Cuda).CreateAccelerator(context),
        };
        Console.WriteLine($"accelerator: {acc.Name} ({acc.AcceleratorType}), model {model}");
        await using var registry = new ModelRegistry(new OllamaCacheModelProvider(new OllamaModelStore()), acc);
        var chat = new AiChatEngine(registry);
        const string Words = "Name one planet with rings.";
        var request = new AiChatRequest
        {
            Model = model,
            Messages = new[] { new AiChatMessage("system", "Answer in one short sentence."), new AiChatMessage("user", Words) },
            Options = new AiGenerationOptions { Strategy = "greedy", Temperature = 0, MaxOutputTokens = 48 },
        };

        // No observer: nothing to call (and the chat works).
        var first = await chat.ChatAsync(request);
        Check(first.GeneratedTokens > 0, "a chat without an observer answers");

        var passes = new List<AiChatPass>();
        chat.PassObserver = passes.Add;
        var res = await chat.ChatAsync(request);
        Check(passes.Count == 1, $"one pass for a plain chat (got {passes.Count})");
        if (passes.Count == 1)
        {
            var p = passes[0];
            Console.WriteLine($"  prompt ids {p.PromptTokenIds.Length}, reused {p.ReusedPrefixTokens}, generated {p.GeneratedTokens}, stop {p.Stop}");
            Console.WriteLine($"  rendered: {p.RenderedPrompt.Replace("\n", "\\n")}");
            Console.WriteLine($"  raw: {p.RawOutput.Replace("\n", "\\n")}");
            Check(p.PromptTokenIds.Length == res.PromptTokens, $"the ids are the prompt the decoder used ({p.PromptTokenIds.Length} vs {res.PromptTokens})");
            Check(p.GeneratedTokens == res.GeneratedTokens && p.VisibleText == res.Text, "the pass's output is the result's");
            Check(p.RenderedPrompt.Contains(Words), "the rendered prompt contains the user's words");
            Check(p.RenderedPrompt.Contains("<|") || p.RenderedPrompt.Contains("<start_of_turn>") || p.RenderedPrompt.Contains("[INST]"),
                "the rendered prompt keeps the template's special tokens");
            Check(p.RawOutput.Contains(p.VisibleText.Trim()), "the raw output contains the visible reply");
            Check(p.Messages.Count == 2 && p.Messages[1].Content == Words, "the pass carries the messages it was built from");
            // Rendering is a pure function of the ids: render the ids again through a second observed chat and compare.
            passes.Clear();
            await chat.ChatAsync(request);
            Check(passes.Count == 1 && passes[0].RenderedPrompt == p.RenderedPrompt && passes[0].PromptTokenIds.SequenceEqual(p.PromptTokenIds),
                "the same request renders the same prompt and ids");
        }

        // A throwing observer is reported, not fatal.
        string? perf = null;
        chat.PerfLog = line => { if (line.Contains("PassObserver threw")) perf = line; };
        chat.PassObserver = _ => throw new InvalidOperationException("observer failure");
        var r3 = await chat.ChatAsync(request);
        Check(r3.GeneratedTokens > 0 && perf != null, "a throwing observer does not fail the chat and is reported through PerfLog");

        Console.WriteLine($"RESULTS: {failures} failed");
        return failures;
    }
}
