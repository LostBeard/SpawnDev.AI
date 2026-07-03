using System.Text.Json;
using System.Text.Json.Serialization;

namespace SpawnDev.AI;

/// <summary>
/// One frame of a served response crossing a message boundary (worker postMessage, or any
/// non-HTTP pipe). A response is either a single terminal frame (json/text) or a stream:
/// start → event* → end. The frame types mirror <see cref="IAiServerTransport"/> writes 1:1.
/// </summary>
public sealed record AiWireFrame
{
    /// <summary>"json" | "text" | "start" | "event" | "raw" | "end" | "error".</summary>
    [JsonPropertyName("t")] public required string T { get; init; }
    /// <summary>HTTP-style status for "json"/"text"/"error" frames.</summary>
    [JsonPropertyName("s")] public int Status { get; init; }
    /// <summary>Stream framing for "start" frames: "sse" | "ndjson".</summary>
    [JsonPropertyName("k")] public string? Kind { get; init; }
    /// <summary>SSE event name for "event" frames (null = plain data / NDJSON line).</summary>
    [JsonPropertyName("n")] public string? Name { get; init; }
    /// <summary>Payload: response JSON, text, event JSON, or raw frame text.</summary>
    [JsonPropertyName("d")] public string? Data { get; init; }

    private static readonly JsonSerializerOptions J = new(JsonSerializerDefaults.Web);

    /// <summary>Serialize for the wire.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, J);

    /// <summary>Parse a wire frame.</summary>
    public static AiWireFrame FromJson(string json) => JsonSerializer.Deserialize<AiWireFrame>(json, J)
        ?? throw new FormatException("null AiWireFrame");
}
