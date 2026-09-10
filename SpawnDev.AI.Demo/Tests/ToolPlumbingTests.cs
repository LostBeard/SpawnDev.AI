using SpawnDev.AI.Server;

namespace SpawnDev.AI.Demo.Tests;

/// <summary>
/// Tools can be enumerated and handed to a generation, and a tool-enabled turn still delivers its text.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 WHAT THIS DOES AND DOES NOT CLAIM. It gates the PLUMBING I added: enumerating tools over MCP,
/// passing definitions into <c>ChatStreamAsync</c>, and - the part that actually broke - receiving the
/// reply when the router switches that turn to NON-streaming, because a tool call only resolves once the
/// whole message exists. Before that, a tool-enabled turn returned "stop" and delivered nothing to
/// <c>onDelta</c>: a reply that silently vanished.
/// </para>
/// <para>
/// ⚠️ It does NOT claim a small model reliably CHOOSES to call a tool, and deliberately does not try to.
/// Both registered tools have bypass paths that would make such a test a lie - the engine force-runs
/// <c>generate_image</c> on clear visual intent (because a 0.5B refuses ~40% of plain image requests) and
/// grounds GitHub questions before generating. A test asking for a picture would pass through forcing
/// while proving nothing about tool choice.
/// </para>
/// </remarks>
public sealed class ToolPlumbingTests
{
    private readonly AiWorkerClient _client;

    /// <summary>New instance.</summary>
    public ToolPlumbingTests(AiWorkerClient client) => _client = client;

    private const string Model = "qwen3:0.6b-q8_0";

    /// <summary>The server publishes its tools in the shape a generation can consume.</summary>
    /// <remarks>
    /// Shape matters as much as presence: the chat surface wants OpenAI-style
    /// <c>{type:"function", function:{name, description, parameters}}</c>, while MCP describes a tool as
    /// <c>{name, description, inputSchema}</c>. If the translation were wrong the definitions would be
    /// accepted and silently ignored, and a character would appear to have no tools for no visible reason.
    /// </remarks>
    [AiTest(Timeout = 60_000)]
    public async Task ToolsEnumerateInAConsumableShape()
    {
        var tools = await _client.ListToolsAsync();
        if (tools.Count == 0)
            throw new Exception("no tools were published - the server registers generate_image and a "
                + "GitHub lookup, so an empty list means the MCP enumeration is not reaching the client");

        foreach (var def in tools)
        {
            using var doc = System.Text.Json.JsonDocument.Parse(def);
            var root = doc.RootElement;
            if (!root.TryGetProperty("type", out var t) || t.GetString() != "function")
                throw new Exception($"a definition is not an OpenAI function wrapper: {Head(def)}");
            if (!root.TryGetProperty("function", out var fn))
                throw new Exception($"a definition has no function body: {Head(def)}");
            if (!fn.TryGetProperty("name", out var n) || string.IsNullOrWhiteSpace(n.GetString()))
                throw new Exception($"a definition has no name, so nothing can call it: {Head(def)}");
            // Parameters must be present even when empty - a missing schema is what makes a model emit a
            // call with arguments the executor cannot parse.
            if (!fn.TryGetProperty("parameters", out _))
                throw new Exception($"'{n.GetString()}' publishes no parameter schema: {Head(def)}");
        }

        Console.WriteLine($"[tools] {tools.Count} tool(s): " + string.Join(", ", tools.Select(NameOf)));

        // The filter a character's grants run through must work against the REAL definitions, not just
        // the hand-written ones in CharacterToolAccessTests.
        var first = NameOf(tools[0]);
        var granted = CharacterLibrary.ToolsFor(
            new ChatAgent("c", "C", Model, "p", null, new[] { first }), tools);
        if (granted.Count != 1)
            throw new Exception($"granting '{first}' against the real catalogue yielded {granted.Count}");
        if (CharacterLibrary.ToolsFor(new ChatAgent("c", "C", Model, "p"), tools).Count != 0)
            throw new Exception("a character with no grants was handed real tools");
    }

    /// <summary>A turn WITH tools available still delivers its reply text.</summary>
    /// <remarks>
    /// 🔴 THE REGRESSION THIS EXISTS FOR. Passing tools makes the router run the turn non-streaming, and
    /// the client originally only handled streamed <c>event</c> frames - so the call succeeded, reported
    /// "stop", and delivered NOTHING. The turn looked like a model that answered with silence.
    /// </remarks>
    [AiTest(Heavy = true, Timeout = 600_000)]
    public async Task AToolEnabledTurnStillReturnsItsText()
    {
        var tools = await _client.ListToolsAsync();
        if (tools.Count == 0) throw new Exception("no tools published; cannot test the tool-enabled path");

        // Deliberately a plain question with no visual or GitHub intent, so neither bypass fires and this
        // really is the tools-present code path rather than a forced tool run.
        var messages = new List<AiChatMessage>
        {
            new("system", "Answer in one short sentence."),
            new("user", "What colour is a ripe banana?"),
        };
        var options = new AiGenerationOptions { MaxOutputTokens = 96, Strategy = "top_p", Temperature = 0.3f, TopP = 0.9f };

        var withTools = "";
        await _client.ChatStreamAsync(Model, messages, options,
            onDelta: d => withTools += d, toolsJson: tools);
        Console.WriteLine($"[tools] with tools available: \"{Head(withTools)}\"");

        if (string.IsNullOrWhiteSpace(withTools))
            throw new Exception("a turn with tools available returned NO text. The router runs such turns "
                + "non-streaming, so the client must handle the single completed message - otherwise every "
                + "tool-enabled reply silently vanishes.");

        // A control WITHOUT tools, so a model that simply says nothing cannot make this pass. Compared on
        // presence rather than content: two samples of a 0.5B need not agree on wording.
        var withoutTools = "";
        await _client.ChatStreamAsync(Model, messages, options, onDelta: d => withoutTools += d);
        Console.WriteLine($"[tools] without tools: \"{Head(withoutTools)}\"");
        if (string.IsNullOrWhiteSpace(withoutTools))
            throw new Exception("FIXTURE TOO WEAK: the control turn produced nothing either, so the "
                + "tool-enabled result above demonstrates nothing about tools");
    }

    private static string NameOf(string def)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(def);
        return doc.RootElement.GetProperty("function").GetProperty("name").GetString() ?? "?";
    }

    private static string Head(string s)
    {
        s = (s ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
        return s.Length <= 140 ? s : s[..140] + "…";
    }
}
