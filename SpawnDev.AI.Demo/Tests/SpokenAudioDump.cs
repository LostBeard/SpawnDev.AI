namespace SpawnDev.AI.Demo.Tests;

/// <summary>
/// Ships synthesised audio out of the browser as base64 over the test console, so the runner can write a
/// playable .wav next to the run.
/// </summary>
/// <remarks>
/// <para>
/// The voice gate scored audio nobody could listen to. Every number it reports - peak, RMS, per-second
/// energy, word overlap, an FNV hash - answers "did the samples change", and none of them answers "does
/// this sound right to a person". Whisper reading a line back at 92% is compatible with a voice that is
/// perfectly clear AND with one a listener would call garbled, which is precisely the complaint that
/// started this work.
/// </para>
/// <para>
/// The console is the channel because it is the ONLY one already wired end to end: the page prints, the
/// runner's <c>page.Console</c> handler receives, and it works identically whether the suite runs under
/// Playwright, in a dev browser, or from a published build on a static server. A download would need
/// user-gesture and download-path plumbing; OPFS would need a second tool to get the bytes back out; a
/// <c>globalThis</c> stash would be invisible when the models move to a shared worker.
/// </para>
/// <para>
/// OFF by default (<c>wav=1</c> in the query string turns it on) - a synthesis is roughly 640 KB of base64,
/// which is noise in a normal run and would slow the console handler for no benefit.
/// </para>
/// </remarks>
internal static class SpokenAudioDump
{
    /// <summary>True when the run asked for audio files (<c>wav=1</c>).</summary>
    internal static bool Enabled { get; set; }

    /// <summary>
    /// Base64 characters per console line. Large enough that a 10-second utterance is ~80 lines rather
    /// than thousands, small enough to stay well inside what a console message carries comfortably.
    /// </summary>
    private const int ChunkChars = 8192;

    /// <summary>
    /// Emit one utterance. Does nothing unless <see cref="Enabled"/>.
    /// </summary>
    /// <param name="label">Filename stem, sanitized here - the runner writes <c>&lt;label&gt;.wav</c>.</param>
    /// <param name="samples">Mono float samples in [-1, 1].</param>
    /// <param name="sampleRate">Sample rate of <paramref name="samples"/>.</param>
    internal static void Emit(string label, float[] samples, int sampleRate)
    {
        if (!Enabled || samples == null || samples.Length == 0) return;

        var safe = Sanitize(label);
        var b64 = Convert.ToBase64String(WavCodec.Encode(samples, sampleRate));
        var chunks = (b64.Length + ChunkChars - 1) / ChunkChars;

        // The header carries the chunk count so the runner can tell a truncated transfer from a complete
        // one instead of silently writing a half file.
        Console.WriteLine($"WAV-BEGIN: {safe}|{sampleRate}|{chunks}|{samples.Length}");
        for (var i = 0; i < chunks; i++)
        {
            var from = i * ChunkChars;
            var len = Math.Min(ChunkChars, b64.Length - from);
            Console.WriteLine($"WAV-DATA: {safe}|{i}|{b64.Substring(from, len)}");
        }
        Console.WriteLine($"WAV-END: {safe}");
    }

    /// <summary>Keep the label usable as a filename and free of the '|' the console contract splits on.</summary>
    /// <remarks>
    /// A plain loop, not LINQ: LINQ on the logging path has silently produced nothing under Blazor WASM
    /// before, and a label that comes out empty here would write every utterance over the same file.
    /// </remarks>
    private static string Sanitize(string label)
    {
        var sb = new System.Text.StringBuilder(label.Length);
        for (var i = 0; i < label.Length; i++)
        {
            var c = label[i];
            sb.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.' ? c : '-');
        }
        var s = sb.ToString().Trim('-');
        return s.Length == 0 ? "audio" : s;
    }
}
