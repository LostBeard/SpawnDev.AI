namespace SpawnDev.AI.Demo.Pages;

/// <summary>
/// Saying what the server is doing, while it does it.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 THE REPORT. Captain, on the deployed demo: "there needs to be model download and loading progress
/// indicators. it takes it roughly 1 minute to respond to the first message and the user has no idea what
/// is going on or how long it will take."
/// </para>
/// <para>
/// That minute is two costs the page never distinguished: a 1.71 GB download (MEASURED 44.4 s for 1.83 GB
/// on the LAN) and a GPU weight upload. All the page had was a caption counting seconds upward, which says
/// a wait is happening and nothing about which wait, how far along, or how much longer. The numbers
/// existed the whole time - <c>HttpModelDownloader</c> tracks bytes and throughput, <c>InferenceSession</c>
/// reports a stage and percent - and both ended at a <c>Console.WriteLine</c> inside a SHARED worker,
/// whose console does not reach the page at all.
/// </para>
/// <para>
/// ⚠️ THE FALLBACK IS KEPT, DELIBERATELY. If the server cannot answer - an older worker, a call that
/// raced a teardown - the caption reverts to the moving seconds counter rather than going blank. A number
/// that moves is the whole difference between "slow" and "broken", and a progress indicator must never be
/// the thing that makes a working turn look dead.
/// </para>
/// </remarks>
public partial class Home
{
    /// <summary>
    /// How often the page asks the worker what it is doing.
    /// </summary>
    /// <remarks>
    /// The download reports every 100 ms server-side and the GPU upload ticks per percent, so a slower poll
    /// than this would visibly stutter on a fast LAN pull; a faster one would just re-ask for numbers that
    /// have not changed. Each poll is one worker round trip against a loop that is already awaiting I/O.
    /// </remarks>
    const int ProgressPollMs = 400;

    /// <summary>
    /// How long an unanswered poll may stand before its last snapshot stops being shown as live.
    /// </summary>
    /// <remarks>
    /// 🔴 THE DEFECT THIS EXISTS FOR, MEASURED on a cold turn with <c>?worker=dedicated</c>: the caption
    /// froze on "Downloading qwen3:1.7b-q8_0 - 1.7 GB of 1.71 GB (99%)" for 16.6 seconds. In a DEDICATED
    /// worker <c>OPFSStream</c> takes <c>createSyncAccessHandle</c> - the fast synchronous read path - and
    /// ILGPU's shader compilation is synchronous too, so the worker's message loop does not run and
    /// <c>GET /ai/progress</c> is not answered until the load is over. The ticker awaited that call, so
    /// the whole caption stalled with it.
    /// <para>
    /// A frozen "99%" is the single worst thing this feature could show: it is the exact appearance of a
    /// stuck download. Past this threshold the page stops claiming the stale numbers are current and says
    /// what it does know, with a clock that moves.
    /// </para>
    /// </remarks>
    const int ProgressStaleMs = 1500;

    /// <summary>The server's last progress snapshot while something slow is running, else null.</summary>
    AiProgress? _progressInfo;

    /// <summary>
    /// True while the page is waiting on the server with no numbers to draw - show an animated bar.
    /// </summary>
    /// <remarks>
    /// 🔴 CAPTAIN, ON THE REFRESHED PAGE: "it didn't show a progress bar on the refreshed page where it
    /// loaded from cache." He is right, and the reason is <see cref="ProgressStaleMs"/>: a DEDICATED worker
    /// loading from OPFS blocks its own message loop, so every poll goes unanswered and there is no
    /// snapshot to render a filled bar from. Falling back to a bare line of text there was wrong - "no
    /// numbers" is not "no progress", and the second visit is exactly where a user has been told the model
    /// is cached and is entitled to see something happening.
    /// <para>
    /// So the bar is always present while a turn is waiting: filled and labelled when the server can
    /// report, animated and indeterminate when it cannot. It never disappears and then comes back, which
    /// would read as the page giving up.
    /// </para>
    /// </remarks>
    bool _progressPending;

    /// <summary>
    /// Keep <see cref="_busyNote"/> and <see cref="_progressInfo"/> current until <paramref name="ct"/> is
    /// cancelled or <paramref name="stillWaiting"/> goes false.
    /// </summary>
    /// <param name="startedAt">When the wait began - the fallback caption counts from here.</param>
    /// <param name="stillWaiting">
    /// False once the wait is over from the PAGE's point of view. For a chat turn that is the first token:
    /// once text is streaming, the text is itself the progress indicator and a caption under it is noise.
    /// </param>
    /// <param name="what">Fallback caption verb, used only when the server reports nothing.</param>
    /// <param name="ct">Cancelled by the caller when its work finishes.</param>
    async Task TrackProgressAsync(DateTime startedAt, Func<bool> stillWaiting, string what,
        CancellationToken ct)
    {
        // 🔴 MEASURED on a cold first turn: the weight upload finished at 60.0 s and the first token
        // arrived at 78.5 s. Those 18.5 s are prompt tokenization, prefill and the WebGPU decode-capture
        // warmup - real work with no percent to report, and with the load tracker already idle. Before
        // this flag the caption fell back to "waiting for the first token", which is true and says nothing
        // about the fact that the slow part is over. Knowing the model is READY changes what a user does
        // with the wait.
        var sawServerWork = false;
        // The last snapshot the server actually returned, including an Idle one. "The load finished" and
        // "the worker has not answered" are different facts and the caption must not confuse them.
        AiProgress? lastAnswer = null;
        // ⚠️ ONE outstanding poll, never a queue. A blocked worker answers none of them and they would all
        // land at once when it unblocks. Issuing the next only after the previous returns also means the
        // poll rate self-limits to whatever the worker can actually serve.
        Task<AiProgress?>? poll = null;
        var pollSentAt = DateTime.UtcNow;
        // The bar goes up immediately and stays up for the whole wait - see _progressPending.
        _progressPending = true;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(ProgressPollMs, ct);
                if (ct.IsCancellationRequested || !stillWaiting()) break;

                if (poll is { IsCompleted: true })
                {
                    // ⚠️ Never let the progress call itself fail the turn: GetProgressAsync already
                    // swallows and returns null, and this is the second belt for anything it cannot catch.
                    try { lastAnswer = await poll; }
                    catch (OperationCanceledException) { break; }
                    catch (Exception ex) { Console.WriteLine($"[progress] {ex.Message}"); lastAnswer = null; }
                    poll = null;
                }
                if (poll == null)
                {
                    poll = Ai.GetProgressAsync(ct);
                    pollSentAt = DateTime.UtcNow;
                }

                var stale = (DateTime.UtcNow - pollSentAt).TotalMilliseconds > ProgressStaleMs;
                var live = !stale && lastAnswer is { Idle: false } ? lastAnswer : null;
                _progressInfo = live;
                if (lastAnswer is { Idle: false }) sawServerWork = true;

                var secs = (DateTime.UtcNow - startedAt).TotalSeconds;
                _busyNote = live?.Describe() ?? StalledCaption(lastAnswer, stale, sawServerWork, what, secs);
                await InvokeAsync(StateHasChanged);
            }
        }
        catch (OperationCanceledException) { /* expected: the work finished */ }
        catch (Exception ex) { Console.WriteLine($"[progress] ticker stopped: {ex.Message}"); }
        finally
        {
            _progressInfo = null;
            _progressPending = false;
        }
    }

    /// <summary>
    /// What to say when there is no live snapshot: the most specific true thing, plus a moving clock.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two different silences, and they mean opposite things. An ANSWERED poll that came back idle means
    /// the load genuinely finished and the remaining wait is prefill and the decode-capture warmup. An
    /// UNANSWERED poll means the worker is too busy to reply - which in a dedicated worker is precisely
    /// when it is loading hardest (see <see cref="ProgressStaleMs"/>).
    /// </para>
    /// <para>
    /// ⚠️ The stalled wording still names the phase, because the last answer says which phase it was in
    /// and that fact does not expire - a download that reached 99% is not going to turn back into a
    /// download. What expires is the right to present the NUMBERS as current.
    /// </para>
    /// </remarks>
    static string StalledCaption(AiProgress? last, bool stale, bool sawServerWork, string what, double secs)
    {
        if (stale && last is { Idle: false })
        {
            var name = last.Model.Length > 0 ? last.Model : "the model";
            // A download that reached the end IS the load that follows it - say the next thing, not the
            // last one, or the caption sits on "99%" through the entire load.
            if (last.Phase == "download" && last.Percent >= 99)
                return $"Downloaded - loading {name} onto the GPU… {secs:F0}s";
            if (last.Phase == "load")
                return $"Loading {name} onto the GPU - {AiProgress.StageLabel(last.Stage)}… {secs:F0}s";
            return $"Downloading {name}… {secs:F0}s (the worker is busy; numbers will catch up)";
        }
        if (sawServerWork) return $"Model is loaded - preparing the first reply… {secs:F0}s";
        return $"{what}… {secs:F0}s" + (secs > 20 ? " (loading the model and compiling kernels)" : "");
    }

    /// <summary>Bar width for a snapshot: the real percent, or a full bar when there is none.</summary>
    /// <remarks>
    /// A snapshot with no meaningful fraction (see <see cref="AiProgress.Indeterminate"/>) renders as a
    /// full bar carrying the <c>indeterminate</c> class, which animates. A 0%-wide bar would read as
    /// "stuck at zero" - which is exactly the doubt this is here to remove.
    /// </remarks>
    static int BarPercent(AiProgress p) => p.Indeterminate ? 100 : Math.Clamp(p.Percent, 0, 100);
}
