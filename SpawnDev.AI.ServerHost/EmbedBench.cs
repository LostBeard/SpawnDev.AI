using ILGPU.Runtime;
using SpawnDev.AI;
using SpawnDev.AI.Server;
using SpawnDev.ILGPU.ML;
using SpawnDev.ILGPU.ML.Hub;

/// <summary>
/// `embed-bench [cpu|cuda|opencl]`: AiMemoryEngine embedding time per text at MaxTokens 16 / 32 / 64 / 128 (each a fresh engine, so the
/// session is built for that length), and whether the embeddings agree with the 128-token ones. Every text is padded to MaxTokens, and a
/// memory is ~15 tokens, so most of a 128-token embedding is padding: this measures how much (Seven 2026-11-02: 2.4 s per embedding on the
/// CPU accelerator on Aubs' PC; this PC measured 355 ms on 2026-11-01).
/// </summary>
internal static class EmbedBench
{
    static readonly string[] Texts =
    {
        "We found diamonds at -312 11 845 in a ravine below the birch forest.",
        "Aubs's favourite colour is purple now.",
        "Scaffolding placed over deep water sank twice; place blocks from the shore instead.",
        "Todd promised Aubs a pet wolf on Saturday.",
        "Where did we find the diamonds?",
        "What did we build last Tuesday?",
    };

    public static async Task<int> RunAsync(string[] args)
    {
        var which = args.Length > 1 ? args[1] : "cpu";
        int failures = 0;
        using var context = MLContext.Create().ToContext();
        using var acc = which switch
        {
            "cuda" => context.Devices.First(d => d.AcceleratorType == AcceleratorType.Cuda).CreateAccelerator(context),
            "opencl" => context.Devices.First(d => d.AcceleratorType == AcceleratorType.OpenCL).CreateAccelerator(context),
            _ => MLContext.CreateCpuAccelerator(context),
        };
        Console.WriteLine($"accelerator: {acc.Name} ({acc.AcceleratorType}), {Environment.ProcessorCount} logical CPUs");
        using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        var models = new HttpClientModelSource(http, FileModelStore.Default());

        Dictionary<int, float[][]> vectors = new();
        foreach (var maxTokens in new[] { 128, 64, 32, 16 })
        {
            var store = new AiMemoryStore(ModelHub.KnownModels.AllMiniLmL6V2, 384);
            using var engine = new AiMemoryEngine(models, acc, store) { MaxTokens = maxTokens };
            var load = System.Diagnostics.Stopwatch.StartNew();
            await engine.EnsureReadyAsync();
            var first = await engine.EmbedAsync(Texts[0]);
            double loadMs = load.Elapsed.TotalMilliseconds;
            var times = new List<double>();
            var vs = new float[Texts.Length][];
            for (int r = 0; r < 3; r++)
                for (int i = 0; i < Texts.Length; i++)
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    vs[i] = await engine.EmbedAsync(Texts[i]);
                    times.Add(sw.Elapsed.TotalMilliseconds);
                }
            times.Sort();
            vectors[maxTokens] = vs;
            Console.WriteLine($"BENCH: embed {acc.AcceleratorType} MaxTokens={maxTokens}: p50 {times[times.Count / 2]:F1} ms (min {times[0]:F1}, max {times[^1]:F1}), load+first {loadMs:F0} ms");
        }

        // A text that fits the shorter window must embed the same at every length (padding is masked); one that does not fit is truncated.
        foreach (var maxTokens in new[] { 64, 32, 16 })
        {
            double worst = 1;
            for (int i = 0; i < Texts.Length; i++)
                worst = Math.Min(worst, Cosine(vectors[128][i], vectors[maxTokens][i]));
            Console.WriteLine($"  MaxTokens={maxTokens} vs 128: lowest cosine over {Texts.Length} texts {worst:F5}");
            if (maxTokens >= 32 && worst < 0.999)
            {
                Console.WriteLine($"TEST: embeddings at MaxTokens={maxTokens} match 128 for texts that fit|FAIL");
                failures++;
            }
        }
        Console.WriteLine($"RESULTS: {failures} failed");
        return failures;
    }

    static double Cosine(float[] a, float[] b)
    {
        double d = 0, na = 0, nb = 0;
        for (int i = 0; i < a.Length; i++) { d += a[i] * b[i]; na += a[i] * a[i]; nb += b[i] * b[i]; }
        return d / Math.Sqrt(na * nb);
    }
}
