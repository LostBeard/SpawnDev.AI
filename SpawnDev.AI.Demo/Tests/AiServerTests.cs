using System.Text.Json;
using SpawnDev.AI.Server;

namespace SpawnDev.AI.Demo.Tests;

/// <summary>
/// The in-browser AI server's transport and catalogue, WITHOUT loading a model. These are the cheap tests:
/// they prove the shared worker comes up and the Ollama-compatible surface answers, which is the ground
/// every heavier test stands on.
/// </summary>
public sealed class AiServerTests
{
    private readonly AiWorkerClient _client;

    /// <summary>New instance. <paramref name="client"/> is the window-side client the UI itself uses.</summary>
    public AiServerTests(AiWorkerClient client) => _client = client;

    /// <summary>The shared worker starts and reports ready.</summary>
    [AiTest(Timeout = 120_000)]
    public async Task WorkerInitialises()
    {
        var status = await _client.InitAsync();
        if (!_client.Ready)
            throw new Exception($"client not ready after InitAsync; status='{status}' Status='{_client.Status}'");
    }

    /// <summary>
    /// <c>/api/tags</c> lists the models configured in Program.cs.
    /// </summary>
    /// <remarks>
    /// Asserts a NON-EMPTY list rather than just valid JSON: an empty catalogue is exactly what a broken
    /// model provider returns, and it would still parse.
    /// </remarks>
    [AiTest(Timeout = 120_000)]
    public async Task TagsListsConfiguredModels()
    {
        await _client.InitAsync();
        var json = await _client.RequestJsonAsync("GET", "/api/tags");
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("models", out var models))
            throw new Exception($"/api/tags has no 'models' property: {Trim(json)}");
        var names = models.EnumerateArray()
            .Select(m => m.TryGetProperty("name", out var n) ? n.GetString() : null)
            .Where(n => !string.IsNullOrEmpty(n)).ToList();
        if (names.Count == 0)
            throw new Exception($"/api/tags returned an EMPTY model list: {Trim(json)}");
        Console.WriteLine($"[AiServerTests] /api/tags -> {names.Count} models: {string.Join(", ", names)}");
    }

    /// <summary><c>/api/version</c> answers.</summary>
    [AiTest(Timeout = 120_000)]
    public async Task VersionAnswers()
    {
        await _client.InitAsync();
        var json = await _client.RequestJsonAsync("GET", "/api/version");
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("version", out var v) || string.IsNullOrEmpty(v.GetString()))
            throw new Exception($"/api/version returned no version: {Trim(json)}");
    }

    /// <summary>
    /// An unknown model is reported as an error rather than hanging or answering from the wrong model.
    /// </summary>
    [AiTest(Timeout = 120_000)]
    public async Task UnknownModelIsReportedNotSilentlySubstituted()
    {
        await _client.InitAsync();
        var messages = new[] { new AiChatMessage("user", "hello") };
        string? failure = null;
        try
        {
            var text = await _client.ChatStreamAsync("definitely-not-a-real-model:0b", messages);
            // Answering from SOME other model would be worse than failing - say so explicitly.
            failure = $"expected an error for an unknown model, got a reply: '{Trim(text)}'";
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AiServerTests] unknown model correctly errored: {ex.GetType().Name}: {ex.Message}");
        }
        if (failure != null) throw new Exception(failure);
    }

    private static string Trim(string s) => s.Length <= 300 ? s : s[..300] + "...";
}
