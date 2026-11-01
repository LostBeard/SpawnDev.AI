using ILGPU.Runtime;
using SpawnDev.AI;
using SpawnDev.AI.Server;
using SpawnDev.ILGPU.ML;
using SpawnDev.ILGPU.ML.Hub;

/// <summary>
/// `memory-consolidate [model] [cpu|cuda|opencl] [quality]`: AiMemoryEngine.ConsolidateAsync with a REAL chat model from the local Ollama cache.
/// The structural checks hold whatever the model writes (provenance, subjects, scope, flags, compaction, privacy after compaction); with `quality`
/// the model-dependent ones are asserted too (pink -> purple superseded, coordinates kept) - meant for a capable model (llama3.1:8b on a GPU).
/// </summary>
internal static class ConsolidateChecks
{
    public static async Task<int> RunAsync(string[] args)
    {
        var model = args.Length > 1 ? args[1] : "qwen3:1.7b";
        var which = args.Length > 2 ? args[2] : "cpu";
        bool quality = args.Contains("quality");
        int failures = 0;
        void Check(bool ok, string what) { Console.WriteLine($"TEST: {what}|{(ok ? "PASS" : "FAIL")}"); if (!ok) failures++; }
        void Note(bool ok, string what) { if (quality) Check(ok, what); else Console.WriteLine($"NOTE: {what}|{(ok ? "yes" : "no")} (not asserted for {model})"); }

        using var context = MLContext.Create().ToContext();
        using var acc = which switch
        {
            "cuda" => context.Devices.First(d => d.AcceleratorType == AcceleratorType.Cuda).CreateAccelerator(context),
            "opencl" => context.Devices.First(d => d.AcceleratorType == AcceleratorType.OpenCL).CreateAccelerator(context),
            _ => MLContext.CreateCpuAccelerator(context),
        };
        Console.WriteLine($"accelerator: {acc.Name} ({acc.AcceleratorType}), chat model {model}");
        using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        await using var registry = new ModelRegistry(new OllamaCacheModelProvider(new OllamaModelStore()), acc);
        var chat = new AiChatEngine(registry);
        long now = new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc).Ticks;
        long Days(int d) => now - TimeSpan.FromDays(d).Ticks;

        var store = new AiMemoryStore(ModelHub.KnownModels.AllMiniLmL6V2, 384);
        using var engine = new AiMemoryEngine(new HttpClientModelSource(http, FileModelStore.Default()), acc, store);
        // an older fact the log will update
        var pink = await engine.RememberAsync(new AiMemoryEntry { Kind = AiMemoryKind.Fact, Text = "Aubs's favourite colour is pink.", Scope = "mc:home", Subjects = new() { "aubs" }, TimeTicks = Days(60), Importance = 0.7f });
        var log = new (string Text, string[] Subjects, int DaysAgo, float Importance, bool Pinned)[]
        {
            ("Aubs and Vertex found diamonds at -312 11 845 in a ravine below the birch forest.", new[] { "aubs" }, 20, 0.9f, false),
            ("Built a cobblestone tower next to spawn with Aubs.", new[] { "aubs" }, 18, 0.5f, false),
            ("Aubs said her favourite colour is purple now, not pink.", new[] { "aubs" }, 16, 0.6f, false),
            ("Ben visited and asked for a castle with a moat.", new[] { "ben" }, 15, 0.5f, false),
            ("Todd promised Aubs a pet wolf on Saturday.", new[] { "todd", "aubs" }, 14, 1.0f, true),
            ("Walked around the village.", Array.Empty<string>(), 13, 0.1f, false),
            ("The nether portal is behind the house at 40 64 -12.", Array.Empty<string>(), 12, 0.8f, false),
            ("Aubs named the cat Mittens.", new[] { "aubs" }, 2, 0.6f, false),
        };
        var episodeIds = new List<string>();
        foreach (var e in log)
            episodeIds.Add((await engine.RememberAsync(new AiMemoryEntry { Kind = AiMemoryKind.Episode, Text = e.Text, Scope = "mc:home", Subjects = e.Subjects.ToList(), TimeTicks = Days(e.DaysAgo), Importance = e.Importance, Pinned = e.Pinned }))!);
        var before = store.List(new AiMemoryFilter { IncludeSuperseded = true }).Select(x => x.Id).ToHashSet();

        var clock = System.Diagnostics.Stopwatch.StartNew();
        var report = await engine.ConsolidateAsync(chat, model, new AiConsolidationOptions { CompactAfter = TimeSpan.FromDays(10), NowTicks = now });
        Console.WriteLine($"  {report} in {clock.Elapsed.TotalSeconds:F0} s");
        var facts = store.List(new AiMemoryFilter { Kinds = new[] { AiMemoryKind.Fact }, IncludeSuperseded = true }).Where(f => !before.Contains(f.Id)).ToList();
        foreach (var f in facts) Console.WriteLine($"  FACT {(f.SupersededBy != null ? "(superseded) " : "")}{f.Text}  <- [{string.Join(",", f.DerivedFrom.Select(id => episodeIds.IndexOf(id) + 1))}] subjects [{string.Join(",", f.Subjects)}]");

        // ---- structural: hold whatever the model wrote ----
        Check(report.EpisodesDigested == log.Length, $"every episode was digested ({report.EpisodesDigested}/{log.Length})");
        Check(store.List(new AiMemoryFilter { Kinds = new[] { AiMemoryKind.Episode }, IncludeSuperseded = true }).All(e => e.Consolidated), "every remaining episode is marked consolidated");
        Check(facts.Count > 0, $"the model produced at least one fact ({facts.Count}; {report.LinesIgnored} lines it wrote were not facts)");
        bool provenance = facts.All(f => f.DerivedFrom.Count > 0 && f.DerivedFrom.All(episodeIds.Contains) && f.Scope == "mc:home" && f.Source == "consolidation");
        Check(provenance, "every new fact names its source episodes, keeps their scope and says it came from consolidation");
        bool subjects = facts.All(f =>
        {
            var want = f.DerivedFrom.SelectMany(id => log[episodeIds.IndexOf(id)].Subjects).ToHashSet();
            return want.All(f.Subjects.Contains);
        });
        Check(subjects, "every new fact carries the subjects of all its source episodes (what lets a person be forgotten after compaction)");
        var left = store.List(new AiMemoryFilter { Kinds = new[] { AiMemoryKind.Episode }, IncludeSuperseded = true }).Select(e => e.Id).ToHashSet();
        bool compacted = log.Select((e, i) => (e, i)).All(x =>
            left.Contains(episodeIds[x.i]) == (x.e.Pinned || x.e.Importance >= 0.8f || x.e.DaysAgo <= 10));
        Check(compacted && report.EpisodesCompacted == log.Count(e => !e.Pinned && e.Importance < 0.8f && e.DaysAgo > 10),
            $"compaction removed exactly the old, unpinned, unimportant episodes ({report.EpisodesCompacted}) and kept the promise, the diamonds and recent ones");
        var factsFromCompacted = facts.Where(f => f.DerivedFrom.Any(id => !left.Contains(id))).ToList();
        Check(factsFromCompacted.All(f => store.Get(f.Id) != null), $"facts digested from compacted episodes survive compaction ({factsFromCompacted.Count})");
        // By PROVENANCE, not by the fact's own Subjects: selecting by Subjects checked an empty set when the subjects were lost (red-check
        // 2026-11-01) - exactly the breach this exists to catch. Every fact built from an episode about Aubs must go.
        var aubsFacts = facts.Where(f => f.DerivedFrom.Any(id => episodeIds.IndexOf(id) is var i && i >= 0 && log[i].Subjects.Contains("aubs"))).Select(f => f.Id).ToList();
        store.ForgetSubject("aubs");
        Check(aubsFacts.Count > 0 && aubsFacts.All(id => store.Get(id) == null) && store.List(new AiMemoryFilter { IncludeSuperseded = true }).All(e => !e.Subjects.Contains("aubs")),
            $"forgetting Aubs after compaction removes every fact built from an episode about her ({aubsFacts.Count})");

        // ---- quality: depends on the model ----
        Note(store.Get(pink!) == null || facts.Any(f => f.Text.Contains("purple", StringComparison.OrdinalIgnoreCase)), "a fact says the favourite colour is purple");
        Note(facts.Any(f => f.Text.Contains("-312") && f.Text.Contains("845")), "the diamond coordinates were kept exactly");
        Note(report.FactsReplaced >= 1, $"the purple fact superseded the pink one (replaced {report.FactsReplaced})");

        Console.WriteLine($"RESULTS: {failures} failed");
        return failures;
    }
}
