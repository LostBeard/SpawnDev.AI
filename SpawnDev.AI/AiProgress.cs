using System.Text.Json.Serialization;

namespace SpawnDev.AI;

/// <summary>
/// What the server is doing while a client waits: nothing, fetching weights, or putting them on the GPU.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 WHY THIS EXISTS. Captain, on the deployed demo: "it takes it roughly 1 minute to respond to the
/// first message and the user has no idea what is going on or how long it will take." That minute is two
/// distinct costs - a 1.71 GB download and a GPU weight upload - and the app reported neither. All it had
/// was a caption counting seconds upward, which says a wait is happening and nothing about what or how
/// much longer.
/// </para>
/// <para>
/// Both numbers already existed on the server side: <c>HttpModelDownloader</c> tracks bytes and throughput
/// for every download in flight, and <c>InferenceSession</c> reports a load stage and percent. Neither had
/// a route to a UI, because the model loads inside a worker and the page that shows progress is not the
/// page doing the work. This record is that route.
/// </para>
/// <para>
/// ⚠️ A SNAPSHOT, not an event. A client polls it, so a missed sample costs nothing and a client that
/// arrives late (a second tab attaching to a shared worker mid-download) still sees the true state.
/// </para>
/// </remarks>
public sealed record AiProgress
{
    /// <summary>"idle", "download" (bytes are moving) or "load" (weights are going to the GPU).</summary>
    [JsonPropertyName("phase")] public string Phase { get; init; } = "idle";

    /// <summary>What is being fetched or loaded - a model name, or a store key when that is all we know.</summary>
    [JsonPropertyName("model")] public string Model { get; init; } = "";

    /// <summary>Load stage: "fetch", "parse", "build_graph", "compile", "upload", "ready". Empty while downloading.</summary>
    [JsonPropertyName("stage")] public string Stage { get; init; } = "";

    /// <summary>
    /// Percent complete of <see cref="Phase"/>, or -1 when it cannot be known.
    /// </summary>
    /// <remarks>
    /// ⚠️ For "load" this is percent of the CURRENT STAGE, not of the whole load, and the UI must say so.
    /// The alternative was to weight the stages into one overall bar, which would have meant inventing
    /// weights: "upload" dominates a warm load and a cold one differently, and a made-up number that looks
    /// authoritative is worse than a true one that needs a label.
    /// </remarks>
    [JsonPropertyName("percent")] public int Percent { get; init; } = -1;

    /// <summary>Bytes fetched so far in this download, including any carried in by a resume.</summary>
    [JsonPropertyName("bytesReceived")] public long BytesReceived { get; init; }

    /// <summary>Expected total bytes, or 0/-1 when the origin did not say.</summary>
    [JsonPropertyName("totalBytes")] public long TotalBytes { get; init; }

    /// <summary>Throughput of the current download, 0 before enough time has passed to mean anything.</summary>
    [JsonPropertyName("bytesPerSecond")] public double BytesPerSecond { get; init; }

    /// <summary>True when this download continued an interrupted one rather than starting over.</summary>
    [JsonPropertyName("resumed")] public bool Resumed { get; init; }

    /// <summary>Seconds since this activity began - what the caption counted before it could say anything else.</summary>
    [JsonPropertyName("elapsedSeconds")] public double ElapsedSeconds { get; init; }

    /// <summary>Nothing is loading or downloading.</summary>
    [JsonIgnore] public bool Idle => Phase == "idle";

    /// <summary>
    /// True when there is no meaningful fraction to draw - the bar should animate rather than fill.
    /// </summary>
    /// <remarks>
    /// 🔴 MEASURED on a cold first turn: "reading the model file" held 0% for 6.6 s and "fetching weights"
    /// for 7.0 s. Those stages report 0 and then 100 with nothing in between, so a filled bar shows a hard
    /// zero for seconds at a time - which is precisely the "is it stuck?" this feature exists to answer.
    /// A download at a true 0% is different: it has a total, and one poll later it is at 1%.
    /// </remarks>
    [JsonIgnore] public bool Indeterminate => Percent < 0 || (Phase == "load" && Percent <= 0);

    /// <summary>
    /// Seconds left at the current rate, or null when there is no rate or no total to finish.
    /// </summary>
    /// <remarks>
    /// Download only, deliberately. A GPU upload has no comparable rate to extrapolate from - the per-stage
    /// percent is the honest signal there, and inventing an ETA from it would be guessing.
    /// </remarks>
    [JsonIgnore]
    public double? SecondsRemaining
        => Phase == "download" && BytesPerSecond > 1 && TotalBytes > BytesReceived
            ? (TotalBytes - BytesReceived) / BytesPerSecond
            : null;

    /// <summary>One line a user can read: what is happening, how far along, and how much longer.</summary>
    public string Describe() => Phase switch
    {
        "download" => $"Downloading {Short(Model)} - {Size(BytesReceived)}"
                    + (TotalBytes > 0 ? $" of {Size(TotalBytes)} ({Percent}%)" : "")
                    + (BytesPerSecond > 1 ? $" - {Size((long)BytesPerSecond)}/s" : "")
                    + (SecondsRemaining is { } r ? $" - about {Duration(r)} left" : "")
                    + (Resumed ? " (resumed)" : ""),
        // No percent on a stage that has none to give: "reading the model file 0%" for six seconds reads
        // as stuck, while "reading the model file" under a moving bar reads as working.
        "load" => $"Loading {Short(Model)} onto the GPU - {StageLabel(Stage)}"
                + (Indeterminate ? "" : $" {Percent}%"),
        _ => "",
    };

    /// <summary>Plain-English stage name. The raw stage ids are engine internals, not user-facing words.</summary>
    public static string StageLabel(string stage) => stage switch
    {
        "fetch" => "fetching weights",
        "parse" => "reading the model file",
        "build_graph" => "building the graph",
        "compile" => "compiling kernels",
        "upload" => "uploading weights",
        "ready" => "ready",
        "" => "working",
        _ => stage,
    };

    /// <summary>A store key like <c>hf_repo_main_model.gguf</c> is unreadable; show the tail that identifies it.</summary>
    private static string Short(string model)
    {
        if (string.IsNullOrEmpty(model)) return "the model";
        var i = model.LastIndexOf('/');
        return i >= 0 && i < model.Length - 1 ? model[(i + 1)..] : model;
    }

    private static string Size(long bytes)
        => bytes >= 1_073_741_824 ? $"{bytes / 1_073_741_824.0:0.##} GB"
         : bytes >= 1_048_576 ? $"{bytes / 1_048_576.0:0.#} MB"
         : $"{bytes / 1024.0:0} KB";

    private static string Duration(double seconds)
        => seconds >= 90 ? $"{seconds / 60:0} min" : $"{seconds:0}s";
}
