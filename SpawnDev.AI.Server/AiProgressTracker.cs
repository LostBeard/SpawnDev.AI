using SpawnDev.ILGPU.ML.Hub;

namespace SpawnDev.AI.Server;

/// <summary>
/// The one place that knows what the server is busy with, so a client can be told.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 THE GAP THIS CLOSES. Everything needed to answer "what is happening and how much longer" already
/// existed and none of it left the worker. <c>HttpModelDownloader.ActiveDownloads</c> carries bytes,
/// total and throughput for every download in flight; every engine has an <c>OnLoadProgress</c> hook
/// reporting stage and percent. Both ended at a <c>Console.WriteLine</c> - and a shared worker's console
/// does not even reach the page. From the UI a cold first message was a minute of silence.
/// </para>
/// <para>
/// ⚠️ DOWNLOADS ARE READ LIVE, NEVER MIRRORED. The download state is asked of the source at snapshot time
/// rather than copied here on an event, because a mirror goes stale exactly when it matters - a download
/// that stalls stops firing events, and a cached "84%, 40 MB/s" would then be a lie that looks healthy.
/// Reading through means a stalled download reports its true frozen byte count.
/// </para>
/// <para>
/// ⚠️ A download in flight OUTRANKS a load stage. They overlap: the chat provider reports stage "fetch"
/// and the bytes move underneath it, and later the same load reports "upload" with nothing downloading.
/// Bytes are the more specific fact, so whenever any download is running that is what is reported.
/// </para>
/// </remarks>
public sealed class AiProgressTracker
{
    private readonly IModelSource _source;
    private readonly object _sync = new();
    private string _model = "", _stage = "";
    private int _percent = -1;
    private long _startedAt;

    /// <param name="source">
    /// Model delivery. A source that also implements <see cref="ICachingModelSource"/> can report its
    /// downloads; one that cannot (a plain ranged reader, a local-file source) simply contributes no
    /// download rows, and load stages still work.
    /// </param>
    public AiProgressTracker(IModelSource source) => _source = source;

    /// <summary>
    /// Record a load stage for <paramref name="model"/> - wired to every engine's <c>OnLoadProgress</c>.
    /// </summary>
    /// <remarks>
    /// The clock restarts when a DIFFERENT thing starts loading, not on every stage, so
    /// <see cref="AiProgress.ElapsedSeconds"/> measures the whole load rather than the current stage.
    /// </remarks>
    public void ReportStage(string model, string stage, int percent)
    {
        // The end-of-load marker every provider emits from its finally. Handled here rather than at each
        // call site so no subscriber can forget it and leave a dead stage on screen.
        if (stage is "idle" or "") { ReportIdle(); return; }
        lock (_sync)
        {
            if (!string.Equals(_model, model, StringComparison.Ordinal) || _startedAt == 0)
                _startedAt = Environment.TickCount64;
            _model = model;
            _stage = stage;
            _percent = percent;
        }
    }

    /// <summary>
    /// Loading finished (or failed) - stop reporting a stage that is no longer running.
    /// </summary>
    /// <remarks>
    /// 🔴 MUST be called from a <c>finally</c>. A load that throws half way through "upload" would
    /// otherwise leave the tracker reporting "uploading weights 43%" forever, and a UI that trusts it
    /// shows a frozen bar over a failed request - strictly worse than the silence this replaces.
    /// </remarks>
    public void ReportIdle()
    {
        lock (_sync) { _model = ""; _stage = ""; _percent = -1; _startedAt = 0; }
    }

    /// <summary>What the server is doing right now.</summary>
    public AiProgress Snapshot()
    {
        string model, stage;
        int percent;
        long startedAt;
        lock (_sync) { model = _model; stage = _stage; percent = _percent; startedAt = _startedAt; }

        var elapsed = startedAt == 0 ? 0 : (Environment.TickCount64 - startedAt) / 1000.0;

        // Bytes first: see the class remarks. The largest download wins when several run at once - it is
        // the one whose finish the user is actually waiting on.
        if (_source is ICachingModelSource caching)
        {
            ActiveModelDownload? biggest = null;
            foreach (var d in caching.ActiveDownloads)
                if (biggest is not { } b || d.TotalBytes > b.TotalBytes) biggest = d;

            if (biggest is { } dl)
                return new AiProgress
                {
                    Phase = "download",
                    // Prefer the model NAME the loader gave us over the store key, which is a flattened
                    // path (hf_repo_main_model.gguf) and reads as machine output.
                    Model = model.Length > 0 ? model : dl.Key,
                    BytesReceived = dl.BytesReceived,
                    TotalBytes = dl.TotalBytes,
                    BytesPerSecond = dl.BytesPerSecond,
                    Resumed = dl.Resumed,
                    Percent = dl.Fraction is { } f ? (int)Math.Round(f * 100) : -1,
                    ElapsedSeconds = elapsed,
                };
        }

        if (model.Length == 0) return new AiProgress();

        return new AiProgress
        {
            Phase = "load",
            Model = model,
            Stage = stage,
            Percent = percent,
            ElapsedSeconds = elapsed,
        };
    }
}
