using ILGPU.Runtime;
using SpawnDev.AI.Server;
using SpawnDev.ILGPU.ML;
using SpawnDev.ILGPU.ML.Hub;

/// <summary>
/// `speaker-mem [cpu|cuda|opencl]`: what AiSpeakerEngine (ReDimNet2-B6, a 51 MB model) keeps on the device. Seven measured +2,089 MiB on an
/// RTX 2060 for loading it and embedding once (2026-11-02). Reports the working pool after the warm-up, what ReleaseWorkingMemoryAsync frees,
/// the embedding time warm vs right after a release (the cost of giving the pool back), and on CUDA the device's free memory at each step.
/// </summary>
internal static class SpeakerMemProbe
{
    public static async Task<int> RunAsync(string[] args)
    {
        var which = args.Length > 1 ? args[1] : "cpu";
        using var context = MLContext.Create().ToContext();
        using var acc = which switch
        {
            "cuda" => context.Devices.First(d => d.AcceleratorType == AcceleratorType.Cuda).CreateAccelerator(context),
            "opencl" => context.Devices.First(d => d.AcceleratorType == AcceleratorType.OpenCL).CreateAccelerator(context),
            _ => MLContext.CreateCpuAccelerator(context),
        };
        string Free() => acc.TryGetFreeMemory(out long f) ? $", device free {f / 1048576:N0} MiB" : "";
        Console.WriteLine($"accelerator: {acc.Name} ({acc.AcceleratorType}){Free()}");
        using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        var models = new HttpClientModelSource(http, FileModelStore.Default());
        using var engine = new AiSpeakerEngine(models, acc);
        await engine.EnsureReadyAsync();
        const double MiB = 1048576.0;
        Console.WriteLine($"after load + warm-up: working pool {engine.WorkingMemoryBytes / MiB:N0} MiB{Free()}");

        var rnd = new Random(3);
        var clip = new float[16000 * 3];
        for (int i = 0; i < clip.Length; i++) clip[i] = (float)Math.Sin(i * 0.05) * 0.3f + (float)(rnd.NextDouble() - 0.5) * 0.05f;
        double Time(Func<Task> f) { var sw = System.Diagnostics.Stopwatch.StartNew(); f().GetAwaiter().GetResult(); return sw.Elapsed.TotalMilliseconds; }
        var warm = new List<double>();
        for (int r = 0; r < 5; r++) warm.Add(Time(() => engine.EmbedAsync(clip, 16000)));
        warm.Sort();
        Console.WriteLine($"warm embedding (3 s clip): median {warm[2]:F1} ms; working pool {engine.WorkingMemoryBytes / MiB:N0} MiB{Free()}");

        long freed = await engine.ReleaseWorkingMemoryAsync();
        Console.WriteLine($"ReleaseWorkingMemoryAsync freed {freed / MiB:N0} MiB; working pool {engine.WorkingMemoryBytes / MiB:N0} MiB{Free()}");
        var afterRelease = Time(() => engine.EmbedAsync(clip, 16000));
        var again = Time(() => engine.EmbedAsync(clip, 16000));
        Console.WriteLine($"first embedding after the release: {afterRelease:F1} ms (warm {warm[2]:F1}), the next {again:F1} ms; working pool {engine.WorkingMemoryBytes / MiB:N0} MiB{Free()}");
        Console.WriteLine($"BENCH: speaker-mem {acc.AcceleratorType} pool={engine.WorkingMemoryBytes / MiB:F0}MiB warm={warm[2]:F1}ms afterRelease={afterRelease:F1}ms");
        Console.WriteLine("RESULTS: 0 failed");
        return 0;
    }
}
