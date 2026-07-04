namespace SpawnDev.AI;

/// <summary>
/// A server-side tool the model can invoke mid-conversation. One registration serves THREE
/// surfaces: (1) the internal agentic loop - AiChatEngine injects registered tool definitions,
/// parses the model's tool calls, EXECUTES them server-side, and continues generation with the
/// results; (2) the MCP surface (tools/list + tools/call); (3) any protocol client that lists
/// server tools. The tool contract is deliberately JSON-in/JSON-out so it marshals identically
/// across HTTP, worker MessagePorts, and MCP.
/// </summary>
public interface IAiTool
{
    /// <summary>Tool name as the model calls it (snake_case, e.g. "generate_image").</summary>
    string Name { get; }

    /// <summary>One-to-two-sentence description the model uses to decide WHEN to call this.</summary>
    string Description { get; }

    /// <summary>JSON Schema (as a JSON string) for the arguments object.</summary>
    string ParametersJsonSchema { get; }

    /// <summary>Execute with the model-supplied arguments JSON. Return the result the model reads.</summary>
    Task<AiToolExecutionResult> ExecuteAsync(string argumentsJson, CancellationToken ct = default);
}

/// <summary>
/// A tool execution result. <see cref="TextForModel"/> is what re-enters the conversation as the
/// tool response (keep it short - the model reads it). Binary artifacts (a generated image) travel
/// OUT OF BAND via <see cref="Artifacts"/> so multi-MB payloads never round-trip through the
/// model's context: the text references them by id (e.g. "ai-artifact://{id}") and UIs/clients
/// resolve the bytes from the artifact store.
/// </summary>
public sealed record AiToolExecutionResult(string TextForModel, IReadOnlyList<AiToolArtifact>? Artifacts = null)
{
    /// <summary>True when the execution failed; <see cref="TextForModel"/> then carries the error
    /// message for the model (models handle tool errors well when told plainly).</summary>
    public bool IsError { get; init; }
}

/// <summary>A binary artifact produced by a tool (image, audio, file).</summary>
public sealed record AiToolArtifact(string Id, string MimeType, byte[] Data, string? Label = null);

/// <summary>
/// The registry of server-side tools + the artifact store their binary outputs land in. Register
/// tools at startup (DI singleton); the chat engine, MCP surface, and protocol routers all read
/// from here.
/// </summary>
public sealed class AiToolRegistry
{
    private readonly Dictionary<string, IAiTool> _tools = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, AiToolArtifact> _artifacts = new();
    private readonly object _lock = new();

    /// <summary>Max artifacts retained (oldest evicted beyond this - they are full images in memory).</summary>
    public int MaxArtifacts { get; set; } = 32;
    private readonly Queue<string> _artifactOrder = new();

    public void Register(IAiTool tool)
    { lock (_lock) _tools[tool.Name] = tool; }

    public IReadOnlyList<IAiTool> List()
    { lock (_lock) return _tools.Values.ToList(); }

    public IAiTool? Get(string name)
    { lock (_lock) return _tools.GetValueOrDefault(name); }

    /// <summary>Store an artifact for later retrieval by UIs/clients (bounded, oldest-evicted).</summary>
    public void StoreArtifact(AiToolArtifact artifact)
    {
        lock (_lock)
        {
            _artifacts[artifact.Id] = artifact;
            _artifactOrder.Enqueue(artifact.Id);
            while (_artifactOrder.Count > MaxArtifacts)
            {
                var evict = _artifactOrder.Dequeue();
                _artifacts.Remove(evict);
            }
        }
    }

    public AiToolArtifact? GetArtifact(string id)
    { lock (_lock) return _artifacts.GetValueOrDefault(id); }
}
