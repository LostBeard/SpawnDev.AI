namespace SpawnDev.AI.Server;

/// <summary>
/// Decides which models may co-reside on the shared GPU, and evicts only when one genuinely does not fit.
/// </summary>
/// <remarks>
/// <para>
/// This replaces a symmetric eviction ring in which every model kind evicted every other kind before it
/// loaded. That was safe and enormously wasteful: MEASURED 2026-08-30, three interleaved image+chat turns
/// spent <b>~130 s re-uploading an SD-Turbo UNet that had just been evicted</b> (38.4 s, 44.4 s, 46.6 s) on
/// a GPU with <b>10.6 GB free</b> - nothing needed evicting at all. A hands-free conversation makes it
/// worse again, because one turn touches three kinds in a row (transcribe -> chat -> speak) and a symmetric
/// ring turns that into three model reloads per turn.
/// </para>
/// <para>
/// ⚠️ The opposite mistake is just as real and I have made it: simply letting everything co-reside. Rose
/// froze at <b>11762/12282 MiB (96%)</b> when Whisper joined an 8B LLM and the voice cloner on one 12 GB
/// card - a starved CUDA op wedged, and being a native call with no cancellation, the turn never completed.
/// "Evict nothing" is not the fix for "evict everything"; a BUDGET is.
/// </para>
/// <para>
/// So: each kind declares what it costs while resident, and eviction happens only when the incoming model
/// would push the total past the budget - least-recently-used first.
/// </para>
/// <para>
/// ⚠️ This is the PROACTIVE layer, and it is not the only one. <c>BufferPool.AllocateWithReclaim</c> in
/// SpawnDev.ILGPU.ML is REACTIVE and works at a different scale: when an allocation fails it disposes
/// returned-but-not-live bucketed buffers inside ONE pool. It frees cache, never a model, so it cannot
/// resolve "two large models want the card at once" - that is this class's job. They complement each
/// other and must not be confused for one another.
/// </para>
/// <para>
/// ⚠️ A reclaim FIRING is therefore a signal that this budget is too generous: it means an allocation
/// already failed and the lower layer is scavenging. It is also actively dangerous during graph capture -
/// <c>CudaGraphCapture</c> refuses to capture at all if a warm pass tripped it, because a reclaim mid-capture
/// disposes buffers whose last dispatch may still be in flight. <c>BufferPool.ReclaimFireCount</c> is the
/// number to watch when tuning the budget down.
/// </para>
/// <para>
/// ⚠️ The budget is over OUR OWN resident models, not the card. On CUDA the initial budget comes from real
/// free VRAM; in the browser no such number exists - WebGPU exposes buffer limits, not free memory - so it
/// falls back to a conservative default. Either way this cannot see the desktop's own GPU use or another
/// process, which is precisely how the Rose freeze happened. Leave headroom; do not tune it to the edge.
/// </para>
/// </remarks>
public sealed class GpuResidency
{
    private sealed class Kind
    {
        public string Name = "";
        public Func<bool> IsResident = () => false;
        public long Bytes;
        public Func<Task> Evict = () => Task.CompletedTask;
        public DateTime LastUsedUtc = DateTime.MinValue;
    }

    private readonly List<Kind> _kinds = new();
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Bytes our resident models may occupy in total.</summary>
    public long BudgetBytes { get; set; }

    /// <summary>Logs evictions and the reason. Null for quiet operation.</summary>
    public Action<string>? OnLog { get; set; }

    /// <summary>
    /// New residency manager.
    /// </summary>
    /// <param name="budgetBytes">
    /// Total our models may hold. Callers should derive this from real free VRAM where that is knowable and
    /// keep headroom for the OS desktop and anything else sharing the card.
    /// </param>
    public GpuResidency(long budgetBytes) => BudgetBytes = budgetBytes;

    /// <summary>Register a model kind and what it costs while resident.</summary>
    /// <param name="name">Kind name, used in logs.</param>
    /// <param name="isResident">Whether this kind currently holds GPU memory.</param>
    /// <param name="bytes">Approximate resident footprint.</param>
    /// <param name="evict">Releases this kind's GPU memory.</param>
    public void Register(string name, Func<bool> isResident, long bytes, Func<Task> evict)
    {
        lock (_kinds)
            _kinds.Add(new Kind { Name = name, IsResident = isResident, Bytes = bytes, Evict = evict });
    }

    /// <summary>Note that a kind was just used, so it sorts as most-recently-used.</summary>
    public void Touch(string name)
    {
        lock (_kinds)
            foreach (var k in _kinds)
                if (k.Name == name) k.LastUsedUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Make room for <paramref name="name"/> to become resident, evicting least-recently-used kinds ONLY
    /// if it would not otherwise fit.
    /// </summary>
    public async Task EnsureRoomForAsync(string name)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            Kind[] snapshot;
            long incoming;
            lock (_kinds)
            {
                snapshot = _kinds.ToArray();
                incoming = snapshot.FirstOrDefault(k => k.Name == name)?.Bytes ?? 0;
                foreach (var k in _kinds) if (k.Name == name) k.LastUsedUtc = DateTime.UtcNow;
            }

            long ResidentOther() => snapshot
                .Where(k => k.Name != name && k.IsResident())
                .Sum(k => k.Bytes);

            if (ResidentOther() + incoming <= BudgetBytes)
            {
                // The common case, and the whole point: everything fits, so nothing is unloaded. A model
                // that stays resident is a model nobody has to re-upload on the next turn.
                return;
            }

            // Least-recently-used first, so the kind most likely to be wanted next survives longest.
            foreach (var victim in snapshot
                .Where(k => k.Name != name && k.IsResident())
                .OrderBy(k => k.LastUsedUtc))
            {
                if (ResidentOther() + incoming <= BudgetBytes) break;
                OnLog?.Invoke($"[GpuResidency] evicting {victim.Name} ({victim.Bytes / 1048576} MB) to make "
                            + $"room for {name} ({incoming / 1048576} MB); budget {BudgetBytes / 1048576} MB");
                await victim.Evict().ConfigureAwait(false);
            }

            if (ResidentOther() + incoming > BudgetBytes)
            {
                // Say so rather than proceeding silently. Loading anyway is what drives a card to 96% and
                // wedges a native op that cannot be cancelled - a hang, not an exception.
                OnLog?.Invoke($"[GpuResidency] ⚠️ {name} ({incoming / 1048576} MB) still does not fit after "
                            + $"evicting everything else (budget {BudgetBytes / 1048576} MB). Loading anyway; "
                            + "if the device wedges or OOMs, this is the reason.");
            }
        }
        finally { _gate.Release(); }
    }
}
