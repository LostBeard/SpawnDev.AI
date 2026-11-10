using System.Text.Json;
using ILGPU.Runtime;
using SpawnDev.AI;
using SpawnDev.AI.Server;
using SpawnDev.ILGPU.ML;
using SpawnDev.ILGPU.ML.Pipelines;
using SpawnDev.ILGPU.ML.Preprocessing;

/// <summary>
/// `lora-test [cuda|opencl] [fixtureDir]`: LoRA adapters through <see cref="ModelRegistry.SetAdapterAsync(string, string?, CancellationToken)"/>, on both
/// engines, against SpawnDev.ILGPU.ML's own outputs for the same adapter (Tuvok's smollm2:360m synthetic fixture: 24 dev lines, each with the
/// prompt ids, the greedy ids GgufGenerator produced with the adapter and without it, per engine).
/// Checks, per engine: the adapter set before load is applied at load (24/24 lines equal the adapter ids); clearing it gives the base model
/// back (each line equals that engine's base ids, and differs from the adapter); on the batch decoder an adapter set on the RESIDENT model is
/// swapped in place (same loaded model, no reload) and generates the adapter ids; a chat pass records the adapter label and the stop token that
/// ended it.
/// </summary>
internal static class LoraChecks
{
    const string Model = "smollm2:360m";

    sealed record Line(string id, int[] prompt_ids, int[] target_ids, int end_of_turn_id, int[] base_ids, int[] adapter_ids);

    public static async Task<int> RunAsync(string[] args)
    {
        var which = args.Length > 1 ? args[1] : "cuda";
        var dir = args.Length > 2 ? args[2] : @"D:\users\tj\Projects\_Training\fixtures\smollm2-360m-synthetic-lora";
        int failures = 0;
        void Check(bool ok, string what) { Console.WriteLine($"TEST: {what}|{(ok ? "PASS" : "FAIL")}"); if (!ok) failures++; }
        List<Line> Read(string file) => File.ReadAllLines(Path.Combine(dir, file)).Where(l => l.Trim().Length > 0)
            .Select(l => JsonSerializer.Deserialize<Line>(l)!).ToList();
        var batchRef = Read("generation_eval.jsonl");             // Auto = BatchDecoder on CUDA
        var sessionRef = Read("generation_eval.generator.jsonl"); // the session engine

        using var context = MLContext.Create().ToContext();
        using var acc = which switch
        {
            "opencl" => context.Devices.First(d => d.AcceleratorType == AcceleratorType.OpenCL).CreateAccelerator(context),
            _ => context.Devices.First(d => d.AcceleratorType == AcceleratorType.Cuda).CreateAccelerator(context),
        };
        Console.WriteLine($"accelerator: {acc.Name} ({acc.AcceleratorType}), model {Model}, fixture {dir}");

        async Task<int[][]> RunLines(ModelRegistry reg, List<Line> lines)
        {
            var outs = new int[lines.Count][];
            for (int i = 0; i < lines.Count; i++)
            {
                using var lease = await reg.AcquireAsync(Model);
                var cfg = new GenerationConfig { Strategy = "greedy", Temperature = 0, MaxNewTokens = lines[i].target_ids.Length + 4 };
                var res = await lease.Model.Generator.GenerateAsync(lines[i].prompt_ids, cfg, null, new[] { lines[i].end_of_turn_id });
                outs[i] = res.GeneratedTokenIds;
            }
            return outs;
        }
        int Same(int[][] got, List<Line> lines, Func<Line, int[]> want) => got.Select((g, i) => g.SequenceEqual(want(lines[i])) ? 1 : 0).Sum();

        foreach (var (engine, refs) in new[] { (GgufEngine.Generator, sessionRef), (GgufEngine.Auto, batchRef) })
        {
            await using var registry = new ModelRegistry(new OllamaCacheModelProvider(new OllamaModelStore()) { Engine = engine }, acc);
            // 1) Set before load: applied when the model loads.
            await registry.SetAdapterAsync(Model, dir);
            var adapterOut = await RunLines(registry, refs);
            string used;
            using (var l = await registry.AcquireAsync(Model)) used = l.Model.Generator.Engine.ToString();
            int a = Same(adapterOut, refs, r => r.adapter_ids);
            int hit = Same(adapterOut, refs, r => r.target_ids);
            Console.WriteLine($"  [{engine} -> {used}] adapter: {a}/{refs.Count} equal ML's adapter ids, {hit}/{refs.Count} equal the target");
            Check(a == refs.Count, $"{engine}: an adapter set before load generates ML's adapter ids on every line ({a}/{refs.Count})");

            // 2) Cleared: the base model again (session engine: reloaded; batch decoder: swapped in place).
            await registry.SetAdapterAsync(Model, (string?)null);
            var baseOut = await RunLines(registry, refs);
            int b = Same(baseOut, refs, r => r.base_ids);
            int differ = baseOut.Select((g, i) => g.SequenceEqual(adapterOut[i]) ? 0 : 1).Sum();
            Console.WriteLine($"  [{engine}] base: {b}/{refs.Count} equal ML's base ids, {differ}/{refs.Count} differ from the adapter output");
            // name each miss (line id, the first differing position, got vs want) so a near-tie can be measured at that step
            for (int i = 0; i < refs.Count; i++)
            {
                if (baseOut[i].SequenceEqual(refs[i].base_ids)) continue;
                int at = 0;
                while (at < baseOut[i].Length && at < refs[i].base_ids.Length && baseOut[i][at] == refs[i].base_ids[at]) at++;
                Console.WriteLine($"    miss {refs[i].id}: first difference at generated token {at}; got [{string.Join(",", baseOut[i])}], want [{string.Join(",", refs[i].base_ids)}], prompt {refs[i].prompt_ids.Length} ids");
            }
            Check(b == refs.Count, $"{engine}: cleared, the model generates ML's base ids on every line ({b}/{refs.Count})");
            Check(differ == refs.Count, $"{engine}: base and adapter outputs differ on every line ({differ}/{refs.Count})");

            // 3) Batch decoder: set on the RESIDENT model, swapped in place.
            if (used == nameof(GgufEngine.BatchDecoder))
            {
                LoadedModel before;
                using (var l = await registry.AcquireAsync(Model)) before = l.Model;
                await registry.SetAdapterAsync(Model, dir);
                LoadedModel after;
                using (var l = await registry.AcquireAsync(Model)) after = l.Model;
                Check(ReferenceEquals(before, after) && after.AdapterLabel == Path.GetFileName(dir),
                    "batch decoder: an adapter set on the resident model is swapped in place (no reload), labelled with its directory");
                var swapped = await RunLines(registry, refs);
                int s = Same(swapped, refs, r => r.adapter_ids);
                Check(s == refs.Count, $"batch decoder: the swapped-in adapter generates ML's adapter ids ({s}/{refs.Count})");
            }

            // 4) A chat pass records the adapter and the token that ended it.
            if (engine == GgufEngine.Auto)
            {
                var chat = new AiChatEngine(registry);
                AiChatPass? pass = null;
                chat.PassObserver = p => pass = p;
                await chat.ChatAsync(new AiChatRequest
                {
                    Model = Model,
                    Messages = new[] { new AiChatMessage("user", "Say hello.") },
                    Options = new AiGenerationOptions { Strategy = "greedy", Temperature = 0, MaxOutputTokens = 64 },
                });
                Console.WriteLine($"  chat pass: adapter {pass?.AdapterLabel}, stop {pass?.Stop}, end token {pass?.EndTokenId?.ToString() ?? "null"}, end-of-turn [{string.Join(",", pass?.EndOfTurnTokenIds ?? Array.Empty<int>())}]");
                Check(pass?.AdapterLabel == Path.GetFileName(dir), "a chat pass records the adapter label");
                // No stop strings in this request: a natural stop must name its token, a length stop must not.
                Check(pass != null && (pass.Stop == AiStopKind.Length ? pass.EndTokenId == null
                    : pass.EndTokenId != null && !pass.GeneratedTokenIds.Contains(pass.EndTokenId.Value)),
                    "a chat pass records the stop token that ended it (null on a length stop), not among the generated ids");
            }
        }
        Console.WriteLine($"RESULTS: {failures} failed");
        return failures;
    }

    /// <summary>
    /// `lora-ab [cuda|opencl] [model] [outJson] [fixtureDir]`: the fixture's adapter on ANOTHER base model (default the Q8_0 build of
    /// smollm2:360m), on the batch decoder, writing every phase's greedy ids to <c>outJson</c> so two runs (two SpawnDev.ILGPU.ML versions,
    /// or a switch on and off) can be compared token for token. The fixture's reference ids are for its F16 base, so this run cannot be
    /// judged against them; it checks only what holds within one run: the adapter changes the output, and swapping the adapter back
    /// in place gives the same ids as setting it at load.
    /// </summary>
    /// <remarks>
    /// Added for ML 5.8.6's Q8_0G regrouping (Q8_0 weights rearranged in place at load): the fixture and ML's own LoRA decoder tests all
    /// use F16 bases, so nothing ran an adapter over Q8_0 weights.
    /// </remarks>
    public static async Task<int> RunAbAsync(string[] args)
    {
        var which = args.Length > 1 ? args[1] : "cuda";
        var model = args.Length > 2 ? args[2] : "smollm2:360m-instruct-q8_0";
        var outJson = args.Length > 3 ? args[3] : Path.Combine(Path.GetTempPath(), "lora-ab.json");
        var dir = args.Length > 4 ? args[4] : @"D:\users\tj\Projects\_Training\fixtures\smollm2-360m-synthetic-lora";
        int failures = 0;
        void Check(bool ok, string what) { Console.WriteLine($"TEST: {what}|{(ok ? "PASS" : "FAIL")}"); if (!ok) failures++; }
        var lines = File.ReadAllLines(Path.Combine(dir, "generation_eval.jsonl")).Where(l => l.Trim().Length > 0)
            .Select(l => JsonSerializer.Deserialize<Line>(l)!).ToList();

        using var context = MLContext.Create().ToContext();
        using var acc = which switch
        {
            "opencl" => context.Devices.First(d => d.AcceleratorType == AcceleratorType.OpenCL).CreateAccelerator(context),
            _ => context.Devices.First(d => d.AcceleratorType == AcceleratorType.Cuda).CreateAccelerator(context),
        };
        Console.WriteLine($"accelerator: {acc.Name} ({acc.AcceleratorType}), model {model}, fixture {dir}");

        await using var registry = new ModelRegistry(new OllamaCacheModelProvider(new OllamaModelStore()) { Engine = GgufEngine.Auto }, acc);
        async Task<int[][]> RunLines()
        {
            var outs = new int[lines.Count][];
            for (int i = 0; i < lines.Count; i++)
            {
                using var lease = await registry.AcquireAsync(model);
                var cfg = new GenerationConfig { Strategy = "greedy", Temperature = 0, MaxNewTokens = lines[i].target_ids.Length + 4 };
                var res = await lease.Model.Generator.GenerateAsync(lines[i].prompt_ids, cfg, null, new[] { lines[i].end_of_turn_id });
                outs[i] = res.GeneratedTokenIds;
            }
            return outs;
        }

        await registry.SetAdapterAsync(model, dir);
        var adapterOut = await RunLines();
        string used;
        using (var l = await registry.AcquireAsync(model)) used = l.Model.Generator.Engine.ToString();
        await registry.SetAdapterAsync(model, (string?)null);   // batch decoder: in place
        var baseOut = await RunLines();
        await registry.SetAdapterAsync(model, dir);             // in place again
        var againOut = await RunLines();

        int differ = baseOut.Select((b, i) => b.SequenceEqual(adapterOut[i]) ? 0 : 1).Sum();
        int same = againOut.Select((a, i) => a.SequenceEqual(adapterOut[i]) ? 1 : 0).Sum();
        Console.WriteLine($"  [{used}] base differs from adapter on {differ}/{lines.Count}; adapter swapped back equals adapter-at-load on {same}/{lines.Count}");
        Check(used == nameof(GgufEngine.BatchDecoder), $"the model runs on the batch decoder (used {used})");
        Check(differ >= lines.Count / 2, $"the adapter changes the output ({differ}/{lines.Count} lines differ from the base)");
        Check(same == lines.Count, $"swapping the adapter back in place gives the adapter-at-load ids ({same}/{lines.Count})");
        File.WriteAllText(outJson, JsonSerializer.Serialize(new { model, accelerator = acc.AcceleratorType.ToString(), adapter = adapterOut, @base = baseOut, again = againOut }));
        Console.WriteLine($"  ids written to {outJson}");
        Console.WriteLine($"RESULTS: {failures} failed");
        return failures;
    }
}
