using ILGPU.Runtime;
using SpawnDev.ILGPU;
using SpawnDev.AI.Server;
using SpawnDev.ILGPU.ML;
using SpawnDev.ILGPU.ML.Hub;
using SpawnDev.ILGPU.ML.Pipelines;
using SpawnDev.SpawnJS.JSObjects;

namespace SpawnDev.AI.Demo.Tests;

/// <summary>
/// AiFaceEngine in the browser, on WebGPU, on real pictures (public-domain portraits, <c>test-data/faces/PROVENANCE.md</c>). The desktop paths (managed bytes, a device
/// view) are gated by ServerHost <c>face-test</c> on the CPU; this is the JS typed-array path, which exists only here. Heavy: the two models are about 38 MB.
/// </summary>
public sealed class AiFaceEngineTests
{
    private readonly HttpClient _http;
    private readonly IModelSource _models;

    public AiFaceEngineTests(HttpClient http, IModelSource models) { _http = http; _models = models; }

    /// <summary>
    /// A picture handed over as a JS typed array (uploaded to the device once, never through the .NET heap) gives the same face and the same embedding as the managed path,
    /// and recognition separates two people across photographs eight years apart. Fails if the typed-array upload, the pixel order or the shared frame buffer is wrong.
    /// </summary>
    [AiTest(Heavy = true, Timeout = 600_000)]
    public async Task TypedArrayPictures_GiveTheSameFacesAsManagedOnes_AndRecognitionWorks()
    {
        var builder = MLContext.Create();
        await builder.AllAcceleratorsAsync();
        using var context = builder.ToContext();
        using var accelerator = await context.CreatePreferredAcceleratorAsync() ?? throw new Exception("no WebGPU accelerator");
        if (accelerator.AcceleratorType != AcceleratorType.WebGPU) throw new Exception($"expected WebGPU, got {accelerator.AcceleratorType}");
        using var engine = new AiFaceEngine(_models, accelerator);
        float[]? first = null;
        foreach (var name in new[] { "trump_2017", "trump_2025", "biden_2013" })
        {
            // The fixture comes through managed bytes because the test compares against the managed path; the engine's typed-array path never sees them.
            var bytes = await _http.GetByteArrayAsync($"test-data/faces/{name}.bgra");
            int w = 360, h = bytes.Length / (360 * 4);
            using var js = new Uint8Array(bytes);
            var viaJs = await engine.LookAsync(js, w, h, PixelOrder.Bgra, observe: false);
            var viaManaged = await engine.LookAsync(bytes, w, h, observe: false);
            if (viaJs.Count != 1 || viaManaged.Count != 1) throw new Exception($"{name}: {viaJs.Count} faces via JS, {viaManaged.Count} via managed; expected 1");
            var sim = SpeakerRegistry.Cosine(viaJs[0].Embedding!, viaManaged[0].Embedding!);
            Console.WriteLine($"[faces] {name}: typed array vs managed cosine {sim:F6}");
            if (sim < 0.9999f) throw new Exception($"{name}: the typed-array path gave a different embedding (cosine {sim:F6})");
            if (name == "trump_2017") { first = viaJs[0].Embedding; engine.Enroll("Ada", first!); }
            else
            {
                var m = viaJs[0].Match!;
                Console.WriteLine($"[faces] {name}: {m.Name ?? "nobody"} at {m.Score:F3}");
                if (name == "trump_2025" && !(m.Known && m.Name == "Ada")) throw new Exception($"the same person 8 years later was not recognised ({m.Name ?? "nobody"} {m.Score:F3})");
                if (name == "biden_2013" && m.Known) throw new Exception($"a different person was recognised as {m.Name} ({m.Score:F3})");
            }
        }
    }
}
