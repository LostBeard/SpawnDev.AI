using System.Text.Json;
using SpawnDev.AI.Server;

namespace SpawnDev.AI.Demo.Tests;

/// <summary>
/// Does a concurrent OPFS writer slow ranged reads on an already-open handle?
/// </summary>
/// <remarks>
/// <para>
/// 🔴 THE LAST UNTESTED CANDIDATE FOR A REAL OBSERVATION. A qwen3:4b load measured 172 ms per file open
/// while its torrent was still downloading, against 0.39-0.46 ms for the same code path idle. Directory
/// size, held exclusive locks and entry size were each measured and cleared. Concurrent writing was what
/// remained, it had never been tested, and it was therefore written down as a GUESS rather than a cause -
/// which is the state this test exists to end.
/// </para>
/// <para>
/// ⚠️ IT ASKS THE VERSION OF THE QUESTION THAT STILL MATTERS. Under the content-file layout a model load
/// performs ONE file open and then hundreds of ranged reads on that open handle, so inflated OPEN cost is
/// no longer interesting. Inflated READ cost is, because the demo loads a model while its remaining pieces
/// are still arriving - and the shape measured here (310 reads of 2 MB) is the shape a real
/// qwen2.5:0.5b load actually performs.
/// </para>
/// <para>
/// ⚠️ NO THRESHOLD IS ASSERTED, on purpose. A contention ratio varies with the disk, the browser and
/// whatever else the machine is doing, so failing a build on it would make the suite flaky without making
/// the code better. This fails only when the measurement itself did not happen - the number is for the
/// reader. Rule 5 is satisfied by that assertion being real: a probe that returned zeros would fail.
/// </para>
/// <para>
/// ⚠️ Heavy: writes and reads ~1.2 GB of OPFS across its passes, and removes its own directory afterwards.
/// </para>
/// </remarks>
public sealed class OpfsContentionTests
{
    private readonly AiWorkerClient _ai;

    /// <summary>New instance over the window-side worker client.</summary>
    public OpfsContentionTests(AiWorkerClient ai) => _ai = ai;

    /// <summary>The same ranged reads, idle and while a writer hammers a different file.</summary>
    [AiTest(Heavy = true, Timeout = 1_800_000)]
    public async Task RangedReadsUnderConcurrentWrites()
    {
        var json = await _ai.BenchmarkOpfsContentionAsync();
        var m = JsonSerializer.Deserialize<ContentionRow>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new Exception($"the probe returned unparseable JSON: {json}");

        // 🔴 A ZERO MEANS THE PASS DID NOT RUN, which is the one way this can legitimately fail.
        if (m.Reads <= 0)
            throw new Exception("the probe reported no reads, so nothing was measured");
        if (m.IdleReadMs <= 0)
            throw new Exception($"{m.Reads} reads completed in no measurable time idle - the probe did not run");
        if (m.LoadedReadMs <= 0)
            throw new Exception($"{m.Reads} reads completed in no measurable time under load - the loaded "
                + "pass did not run");

        // 🔴 THE FIXTURE MUST BE CAPABLE OF SHOWING CONTENTION. If the background writer never completed a
        // single write, the "under load" pass had no load in it and a ratio near 1.0 would mean nothing -
        // the exact shape of a test that cannot fail.
        if (m.WritesCompleted <= 0)
            throw new Exception("FIXTURE TOO WEAK: the background writer completed ZERO writes, so the "
                + "'under load' pass ran with no load and its timing proves nothing about contention");

        var mb = m.Reads * (double)m.ReadBytes / 1048576.0;
        Console.WriteLine($"[contend] {m.Reads} x {m.ReadBytes / 1024} KiB ({mb:F0} MB), sync "
            + (m.SyncAvailable ? "available" : "UNAVAILABLE (Blob path)"));
        Console.WriteLine($"[contend]   idle       : {m.IdleReadMs,8:F0} ms "
            + $"({mb / Math.Max(0.001, m.IdleReadMs / 1000.0):F0} MB/s)");
        Console.WriteLine($"[contend]   under load : {m.LoadedReadMs,8:F0} ms "
            + $"({mb / Math.Max(0.001, m.LoadedReadMs / 1000.0):F0} MB/s), "
            + $"{m.WritesCompleted} write(s) completed alongside");
        Console.WriteLine($"[contend]   RATIO      : {m.Ratio:F2}x "
            + (m.Ratio >= 2.0
                ? "- concurrent writing DOES inflate reads; loading a model while pieces are still "
                  + "arriving pays for it"
                : "- concurrent writing does NOT meaningfully inflate reads on an open handle"));
    }

    /// <summary>Mirror of <c>OpfsLayoutProbe.ContentionMeasurement</c> for the window side.</summary>
    private sealed class ContentionRow
    {
        public int Reads { get; set; }
        public int ReadBytes { get; set; }
        public double IdleReadMs { get; set; }
        public double LoadedReadMs { get; set; }
        public long WritesCompleted { get; set; }
        public bool SyncAvailable { get; set; }
        public double Ratio => IdleReadMs > 0 ? LoadedReadMs / IdleReadMs : 0;
    }
}
