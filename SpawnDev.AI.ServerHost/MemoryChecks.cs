using ILGPU.Runtime;
using SpawnDev.AI;
using SpawnDev.AI.Server;
using SpawnDev.ILGPU.ML;
using SpawnDev.ILGPU.ML.Hub;

/// <summary>
/// `memory-test [cpu|cuda|opencl] [entries]`: AiMemoryEngine with the REAL all-MiniLM-L6-v2 on the chosen accelerator (default cpu = Velocity, so it
/// never touches a GPU somebody else is using). Exit code = failures. The latency figure is printed for the chosen backend; Vertex's budget is p95 &lt;
/// 30 ms at 50,000 entries including the query embed.
/// </summary>
internal static class MemoryChecks
{
    // Realistic Vertex memories (what it would log on purpose), a few people, two worlds, all three kinds.
    static readonly (string Text, AiMemoryKind Kind, string Scope, string[] Subjects, float Importance, int DaysAgo)[] Memories =
    {
        ("We found diamonds at -312 11 845 in a ravine below the birch forest.", AiMemoryKind.Episode, "mc:home", new[] { "aubs" }, 0.9f, 3),
        ("Built a cobblestone tower next to spawn with Aubs.", AiMemoryKind.Episode, "mc:home", new[] { "aubs" }, 0.6f, 6),
        ("Aubs's favourite colour is pink.", AiMemoryKind.Fact, "", new[] { "aubs" }, 0.7f, 60),
        ("Aubs's favourite colour is purple now.", AiMemoryKind.Fact, "", new[] { "aubs" }, 0.7f, 2),
        ("Scaffolding placed over deep water sank twice; place blocks from the shore instead.", AiMemoryKind.Lesson, "mc:home", Array.Empty<string>(), 0.8f, 10),
        ("Ben visited and asked for a castle with a moat.", AiMemoryKind.Episode, "mc:home", new[] { "ben" }, 0.5f, 20),
        ("Ben likes building redstone doors.", AiMemoryKind.Fact, "", new[] { "ben" }, 0.6f, 19),
        ("Planted a wheat farm east of the house with Todd.", AiMemoryKind.Episode, "mc:home", new[] { "todd" }, 0.4f, 8),
        ("The nether portal is behind the house at 40 64 -12.", AiMemoryKind.Fact, "mc:home", Array.Empty<string>(), 0.8f, 30),
        ("Todd promised Aubs a pet wolf on Saturday.", AiMemoryKind.Episode, "mc:home", new[] { "todd", "aubs" }, 1.0f, 1),
        ("On the creative server we built a glass dome over the ocean.", AiMemoryKind.Episode, "mc:creative", new[] { "aubs" }, 0.5f, 12),
        ("Creepers blew up the north wall at night; build walls with stone, not wood.", AiMemoryKind.Lesson, "mc:home", Array.Empty<string>(), 0.7f, 15),
        ("Nikki asked Vertex to remember her birthday is in May.", AiMemoryKind.Fact, "", new[] { "nikki" }, 0.9f, 40),
        ("Mined iron in the cave under the village.", AiMemoryKind.Episode, "mc:home", new[] { "todd" }, 0.3f, 25),
        ("The village blacksmith has a chest with diamonds.", AiMemoryKind.Fact, "mc:home", Array.Empty<string>(), 0.6f, 26),
        ("Aubs named the cat Mittens.", AiMemoryKind.Fact, "", new[] { "aubs" }, 0.6f, 5),
        ("Drowned zombies near the river drop tridents sometimes.", AiMemoryKind.Lesson, "mc:home", Array.Empty<string>(), 0.4f, 33),
        ("We built a treehouse in the jungle on the creative server.", AiMemoryKind.Episode, "mc:creative", new[] { "aubs", "ben" }, 0.5f, 9),
        ("Ben's minecraft skin is a blue astronaut.", AiMemoryKind.Fact, "", new[] { "ben" }, 0.3f, 18),
        ("Last Tuesday we built a bridge across the canyon to the desert temple.", AiMemoryKind.Episode, "mc:home", new[] { "aubs" }, 0.7f, 5),
    };

    static readonly string[] Queries =
    {
        "Where did we find the diamonds?", "What colour does Aubs like?", "How should I build over water?", "What did we build last Tuesday?",
        "Where is the nether portal?", "What does Ben like to build?", "What did Todd promise?", "When is Nikki's birthday?",
        "What happened to the wall?", "What is the cat called?",
    };

    public static async Task<int> RunAsync(string[] args)
    {
        var which = args.Length > 1 ? args[1] : "cpu";
        int bigN = args.Length > 2 && int.TryParse(args[2], out var bn) ? bn : 50_000;
        int failures = 0;
        void Check(bool ok, string what) { Console.WriteLine($"TEST: {what}|{(ok ? "PASS" : "FAIL")}"); if (!ok) failures++; }

        using var context = MLContext.Create().ToContext();
        using var acc = which switch
        {
            "cuda" => context.Devices.First(d => d.AcceleratorType == AcceleratorType.Cuda).CreateAccelerator(context),
            "opencl" => context.Devices.First(d => d.AcceleratorType == AcceleratorType.OpenCL).CreateAccelerator(context),
            _ => MLContext.CreateCpuAccelerator(context),
        };
        Console.WriteLine($"accelerator: {acc.Name} ({acc.AcceleratorType})");
        using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        var models = new HttpClientModelSource(http, FileModelStore.Default());
        long now = new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc).Ticks;
        const string Model = ModelHub.KnownModels.AllMiniLmL6V2;

        // ---- 1. a store of real memories, through the engine ----
        var store = new AiMemoryStore(Model, 384);
        using var engine = new AiMemoryEngine(models, acc, store);
        var ids = new List<string>();
        var superseded = "";
        foreach (var m in Memories)
        {
            var id = await engine.RememberAsync(new AiMemoryEntry
            {
                Text = m.Text, Kind = m.Kind, Scope = m.Scope, Subjects = m.Subjects.ToList(), Importance = m.Importance,
                TimeTicks = now - TimeSpan.FromDays(m.DaysAgo).Ticks,
            });
            ids.Add(id!);
            if (m.Text.Contains("pink")) superseded = id!;
        }
        store.Supersede(superseded, ids[Array.FindIndex(Memories, x => x.Text.Contains("purple"))]);

        // ---- 2. parity with the reference search, over several filters ----
        var filters = new (string Name, AiMemoryFilter? F)[]
        {
            ("none", null),
            ("scope mc:home", new AiMemoryFilter { Scope = "mc:home" }),
            ("facts + lessons", new AiMemoryFilter { Kinds = new[] { AiMemoryKind.Fact, AiMemoryKind.Lesson } }),
            ("last 10 days", new AiMemoryFilter { FromTicks = now - TimeSpan.FromDays(10).Ticks }),
            ("about aubs", new AiMemoryFilter { AnySubject = new[] { "aubs" } }),
            ("with superseded", new AiMemoryFilter { IncludeSuperseded = true }),
        };
        int compared = 0, mismatched = 0; string? firstMismatch = null;
        foreach (var q in Queries)
        {
            var qv = await engine.EmbedAsync(q);
            foreach (var (name, f) in filters)
            {
                var dev = await engine.SearchAsync(q, 5, f, nowTicks: now);
                var cpu = store.Search(qv, 5, f, nowTicks: now);
                compared++;
                bool same = dev.Count == cpu.Count && dev.Zip(cpu).All(p => p.First.Entry.Id == p.Second.Entry.Id && Math.Abs(p.First.Score - p.Second.Score) < 1e-3f);
                if (!same) { mismatched++; firstMismatch ??= $"'{q}' [{name}]: device [{string.Join(", ", dev.Select(h => $"{h.Score:F4}"))}] vs reference [{string.Join(", ", cpu.Select(h => $"{h.Score:F4}"))}]"; }
            }
        }
        if (firstMismatch != null) Console.WriteLine("  first mismatch: " + firstMismatch);
        Check(mismatched == 0, $"device search = reference search for {compared} query x filter cases (same ids in order, scores within 1e-3)");

        // ---- 3. it finds what a person would expect ----
        var diamonds = await engine.SearchAsync("Where did we find the diamonds?", 3, nowTicks: now);
        Console.WriteLine($"  diamonds -> {diamonds[0].Entry.Text} ({diamonds[0].Similarity:F3})");
        Check(diamonds[0].Entry.Text.Contains("-312 11 845"), "\"Where did we find the diamonds?\" finds the episode with the coordinates");
        var colour = await engine.SearchAsync("What colour does Aubs like?", 1, nowTicks: now);
        Check(colour.Count == 1 && colour[0].Entry.Text.Contains("purple"), $"the superseded fact (pink) is not the answer: '{colour.FirstOrDefault()?.Entry.Text}'");
        var lesson = await engine.SearchAsync("How should I build over water?", 1, new AiMemoryFilter { Kinds = new[] { AiMemoryKind.Lesson } }, nowTicks: now);
        Check(lesson.Count == 1 && lesson[0].Entry.Text.Contains("shore"), $"the lesson about water comes back for a building question: '{lesson.FirstOrDefault()?.Entry.Text}'");

        // ---- 4. the engine follows the store ----
        var direct = store.Add(new AiMemoryEntry { Text = "The llama pen is north of the lake.", Kind = AiMemoryKind.Fact, TimeTicks = now }, await engine.EmbedAsync("The llama pen is north of the lake."));
        var found = await engine.SearchAsync("Where are the llamas?", 1, nowTicks: now);
        Check(found.Count == 1 && found[0].Entry.Id == direct, "an entry added straight to the store is searchable at once");
        var benFact = store.Add(new AiMemoryEntry { Text = "Ben and Aubs both like castles.", Kind = AiMemoryKind.Fact, DerivedFrom = new() { ids[5] }, TimeTicks = now },
            await engine.EmbedAsync("Ben and Aubs both like castles."));
        var gone = store.ForgetSubject("ben");
        var afterForget = new List<AiMemoryHit>();
        foreach (var q in new[] { "castle with a moat", "redstone doors", "blue astronaut skin", "Ben and Aubs both like castles" })
            afterForget.AddRange(await engine.SearchAsync(q, 10, new AiMemoryFilter { IncludeSuperseded = true }, nowTicks: now));
        Check(gone.Contains(benFact!) && afterForget.All(h => !h.Entry.Subjects.Contains("ben") && h.Entry.Id != benFact),
            $"forgetting Ben removes his memories and the fact derived from them from search ({gone.Count} removed)");

        // ---- 5. a store from another model is refused, then re-embedded ----
        {
            var old = new AiMemoryStore("old/model-384", 384);
            var rnd = new Random(3);
            foreach (var m in Memories.Take(8))
            {
                var v = new float[384]; for (int d = 0; d < 384; d++) v[d] = (float)rnd.NextDouble() - 0.5f;
                old.Add(new AiMemoryEntry { Text = m.Text, Kind = m.Kind, TimeTicks = now }, v);
            }
            using var e2 = new AiMemoryEngine(models, acc, old);
            bool refused = false;
            try { await e2.EnsureReadyAsync(); } catch (InvalidOperationException) { refused = true; }
            await e2.ReembedAsync();
            var r = await e2.SearchAsync("Where did we find the diamonds?", 1, nowTicks: now);
            Check(refused && old.ModelId == Model && r.Count == 1 && r[0].Entry.Text.Contains("diamonds"),
                "a store of another model's vectors is refused, then re-embedded and searched");
        }

        // ---- 6. latency at Vertex's lifetime size ----
        {
            var big = new AiMemoryStore(Model, 384);
            var basis = Memories.Select(m => store.GetEmbedding(ids[Array.IndexOf(Memories, m)])).Where(v => v != null).ToList();
            var rnd = new Random(11);
            for (int n = 0; n < bigN; n++)
            {
                var b = basis[n % basis.Count]!;
                var v = new float[384]; for (int d = 0; d < 384; d++) v[d] = b[d] + (float)(rnd.NextDouble() - 0.5) * 0.2f;
                big.Add(new AiMemoryEntry { Text = $"memory {n}", Kind = (AiMemoryKind)(n % 3), Scope = n % 2 == 0 ? "mc:home" : "mc:creative", TimeTicks = now - n * TimeSpan.TicksPerMinute, Importance = (n % 10) / 10f }, v);
            }
            using var e3 = new AiMemoryEngine(models, acc, big);
            var load = System.Diagnostics.Stopwatch.StartNew();
            await e3.EnsureReadyAsync();
            Console.WriteLine($"  {bigN:N0} entries uploaded in {load.Elapsed.TotalMilliseconds:F0} ms");
            for (int w = 0; w < 3; w++) await e3.SearchAsync(Queries[w], 10, new AiMemoryFilter { Scope = "mc:home" }, nowTicks: now);
            var times = new List<double>();
            for (int n = 0; n < 50; n++)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var r = await e3.SearchAsync(Queries[n % Queries.Length], 10, new AiMemoryFilter { Scope = "mc:home" }, nowTicks: now);
                times.Add(sw.Elapsed.TotalMilliseconds);
                if (r.Count != 10) { Check(false, $"a 50k search returned {r.Count} results"); break; }
            }
            times.Sort();
            double p50 = times[times.Count / 2], p95 = times[(int)(times.Count * 0.95) - 1];
            Console.WriteLine($"BENCH: memory-search {acc.AcceleratorType} entries={bigN} k=10 (embed + filter + rank) p50={p50:F1} ms p95={p95:F1} ms");
            // The split: the embedding alone, and filter + rank alone (a query already embedded).
            var embedT = new List<double>(); var rankT = new List<double>();
            var qv = await e3.EmbedAsync(Queries[0]);
            for (int n = 0; n < 30; n++)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew(); await e3.EmbedAsync(Queries[n % Queries.Length]); embedT.Add(sw.Elapsed.TotalMilliseconds);
                sw.Restart(); await e3.SearchAsync(qv, 10, new AiMemoryFilter { Scope = "mc:home" }, nowTicks: now); rankT.Add(sw.Elapsed.TotalMilliseconds);
            }
            embedT.Sort(); rankT.Sort();
            Console.WriteLine($"BENCH: memory-split {acc.AcceleratorType} embed p50={embedT[15]:F1} ms, filter+rank p50={rankT[15]:F1} ms (entries={bigN})");
            Check(true, $"latency measured on {acc.AcceleratorType}: p95 {p95:F1} ms (budget 30 ms on the GPU)");
        }

        Console.WriteLine($"RESULTS: {failures} failed");
        return failures;
    }
}
