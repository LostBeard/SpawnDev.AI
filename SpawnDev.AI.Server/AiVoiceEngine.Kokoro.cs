using ILGPU.Runtime;
using SpawnDev.ILGPU.ML.Pipelines;

namespace SpawnDev.AI.Server;

/// <summary>
/// The BUILT-IN voices: Kokoro-82M, which speaks a named voice instead of cloning one.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 WHY THERE ARE TWO VOICE MODELS. ZipVoice CLONES - it is the only thing here that can speak in a
/// voice the user recorded - and it is <b>3.6x SLOWER than realtime</b> (MEASURED: 73.0 s of compute for
/// 20.4 s of audio; its flow decoder is 8,621 nodes and runs four times per utterance). That is the pause
/// between spoken chunks, and no amount of chunking or pipelining can close a gap that is the renderer.
/// Kokoro renders the same sentence <b>faster than realtime in a browser</b> (MEASURED on WebGPU, real
/// RTX 4070: 1.84 s for 2.27 s of audio, RTF 0.81x) because it is 1,885 nodes in ONE pass.
/// </para>
/// <para>
/// So: Kokoro is the default and handles every ordinary reply; ZipVoice stays for the opt-in case of
/// speaking in a voice somebody actually recorded. They are alternatives, not a fallback chain - a
/// built-in voice never silently becomes a clone, and a clone never silently becomes a built-in.
/// </para>
/// <para>
/// ⚠️ They COEXIST rather than evicting each other: dropping one to load the other makes a user who
/// switches voices pay a full model load per switch, and ZipVoice only loads at all if a cloned voice is
/// picked, which most sessions never do. The pair is registered with GpuResidency as one "voice" kind
/// sized for BOTH (see ServerHost/Program.cs) - so the budget still sees what is actually held.
/// </para>
/// </remarks>
public sealed partial class AiVoiceEngine
{
    /// <summary>Marks a voice id as a built-in Kokoro voice rather than a clone.</summary>
    /// <remarks>
    /// The prefix shares a namespace with prepared clones deliberately: one id space means the demo, the
    /// router and the wire all keep taking a single <c>voice_id</c>, and the engine decides what that id
    /// means. Nothing above this class had to learn a second concept.
    /// </remarks>
    public const string KokoroPrefix = "kokoro:";

    /// <summary>Friendly name reported back to callers for a built-in voice.</summary>
    public string KokoroModelName { get; set; } = "kokoro-82m";

    /// <summary>The voice ids this engine can speak without any preparation.</summary>
    public static IReadOnlyList<string> KokoroVoiceIds { get; } =
        KokoroVoicePack.EnglishVoiceNames.Select(n => KokoroPrefix + n).ToArray();

    /// <summary>The default built-in voice - what an unset voice means.</summary>
    public static string DefaultKokoroVoiceId { get; } = KokoroPrefix + KokoroVoicePack.DefaultVoiceName;

    /// <summary>Whether this id names a built-in voice.</summary>
    public static bool IsKokoroVoice(string? voiceId)
        => !string.IsNullOrEmpty(voiceId) && voiceId.StartsWith(KokoroPrefix, StringComparison.Ordinal)
           && KokoroVoicePack.EnglishVoiceNames.Contains(voiceId[KokoroPrefix.Length..]);

    /// <summary>The voice name inside a built-in id, e.g. <c>kokoro:af_heart</c> -> <c>af_heart</c>.</summary>
    public static string KokoroVoiceName(string voiceId)
        => voiceId.StartsWith(KokoroPrefix, StringComparison.Ordinal)
            ? voiceId[KokoroPrefix.Length..] : voiceId;

    /// <summary>The text-to-phoneme front end, built ONCE.</summary>
    /// <remarks>
    /// 🔴 NOT per call. <c>EmbeddedData.CreatePhonemizer()</c> gunzips and parses an embedded CMUdict AND
    /// a letter-to-sound table every time it is called - and a spoken reply is chunked per sentence, so
    /// "once per utterance" is really once per sentence, on the WASM heap, in front of the user. It is
    /// immutable once built and holds no device state, so one instance serves every call.
    /// </remarks>
    private static readonly Lazy<SpawnDev.Phonemizer.EnglishPhonemizer> _phonemizer =
        new(SpawnDev.Phonemizer.EmbeddedData.CreatePhonemizer, isThreadSafe: true);

    private KokoroPipeline? _kokoro;
    private readonly SemaphoreSlim _kokoroGate = new(1, 1);
    private readonly Dictionary<string, KokoroVoicePack> _kokoroPacks = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether the built-in voice model is resident.</summary>
    public bool IsKokoroLoaded => _kokoro != null;

    /// <summary>Speak in a built-in voice. Nothing to prepare - the voice is a name, not a recording.</summary>
    private async Task<AiSpeech> SpeakKokoroAsync(string text, string voiceId,
        int? maxSpokenCharacters, CancellationToken ct)
    {
        var name = KokoroVoiceName(voiceId);
        await EnsureKokoroLoadedAsync(ct).ConfigureAwait(false);
        var pack = await LoadKokoroPackAsync(name, ct).ConfigureAwait(false);

        text = TrimToSpeakableLength(text, maxSpokenCharacters);

        // ⚠️ The phonemizer runs OUTSIDE the inference gate. It is pure CPU string work and holding the
        // gate through it would serialise two callers on something that does not touch the device.
        var phonemes = _phonemizer.Value.ToSymbols(text);

        // One synthesis at a time, for the same reason the clone path does it: the pipelines share an
        // accelerator, a buffer pool and graph-capture state.
        await _inferGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var started = DateTime.UtcNow;
            var audio = await _kokoro!.SpeakAsync(phonemes, pack, ct: ct).ConfigureAwait(false);
            var ms = (DateTime.UtcNow - started).TotalMilliseconds;
            if (VerboseLogging)
                Console.WriteLine($"[voice] kokoro '{name}': {audio.Samples.Length} samples "
                    + $"({audio.Seconds:F2}s) in {ms:F0} ms, RTF {ms / 1000.0 / Math.Max(audio.Seconds, 1e-6):F2}x, "
                    + $"{audio.Tokens} tokens, {audio.DroppedPhonemes} dropped");
            return new AiSpeech(audio.Samples, audio.SampleRate, KokoroModelName, ms)
            {
                SpokenText = text,
            };
        }
        finally { _inferGate.Release(); }
    }

    /// <summary>Load the built-in voice model, once.</summary>
    /// <remarks>
    /// ⚠️ Through <c>IModelSource.OpenAsync</c> as a STREAM, never <c>FetchBytesAsync</c>. The fp32 export
    /// is ~326 MB and this runs in a browser worker: a byte[] of it lands on the .NET WASM managed heap,
    /// which is small, and the crossing IS the cost of filling it. The stream goes cache -> GPU with no
    /// managed copy of the weights in between.
    /// </remarks>
    private async Task EnsureKokoroLoadedAsync(CancellationToken ct)
    {
        if (_kokoro != null) return;
        await _kokoroGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_kokoro != null) return;
            if (EvictOtherKind != null) await EvictOtherKind().ConfigureAwait(false);

            OnLoadProgress?.Invoke("voice-model", 10);
            var stream = await _source.OpenAsync(KokoroVoicePack.VoiceRepoId, "onnx/model.onnx", ct)
                .ConfigureAwait(false);
            await using (stream)
            {
                if (stream.Length <= 0)
                    throw new Exception(
                        $"the hub returned a zero-length stream for {KokoroVoicePack.VoiceRepoId}/onnx/model.onnx");
                OnLoadProgress?.Invoke("voice-model", 60);
                _kokoro = await KokoroPipeline.CreateFromStreamAsync(_accelerator, stream, ct)
                    .ConfigureAwait(false);
            }
            OnLoadProgress?.Invoke("ready", 100);
            Console.WriteLine($"[AiVoiceEngine] {KokoroModelName} ready on {_accelerator.AcceleratorType} "
                + $"({_kokoro.Session.NodeCount} nodes)");
        }
        finally
        {
            // The end-of-load marker, in the finally for the same reason the clone path puts it there: a
            // load that threw must not leave a progress bar running forever.
            OnLoadProgress?.Invoke("idle", 100);
            _kokoroGate.Release();
        }
    }

    /// <summary>Fetch and cache one voice's style table.</summary>
    /// <remarks>
    /// ⚠️ <c>FetchBytesAsync</c> is right HERE and wrong for the model: a voice pack is 522,240 bytes -
    /// a 510x256 table - which is the KB-scale case that path exists for. It is also cached per name
    /// rather than per call: the table is identical for every line a voice speaks.
    /// </remarks>
    private async Task<KokoroVoicePack> LoadKokoroPackAsync(string name, CancellationToken ct)
    {
        if (_kokoroPacks.TryGetValue(name, out var cached)) return cached;

        // ⚠️ Under the load gate. The desktop host serves real concurrent requests, so two first-time
        // calls for different voices would otherwise mutate this Dictionary at the same time - and a
        // torn Dictionary is a corrupted process, not a slow one. Re-checked inside, because the voice
        // may have been fetched while this call waited. No deadlock: EnsureKokoroLoadedAsync has already
        // taken and released the same gate before this runs.
        await _kokoroGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_kokoroPacks.TryGetValue(name, out cached)) return cached;
            OnLoadProgress?.Invoke("voice", 80);
            var bytes = await _source
                .FetchBytesAsync(KokoroVoicePack.VoiceRepoId, KokoroVoicePack.VoicePath(name), ct)
                .ConfigureAwait(false);
            // FromBytes checks the length, which is what catches an error page or a truncated transfer
            // saved as a .bin - the realistic failure, and one that would otherwise be a wrong voice
            // rather than an error.
            var pack = KokoroVoicePack.FromBytes(name, bytes);
            _kokoroPacks[name] = pack;
            return pack;
        }
        finally
        {
            OnLoadProgress?.Invoke("idle", 100);
            _kokoroGate.Release();
        }
    }

    /// <summary>Free the model but keep the engine usable - what an EVICT means.</summary>
    /// <remarks>
    /// ⚠️ The voice packs survive. They are 522 KB each, they came over the network, and they are
    /// identical for every line a voice speaks - throwing them away to reclaim half a megabyte would buy
    /// nothing and cost a round trip on the next reply.
    /// </remarks>
    private void UnloadKokoro()
    {
        _kokoro?.Dispose();
        _kokoro = null;
    }

    private void DisposeKokoro()
    {
        UnloadKokoro();
        _kokoroPacks.Clear();
        _kokoroGate.Dispose();
    }
}
