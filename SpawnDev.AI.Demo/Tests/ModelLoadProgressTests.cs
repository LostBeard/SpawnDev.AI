using SpawnDev.AI.Server;
using SpawnDev.ILGPU.ML.Hub;

namespace SpawnDev.AI.Demo.Tests;

/// <summary>
/// The page can find out what the server is doing, while it is doing it.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 THE REPORT. Captain, on the deployed demo: "there needs to be model download and loading progress
/// indicators. it takes it roughly 1 minute to respond to the first message and the user has no idea what
/// is going on or how long it will take."
/// </para>
/// <para>
/// ⚠️ THE THING THAT CAN SILENTLY BREAK is not the arithmetic - it is whether a progress call is ANSWERED
/// while a load is in flight. The load and the poll share one worker thread. If any part of the load
/// stopped yielding (a synchronous read, a tight upload loop), the poll would simply not be serviced until
/// the load finished, and the bar would sit frozen for the whole minute and then flash to 100% - the exact
/// symptom the feature exists to remove, with no error anywhere. That is what the heavy test measures: not
/// "progress exists" but "progress arrived DURING, more than once, with the numbers moving".
/// </para>
/// </remarks>
public sealed class ModelLoadProgressTests
{
    private readonly AiWorkerClient _client;
    private readonly HubModelSource _source;

    /// <param name="client">The window-side client the UI itself uses.</param>
    /// <param name="source">The app's real model source - what the tracker reads downloads from.</param>
    public ModelLoadProgressTests(AiWorkerClient client, HubModelSource source)
    {
        _client = client;
        _source = source;
    }

    /// <summary>The two smallest models in the demo catalogue - this test swaps between them.</summary>
    /// <remarks>
    /// ⚠️ TWO, and that is what makes the test deterministic. One model would report nothing on a second
    /// run, because it is already resident and warming it returns immediately - the test would then pass
    /// or fail on cache state rather than on the feature. Asking for a DIFFERENT model than the resident
    /// one forces <c>ModelRegistry</c> to evict and load, every run, cached bytes or not.
    /// </remarks>
    private const string ModelA = "smollm2:360m-instruct-q8_0";
    private const string ModelB = "qwen3:0.6b-q8_0";

    /// <summary>
    /// The tracker reports a stage while one is running and goes quiet the moment it stops.
    /// </summary>
    /// <remarks>
    /// 🔴 THE HALF THAT FAILS SILENTLY IS THE END MARKER. Every engine reports stages as it loads; NONE of
    /// them reported when a load stopped, so a load that threw in "upload" would leave the tracker saying
    /// "uploading weights 43%" for the rest of the session. A UI trusting that shows a frozen bar over a
    /// failed request, which is worse than the silence this feature replaced - and it is invisible on the
    /// happy path, where every load ends in "ready".
    /// <para>
    /// Not heavy: it exercises the real tracker over the app's real model source, and loads nothing.
    /// </para>
    /// </remarks>
    [AiTest(Timeout = 30_000)]
    public Task AStageIsReportedWhileItRunsAndClearedWhenItStops()
    {
        var tracker = new AiProgressTracker(_source);

        if (!tracker.Snapshot().Idle)
            throw new Exception("a fresh tracker reported work in progress - a page would show a bar for "
                + "a load that never started");

        tracker.ReportStage("test:model", "upload", 43);
        var mid = tracker.Snapshot();
        if (mid.Idle) throw new Exception("a running stage reported as idle");
        if (mid.Phase != "load") throw new Exception($"phase came back '{mid.Phase}', expected 'load'");
        if (mid.Stage != "upload" || mid.Percent != 43)
            throw new Exception($"stage/percent came back '{mid.Stage}' {mid.Percent}, expected upload 43");
        if (mid.Model != "test:model")
            throw new Exception($"the model name was lost (got '{mid.Model}') - with several kinds sharing "
                + "one GPU, 'uploading weights 43%' of WHAT is the question a user has");
        if (mid.Describe().Length == 0)
            throw new Exception("Describe() produced nothing - that string IS the caption the page shows");

        // The end marker every engine now emits from its finally.
        tracker.ReportStage("test:model", "idle", 100);
        if (!tracker.Snapshot().Idle)
            throw new Exception("the tracker kept reporting a stage after the load ended - a UI would show "
                + "a frozen progress bar indefinitely");

        // And the explicit form, which is what a caller with no stage to report uses.
        tracker.ReportStage("test:model", "compile", 10);
        tracker.ReportIdle();
        if (!tracker.Snapshot().Idle) throw new Exception("ReportIdle did not clear the tracker");
        return Task.CompletedTask;
    }

    /// <summary>
    /// A download that has not started reports nothing, and an ETA is never invented.
    /// </summary>
    /// <remarks>
    /// ⚠️ <c>SecondsRemaining</c> is the number a user will plan around, so it must be absent rather than
    /// wrong. A GPU upload has no rate to extrapolate from and a download with no Content-Length has no
    /// total; both must return null instead of a confident figure.
    /// </remarks>
    [AiTest(Timeout = 30_000)]
    public Task AnEtaIsOnlyGivenWhenItCanBeKnown()
    {
        var loading = new AiProgress { Phase = "load", Stage = "upload", Percent = 60, Model = "m" };
        if (loading.SecondsRemaining != null)
            throw new Exception("an ETA was produced for a GPU upload, which has no rate to project from");

        var noTotal = new AiProgress
        {
            Phase = "download", Model = "m", BytesReceived = 1_000_000, TotalBytes = -1,
            BytesPerSecond = 5_000_000, Percent = -1,
        };
        if (noTotal.SecondsRemaining != null)
            throw new Exception("an ETA was produced for a download with no known total");
        if (!noTotal.Describe().Contains("Downloading"))
            throw new Exception("a download with no total must still say it is downloading");

        var known = new AiProgress
        {
            Phase = "download", Model = "Qwen3-1.7B-Q8_0.gguf", BytesReceived = 500_000_000,
            TotalBytes = 1_000_000_000, BytesPerSecond = 50_000_000, Percent = 50,
        };
        if (known.SecondsRemaining is not { } eta)
            throw new Exception("no ETA from a download with a total and a rate - the one case it is known");
        if (Math.Abs(eta - 10) > 0.5)
            throw new Exception($"500 MB left at 50 MB/s should be ~10s, got {eta:F1}s");
        var line = known.Describe();
        if (!line.Contains("50%") || !line.Contains("10s"))
            throw new Exception($"the caption lost its percent or its ETA: '{line}'");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Progress arrives DURING a real model load, repeatedly, with the numbers advancing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 THE PROPERTY UNDER TEST IS CONCURRENCY, not arithmetic. The poll and the load share one worker
    /// thread; the poll is only answered because the download and the weight upload both <c>await</c>
    /// between chunks. A change that made either synchronous would freeze the bar for the entire load and
    /// break nothing else - no exception, no failing assertion anywhere else in this suite.
    /// </para>
    /// <para>
    /// ⚠️ IT SWAPS MODELS rather than loading one. See <see cref="ModelA"/>: warming the resident model is
    /// a no-op, so a single-model version of this test would go green on a cold run and stop testing
    /// anything on every run after.
    /// </para>
    /// <para>
    /// ⚠️ The assertion is on DISTINCT snapshots, not on the count of polls. A worker that answered every
    /// poll from one frozen value would satisfy "we got 40 replies" perfectly.
    /// </para>
    /// </remarks>
    [AiTest(Heavy = true, Timeout = 900_000)]
    public async Task ProgressArrivesWhileAModelIsActuallyLoading()
    {
        await _client.InitAsync();

        // Make ModelA resident first, so asking for ModelB is guaranteed to be a real load.
        var (warmedA, failedA) = await _client.WarmAsync(new[] { "chat" }, ModelA);
        if (failedA.Length > 0)
            throw new Exception($"could not make the fixture model resident: {failedA[0].Error}");
        if (warmedA.Length == 0)
            throw new Exception("warming reported nothing warmed - the fixture cannot demonstrate a swap");

        var settled = await _client.GetProgressAsync();
        if (settled == null)
            throw new Exception("the server does not answer GET /ai/progress at all - the page has no way "
                + "to report what it is doing");
        if (!settled.Idle)
            throw new Exception($"the tracker still reports '{settled.Phase}/{settled.Stage}' after a load "
                + "finished - a UI would show a bar that never clears");

        // Now the swap, with a poller watching it.
        var seen = new List<AiProgress>();
        using var polling = new CancellationTokenSource();
        var poller = Task.Run(async () =>
        {
            while (!polling.IsCancellationRequested)
            {
                var p = await _client.GetProgressAsync(polling.Token);
                if (p is { Idle: false }) seen.Add(p);
                try { await Task.Delay(200, polling.Token); } catch (OperationCanceledException) { break; }
            }
        });

        var swapClock = System.Diagnostics.Stopwatch.StartNew();
        var (warmedB, failedB) = await _client.WarmAsync(new[] { "chat" }, ModelB);
        swapClock.Stop();
        polling.Cancel();
        try { await poller; } catch (OperationCanceledException) { }

        if (failedB.Length > 0) throw new Exception($"the swap load failed: {failedB[0].Error}");

        // One real payload, printed the first time this goes green (Rule 5).
        Console.WriteLine($"[progress] swap to {ModelB} took {swapClock.Elapsed.TotalSeconds:F1}s, "
                        + $"{seen.Count} non-idle snapshots");
        foreach (var s in Sample(seen, 6))
            Console.WriteLine($"[progress]   {s.ElapsedSeconds,6:F1}s {s.Phase,-8} {s.Describe()}");

        if (seen.Count == 0)
            throw new Exception($"the load took {swapClock.Elapsed.TotalSeconds:F1}s and NOT ONE progress "
                + "snapshot arrived while it ran. Either the tracker is not wired to the engines, or the "
                + "load stopped yielding and the worker could not answer until it was over - which is the "
                + "frozen bar this feature exists to prevent");
        if (seen.Count < 3)
            throw new Exception($"only {seen.Count} snapshot(s) arrived during a "
                + $"{swapClock.Elapsed.TotalSeconds:F1}s load - a bar that updates once or twice reads as "
                + "stuck, not as progress");

        foreach (var s in seen)
        {
            if (s.Phase is not ("download" or "load"))
                throw new Exception($"a non-idle snapshot reported phase '{s.Phase}'");
            if (s.Model.Length == 0)
                throw new Exception("a snapshot did not say WHAT was loading");
            if (s.Describe().Length == 0)
                throw new Exception($"a {s.Phase} snapshot produced an empty caption");
        }

        // MOVEMENT. A worker answering every poll from one stale value would pass everything above.
        var distinct = seen.Select(s => $"{s.Phase}|{s.Stage}|{s.Percent}|{s.BytesReceived}")
                           .Distinct().Count();
        if (distinct < 3)
            throw new Exception($"{seen.Count} snapshots carried only {distinct} distinct value(s) - the "
                + "numbers are not moving, so the page would show a frozen bar that happens to be answered");

        // If any of it was a download, the byte count must only ever go up.
        var downloads = seen.Where(s => s.Phase == "download").ToList();
        for (int i = 1; i < downloads.Count; i++)
            if (downloads[i].BytesReceived < downloads[i - 1].BytesReceived)
                throw new Exception($"downloaded bytes went backwards: {downloads[i - 1].BytesReceived} -> "
                    + $"{downloads[i].BytesReceived}");

        var after = await _client.GetProgressAsync();
        if (after is not { Idle: true })
            throw new Exception($"after the load finished the tracker still reports "
                + $"'{after?.Phase}/{after?.Stage}' - the bar would never clear");
    }

    /// <summary>Evenly spaced examples, so a long load prints a readable trace instead of hundreds of lines.</summary>
    private static IEnumerable<AiProgress> Sample(List<AiProgress> all, int count)
    {
        if (all.Count <= count) return all;
        var step = (double)all.Count / count;
        return Enumerable.Range(0, count).Select(i => all[(int)(i * step)]);
    }
}
