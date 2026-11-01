using ILGPU.Runtime;
using SpawnDev.AI.Server;
using SpawnDev.ILGPU;
using SpawnDev.ILGPU.ML;
using SpawnDev.ILGPU.ML.Hub;

namespace SpawnDev.AI.Demo.Tests;

/// <summary>
/// AiMemoryEngine in the browser, on WebGPU, with the real all-MiniLM-L6-v2: the device search equals the reference (<see cref="AiMemoryStore.Search"/>)
/// and the store's changes reach the device. The desktop gate (ServerHost memory-test) covers CUDA / Velocity and the 50,000-entry latency. Heavy: ~90 MB model.
/// </summary>
public sealed class AiMemoryEngineTests
{
    private readonly IModelSource _models;
    public AiMemoryEngineTests(IModelSource models) => _models = models;

    private static readonly (string Text, AiMemoryKind Kind, string Scope, string[] Subjects, float Importance, int DaysAgo)[] Memories =
    {
        ("We found diamonds at -312 11 845 in a ravine below the birch forest.", AiMemoryKind.Episode, "mc:home", new[] { "aubs" }, 0.9f, 3),
        ("Built a cobblestone tower next to spawn with Aubs.", AiMemoryKind.Episode, "mc:home", new[] { "aubs" }, 0.6f, 6),
        ("Aubs's favourite colour is pink.", AiMemoryKind.Fact, "", new[] { "aubs" }, 0.7f, 60),
        ("Aubs's favourite colour is purple now.", AiMemoryKind.Fact, "", new[] { "aubs" }, 0.7f, 2),
        ("Scaffolding placed over deep water sank twice; place blocks from the shore instead.", AiMemoryKind.Lesson, "mc:home", Array.Empty<string>(), 0.8f, 10),
        ("Ben visited and asked for a castle with a moat.", AiMemoryKind.Episode, "mc:home", new[] { "ben" }, 0.5f, 20),
        ("Ben likes building redstone doors.", AiMemoryKind.Fact, "", new[] { "ben" }, 0.6f, 19),
        ("The nether portal is behind the house at 40 64 -12.", AiMemoryKind.Fact, "mc:home", Array.Empty<string>(), 0.8f, 30),
        ("On the creative server we built a glass dome over the ocean.", AiMemoryKind.Episode, "mc:creative", new[] { "aubs" }, 0.5f, 12),
        ("Aubs named the cat Mittens.", AiMemoryKind.Fact, "", new[] { "aubs" }, 0.6f, 5),
    };

    /// <summary>
    /// Device search = reference search on WebGPU for every query x filter case; a superseded fact is not the answer; forgetting a person takes
    /// their memories out of search. Fails if the WebGPU filter / rank kernel, the index, or the store-to-device event path is wrong.
    /// </summary>
    [AiTest(Heavy = true, Timeout = 600_000)]
    public async Task DeviceSearch_EqualsReference_OnWebGpu_AndFollowsTheStore()
    {
        var builder = MLContext.Create();
        await builder.AllAcceleratorsAsync();
        using var context = builder.ToContext();
        using var acc = await context.CreatePreferredAcceleratorAsync() ?? throw new Exception("no accelerator");
        if (acc.AcceleratorType != AcceleratorType.WebGPU) throw new Exception($"expected WebGPU, got {acc.AcceleratorType}");
        long now = new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc).Ticks;
        var store = new AiMemoryStore(ModelHub.KnownModels.AllMiniLmL6V2, 384);
        using var engine = new AiMemoryEngine(_models, acc, store);
        var ids = new List<string>();
        foreach (var m in Memories)
            ids.Add((await engine.RememberAsync(new AiMemoryEntry { Text = m.Text, Kind = m.Kind, Scope = m.Scope, Subjects = m.Subjects.ToList(), Importance = m.Importance, TimeTicks = now - TimeSpan.FromDays(m.DaysAgo).Ticks }))!);
        store.Supersede(ids[2], ids[3]);

        var queries = new[] { "Where did we find the diamonds?", "What colour does Aubs like?", "How should I build over water?", "What does Ben like?" };
        var filters = new AiMemoryFilter?[]
        {
            null, new() { Scope = "mc:home" }, new() { Kinds = new[] { AiMemoryKind.Fact } }, new() { FromTicks = now - TimeSpan.FromDays(10).Ticks },
            new() { AnySubject = new[] { "aubs" } }, new() { IncludeSuperseded = true },
        };
        int cases = 0;
        foreach (var q in queries)
        {
            var qv = await engine.EmbedAsync(q);
            foreach (var f in filters)
            {
                var dev = await engine.SearchAsync(q, 5, f, nowTicks: now);
                var cpu = store.Search(qv, 5, f, nowTicks: now);
                cases++;
                if (dev.Count != cpu.Count || dev.Zip(cpu).Any(p => p.First.Entry.Id != p.Second.Entry.Id || Math.Abs(p.First.Score - p.Second.Score) > 1e-3f))
                    throw new Exception($"'{q}': device [{string.Join(", ", dev.Select(h => $"{h.Score:F4}"))}] != reference [{string.Join(", ", cpu.Select(h => $"{h.Score:F4}"))}]");
            }
        }
        Console.WriteLine($"[memory] WebGPU device search = reference in {cases} cases");
        var colour = await engine.SearchAsync("What colour does Aubs like?", 1, nowTicks: now);
        if (!colour[0].Entry.Text.Contains("purple")) throw new Exception($"the answer was '{colour[0].Entry.Text}', not the current fact");
        store.ForgetSubject("ben");
        var after = await engine.SearchAsync("castle with a moat, redstone doors", 10, new AiMemoryFilter { IncludeSuperseded = true }, nowTicks: now);
        if (after.Any(h => h.Entry.Subjects.Contains("ben"))) throw new Exception("a forgotten person's memory came back from search");
    }
}
