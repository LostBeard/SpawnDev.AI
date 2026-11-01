using System.Runtime.InteropServices;
using ILGPU;
using ILGPU.Runtime;
using SpawnDev.AI;
using SpawnDev.AI.Server;
using SpawnDev.ILGPU.ML;
using SpawnDev.ILGPU.ML.Hub;
using SpawnDev.ILGPU.ML.Pipelines;

/// <summary>
/// `face-test [fixtureDir]`: AiFaceEngine on real pictures (public-domain portraits, SpawnDev.AI.Demo/wwwroot/test-data/faces/PROVENANCE.md), on the CPU (Velocity)
/// accelerator so it never touches a GPU someone else is using. Checks that the managed path and the device-view path give the SAME faces and embeddings, that
/// LookAsync's upload-once path matches detect + embed, and that recognition still separates two people across photographs years apart. Exit code = failures.
/// </summary>
internal static class FaceChecks
{
    public static async Task<int> RunAsync(string[] args)
    {
        var dir = args.Length > 1 ? args[1] : FindFixtures();
        int failures = 0;
        void Check(bool ok, string what) { Console.WriteLine($"TEST: {what}|{(ok ? "PASS" : "FAIL")}"); if (!ok) failures++; }
        (byte[] Bgra, int W, int H) Picture(string name)
        {
            var bytes = File.ReadAllBytes(Path.Combine(dir, name + ".bgra"));
            return (bytes, 360, bytes.Length / (360 * 4));
        }

        using var context = MLContext.Create().ToContext();
        using var accelerator = MLContext.CreateCpuAccelerator(context);
        Console.WriteLine($"accelerator: {accelerator.Name} ({accelerator.AcceleratorType})");
        using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        using var engine = new AiFaceEngine(new HttpClientModelSource(http, FileModelStore.Default()), accelerator);

        foreach (var name in new[] { "trump_2017", "trump_2025", "biden_2013" })
        {
            var (px, w, h) = Picture(name);
            var managed = await engine.DetectAsync(px, w, h);
            using var dev = accelerator.Allocate1D<int>((long)w * h);
            var words = MemoryMarshal.Cast<byte, int>(px.AsSpan());
            dev.View.CopyFromCPU(ref MemoryMarshal.GetReference(words), words.Length);
            var viewed = await engine.DetectAsync(dev.View, w, h, PixelOrder.Bgra);
            bool sameFaces = managed.Count == viewed.Count && managed.Zip(viewed).All(p =>
                Math.Abs(p.First.X - p.Second.X) < 1e-3 && Math.Abs(p.First.Y - p.Second.Y) < 1e-3 && Math.Abs(p.First.Width - p.Second.Width) < 1e-3
                && Math.Abs(p.First.Score - p.Second.Score) < 1e-5);
            Check(managed.Count == 1 && sameFaces, $"{name}: one face, and the device-view path finds the same box ({managed.Count} / {viewed.Count})");
            if (managed.Count != 1) continue;
            var eManaged = await engine.EmbedAsync(px, w, h, managed[0]);
            var eViewed = await engine.EmbedAsync(dev.View, w, h, managed[0], PixelOrder.Bgra);
            var sim = SpeakerRegistry.Cosine(eManaged, eViewed);
            Check(eManaged.Length == 128 && sim > 0.9999f, $"{name}: device-view embedding equals the managed one (cosine {sim:F6})");

            // An RGBA copy of the same picture through PixelOrder.Rgba is the same face.
            var rgba = (byte[])px.Clone();
            for (int i = 0; i < rgba.Length; i += 4) (rgba[i], rgba[i + 2]) = (rgba[i + 2], rgba[i]);
            var wRgba = MemoryMarshal.Cast<byte, int>(rgba.AsSpan());
            dev.View.CopyFromCPU(ref MemoryMarshal.GetReference(wRgba), wRgba.Length);
            var eRgba = await engine.EmbedAsync(dev.View, w, h, managed[0], PixelOrder.Rgba);
            var simRgba = SpeakerRegistry.Cosine(eManaged, eRgba);
            Check(simRgba > 0.9999f, $"{name}: the same picture as RGBA with PixelOrder.Rgba gives the same embedding (cosine {simRgba:F6})");

            // LookAsync uploads once and reuses the view: same embedding as detect + embed.
            var looked = await engine.LookAsync(px, w, h, observe: false);
            var simLook = looked.Count == 1 && looked[0].Embedding != null ? SpeakerRegistry.Cosine(eManaged, looked[0].Embedding!) : -1f;
            Check(simLook > 0.9999f, $"{name}: LookAsync (one upload) matches detect + embed (cosine {simLook:F6})");
        }

        // Recognition still works end to end: introduce one photo, recognise a photo of the same person 8 years later, not someone else.
        {
            var (a, aw, ah) = Picture("trump_2017");
            var first = await engine.LookAsync(a, aw, ah);
            engine.Enroll("Ada", first[0].Embedding!);
            var (b, bw, bh) = Picture("trump_2025");
            var again = (await engine.LookAsync(b, bw, bh, observe: false))[0].Match!;
            Console.WriteLine($"  same person, other photo: {again.Name ?? "nobody"} {again.Score:F3}");
            Check(again.Known && again.Name == "Ada", "the same person in a photograph 8 years later is recognised");
            var (c, cw, ch) = Picture("biden_2013");
            var other = (await engine.LookAsync(c, cw, ch, observe: false))[0].Match!;
            Console.WriteLine($"  another person: {other.Name ?? "nobody"} {other.Score:F3}");
            Check(!other.Known, "a different person is not recognised as Ada");
        }

        Console.WriteLine($"RESULTS: {failures} failed");
        return failures;
    }

    private static string FindFixtures()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            var p = Path.Combine(d.FullName, "SpawnDev.AI.Demo", "wwwroot", "test-data", "faces");
            if (Directory.Exists(p)) return p;
        }
        throw new DirectoryNotFoundException("SpawnDev.AI.Demo/wwwroot/test-data/faces not found above " + AppContext.BaseDirectory + "; pass the folder");
    }
}
