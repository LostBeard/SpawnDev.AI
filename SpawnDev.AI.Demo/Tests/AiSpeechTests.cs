using SpawnDev.AI.Server;
using SpawnDev.ILGPU.ML.Pipelines;

namespace SpawnDev.AI.Demo.Tests;

/// <summary>
/// Speech-to-text through the in-browser worker: real audio in, a real transcript out.
/// </summary>
/// <remarks>
/// The fixture is a 4.0 s / 16 kHz / mono LibriVox excerpt whose transcript is KNOWN rather than
/// transcribed - "All LibriVox recordings are in the public domain", the standardized LibriVox preamble.
/// That is the whole reason it was chosen upstream (see <c>wwwroot/test-audio/PROVENANCE.md</c>): a
/// reference clip whose text came from a transcription can only ever prove agreement with another model,
/// not correctness. Creative Commons Public Domain Mark 1.0.
/// <para>
/// ⚠️ The assertion is a WORD ERROR RATE against that known text, not "the transcript is non-empty".
/// Whisper-tiny will not be perfect and should not be required to be, but a non-emptiness check would pass
/// on any garbage the decoder produced - which is exactly how the chat tests in this suite were vacuous
/// until 2026-08-30.
/// </para>
/// </remarks>
public sealed class AiSpeechTests
{
    private readonly AiWorkerClient _client;
    private readonly HttpClient _http;

    /// <summary>What the fixture actually says, verbatim.</summary>
    private const string KnownTranscript = "All LibriVox recordings are in the public domain.";

    private const string FixtureUrl = "test-audio/librivox-public-domain.wav";

    /// <summary>New instance.</summary>
    /// <param name="client">The window-side client the UI itself uses.</param>
    /// <param name="http">App-base-addressed client, for fetching the fixture.</param>
    public AiSpeechTests(AiWorkerClient client, HttpClient http)
    {
        _client = client;
        _http = http;
    }

    /// <summary>
    /// The fixture decodes to plausible audio before any model is involved.
    /// </summary>
    /// <remarks>
    /// Cheap and model-free on purpose: if the WAV parse is wrong, every transcription test below fails for
    /// a reason that has nothing to do with speech recognition, and this test says so in milliseconds.
    /// </remarks>
    [AiTest(Timeout = 60_000)]
    public async Task FixtureDecodesToPlausiblePcm()
    {
        var (samples, sampleRate) = await LoadFixtureAsync();

        if (sampleRate != 16000)
            throw new Exception($"fixture sample rate is {sampleRate}, expected 16000");
        var seconds = samples.Length / (double)sampleRate;
        if (seconds is < 3.0 or > 5.0)
            throw new Exception($"fixture is {seconds:F2}s of audio, expected ~4s ({samples.Length} samples)");

        float peak = 0, sumSq = 0;
        foreach (var v in samples)
        {
            var a = Math.Abs(v);
            if (a > peak) peak = a;
            sumSq += v * v;
            if (!float.IsFinite(v)) throw new Exception("fixture decoded to non-finite samples");
            if (a > 1.0001f) throw new Exception($"fixture sample {v} is outside [-1,1] - wrong PCM scaling");
        }
        var rms = Math.Sqrt(sumSq / samples.Length);
        // Silence and a decode that produced constant zeros are the same shape; require real signal.
        if (peak < 0.05f) throw new Exception($"fixture peak is {peak:F4} - decoded to near-silence");
        if (rms < 0.005) throw new Exception($"fixture RMS is {rms:F5} - decoded to near-silence");

        Console.WriteLine($"[AiSpeechTests] fixture ok: {seconds:F2}s @ {sampleRate}Hz, "
                        + $"peak={peak:F3} rms={rms:F4}");
    }

    /// <summary>
    /// Transcribe the fixture and require the transcript to MATCH its known text.
    /// </summary>
    /// <remarks>
    /// Threshold is a word error rate of 0.34, i.e. at most roughly two of the eight words wrong. Whisper
    /// tiny on a clean 4 s clip should be far better; the allowance is for casing/punctuation drift and the
    /// odd substitution, not for a broken decode. A WER of 1.0 (or an empty transcript) is what a broken
    /// speech path produces, and that fails.
    /// </remarks>
    [AiTest(Heavy = true, Timeout = 900_000)]
    public async Task TranscribesKnownAudio()
    {
        await _client.InitAsync();
        var (samples, sampleRate) = await LoadFixtureAsync();

        var (text, model, ms) = await _client.TranscribeAsync(samples, sampleRate);
        Console.WriteLine($"[AiSpeechTests] {model} in {ms:F0}ms -> '{text}'");

        if (string.IsNullOrWhiteSpace(text))
            throw new Exception("transcription returned EMPTY text for 4s of clear speech");

        var wer = SpokenTextCheck.WordErrorRate(KnownTranscript, text);
        Console.WriteLine($"[AiSpeechTests] WER vs known transcript = {wer:P1}");
        if (wer > 0.34)
            throw new Exception(
                $"transcript does not match the fixture's KNOWN text (WER {wer:P1}). " +
                $"expected ~'{KnownTranscript}' got '{text}'");
    }

    /// <summary>
    /// Transcribing TWICE gives the same transcript - the speech path is deterministic and its cache is
    /// reusable.
    /// </summary>
    /// <remarks>
    /// The second call must not re-load the model, so it also exercises the resident-model path. A differing
    /// second transcript would point at state left behind by the first decode - the same class of defect
    /// that made LFM2's per-step cache answer one prompt two ways.
    /// </remarks>
    [AiTest(Heavy = true, Timeout = 900_000)]
    public async Task RepeatedTranscriptionIsStable()
    {
        await _client.InitAsync();
        var (samples, sampleRate) = await LoadFixtureAsync();

        var (first, _, firstMs) = await _client.TranscribeAsync(samples, sampleRate);
        var (second, _, secondMs) = await _client.TranscribeAsync(samples, sampleRate);

        if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(second))
            throw new Exception($"empty transcript (first='{first}' second='{second}')");
        if (!string.Equals(first.Trim(), second.Trim(), StringComparison.Ordinal))
            throw new Exception(
                $"the same audio transcribed DIFFERENTLY twice - first='{first}' second='{second}'");

        Console.WriteLine($"[AiSpeechTests] stable across two runs ({firstMs:F0}ms then {secondMs:F0}ms, "
                        + $"warm should be faster): '{first}'");
    }

    /// <summary>Fetch and decode the fixture to mono float PCM.</summary>
    private async Task<(float[] Samples, int SampleRate)> LoadFixtureAsync()
    {
        var wav = await _http.GetByteArrayAsync(FixtureUrl);
        return DecodeWav(wav);
    }

    /// <summary>
    /// Decode a PCM WAV to mono float in [-1, 1].
    /// </summary>
    /// <remarks>
    /// Deliberately a plain parser rather than the browser's AudioContext: decoding through Web Audio is
    /// async, needs a user-gesture-free context, and resamples to the context rate - three things that would
    /// make a FAILING transcription ambiguous between "the model is wrong" and "the audio arrived
    /// differently than I think". Supports 8/16/24/32-bit PCM and 32-bit float, and mixes multi-channel down
    /// to mono; throws naming the format rather than returning silence for anything else.
    /// </remarks>
    private static (float[] Samples, int SampleRate) DecodeWav(byte[] wav)
    {
        if (wav.Length < 44) throw new Exception($"WAV is {wav.Length} bytes - too small to hold a header");
        if (System.Text.Encoding.ASCII.GetString(wav, 0, 4) != "RIFF"
            || System.Text.Encoding.ASCII.GetString(wav, 8, 4) != "WAVE")
            throw new Exception("not a RIFF/WAVE file");

        int channels = 0, sampleRate = 0, bits = 0, format = 1;
        int dataOffset = -1, dataLength = 0;

        // Walk the chunks: 'data' is not always at 36, and assuming it is silently mis-decodes any file
        // carrying a LIST/fact chunk first.
        var pos = 12;
        while (pos + 8 <= wav.Length)
        {
            var id = System.Text.Encoding.ASCII.GetString(wav, pos, 4);
            var size = BitConverter.ToInt32(wav, pos + 4);
            var body = pos + 8;
            if (size < 0 || body + size > wav.Length) size = wav.Length - body;

            if (id == "fmt ")
            {
                format = BitConverter.ToUInt16(wav, body);
                channels = BitConverter.ToUInt16(wav, body + 2);
                sampleRate = BitConverter.ToInt32(wav, body + 4);
                bits = BitConverter.ToUInt16(wav, body + 14);
            }
            else if (id == "data")
            {
                dataOffset = body;
                dataLength = size;
            }
            pos = body + size + (size % 2); // chunks are word-aligned
        }

        if (dataOffset < 0) throw new Exception("WAV has no 'data' chunk");
        if (channels <= 0) throw new Exception("WAV has no 'fmt ' chunk");

        var bytesPerSample = bits / 8;
        var frames = dataLength / (bytesPerSample * channels);
        var mono = new float[frames];

        for (var f = 0; f < frames; f++)
        {
            double sum = 0;
            for (var c = 0; c < channels; c++)
            {
                var o = dataOffset + (f * channels + c) * bytesPerSample;
                sum += (format, bits) switch
                {
                    (3, 32) => BitConverter.ToSingle(wav, o),
                    (1, 8) => (wav[o] - 128) / 128.0,
                    (1, 16) => BitConverter.ToInt16(wav, o) / 32768.0,
                    (1, 24) => ((wav[o] | (wav[o + 1] << 8) | ((sbyte)wav[o + 2] << 16))) / 8388608.0,
                    (1, 32) => BitConverter.ToInt32(wav, o) / 2147483648.0,
                    _ => throw new Exception($"unsupported WAV format {format} at {bits} bits"),
                };
            }
            mono[f] = (float)(sum / channels);
        }

        return (mono, sampleRate);
    }
}
