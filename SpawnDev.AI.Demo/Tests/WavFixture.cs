namespace SpawnDev.AI.Demo.Tests;

/// <summary>
/// Decodes the WAV fixtures the speech and voice tests share.
/// </summary>
/// <remarks>
/// Extracted from AiSpeechTests so the voice tests use the SAME decoder rather than a second one. Two
/// decoders is how "the model is wrong" and "the audio arrived differently than I think" become
/// indistinguishable across two test files.
/// </remarks>
internal static class WavFixture
{
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
    internal static (float[] Samples, int SampleRate) Decode(byte[] wav)
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

    /// <summary>
    /// Encode mono float samples in [-1, 1] as a 16-bit PCM WAV.
    /// </summary>
    /// <remarks>
    /// The inverse of <see cref="Decode"/>, and here for the same reason it is: the voice gate could
    /// measure the audio (peak, RMS, word overlap, an FNV hash) but could not PRODUCE it, so no human had
    /// ever heard what the gate was scoring. Word overlap says "changed enough to still transcribe"; it
    /// cannot say "sounds right". 16-bit PCM because every player opens it without a codec.
    /// Samples are CLAMPED, not scaled - a normalise here would hide exactly the clipping worth hearing.
    /// </remarks>
    internal static byte[] Encode(float[] samples, int sampleRate)
    {
        var dataBytes = samples.Length * 2;
        var wav = new byte[44 + dataBytes];
        var ascii = System.Text.Encoding.ASCII;

        ascii.GetBytes("RIFF").CopyTo(wav, 0);
        BitConverter.GetBytes(36 + dataBytes).CopyTo(wav, 4);
        ascii.GetBytes("WAVE").CopyTo(wav, 8);
        ascii.GetBytes("fmt ").CopyTo(wav, 12);
        BitConverter.GetBytes(16).CopyTo(wav, 16);             // PCM fmt chunk size
        BitConverter.GetBytes((ushort)1).CopyTo(wav, 20);      // format = PCM
        BitConverter.GetBytes((ushort)1).CopyTo(wav, 22);      // channels = mono
        BitConverter.GetBytes(sampleRate).CopyTo(wav, 24);
        BitConverter.GetBytes(sampleRate * 2).CopyTo(wav, 28); // byte rate
        BitConverter.GetBytes((ushort)2).CopyTo(wav, 32);      // block align
        BitConverter.GetBytes((ushort)16).CopyTo(wav, 34);     // bits per sample
        ascii.GetBytes("data").CopyTo(wav, 36);
        BitConverter.GetBytes(dataBytes).CopyTo(wav, 40);

        for (var i = 0; i < samples.Length; i++)
        {
            var v = samples[i];
            if (v > 1f) v = 1f;
            else if (v < -1f) v = -1f;
            BitConverter.GetBytes((short)(v * 32767f)).CopyTo(wav, 44 + i * 2);
        }
        return wav;
    }
}
