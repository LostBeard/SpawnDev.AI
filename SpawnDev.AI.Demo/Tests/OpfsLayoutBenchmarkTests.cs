using System.Diagnostics;
using SpawnDev.AsyncFileSystem;

namespace SpawnDev.AI.Demo.Tests;

/// <summary>
/// What it costs to store a model as 4 MB torrent pieces instead of as the one file it really is.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 THIS EXISTS TO INFORM A DESIGN DECISION WITH A NUMBER RATHER THAN AN OPINION. SpawnDev.WebTorrent
/// currently caches to OPFS as one file per PIECE - the demo's profile holds 6398 files, mostly exactly
/// 4,194,304 bytes - so a 6.87 GB model is ~1758 separate files. TJ's question is whether that costs
/// enough on every load to be worth changing to one file per torrent CONTENT file, which is what mature
/// torrent clients do (sparse writes into the destination file).
/// </para>
/// <para>
/// ⚠️ RANDOM ACCESS IS THE HALF THAT MATTERS MOST, and the reason this measures it separately. A GGUF
/// load is not one sequential pass: the parser reads a header, then seeks tensor by tensor. Under a
/// piece layout every seek can mean locating and opening a DIFFERENT file; under a single-file layout it
/// is a seek on a handle that is already open. Sequential throughput alone would understate the gap.
/// </para>
/// <para>
/// ⚠️ Heavy: it writes and reads a few hundred MB of OPFS. It also cleans up after itself, because
/// leaving benchmark data in the same store the models cache into would eat a user's quota.
/// </para>
/// </remarks>
public sealed class OpfsLayoutBenchmarkTests
{
    private readonly IAsyncFS _fs;

    /// <summary>New instance over the app's OPFS filesystem.</summary>
    public OpfsLayoutBenchmarkTests(IAsyncFS fs) => _fs = fs;

    private const string Dir = "layout-bench";
    private const int PieceSize = 4 * 1024 * 1024;   // what SpawnDev.WebTorrent actually uses
    private const int PieceCount = 64;               // 256 MB - representative without being slow
    private const long TotalBytes = (long)PieceSize * PieceCount;

    /// <summary>Piece-per-file versus one-file, sequentially and at random offsets.</summary>
    [AiTest(Heavy = true, Timeout = 1_200_000)]
    public async Task PieceFilesVersusOneFile()
    {
        var buffer = new byte[1024 * 1024];
        new Random(1234).NextBytes(buffer);

        try
        {
            if (!await _fs.DirectoryExists(Dir)) await _fs.CreateDirectory(Dir);

            // ── Lay the same bytes out both ways ────────────────────────────────────────────────────
            var writePieces = Stopwatch.StartNew();
            for (int i = 0; i < PieceCount; i++)
            {
                await using var w = await _fs.GetWriteStream($"{Dir}/piece-{i:D5}.bin");
                for (int written = 0; written < PieceSize; written += buffer.Length)
                    await w.WriteAsync(buffer, 0, Math.Min(buffer.Length, PieceSize - written));
            }
            writePieces.Stop();

            var writeOne = Stopwatch.StartNew();
            {
                await using var w = await _fs.GetWriteStream($"{Dir}/whole.bin");
                for (long written = 0; written < TotalBytes; written += buffer.Length)
                    await w.WriteAsync(buffer, 0, (int)Math.Min(buffer.Length, TotalBytes - written));
            }
            writeOne.Stop();

            Report("write, 64 piece files", writePieces.Elapsed);
            Report("write, one file      ", writeOne.Elapsed);

            // ── Sequential read: the whole thing, front to back ─────────────────────────────────────
            var readPieces = Stopwatch.StartNew();
            long got = 0;
            for (int i = 0; i < PieceCount; i++)
            {
                await using var r = await _fs.GetReadStream($"{Dir}/piece-{i:D5}.bin");
                int n;
                while ((n = await r.ReadAsync(buffer, 0, buffer.Length)) > 0) got += n;
            }
            readPieces.Stop();
            if (got != TotalBytes) throw new Exception($"piece read returned {got} of {TotalBytes} bytes");

            var readOne = Stopwatch.StartNew();
            got = 0;
            {
                await using var r = await _fs.GetReadStream($"{Dir}/whole.bin");
                int n;
                while ((n = await r.ReadAsync(buffer, 0, buffer.Length)) > 0) got += n;
            }
            readOne.Stop();
            if (got != TotalBytes) throw new Exception($"whole read returned {got} of {TotalBytes} bytes");

            Report("read sequential, 64 piece files", readPieces.Elapsed);
            Report("read sequential, one file      ", readOne.Elapsed);

            // ── Random access: what a GGUF load actually does ───────────────────────────────────────
            // 200 reads of 1 MB at random offsets. Under the piece layout each one may land in a
            // different file, so it pays an open; under one file it is a seek on an open handle.
            const int Hops = 200;
            var rng = new Random(99);
            var offsets = new long[Hops];
            for (int i = 0; i < Hops; i++)
                offsets[i] = (long)(rng.NextDouble() * (TotalBytes - buffer.Length));

            var randPieces = Stopwatch.StartNew();
            foreach (var off in offsets)
            {
                var piece = (int)(off / PieceSize);
                var within = off % PieceSize;
                await using var r = await _fs.GetReadStream($"{Dir}/piece-{piece:D5}.bin");
                r.Seek(within, SeekOrigin.Begin);
                await r.ReadAsync(buffer, 0, (int)Math.Min(buffer.Length, PieceSize - within));
            }
            randPieces.Stop();

            var randOne = Stopwatch.StartNew();
            {
                await using var r = await _fs.GetReadStream($"{Dir}/whole.bin");
                foreach (var off in offsets)
                {
                    r.Seek(off, SeekOrigin.Begin);
                    await r.ReadAsync(buffer, 0, buffer.Length);
                }
            }
            randOne.Stop();

            Console.WriteLine($"[layout] random {Hops}x1MB, piece files: {randPieces.ElapsedMilliseconds} ms "
                + $"({randPieces.Elapsed.TotalMilliseconds / Hops:F1} ms per hop)");
            Console.WriteLine($"[layout] random {Hops}x1MB, one file   : {randOne.ElapsedMilliseconds} ms "
                + $"({randOne.Elapsed.TotalMilliseconds / Hops:F1} ms per hop)");

            var seqRatio = readPieces.Elapsed.TotalMilliseconds / Math.Max(1, readOne.Elapsed.TotalMilliseconds);
            var randRatio = randPieces.Elapsed.TotalMilliseconds / Math.Max(1, randOne.Elapsed.TotalMilliseconds);
            Console.WriteLine($"[layout] VERDICT: pieces are {seqRatio:F2}x the sequential time and "
                + $"{randRatio:F2}x the random-access time of a single file");
            Console.WriteLine($"[layout] extrapolated to a 6.87 GB model (~1758 pieces), the sequential "
                + $"difference alone is {(readPieces.Elapsed - readOne.Elapsed).TotalSeconds * (6.87 * 1024 / 256):F0} s");

            // ⚠️ Deliberately NOT asserted as a threshold. This is a measurement to inform a decision,
            // and a number that varies with disk and browser has no business failing a build. It fails
            // only if the measurement itself did not happen.
            if (readOne.Elapsed.TotalMilliseconds <= 0 || readPieces.Elapsed.TotalMilliseconds <= 0)
                throw new Exception("a read completed in no measurable time; the benchmark did not run");
        }
        finally
        {
            // Leaving a few hundred MB behind would eat the quota the models cache into.
            try { if (await _fs.DirectoryExists(Dir)) await _fs.Remove(Dir, recursive: true); }
            catch (Exception ex) { Console.WriteLine($"[layout] cleanup failed: {ex.Message}"); }
        }
    }

    private static void Report(string what, TimeSpan took)
        => Console.WriteLine($"[layout] {what}: {took.TotalMilliseconds:F0} ms "
            + $"({TotalBytes / 1048576.0 / Math.Max(0.001, took.TotalSeconds):F0} MB/s)");
}
