using ILGPU.Runtime;
using SpawnDev.ILGPU.ML;
using SpawnDev.ILGPU.ML.Hub;
using SpawnDev.ILGPU.ML.Pipelines;

namespace SpawnDev.AI.Server;

/// <summary>One face seen in a picture, and who it looks like.</summary>
/// <param name="Face">Where it is (box and five landmarks, in the picture's pixels) and how sure the detector is.</param>
/// <param name="Match">Who it looks like among the enrolled faces (before any guest was filed); null when the face was only detected.</param>
/// <param name="Guest">The provisional guest the face was filed under, when it was nobody enrolled; null otherwise.</param>
/// <param name="Embedding">The face embedding (128 floats), so a caller can enrol it ("this is Nikki") without computing it again; null when only detected.</param>
public sealed record AiFaceResult(FaceDetection Face, SpeakerMatch? Match, SpeakerProfile? Guest, float[]? Embedding)
{
    /// <summary>The name to use for this face: the matched person, the guest, or null.</summary>
    public string? Name => Match is { Known: true } ? Match.Name : Guest?.Name;
}

/// <summary>
/// Face detection and recognition for the AI server: YuNet finds faces, SFace gives each a 128-number embedding, and a registry of the faces that have been introduced
/// says whose it is. The same shape as <see cref="AiSpeakerEngine"/> for voices, on the shared accelerator.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Registry"/> is a <see cref="SpeakerRegistry"/>: it only needs unit-length embeddings of any length, so one tested piece (enrol, observe, margin rule, guests, expiry, JSON)
/// serves voices and faces. The thresholds are a face's own: OpenCV's recommended cosine for "same person" is 0.363; MEASURED on six public-domain portraits (two people photographed
/// years apart) the same person scored 0.74 and 0.61 and different people at most 0.26. A camera across a room is harder than a portrait: calibrate on the household.
/// </para>
/// <para>
/// Face embeddings are personal data. They are not sent anywhere by this engine, and the registry can forget one person (<see cref="SpeakerRegistry.Forget"/>) or all of them. Strangers
/// nobody names can be forgotten automatically (<see cref="SpeakerRegistry.GuestLifetime"/>).
/// </para>
/// </remarks>
public sealed class AiFaceEngine : IDisposable
{
    private readonly IModelSource _source;
    private readonly Accelerator _accelerator;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly SemaphoreSlim _inferGate = new(1, 1);
    private YuNetFaceDetector? _detector;
    private SFaceRecognizer? _recognizer;

    /// <summary>HuggingFace repo of the face detector (MIT; YuNet).</summary>
    public string DetectorRepo { get; set; } = "opencv/face_detection_yunet";

    /// <summary>File in <see cref="DetectorRepo"/> (fixed 640 x 640 input).</summary>
    public string DetectorFile { get; set; } = "face_detection_yunet_2023mar.onnx";

    /// <summary>HuggingFace repo of the face recogniser (Apache-2.0; SFace).</summary>
    public string RecognizerRepo { get; set; } = "opencv/face_recognition_sface";

    /// <summary>File in <see cref="RecognizerRepo"/>.</summary>
    public string RecognizerFile { get; set; } = "face_recognition_sface_2021dec.onnx";

    /// <summary>The faces that have been introduced. Persist it with <see cref="SpeakerRegistry.ToJson"/>.</summary>
    public SpeakerRegistry Registry { get; set; } = NewRegistry();

    /// <summary>A registry with a face's thresholds: cosine 0.363 (OpenCV's own) to call a face someone, a 0.05 lead over the runner-up, a few embeddings per person (poses, light), and "Visitor N" for strangers (so they never share a name with a voice's "Guest N").</summary>
    public static SpeakerRegistry NewRegistry() => new() { AcceptThreshold = SFaceRecognizer.OpenCvCosineThreshold, MinMargin = 0.05f, MaxEmbeddingsPerProfile = 16, GuestPrefix = "Visitor" };

    /// <summary>Faces with a detector confidence below this are ignored (OpenCV's demos use 0.7 to 0.9).</summary>
    public float MinDetectionScore { get; set; } = 0.8f;

    /// <summary>Faces whose box is narrower than this many pixels are detected but not recognised: a face of 20 pixels has no identity to read.</summary>
    public int MinRecognizableWidth { get; set; } = 48;

    /// <summary>Reports load progress as (stage, percent).</summary>
    public Action<string, int>? OnLoadProgress { get; set; }

    /// <summary>Whether the face models are resident.</summary>
    public bool IsLoaded => _recognizer != null;

    public AiFaceEngine(IModelSource source, Accelerator accelerator)
    {
        _source = source;
        _accelerator = accelerator;
    }

    /// <summary>Loads both models and runs each once, so the first real picture does not pay for kernel compilation.</summary>
    public async Task EnsureReadyAsync(CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct).ConfigureAwait(false);
        await _inferGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var blank = new byte[320 * 240 * 4];
            await _detector!.DetectAsync(blank, 320, 240).ConfigureAwait(false);
            await _recognizer!.EmbedAlignedAsync(new byte[SFaceRecognizer.AlignedSize * SFaceRecognizer.AlignedSize * 3]).ConfigureAwait(false);
            Console.WriteLine($"[AiFaceEngine] warm detection + embedding: {clock.Elapsed.TotalSeconds:F1}s (kernel compilation)");
        }
        finally { _inferGate.Release(); }
    }

    /// <summary>The faces in a 32-bit B, G, R, A picture (top row first, no padding), best first. Changes nothing.</summary>
    public async Task<IReadOnlyList<FaceDetection>> DetectAsync(byte[] bgra, int width, int height, CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct).ConfigureAwait(false);
        await _inferGate.WaitAsync(ct).ConfigureAwait(false);
        try { return await _detector!.DetectAsync(bgra, width, height, MinDetectionScore).ConfigureAwait(false); }
        finally { _inferGate.Release(); }
    }

    /// <summary>The embedding of one detected face.</summary>
    public async Task<float[]> EmbedAsync(byte[] bgra, int width, int height, FaceDetection face, CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct).ConfigureAwait(false);
        await _inferGate.WaitAsync(ct).ConfigureAwait(false);
        try { return await _recognizer!.EmbedAsync(bgra, width, height, face).ConfigureAwait(false); }
        finally { _inferGate.Release(); }
    }

    /// <summary>
    /// Looks at a picture and files what it sees: each face large enough to read is named if it is someone enrolled, otherwise filed under (or joined to) a provisional guest.
    /// </summary>
    /// <param name="observe">False: only identify, change nothing in the registry.</param>
    public async Task<IReadOnlyList<AiFaceResult>> LookAsync(byte[] bgra, int width, int height, bool observe = true, CancellationToken ct = default)
    {
        var faces = await DetectAsync(bgra, width, height, ct).ConfigureAwait(false);
        var results = new List<AiFaceResult>();
        foreach (var face in faces)
        {
            if (face.Width < MinRecognizableWidth) { results.Add(new AiFaceResult(face, null, null, null)); continue; }
            var embedding = await EmbedAsync(bgra, width, height, face, ct).ConfigureAwait(false);
            if (!observe) { results.Add(new AiFaceResult(face, Registry.Identify(embedding), null, embedding)); continue; }
            var match = Registry.Observe(embedding, 1, out var guest);
            results.Add(new AiFaceResult(face, match, guest, embedding));
        }
        return results;
    }

    /// <summary>Adds this embedding to a person's face (creating them if new). "Vertex, this is Nikki."</summary>
    public SpeakerProfile Enroll(string name, float[] embedding) => Registry.Enroll(name, embedding, 1);

    private async Task EnsureLoadedAsync(CancellationToken ct)
    {
        if (_recognizer != null) return;
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_recognizer != null) return;
            OnLoadProgress?.Invoke("face detector", 10);
            InferenceSession detector, recognizer;
            await using (var stream = await _source.OpenAsync(DetectorRepo, DetectorFile, ct).ConfigureAwait(false))
                detector = await InferenceSession.CreateFromOnnxStreamAsync(_accelerator, stream, inputShapes: YuNetFaceDetector.InputShapes(), ct: ct).ConfigureAwait(false);
            OnLoadProgress?.Invoke("face recogniser", 50);
            await using (var stream = await _source.OpenAsync(RecognizerRepo, RecognizerFile, ct).ConfigureAwait(false))
                recognizer = await InferenceSession.CreateFromOnnxStreamAsync(_accelerator, stream,
                    inputShapes: new Dictionary<string, int[]> { ["data"] = new[] { 1, 3, SFaceRecognizer.AlignedSize, SFaceRecognizer.AlignedSize } }, ct: ct).ConfigureAwait(false);
            _detector = new YuNetFaceDetector(detector, _accelerator);
            _recognizer = new SFaceRecognizer(recognizer, _accelerator);
            OnLoadProgress?.Invoke("ready", 100);
            Console.WriteLine($"[AiFaceEngine] YuNet + SFace ready on {_accelerator.AcceleratorType}");
        }
        finally
        {
            OnLoadProgress?.Invoke("idle", 100);
            _gate.Release();
        }
    }

    /// <summary>Release the resident models.</summary>
    public Task EvictAsync()
    {
        _detector?.Dispose(); _recognizer?.Dispose();
        _detector = null; _recognizer = null;
        return Task.CompletedTask;
    }

    /// <summary>Disposes the resident models. Never disposes the accelerator - the app owns it.</summary>
    public void Dispose()
    {
        _detector?.Dispose(); _recognizer?.Dispose();
        _detector = null; _recognizer = null;
        _gate.Dispose();
        _inferGate.Dispose();
    }
}
