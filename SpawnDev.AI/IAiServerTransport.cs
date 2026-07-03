namespace SpawnDev.AI;

/// <summary>How an event stream is framed on the wire.</summary>
public enum AiEventStreamKind
{
    /// <summary>Server-sent events ("event: x\ndata: {json}\n\n") - the OpenAI/Anthropic streaming shape.</summary>
    Sse,
    /// <summary>Newline-delimited JSON - the Ollama-native streaming shape.</summary>
    Ndjson,
}

/// <summary>
/// The transport half of the server: everything the protocol router needs to answer a request,
/// independent of WHERE the bytes go. The desktop host implements this over ASP.NET's HttpContext;
/// the browser host implements it over a worker MessagePort (each event becomes a postMessage) - the
/// SAME router and protocol code serve both. Implementations must FLUSH per write (streaming clients
/// time out on buffered silence).
/// </summary>
public interface IAiServerTransport
{
    /// <summary>Cancelled when the client goes away - generation must stop and free the GPU.</summary>
    CancellationToken Aborted { get; }

    /// <summary>Write a complete JSON response with an HTTP-style status code (200/404/...).</summary>
    Task WriteJsonAsync(int statusCode, object payload);

    /// <summary>Write a plain-text response (liveness probes).</summary>
    Task WriteTextAsync(int statusCode, string text);

    /// <summary>Begin an event stream of the given framing. Called once, before any WriteEventAsync.</summary>
    Task StartEventStreamAsync(AiEventStreamKind kind);

    /// <summary>Write one stream event. <paramref name="eventName"/> is the SSE event name (null for a
    /// plain data frame / an NDJSON line).</summary>
    Task WriteEventAsync(string? eventName, object payload);

    /// <summary>Write a raw stream frame verbatim (the OpenAI "data: [DONE]" terminator).</summary>
    Task WriteRawAsync(string text);
}
