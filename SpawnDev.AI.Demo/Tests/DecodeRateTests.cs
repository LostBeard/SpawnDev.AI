using SpawnDev.AI.Server;

namespace SpawnDev.AI.Demo.Tests;

/// <summary>
/// The browser's decode rate, measured by the engine and reported through Ollama's final-frame usage fields.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 WHY THIS EXISTS. Desktop decode speed has a bench (ServerHost <c>chat-bench</c>), the browser had none: the only browser tok/s
/// lines came from tests that time a 0.1-0.5 s reply with a wall clock on the WINDOW side of the worker hop, and that is noise, not a
/// rate. When SpawnDev.ILGPU.ML 5.8.1-local.8 doubled CUDA Q4_K decode (29.2 -> 58.4 tok/s, llama3.1:8b) the browser number could not
/// say whether WebGPU gained anything (qwen3:4b-q4_k_m read 66.9 then 66.1 over half a second).
/// </para>
/// <para>
/// This takes the ENGINE's own split (<see cref="AiChatUsage"/>, from <c>eval_count</c> / <c>eval_duration</c>), so the worker hop
/// and the window's event loop are not in the number, and decodes 128 greedy tokens per run, median of 3 after a warm-up turn that
/// pays download, load and kernel compilation.
/// </para>
/// <para>
/// ⚠️ The assertions are about the MEASUREMENT, not a speed floor: the final frame must carry the timing fields, the run must produce
/// enough tokens to time, and the rate must be finite. A speed floor would fail on a slower GPU for no fault of the code. Compare the
/// BENCH line across releases in _TestStats instead.
/// </para>
/// </remarks>
public sealed class DecodeRateTests
{
    private readonly AiWorkerClient _client;

    /// <summary>New instance.</summary>
    /// <param name="client">The window-side client the UI itself uses.</param>
    public DecodeRateTests(AiWorkerClient client) => _client = client;

    /// <summary>A Q4_K_M model (the format local.8's Q4_K GEMV targets) the demo offers.</summary>
    private const string Model = "qwen3:4b-q4_k_m";

    /// <summary>The engine reports a decode rate for a 128-token greedy reply in the browser, and the BENCH line records it.</summary>
    // 20 min: a cold run downloads ~2.5 GB from the hub before the first token.
    [AiTest(Heavy = true, Timeout = 1_200_000)]
    public async Task BrowserDecodeRateIsMeasured()
    {
        var messages = new List<AiChatMessage>
        {
            new("user", "Write a detailed paragraph about why GPUs are good at running neural networks. Keep going until you run out of space."),
        };
        var greedy = new AiGenerationOptions { Strategy = "greedy", Temperature = 0, MaxOutputTokens = 128 };

        AiChatUsage? warm = null;
        await _client.ChatStreamAsync(Model, messages, greedy, onUsage: u => warm = u);   // download + load + compile
        if (warm == null) throw new Exception("the final frame carried no usage at all: onUsage never fired");
        if (warm.Timings == null)
            throw new Exception("the final frame carried no total_duration: the server did not report AiChatTimings");
        Console.WriteLine($"[decode-rate] {Model} warm-up: load {warm.Timings.LoadMs / 1000:F1}s, {warm.GeneratedTokens} tokens");

        var rates = new List<double>();
        AiChatUsage? last = null;
        for (int r = 0; r < 3; r++)
        {
            AiChatUsage? u = null;
            await _client.ChatStreamAsync(Model, messages, greedy, onUsage: x => u = x);
            if (u?.Timings is not { EvalMs: { } evalMs, PromptEvalMs: { } promptMs })
                throw new Exception($"run {r}: the final frame had no prompt_eval_duration / eval_duration (usage: {u})");
            if (u.GeneratedTokens < 64)
                throw new Exception($"run {r}: only {u.GeneratedTokens} tokens, too few to time a decode rate (stopped early?)");
            if (u.DecodeTokensPerSecond is not { } rate || !double.IsFinite(rate) || rate <= 0)
                throw new Exception($"run {r}: no finite decode rate from {u.GeneratedTokens} tokens over {evalMs:F0} ms");
            rates.Add(rate);
            last = u;
            Console.WriteLine($"[decode-rate] run {r}: {u.GeneratedTokens} tokens, prefill {promptMs:F0} ms for {u.PromptTokens} prompt tokens, "
                + $"decode {rate:F1} tok/s");
        }
        rates.Sort();
        Console.WriteLine($"BENCH: browser {Model} greedy decode: median {rates[1]:F1} tok/s (min {rates[0]:F1}, max {rates[2]:F1}), "
            + $"{last!.GeneratedTokens} tokens, prefill {last.Timings!.PromptEvalMs:F0} ms for {last.PromptTokens} prompt tokens");
    }
}
