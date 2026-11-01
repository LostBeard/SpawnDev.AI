using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace SpawnDev.AI.Server;

/// <summary>
/// Where an Ollama model can be fetched from: how to name its manifest and its content-addressed blobs.
/// </summary>
/// <param name="Name">Shown in progress and errors.</param>
/// <param name="ManifestUrl">(namespace, model, tag) -> the manifest's URL.</param>
/// <param name="BlobUrl">(namespace, model, digest "sha256:hex") -> the blob's URL.</param>
/// <param name="IndexUrl">A JSON list of the models this source hosts (see <see cref="OllamaModelInstaller.ListInstallableAsync"/>), or null when it has none.</param>
public sealed record OllamaModelSource(string Name, Func<string, string, string, Uri> ManifestUrl, Func<string, string, string, Uri> BlobUrl, Func<Uri>? IndexUrl = null)
{
    /// <summary>Ollama's own registry, anonymous (no credentials are ever sent). It has no list of models.</summary>
    public static OllamaModelSource Registry { get; } = new("registry.ollama.ai",
        (ns, model, tag) => new Uri($"https://registry.ollama.ai/v2/{ns}/{model}/manifests/{tag}"),
        (ns, model, digest) => new Uri($"https://registry.ollama.ai/v2/{ns}/{model}/blobs/{digest}"));

    /// <summary>
    /// models.spawndev.com (free egress; <see cref="SpawnDev.ILGPU.ML.Hub.ModelsHost"/> owns the layout and its <c>BaseUrl</c>): the registry's manifest verbatim
    /// and content-addressed blobs shared across models, plus an index of what is hosted. A namespaced model is looked up as "{ns}/{model}".
    /// </summary>
    public static OllamaModelSource SpawnDevModels { get; } = new("models.spawndev.com",
        (ns, model, tag) => new Uri(SpawnDev.ILGPU.ML.Hub.ModelsHost.GetOllamaManifestUrl(ns == "library" ? model : $"{ns}/{model}", tag)),
        (ns, model, digest) => new Uri(SpawnDev.ILGPU.ML.Hub.ModelsHost.GetOllamaBlobUrl(digest)),
        () => new Uri(SpawnDev.ILGPU.ML.Hub.ModelsHost.GetOllamaIndexUrl()));
}

/// <summary>Where an install is.</summary>
/// <param name="Model">"name:tag".</param>
/// <param name="Stage">"manifest", "layer", "verify", "done".</param>
/// <param name="Layer">The layer's media type while one downloads (…image.model is the big one).</param>
/// <param name="Source">The source the current file comes from.</param>
/// <param name="LayerBytes">Bytes of the current layer on disk.</param>
/// <param name="LayerTotal">Its size.</param>
/// <param name="DoneBytes">Bytes of the whole model on disk.</param>
/// <param name="TotalBytes">The whole model's size.</param>
/// <param name="BytesPerSecond">Recent download speed (0 when not downloading).</param>
public sealed record OllamaInstallProgress(string Model, string Stage, string? Layer, string? Source, long LayerBytes, long LayerTotal, long DoneBytes, long TotalBytes, double BytesPerSecond);

/// <summary>A model a source offers.</summary>
public sealed record OllamaInstallableModel(string Name, long SizeBytes, string Source, string? Description);

/// <summary>
/// Installs Ollama models into an Ollama-layout cache (the one <see cref="OllamaModelStore"/> and <c>OllamaCacheModelProvider</c> read, and Ollama itself reads) without
/// Ollama: an app can fetch its model on first run.
/// </summary>
/// <remarks>
/// <para>
/// Each missing layer is streamed straight to <c>blobs/sha256-{hex}.partial</c> and hashed as it arrives; a partial left by an earlier run is resumed with a Range request
/// (its bytes are hashed first, so the final digest still covers every byte). The finished file must hash to its digest or it is deleted. A file is retried on the SAME source
/// (resuming) and, when that source keeps failing, started again from zero on the next one - a file never mixes bytes of two sources. The manifest is written LAST, through a
/// temporary file and a rename, so a model lists only when every blob it names is on disk; an interrupted install is invisible, not broken.
/// </para>
/// <para>
/// Every read has a stall timeout (<see cref="StallTimeout"/>): a connection that stops sending fails and is retried instead of hanging the install for ever.
/// </para>
/// </remarks>
public sealed class OllamaModelInstaller
{
    private const string ManifestAccept = "application/vnd.docker.distribution.manifest.v2+json";
    private readonly HttpClient _http;
    private readonly string _root;

    /// <param name="http">An HttpClient (its own Timeout should be infinite: a 5 GB layer takes longer than any fixed timeout; <see cref="StallTimeout"/> guards each read).</param>
    /// <param name="modelsRoot">The cache root (default <see cref="OllamaModelStore.DefaultRoot"/>).</param>
    /// <param name="sources">Tried in order (default: models.spawndev.com, then registry.ollama.ai).</param>
    public OllamaModelInstaller(HttpClient http, string? modelsRoot = null, IReadOnlyList<OllamaModelSource>? sources = null)
    {
        _http = http;
        _root = modelsRoot ?? OllamaModelStore.DefaultRoot();
        Sources = sources ?? new[] { OllamaModelSource.SpawnDevModels, OllamaModelSource.Registry };
    }

    /// <summary>The sources, in the order they are tried.</summary>
    public IReadOnlyList<OllamaModelSource> Sources { get; }

    /// <summary>A read that delivers nothing for this long fails (and is retried). Default 60 s.</summary>
    public TimeSpan StallTimeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>Attempts per file on one source (each resumes) before moving to the next source. Default 3.</summary>
    public int AttemptsPerSource { get; set; } = 3;

    /// <summary>Free space required beyond the bytes still to download. Default 1 GiB.</summary>
    public long FreeSpaceHeadroomBytes { get; set; } = 1L << 30;

    /// <summary>Reports progress (throttled to a few times a second during a download).</summary>
    public Action<OllamaInstallProgress>? Progress { get; set; }

    /// <summary>
    /// Installs <paramref name="name"/> ("llama3.1:8b", "gemma4" = ":latest", "user/model:tag") unless it is already complete. Returns the installed model.
    /// Throws <see cref="IOException"/> when the disk lacks the space, <see cref="InvalidDataException"/> when no source delivered a file that matches its digest.
    /// </summary>
    public async Task<OllamaModel> InstallAsync(string name, CancellationToken ct = default)
    {
        var (ns, model, tag) = ParseName(name);
        var display = ns == "library" ? $"{model}:{tag}" : $"{ns}/{model}:{tag}";
        var store = new OllamaModelStore(_root);

        // 1. The manifest, from the first source that has it. Its bytes are kept verbatim (written last).
        byte[]? manifestBytes = null; OllamaModelSource? manifestSource = null; var errors = new List<string>();
        Report(display, "manifest", null, null, 0, 0, 0, 0, 0);
        foreach (var src in Sources)
        {
            try { manifestBytes = await GetManifestAsync(src.ManifestUrl(ns, model, tag), ct).ConfigureAwait(false); manifestSource = src; break; }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) { errors.Add($"{src.Name}: {ex.Message}"); }
        }
        if (manifestBytes == null) throw new InvalidDataException($"no source has {display}: {string.Join("; ", errors)}");
        var blobs = ParseBlobs(manifestBytes);

        // Already complete? (the manifest on disk names the same blobs and they are all present)
        var manifestPath = Path.Combine(_root, "manifests", "registry.ollama.ai", ns, model, tag);
        var blobsDir = Path.Combine(_root, "blobs");
        Directory.CreateDirectory(blobsDir);
        long total = blobs.Sum(b => b.Size);
        bool Have((string Digest, long Size, string Media) b) => File.Exists(BlobPath(b.Digest)) && new FileInfo(BlobPath(b.Digest)).Length == b.Size;

        // 2. Space: what is still missing (a partial counts as done for its length) plus headroom.
        long missing = blobs.Where(b => !Have(b)).Sum(b => b.Size - PartialLength(b.Digest));
        var free = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(_root))!).AvailableFreeSpace;
        if (missing + FreeSpaceHeadroomBytes > free)
            throw new IOException($"{display} needs {missing / 1e9:F2} GB more (plus {FreeSpaceHeadroomBytes / 1e9:F1} GB headroom) and the drive has {free / 1e9:F2} GB free");

        // 3. Blobs, each from the manifest's source first and then the others.
        var order = new[] { manifestSource! }.Concat(Sources.Where(s => s != manifestSource)).ToList();
        long done = blobs.Where(Have).Sum(b => b.Size);
        foreach (var b in blobs)
        {
            if (Have(b)) continue;
            await DownloadBlobAsync(display, ns, model, b, order, done, total, ct).ConfigureAwait(false);
            done += b.Size;
        }

        // 4. The manifest, last, atomically.
        Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
        var tmp = manifestPath + ".tmp";
        await File.WriteAllBytesAsync(tmp, manifestBytes, ct).ConfigureAwait(false);
        File.Move(tmp, manifestPath, overwrite: true);
        Report(display, "done", null, manifestSource!.Name, 0, 0, total, total, 0);
        return store.Resolve(display) ?? throw new InvalidDataException($"{display} was written but the cache cannot resolve it (no model layer?)");
    }

    /// <summary>The models the sources list (only sources with an index; registry.ollama.ai has none). A source that cannot be reached is skipped.</summary>
    public async Task<IReadOnlyList<OllamaInstallableModel>> ListInstallableAsync(CancellationToken ct = default)
    {
        var result = new List<OllamaInstallableModel>();
        foreach (var src in Sources)
        {
            if (src.IndexUrl == null) continue;
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(StallTimeout);
                using var doc = JsonDocument.Parse(await _http.GetByteArrayAsync(src.IndexUrl(), cts.Token).ConfigureAwait(false));
                foreach (var m in doc.RootElement.GetProperty("models").EnumerateArray())
                    result.Add(new OllamaInstallableModel(m.GetProperty("name").GetString()!, m.TryGetProperty("size", out var s) ? s.GetInt64() : 0, src.Name,
                        m.TryGetProperty("description", out var d) ? d.GetString() : null));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch { /* an unreachable index is not an error for the others */ }
        }
        return result;
    }

    private async Task DownloadBlobAsync(string display, string ns, string model, (string Digest, long Size, string Media) b, List<OllamaModelSource> order,
        long doneBefore, long total, CancellationToken ct)
    {
        var final = BlobPath(b.Digest);
        var partial = final + ".partial";
        var errors = new List<string>();
        bool first = true;
        foreach (var src in order)
        {
            // A file never mixes two sources' bytes: a NEW source starts from zero. (The first source may resume a partial from an earlier run - the bytes are
            // content-addressed and re-hashed, so whichever source wrote them, a wrong byte fails the digest.)
            if (!first && File.Exists(partial)) File.Delete(partial);
            first = false;
            bool fromZeroTried = false;
            for (int attempt = 1; attempt <= Math.Max(1, AttemptsPerSource); attempt++)
            {
                bool fromZero = !File.Exists(partial);
                fromZeroTried |= fromZero;
                try
                {
                    await FetchToPartialAsync(display, src, src.BlobUrl(ns, model, b.Digest), b, partial, doneBefore, total, ct).ConfigureAwait(false);
                    File.Move(partial, final, overwrite: true);
                    return;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }   // keep the partial: the next install resumes it
                catch (InvalidDataException ex)
                {
                    // Wrong bytes. If they may have come from a partial this source did not write in full (an earlier run, a damaged disk), give the source one
                    // clean download from zero before blaming it; a source that fails a clean download is not tried again for this file.
                    errors.Add($"{src.Name}: {ex.Message}");
                    if (File.Exists(partial)) File.Delete(partial);
                    if (fromZeroTried) break;
                }
                catch (Exception ex) { errors.Add($"{src.Name} attempt {attempt}: {ex.Message}"); }
            }
        }
        throw new InvalidDataException($"{display}: no source delivered {b.Digest} ({b.Size} bytes): {string.Join("; ", errors)}");
    }

    private async Task FetchToPartialAsync(string display, OllamaModelSource src, Uri url, (string Digest, long Size, string Media) b, string partial,
        long doneBefore, long total, CancellationToken ct)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long have = 0;
        var buffer = new byte[1 << 20];
        await using (var existing = File.Exists(partial) ? new FileStream(partial, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, useAsync: true) : null)
        {
            if (existing != null)
            {
                int n;
                while ((n = await existing.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0) { hash.AppendData(buffer, 0, n); have += n; }
            }
        }
        if (have > b.Size) { File.Delete(partial); hash.GetHashAndReset(); have = 0; }

        if (have < b.Size)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            if (have > 0) req.Headers.Range = new RangeHeaderValue(have, null);
            using var stall = CancellationTokenSource.CreateLinkedTokenSource(ct);
            stall.CancelAfter(StallTimeout);
            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, stall.Token).ConfigureAwait(false);
            if (resp.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable && have > 0)
                throw new InvalidDataException("the source refused to resume (416): the partial is not a prefix of this blob");
            resp.EnsureSuccessStatusCode();
            if (have > 0 && resp.StatusCode != HttpStatusCode.PartialContent)
            {
                // The server ignored the Range and is sending the whole blob: start the file over.
                hash.GetHashAndReset(); have = 0;
                File.Delete(partial);
            }
            await using var body = await resp.Content.ReadAsStreamAsync(stall.Token).ConfigureAwait(false);
            await using var file = new FileStream(partial, have > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20, useAsync: true);
            var clock = System.Diagnostics.Stopwatch.StartNew();
            long sinceReport = 0; long reportAt = 0; long lastBytesAt = have;
            while (true)
            {
                stall.CancelAfter(StallTimeout);
                int n;
                try { n = await body.ReadAsync(buffer, stall.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new TimeoutException($"no data from {src.Name} for {StallTimeout.TotalSeconds:F0} s"); }
                if (n == 0) break;
                hash.AppendData(buffer, 0, n);
                await file.WriteAsync(buffer.AsMemory(0, n), ct).ConfigureAwait(false);
                have += n; sinceReport += n;
                if (have > b.Size) throw new InvalidDataException($"{src.Name} sent more than the {b.Size} bytes the manifest lists");
                if (clock.ElapsedMilliseconds - reportAt >= 250)
                {
                    var secs = (clock.ElapsedMilliseconds - reportAt) / 1000.0;
                    Report(display, "layer", b.Media, src.Name, have, b.Size, doneBefore + have, total, secs > 0 ? (have - lastBytesAt) / secs : 0);
                    reportAt = clock.ElapsedMilliseconds; lastBytesAt = have; sinceReport = 0;
                }
            }
            await file.FlushAsync(ct).ConfigureAwait(false);
            if (have < b.Size) throw new IOException($"{src.Name} ended the blob at {have} of {b.Size} bytes");
        }

        Report(display, "verify", b.Media, src.Name, have, b.Size, doneBefore + have, total, 0);
        var got = "sha256:" + Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        if (!string.Equals(got, b.Digest, StringComparison.Ordinal))
            throw new InvalidDataException($"{src.Name} delivered bytes that hash to {got}, not {b.Digest}");
    }

    private async Task<byte[]> GetManifestAsync(Uri url, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Accept.ParseAdd(ManifestAccept);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(StallTimeout);
        using var resp = await _http.SendAsync(req, cts.Token).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        var bytes = await resp.Content.ReadAsByteArrayAsync(cts.Token).ConfigureAwait(false);   // a manifest is ~1 KB
        ParseBlobs(bytes);   // refuse anything that is not a manifest before it is ever written
        return bytes;
    }

    private static List<(string Digest, long Size, string Media)> ParseBlobs(byte[] manifest)
    {
        using var doc = JsonDocument.Parse(manifest);
        var list = new List<(string, long, string)>();
        void Add(JsonElement e)
        {
            var digest = e.GetProperty("digest").GetString()!;
            if (!digest.StartsWith("sha256:", StringComparison.Ordinal) || digest.Length != 71) throw new InvalidDataException($"unexpected digest '{digest}'");
            list.Add((digest, e.GetProperty("size").GetInt64(), e.TryGetProperty("mediaType", out var m) ? m.GetString() ?? "" : ""));
        }
        if (doc.RootElement.TryGetProperty("config", out var config)) Add(config);
        foreach (var l in doc.RootElement.GetProperty("layers").EnumerateArray()) Add(l);
        if (!list.Any(x => x.Item3 == "application/vnd.ollama.image.model")) throw new InvalidDataException("the manifest has no model layer");
        return list;
    }

    private static (string Ns, string Model, string Tag) ParseName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("a model name is needed", nameof(name));
        var tag = "latest";
        var colon = name.LastIndexOf(':');
        if (colon > 0 && name.IndexOf('/', colon) < 0) { tag = name[(colon + 1)..]; name = name[..colon]; }
        var slash = name.IndexOf('/');
        var (ns, model) = slash < 0 ? ("library", name) : (name[..slash], name[(slash + 1)..]);
        foreach (var part in new[] { ns, model, tag })
            if (part.Length == 0 || part.Contains("..") || part.IndexOfAny(Path.GetInvalidFileNameChars().Where(c => c != '/').ToArray()) >= 0)
                throw new ArgumentException($"'{name}:{tag}' is not a model name");
        return (ns, model, tag);
    }

    private string BlobPath(string digest) => Path.Combine(_root, "blobs", digest.Replace(':', '-'));
    private long PartialLength(string digest) { var p = BlobPath(digest) + ".partial"; return File.Exists(p) ? new FileInfo(p).Length : 0; }

    private void Report(string model, string stage, string? layer, string? source, long layerBytes, long layerTotal, long done, long total, double bps)
        => Progress?.Invoke(new OllamaInstallProgress(model, stage, layer, source, layerBytes, layerTotal, done, total, bps));
}
