using SpawnDev.AI.Server;

namespace SpawnDev.AI.Demo.Tests;

/// <summary>
/// A model served from the OLLAMA registry rather than Hugging Face actually loads and answers.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 THIS GATES A CLAIM ALREADY MADE TO USERS. The model picker offers <c>gemma4:12b</c> and asks the
/// user to agree to a 6.87 GB download for it. That entry reaches the UI whether or not the code behind
/// it works, because the catalogue is just configuration - so without this test the app can invite
/// somebody to spend 6.87 GB on a code path nobody has ever run.
/// </para>
/// <para>
/// ⚠️ The ollama path is genuinely DIFFERENT code: <c>HubModelStream.OpenOllamaAsync(model, tag,
/// "model")</c> rather than <c>OpenAsync(repo, file)</c>, reached through <c>HubModelOption.IsOllama</c>.
/// Everything downstream - GGUF parse, tokenizer, session, generator - is then the same generic path the
/// Hugging Face models use, and whether gemma4 survives it text-only is exactly the open question.
/// </para>
/// <para>
/// ⚠️ Heavy in the extreme: a first run downloads 6.87 GB and loads a 12B model onto the GPU. The
/// timeout is sized for a cold hub pull on a fast LAN, and this is the reason it is not in the default
/// heavy set anybody runs casually.
/// </para>
/// </remarks>
public sealed class OllamaSourcedModelTests
{
    private readonly AiWorkerClient _client;

    /// <summary>New instance.</summary>
    public OllamaSourcedModelTests(AiWorkerClient client) => _client = client;

    private const string Model = "gemma4:12b";

    /// <summary>gemma4:12b loads from the ollama registry and answers in text.</summary>
    [AiTest(Heavy = true, Timeout = 2_700_000)]
    public async Task Gemma4LoadsFromTheOllamaRegistryAndAnswers()
    {
        // The catalogue must be offering it in the first place, or this test is about nothing.
        var catalogue = await _client.GetModelCatalogueAsync();
        var entry = catalogue.FirstOrDefault(c => c.Name == Model);
        if (entry == null)
            throw new Exception($"'{Model}' is not in the catalogue, so the picker cannot offer it and "
                + "this test has nothing to verify");
        Console.WriteLine($"[gemma4] catalogue says {entry.SizeBytes:N0} bytes - {entry.Description}");
        if (entry.SizeBytes < 1_000_000_000)
            throw new Exception($"the catalogue advertises {entry.SizeBytes:N0} bytes for a ~6.9 GB model; "
                + "the user would be agreeing to a download under a wrong figure");

        var reply = "";
        var deltas = 0;
        DateTime? first = null;
        var started = DateTime.UtcNow;
        await _client.ChatStreamAsync(Model,
            new List<AiChatMessage>
            {
                new("system", "Answer in one short sentence."),
                new("user", "Name the largest planet in our solar system."),
            },
            new AiGenerationOptions { MaxOutputTokens = 64, Strategy = "top_p", Temperature = 0.3f, TopP = 0.9f },
            onDelta: d => { first ??= DateTime.UtcNow; reply += d; deltas++; });

        var ttft = first is { } f ? (f - started).TotalSeconds : 0;
        var decode = first is { } g ? (DateTime.UtcNow - g).TotalSeconds : 0;
        Console.WriteLine($"[gemma4] first token {ttft:F1}s (download + load included), then "
            + $"{(decode > 0 ? deltas / decode : 0):F1} tok/s");
        Console.WriteLine($"[gemma4] replied: {reply.Replace("\n", " ")}");

        if (string.IsNullOrWhiteSpace(reply))
            throw new Exception("gemma4:12b produced NO text. The ollama-registry load path, the gemma4 "
                + "chat template, or the generic GGUF path does not carry this model - and the picker is "
                + "currently inviting a 6.87 GB download for it.");

        // A weak but real content check: this is a fact a 12B model must get right, and it distinguishes
        // "loaded and answering" from "loaded and emitting noise", which an emptiness check cannot.
        if (!reply.Contains("Jupiter", StringComparison.OrdinalIgnoreCase))
            throw new Exception($"gemma4:12b answered \"{reply.Trim()}\" - it produced text but not the "
                + "right answer, which points at the chat template or tokenizer rather than the loader");

        // Thinking markup must not leak from this family either.
        if (reply.Contains("<think>", StringComparison.OrdinalIgnoreCase))
            throw new Exception("gemma4 leaked thinking markup into its reply");
    }
}
