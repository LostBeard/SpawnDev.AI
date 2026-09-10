using System.Text.Json;
using SpawnDev.AI.Server;

namespace SpawnDev.AI.Demo.Tests;

/// <summary>
/// What it costs to store a model as 4 MB torrent pieces instead of as the file it really is.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 THIS EXISTS TO SETTLE A DESIGN DECISION WITH NUMBERS. SpawnDev.WebTorrent caches to OPFS as one
/// file per PIECE, so a 2375 MB model is 681 files. Instrumenting a real qwen3:4b load measured the reads
/// themselves at 1051 ms across 1337 reads - about 2.2 GB/s - while OPENING those 681 files cost
/// 121,571 ms, of which 117,192 ms was <c>getFileHandle</c>. That is ~99% of the load, and the question is
/// whether writing the torrent's files as actual files removes it.
/// </para>
/// <para>
/// ⚠️ IT DELEGATES TO THE WORKER, and that is the whole point of the rewrite. The previous version of this
/// test ran in the window over <c>GetReadStream</c>, which reads the ENTIRE file into memory and is not a
/// path production takes; and <c>createSyncAccessHandle()</c> - the API the production read path actually
/// uses - throws outside a worker, so a window-scope benchmark could never have measured the cost being
/// investigated. <see cref="IAiWorkerApi.BenchmarkOpfsLayoutAsync"/> runs
/// <c>SpawnDev.WebTorrent.Storage.OpfsLayoutProbe</c> in the worker that hosts the model loader.
/// </para>
/// <para>
/// ⚠️ THESE ARE WARM NUMBERS. The probe writes each layout and reads it back in the same pass, so the
/// entries are as hot as OPFS ever makes them, while the 172 ms figure above is a COLD load after a page
/// reload. A warm run therefore UNDERSTATES the piece layout's cost - which makes a warm result that
/// still shows a large gap conclusive, and a warm result showing no gap inconclusive rather than
/// exonerating. The assertion below reflects that: it fails only if the measurement did not happen.
/// </para>
/// <para>
/// ⚠️ Heavy: writes and reads a few hundred MB of OPFS. The probe removes its own directory afterwards,
/// because leaving that in the store models cache into would eat the user's quota.
/// </para>
/// </remarks>
public sealed class OpfsLayoutBenchmarkTests
{
    private readonly AiWorkerClient _ai;

    /// <summary>New instance over the window-side worker client.</summary>
    public OpfsLayoutBenchmarkTests(AiWorkerClient ai) => _ai = ai;

    /// <summary>Piece-per-file versus one file, swept over entry count, lock pressure and entry size.</summary>
    [AiTest(Heavy = true, Timeout = 1_800_000)]
    public async Task PieceFilesVersusOneFile()
    {
        var json = await _ai.BenchmarkOpfsLayoutAsync(null);
        var results = JsonSerializer.Deserialize<List<LayoutRow>>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new Exception($"the probe returned unparseable JSON: {json}");
        if (results.Count == 0) throw new Exception("the probe returned no measurements");

        foreach (var r in results)
        {
            // 🔴 A ZERO HERE MEANS THE PASS DID NOT RUN. Without this the suite would go green on a probe
            // that silently measured nothing - the exact shape of a test that cannot fail.
            if (r.PieceTotalMs <= 0 || r.FileTotalMs <= 0)
                throw new Exception($"{r.EntryCount}x{r.EntryBytes}: a pass completed in no measurable time "
                    + $"(piece {r.PieceTotalMs} ms, file {r.FileTotalMs} ms) - the benchmark did not run");
            if (r.PieceResolveMs <= 0)
                throw new Exception($"{r.EntryCount}x{r.EntryBytes}: getFileHandle took no measurable time "
                    + "across every entry, which cannot be true - the probe measured the wrong thing");

            Console.WriteLine($"[layout] {r.EntryCount,5} x {r.EntryBytes,9} B, {r.HeldHandles} held: "
                + $"pieces {r.PieceTotalMs,8:F0} ms (resolve {r.PieceResolveMs,8:F0}, "
                + $"{(r.EntryCount > 0 ? r.PieceResolveMs / r.EntryCount : 0),6:F2} ms/open) | "
                + $"one file {r.FileTotalMs,7:F0} ms | ratio {r.PieceTotalMs / r.FileTotalMs,6:F2}x");
        }

        // The two rows that answer "is it the directory size?" - same entry size, 128 vs 1362 entries.
        var small = results.FirstOrDefault(r => r is { EntryCount: 128, EntryBytes: 65_536 });
        var large = results.FirstOrDefault(r => r is { EntryCount: 1362, EntryBytes: 65_536 });
        if (small != null && large != null)
        {
            var perOpenSmall = small.PieceResolveMs / small.EntryCount;
            var perOpenLarge = large.PieceResolveMs / large.EntryCount;
            Console.WriteLine($"[layout] getFileHandle per open: {perOpenSmall:F3} ms at 128 entries -> "
                + $"{perOpenLarge:F3} ms at 1362 entries ({(perOpenSmall > 0 ? perOpenLarge / perOpenSmall : 0):F1}x "
                + "for 10.6x the entries)");
        }

        // The row pair that answers "is it the exclusive locks?" - 681 entries, 8 held vs 0 held.
        var held8 = results.FirstOrDefault(r => r is { EntryCount: 681, HeldHandles: 8 });
        var held0 = results.FirstOrDefault(r => r is { EntryCount: 681, HeldHandles: 0 });
        if (held8 != null && held0 != null)
            Console.WriteLine($"[layout] 681 entries, getFileHandle total: {held8.PieceResolveMs:F0} ms with 8 "
                + $"sync locks held vs {held0.PieceResolveMs:F0} ms with none "
                + $"({(held0.PieceResolveMs > 0 ? held8.PieceResolveMs / held0.PieceResolveMs : 0):F2}x)");
    }

    /// <summary>Mirror of <c>OpfsLayoutProbe.LayoutMeasurement</c> for the window side.</summary>
    private sealed class LayoutRow
    {
        public int EntryCount { get; set; }
        public int EntryBytes { get; set; }
        public int HeldHandles { get; set; }
        public double PieceResolveMs { get; set; }
        public double PieceCreateMs { get; set; }
        public double PieceReadMs { get; set; }
        public double PieceCloseMs { get; set; }
        public double FileResolveMs { get; set; }
        public double FileCreateMs { get; set; }
        public double FileReadMs { get; set; }
        public double FileCloseMs { get; set; }
        public double PieceTotalMs => PieceResolveMs + PieceCreateMs + PieceReadMs + PieceCloseMs;
        public double FileTotalMs => FileResolveMs + FileCreateMs + FileReadMs + FileCloseMs;
    }
}
