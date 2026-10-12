using SpawnDev.AI.Server;

namespace SpawnDev.AI.Demo.Tests;

/// <summary>
/// The tool definitions a model reads in its prompt (<see cref="AiChatEngine.ToolDefinitionJson"/>) show quotes,
/// apostrophes and non-ASCII text as themselves, not as U+0027-style escape sequences.
/// </summary>
/// <remarks>
/// The default serializer escaped them. A model reads the prompt as text and copied the escapes into its own tool
/// calls, mangled: Vertex (llama3.1:8b, 2026-10-12) stored a fact wrapped in the escape for U+00A2 and set a
/// goal "for e". No model needed to check the cause: the definition text itself.
/// </remarks>
public sealed class ToolDefinitionTests
{
    private sealed class QuotedTool : IAiTool
    {
        public string Name => "remember";
        public string Description => "Remember a fact. Use it when a friend says \"remember that...\" or \"don't forget\"; it's kept for next time (café rules).";
        public string ParametersJsonSchema => "{\"type\":\"object\",\"properties\":{\"fact\":{\"type\":\"string\",\"description\":\"what to remember, in the friend's words\"}},\"required\":[\"fact\"]}";
        public Task<AiToolExecutionResult> ExecuteAsync(string argumentsJson, CancellationToken ct = default) => Task.FromResult(new AiToolExecutionResult("ok"));
    }

    /// <summary>No escape sequence for a quote, an apostrophe or an accented letter, and the text still round-trips.</summary>
    [AiTest(Timeout = 10_000)]
    public Task ToolDefinitionsShowQuotesAsTheModelShouldWriteThem()
    {
        var tool = new QuotedTool();
        var json = AiChatEngine.ToolDefinitionJson(tool);
        foreach (var escape in new[] { "\\u0027", "\\u0022", "\\u00e9", "\\u00E9" })
            if (json.Contains(escape, StringComparison.Ordinal))
                throw new Exception($"the definition the model reads contains {escape}: {json}");
        using var doc = System.Text.Json.JsonDocument.Parse(json);   // still valid JSON
        var fn = doc.RootElement.GetProperty("function");
        if (fn.GetProperty("description").GetString() != tool.Description)
            throw new Exception("the description did not survive the round trip: " + json);
        if (fn.GetProperty("parameters").GetProperty("properties").GetProperty("fact").GetProperty("description").GetString()
            != "what to remember, in the friend's words")
            throw new Exception("the schema text did not survive the round trip: " + json);
        return Task.CompletedTask;
    }
}
