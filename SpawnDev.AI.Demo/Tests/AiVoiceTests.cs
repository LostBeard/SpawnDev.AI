using System.Diagnostics;
using SpawnDev.AI.Server;

namespace SpawnDev.AI.Demo.Tests;

/// <summary>
/// Voice OUT, and the hands-free loop end to end: hear a turn, answer it, speak the answer back.
/// </summary>
/// <remarks>
/// <para>
/// The transcription tests next door prove the demo can LISTEN. These prove it can TALK, and then that the
/// two halves actually connect - which is a separate claim, because a VAD segment has to be something the
/// recogniser can transcribe and the recogniser's text has to be something the voice can pronounce.
/// </para>
/// <para>
/// ⚠️ These run through <c>AiWorkerClient</c>, the same client the UI uses, so the worker transport, the
/// engines and the GPU residency policy are all real. A test that called the engine directly would prove
/// the model works and say nothing about the demo.
/// </para>
/// </remarks>
public sealed class AiVoiceTests
{
    private readonly AiWorkerClient _client;
    private readonly HttpClient _http;

    /// <summary>What the fixture actually says, verbatim.</summary>
    private const string KnownTranscript = "All LibriVox recordings are in the public domain.";

    private const string FixtureUrl = "test-audio/librivox-public-domain.wav";

    /// <summary>The chat model, matching AiChatTests and the demo's default.</summary>
    /// <remarks>
    /// ⚠️ Must be a model the demo actually serves. A hardcoded guess fails with "model not found" AFTER
    /// paying for a full transcription, which reads as a speech failure and is not one.
    /// </remarks>
    private const string Model = "qwen2.5:0.5b-instruct-q8_0";

    /// <summary>New instance.</summary>
    public AiVoiceTests(AiWorkerClient client, HttpClient http)
    {
        _client = client;
        _http = http;
    }

    /// <summary>
    /// Speaking a line returns audible speech in the reference voice.
    /// </summary>
    /// <remarks>
    /// ⚠️ Asserts AMPLITUDE, not just length. A pipeline that returns zeros has the right sample count and
    /// the right type and passes every other check - silence is the failure mode that looks exactly like
    /// success. The duration bound catches the opposite failure: a model whose duration prediction has gone
    /// wrong emits a plausible-sounding fraction of a second, or forty seconds, for one short sentence.
    /// </remarks>
    [AiTest(Heavy = true, Timeout = 1_800_000)]
    public async Task SpeaksInTheReferenceVoice()
    {
        await _client.InitAsync();
        var (reference, referenceRate) = await LoadFixtureAsync();

        const string line = "Hello. This is SpawnDev AI, speaking with your own voice.";
        var sw = Stopwatch.StartNew();
        var (samples, rate, model, ms) = await _client.SpeakAsync(line, KnownTranscript, reference, referenceRate);
        sw.Stop();

        if (samples == null || samples.Length == 0)
            throw new Exception("speak returned NO audio");

        float peak = 0f;
        double energy = 0;
        foreach (var v in samples) { peak = MathF.Max(peak, MathF.Abs(v)); energy += (double)v * v; }
        var rms = Math.Sqrt(energy / samples.Length);
        var seconds = samples.Length / (double)rate;

        Console.WriteLine($"[AiVoiceTests] {model}: {seconds:F2}s @ {rate}Hz in {ms:F0}ms "
                        + $"(wall {sw.Elapsed.TotalSeconds:F1}s), peak {peak:F3} rms {rms:F4}");

        if (peak < 0.01f || rms < 0.005)
            throw new Exception($"the reply is effectively SILENCE (peak {peak:F5}, rms {rms:F5}) - zeros "
                              + "have the right length and type and pass every check except this one");
        if (seconds < 0.5 || seconds > 30.0)
            throw new Exception($"{seconds:F2}s for a {line.Length}-character line - the duration prediction "
                              + "inside the encoder decides this, so a wild value means the encoder ran "
                              + "wrong rather than the vocoder");
    }

    /// <summary>
    /// The whole hands-free turn: hear it, answer it, speak the answer in the voice that asked.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ This is the test the demo's hands-free button is actually backed by, and it exists because the
    /// stage tests cannot cover it. Each stage passing says nothing about whether the transcript is
    /// something the tokenizer can speak, or whether three model kinds can be used in one turn without
    /// evicting each other. That last one is the reason a symmetric eviction ring was replaced by a budget:
    /// with a ring this turn re-uploads every model, three times, and no conversation is possible.
    /// </para>
    /// <para>
    /// ⚠️ Asserts on transcript CONTENT before answering. A recogniser handed a bad segment returns
    /// confident, fluent, WRONG words - and the assistant would answer them, out loud, in the user's voice.
    /// </para>
    /// </remarks>
    [AiTest(Heavy = true, Timeout = 2_700_000)]
    public async Task HandsFreeTurn_HearsAnswersAndSpeaksBack()
    {
        await _client.InitAsync();
        var (heard, heardRate) = await LoadFixtureAsync();
        var total = Stopwatch.StartNew();

        // 1. LISTEN
        var sw = Stopwatch.StartNew();
        var (heardText, _, asrMs) = await _client.TranscribeAsync(heard, heardRate);
        heardText = (heardText ?? "").Trim();
        Console.WriteLine($"[AiVoiceTests] heard \"{heardText}\" in {asrMs:F0}ms");

        if (string.IsNullOrWhiteSpace(heardText))
            throw new Exception("the turn transcribed to nothing - the loop has nothing to answer");
        var words = heardText.ToLowerInvariant();
        foreach (var required in new[] { "recordings", "public", "domain" })
            if (!words.Contains(required))
                throw new Exception($"the transcript is missing \"{required}\": \"{heardText}\". The "
                                  + "assistant would answer the wrong question, out loud, in the user's voice.");

        // 2. ANSWER
        sw.Restart();
        // ⚠️ ChatStreamAsync returns the STOP REASON, not the reply - the text arrives through onDelta.
        // Using its return value as the answer would hand the voice the word "stop" to say out loud.
        var replyBuilder = new System.Text.StringBuilder();
        await _client.ChatStreamAsync(
            Model,
            new[] { new AiChatMessage("user", heardText) },
            new AiGenerationOptions { MaxOutputTokens = 48 },
            delta => replyBuilder.Append(delta));
        var chatMs = sw.Elapsed.TotalMilliseconds;
        var reply = replyBuilder.ToString().Trim();
        Console.WriteLine($"[AiVoiceTests] answered in {chatMs:F0}ms: \"{reply}\"");
        if (string.IsNullOrWhiteSpace(reply))
            throw new Exception("the model produced no reply to speak");

        // 3. SPEAK IT BACK, in the voice that asked
        sw.Restart();
        (float[] samples, int rate, _, double ttsMs) = await _client.SpeakAsync(reply, heardText, heard, heardRate);
        if (samples == null || samples.Length == 0)
            throw new Exception("the loop produced no reply audio");

        float peak = 0f;
        foreach (var v in samples) peak = MathF.Max(peak, MathF.Abs(v));
        if (peak < 0.01f)
            throw new Exception($"the spoken reply is silence (peak {peak:F5})");

        var turnSeconds = heard.Length / (double)heardRate;
        var loopMs = asrMs + chatMs + ttsMs;
        Console.WriteLine($"[AiVoiceTests] HANDS-FREE TURN: asr {asrMs:F0} + chat {chatMs:F0} + tts {ttsMs:F0} "
                        + $"= {loopMs:F0}ms to answer a {turnSeconds:F2}s turn with "
                        + $"{samples.Length / (double)rate:F2}s of speech "
                        + $"({loopMs / 1000 / turnSeconds:F2}x the turn; under 1.0 keeps up with a talker) "
                        + $"| wall {total.Elapsed.TotalSeconds:F1}s");
    }

    private async Task<(float[] Samples, int SampleRate)> LoadFixtureAsync()
    {
        var bytes = await _http.GetByteArrayAsync(FixtureUrl);
        var (samples, rate) = WavFixture.Decode(bytes);
        if (samples.Length == 0) throw new Exception($"{FixtureUrl} decoded to zero samples");
        return (samples, rate);
    }
}
