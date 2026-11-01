using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace SpawnDev.AI.Server;

/// <summary>How <see cref="AiMemoryEngine.ConsolidateAsync"/> digests.</summary>
public sealed class AiConsolidationOptions
{
    /// <summary>Episodes per digest (one LLM call). Default 12.</summary>
    public int BatchSize { get; set; } = 12;
    /// <summary>Digests per call; the rest waits for the next idle period. Default 4.</summary>
    public int MaxBatches { get; set; } = 4;
    /// <summary>A new fact at least this similar (cosine) to an existing fact in the same scope is checked against it (same? replaces it?). Default 0.75.</summary>
    public float ConflictSimilarity { get; set; } = 0.75f;
    /// <summary>Consolidated, unpinned episodes older than this, and less important than <see cref="CompactBelowImportance"/>, are compacted away (their facts stay). Null: never.</summary>
    public TimeSpan? CompactAfter { get; set; }
    /// <summary>See <see cref="CompactAfter"/>. Default 0.8: an important episode (a promise, a death) is kept as itself.</summary>
    public float CompactBelowImportance { get; set; } = 0.8f;
    /// <summary>Output budget per LLM call. Default 320 tokens.</summary>
    public int MaxOutputTokens { get; set; } = 320;
    /// <summary>"Now", for compaction ages (default: the clock).</summary>
    public long? NowTicks { get; set; }
}

/// <summary>What a consolidation did.</summary>
public sealed record AiConsolidationReport(int EpisodesDigested, int FactsAdded, int FactsReplaced, int FactsAlreadyKnown, int LinesIgnored, int EpisodesCompacted);

public sealed partial class AiMemoryEngine
{
    private static readonly Regex FactLine = new(@"^\s*(?:[-*]\s*)?FACT\s*:\s*(?<fact>.+?)\s*\|\s*FROM\s*:\s*(?<from>[\d,\s]+)\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Digests the episodes no consolidation has seen into durable FACTS with <paramref name="chat"/> (<paramref name="model"/>), oldest first,
    /// one scope at a time: each fact carries its sources' subjects and scope and <see cref="AiMemoryEntry.DerivedFrom"/>; a fact that says what
    /// an existing one says is not added again, one that updates it supersedes it (the old one is kept for history). Then, if asked, compacts old
    /// digested episodes away (<see cref="AiMemoryStore.Compact"/>; facts stay). For idle time: <paramref name="ct"/> is checked between model calls,
    /// and a batch's episodes are marked consolidated only after its facts are stored, so a cancel leaves nothing half-done.
    /// </summary>
    public async Task<AiConsolidationReport> ConsolidateAsync(AiChatEngine chat, string model, AiConsolidationOptions? options = null, CancellationToken ct = default)
    {
        options ??= new AiConsolidationOptions();
        await EnsureReadyAsync(ct).ConfigureAwait(false);
        int digested = 0, added = 0, replaced = 0, known = 0, ignored = 0;
        for (int b = 0; b < options.MaxBatches; b++)
        {
            ct.ThrowIfCancellationRequested();
            var pending = Store.List(new AiMemoryFilter { Kinds = new[] { AiMemoryKind.Episode } }).Where(e => !e.Consolidated).ToList();
            if (pending.Count == 0) break;
            var scope = pending[0].Scope;
            var batch = pending.Where(e => e.Scope == scope).Take(Math.Max(1, options.BatchSize)).ToList();

            var facts = await DigestAsync(chat, model, batch, options, ct).ConfigureAwait(false);
            ignored += facts.Ignored;
            foreach (var (text, from) in facts.Facts)
            {
                ct.ThrowIfCancellationRequested();
                var sources = from.Where(i => i >= 1 && i <= batch.Count).Select(i => batch[i - 1]).ToList();
                if (sources.Count == 0) { ignored++; continue; }
                var entry = new AiMemoryEntry
                {
                    Kind = AiMemoryKind.Fact, Text = text, Scope = scope, Source = "consolidation",
                    Subjects = sources.SelectMany(s => s.Subjects).Distinct(StringComparer.Ordinal).ToList(),
                    DerivedFrom = sources.Select(s => s.Id).ToList(),
                    TimeTicks = sources.Max(s => s.TimeTicks),
                    Importance = sources.Max(s => s.Importance),
                };
                // Does an existing fact already say this, or does this replace one?
                var near = await SearchAsync(text, 1, new AiMemoryFilter { Kinds = new[] { AiMemoryKind.Fact }, Scope = scope },
                    AiMemoryRanking.RelevanceOnly, ct: ct).ConfigureAwait(false);
                string verdict = "DIFFERENT";
                if (near.Count == 1 && near[0].Similarity >= options.ConflictSimilarity)
                    verdict = await JudgeAsync(chat, model, near[0].Entry.Text, text, options, ct).ConfigureAwait(false);
                if (verdict == "SAME")
                {
                    var old = near[0].Entry;
                    Store.Update(old.Id, e =>
                    {
                        foreach (var id in entry.DerivedFrom) if (!e.DerivedFrom.Contains(id)) e.DerivedFrom.Add(id);
                        foreach (var sj in entry.Subjects) if (!e.Subjects.Contains(sj)) e.Subjects.Add(sj);
                    });
                    known++;
                    continue;
                }
                var newId = await RememberAsync(entry, ct).ConfigureAwait(false);
                if (newId == null) { ignored++; continue; }           // learning paused or a blocked subject
                added++;
                if (verdict == "REPLACES" && Store.Supersede(near[0].Entry.Id, newId)) replaced++;
            }
            foreach (var e in batch) Store.Update(e.Id, x => x.Consolidated = true);
            digested += batch.Count;
        }

        int compacted = 0;
        if (options.CompactAfter is { } after)
        {
            long cutoff = (options.NowTicks ?? DateTime.UtcNow.Ticks) - after.Ticks;
            var old = Store.List(new AiMemoryFilter { Kinds = new[] { AiMemoryKind.Episode }, IncludeSuperseded = true })
                .Where(e => e.Consolidated && !e.Pinned && e.TimeTicks < cutoff && e.Importance < options.CompactBelowImportance)
                .Select(e => e.Id).ToArray();
            compacted = Store.Compact(old).Count;
        }
        await EnsureReadyAsync(ct).ConfigureAwait(false);   // apply the store's events to the device now
        return new AiConsolidationReport(digested, added, replaced, known, ignored, compacted);
    }

    private async Task<(List<(string Text, int[] From)> Facts, int Ignored)> DigestAsync(AiChatEngine chat, string model, List<AiMemoryEntry> batch,
        AiConsolidationOptions options, CancellationToken ct)
    {
        var log = new StringBuilder();
        for (int i = 0; i < batch.Count; i++)
            log.Append(i + 1).Append(". [").Append(new DateTime(batch[i].TimeTicks, DateTimeKind.Utc).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append("] ")
               .AppendLine(batch[i].Text.Replace('\n', ' '));
        var result = await chat.ChatAsync(new AiChatRequest
        {
            Model = model,
            Messages = new[]
            {
                new AiChatMessage("system",
                    "You turn a companion's memory log into short, durable facts. Write one fact per line, in exactly this form:\n" +
                    "FACT: <the fact, one sentence, with names, places and coordinates kept exactly> | FROM: <the numbers of the log lines it comes from>\n" +
                    "Write only what will still matter later: who people are, what they like, where things are, what was built where, promises, lessons. " +
                    "Never invent anything that is not in the log. If nothing in the log will matter later, write NONE."),
                new AiChatMessage("user", "Memory log:\n" + log),
            },
            Options = new AiGenerationOptions { Strategy = "greedy", Temperature = 0, MaxOutputTokens = options.MaxOutputTokens },
        }, ct).ConfigureAwait(false);
        var facts = new List<(string, int[])>();
        int ignored = 0;
        foreach (var raw in result.Text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.Equals("NONE", StringComparison.OrdinalIgnoreCase)) continue;
            var m = FactLine.Match(line);
            if (!m.Success) { ignored++; continue; }
            var from = m.Groups["from"].Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(x => int.TryParse(x, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : -1).Where(n => n > 0).Distinct().ToArray();
            facts.Add((m.Groups["fact"].Value.Trim(), from));
        }
        return (facts, ignored);
    }

    private static async Task<string> JudgeAsync(AiChatEngine chat, string model, string older, string newer, AiConsolidationOptions options, CancellationToken ct)
    {
        var r = await chat.ChatAsync(new AiChatRequest
        {
            Model = model,
            Messages = new[]
            {
                new AiChatMessage("system", "Compare two statements from a memory. Answer with exactly one word: SAME if they say the same thing, " +
                    "REPLACES if the newer one updates or contradicts the older one, DIFFERENT if they are about different things."),
                new AiChatMessage("user", $"Older: {older}\nNewer: {newer}"),
            },
            Options = new AiGenerationOptions { Strategy = "greedy", Temperature = 0, MaxOutputTokens = 8 },
        }, ct).ConfigureAwait(false);
        var word = r.Text.Trim().TrimStart('*', '"', '\'').ToUpperInvariant();
        return word.StartsWith("SAME") ? "SAME" : word.StartsWith("REPLACE") ? "REPLACES" : "DIFFERENT";
    }
}
