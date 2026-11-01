using SpawnDev.AI.Server;

/// <summary>
/// `install-test [modelsRoot]`: OllamaModelInstaller against the REAL network (registry.ollama.ai, and models.spawndev.com which falls through until it hosts the
/// model). Installs all-minilm:22m (46 MB, a real model layer + license + params) into a scratch cache. No GPU. Exit code = failures.
/// </summary>
internal static class InstallerChecks
{
    const string Model = "all-minilm:22m";

    /// <summary>
    /// `install-live [model] [root]`: the full install of a HOSTED model from models.spawndev.com ONLY (no registry fallback that could hide a hosting fault): it is
    /// listed by ListInstallableAsync with its size, every blob comes from the host and matches its digest, and the cache resolves it. Default llama3.1:8b (4.9 GB).
    /// </summary>
    public static async Task<int> RunLiveAsync(string[] args)
    {
        var model = args.Length > 1 ? args[1] : "llama3.1:8b";
        var root = args.Length > 2 ? args[2] : Path.Combine(Path.GetTempPath(), "spawndev-install-live");
        using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        int failures = 0;
        void Check(bool ok, string what) { Console.WriteLine($"TEST: {what}|{(ok ? "PASS" : "FAIL")}"); if (!ok) failures++; }
        var sources = new HashSet<string>();
        long lastPrint = 0;
        var inst = new OllamaModelInstaller(http, root, new[] { OllamaModelSource.SpawnDevModels })
        {
            Progress = p =>
            {
                if (p.Source != null) sources.Add(p.Source);
                if (p.Stage != "layer" || Environment.TickCount64 - lastPrint > 10_000) { lastPrint = Environment.TickCount64; Console.WriteLine($"  {p.Stage} {p.DoneBytes / 1e9:F2}/{p.TotalBytes / 1e9:F2} GB {p.BytesPerSecond / 1e6:F1} MB/s"); }
            },
        };
        var listed = await inst.ListInstallableAsync();
        var entry = listed.FirstOrDefault(m => m.Name == model);
        Console.WriteLine($"  listed: {string.Join(", ", listed.Select(m => $"{m.Name} {m.SizeBytes / 1e9:F2} GB"))}");
        Check(entry != null && entry.SizeBytes > 0, $"{model} is listed by the host's index with its size");
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var installed = await inst.InstallAsync(model);
        Console.WriteLine($"  installed in {clock.Elapsed.TotalMinutes:F1} min: {installed.GgufPath} ({installed.GgufSize:N0} bytes)");
        Check(sources.SetEquals(new[] { "models.spawndev.com" }), $"every file came from models.spawndev.com ({string.Join(",", sources)})");
        Check(new OllamaModelStore(root).Resolve(model)?.GgufSize == installed.GgufSize && installed.GgufSize > 1_000_000_000, "the cache resolves the full model");
        Check(entry == null || entry.SizeBytes >= installed.GgufSize, "the listed size covers the model layer");
        Console.WriteLine($"RESULTS: {failures} failed (cache left at {root}; delete it when done)");
        return failures;
    }

    public static async Task<int> RunAsync(string[] args)
    {
        var root = args.Length > 1 ? args[1] : Path.Combine(Path.GetTempPath(), "spawndev-install-test");
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        Directory.CreateDirectory(root);
        using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        int failures = 0;
        void Check(bool ok, string what) { Console.WriteLine($"TEST: {what}|{(ok ? "PASS" : "FAIL")}"); if (!ok) failures++; }
        var store = new OllamaModelStore(root);
        string BlobsDir() => Path.Combine(root, "blobs");

        // 1. Not enough disk: refused before a byte is downloaded.
        {
            var inst = new OllamaModelInstaller(http, root) { FreeSpaceHeadroomBytes = long.MaxValue / 4 };
            bool refused = false;
            try { await inst.InstallAsync(Model); } catch (IOException ex) { refused = ex.Message.Contains("free"); Console.WriteLine("  " + ex.Message); }
            var files = Directory.Exists(BlobsDir()) ? Directory.GetFiles(BlobsDir()).Length : 0;
            Check(refused && files == 0, $"disk-space check refuses up front ({files} files written)");
        }

        // 2. Interrupted install: cancel after ~15 MB. The model must NOT be listed (manifest last) and a partial must remain.
        long firstRunBytes = 0;
        {
            using var cts = new CancellationTokenSource();
            var inst = new OllamaModelInstaller(http, root)
            {
                Progress = p => { if (p.Stage == "layer") { firstRunBytes = p.LayerBytes; if (p.LayerBytes > 15_000_000) cts.Cancel(); } },
            };
            bool cancelled = false;
            try { await inst.InstallAsync(Model, cts.Token); } catch (OperationCanceledException) { cancelled = true; }
            var partials = Directory.GetFiles(BlobsDir(), "*.partial");
            long partialLen = partials.Length == 1 ? new FileInfo(partials[0]).Length : 0;
            Console.WriteLine($"  cancelled at {firstRunBytes:N0} bytes, partial {partialLen:N0}");
            Check(cancelled, "cancel stops the install");
            // The FILE, not Resolve: OllamaModelStore already hides a manifest whose model blob is missing, but Ollama itself reads this cache too.
            var manifests = Directory.Exists(Path.Combine(root, "manifests")) ? Directory.GetFiles(Path.Combine(root, "manifests"), "*", SearchOption.AllDirectories).Length : 0;
            Check(manifests == 0 && store.Resolve(Model) == null, $"an interrupted model has no manifest on disk ({manifests} found): it is written last");
            Check(partials.Length == 1 && partialLen > 10_000_000, "the partial is kept for resume");
        }

        // 3. Resume: the second run downloads only the rest, from models.spawndev.com's 404 through to registry.ollama.ai, and verifies.
        {
            long resumedFrom = -1; string? source = null;
            var inst = new OllamaModelInstaller(http, root)
            {
                Progress = p => { if (p.Stage == "layer" && resumedFrom < 0) { resumedFrom = p.LayerBytes; source = p.Source; } },
            };
            var model = await inst.InstallAsync(Model);
            Console.WriteLine($"  resumed at {resumedFrom:N0} bytes from {source}; gguf {model.GgufPath}");
            Check(resumedFrom > 10_000_000, "the second run resumed instead of starting over");
            Check(source == "registry.ollama.ai", "a source without the model (404) falls through to the next");
            Check(store.Resolve(Model)?.GgufSize == 45_949_216, "the installed model resolves with its full size");
            Check(Directory.GetFiles(BlobsDir(), "*.partial").Length == 0, "no partial left behind");
        }

        // 4. A damaged partial: hash fails, the file is fetched again from zero, the install still completes with the right digest.
        {
            var gguf = store.Resolve(Model)!.GgufPath;
            var manifest = Directory.GetFiles(Path.Combine(root, "manifests"), "*", SearchOption.AllDirectories).Single();
            File.Delete(manifest);
            var bytes = File.ReadAllBytes(gguf);
            File.Delete(gguf);
            Array.Clear(bytes, 1_000_000, 4096);                 // damage a stretch in the middle of the first 20 MB
            File.WriteAllBytes(gguf + ".partial", bytes[..20_000_000]);
            var stages = new List<string>();
            var inst = new OllamaModelInstaller(http, root) { Progress = p => { if (stages.Count == 0 || stages[^1] != p.Stage) stages.Add(p.Stage); } };
            var model = await inst.InstallAsync(Model);
            using var sha = System.Security.Cryptography.SHA256.Create();
            await using var f = File.OpenRead(model.GgufPath);
            var digest = "sha256-" + Convert.ToHexString(await sha.ComputeHashAsync(f)).ToLowerInvariant();
            Console.WriteLine($"  stages: {string.Join(" > ", stages)}");
            Check(Path.GetFileName(model.GgufPath) == digest, "a damaged partial is detected and replaced; the blob matches its digest");
        }

        // 5. Already installed: a second install downloads nothing.
        {
            bool downloaded = false;
            var inst = new OllamaModelInstaller(http, root) { Progress = p => { if (p.Stage == "layer") downloaded = true; } };
            await inst.InstallAsync(Model);
            Check(!downloaded, "installing an installed model downloads nothing");
        }

        Console.WriteLine($"RESULTS: {failures} failed");
        Directory.Delete(root, recursive: true);
        return failures;
    }
}
