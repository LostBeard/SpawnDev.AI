# The tool system

A server-side tool the model can invoke mid-conversation implements `IAiTool`:

```csharp
public interface IAiTool
{
    string Name { get; }                    // snake_case, e.g. "generate_image"
    string Description { get; }              // 1-2 sentences the model uses to decide WHEN to call
    string ParametersJsonSchema { get; }     // JSON Schema (as a string) for the arguments object
    Task<AiToolExecutionResult> ExecuteAsync(string argumentsJson, CancellationToken ct = default);
}
```

Register tools once in an `AiToolRegistry`; that single registration serves **three surfaces**:

1. **The internal agentic loop** - `AiChatEngine` injects the tool definitions, parses the model's tool
   calls out of the generated text, executes them server-side, and continues the conversation with the
   results (bounded by `MaxToolRounds`, default 3). Client-supplied tools always take precedence over
   server tools.
2. **MCP** - `POST /mcp` exposes `tools/list` + `tools/call` (JSON-RPC 2.0) to any MCP agent.
3. **Protocol clients** - tool calls surface as `tool_calls` (OpenAI/Ollama) or `tool_use` (Anthropic).

## Binary outputs travel out of band

`AiToolExecutionResult.TextForModel` is the short text that re-enters the conversation (the model reads
it). Binary artifacts (a generated image) go in `Artifacts` and are stored in the registry's artifact
store, **never** routed through the model's context - multi-MB payloads would blow a small model's
window. The engine appends a deterministic `![label](ai-artifact://{id})` markdown reference it
controls (models told "don't repeat the id" won't reliably echo it); UIs and protocol clients resolve
the bytes from `GET /ai/artifacts/{id}`.

## Built-in tools

### `generate_image`
Generates an image from a text prompt via the on-device diffusion model (SD-Turbo). Args:
`{ prompt, seed? }`. The PNG goes to the artifact store; the text result references it by id. Backed by
`AiImageEngine`; also serves `POST /v1/images/generations` directly.

### `github_lookup`
Read-only, host-**allowlisted** GitHub access so the model can answer questions about the SpawnDev
libraries, their code/docs, and the crew. Args: `{ repo?, path? }` - no args lists all SpawnDev repos;
a repo name reads its description + README; a path reads a specific file. Every request URL is built
internally from a validated `owner/name`+path against `api.github.com` / `raw.githubusercontent.com`
only (no SSRF; owner defaults to `LostBeard`). Both hosts send permissive CORS, so it runs in the
browser worker unchanged. Anonymous, in-process cached. This tool also implements
`IAiGroundingProvider` - see [reliability.md](reliability.md).

## A grounding provider

An optional capability a tool can implement to answer *before* the model generates:

```csharp
public interface IAiGroundingProvider
{
    // authoritative reference text for this message, or null if it's not in this tool's domain
    Task<string?> GetGroundingAsync(string userMessage, CancellationToken ct = default);
}
```

The engine consults every registered grounding-provider tool up front and injects the returned
reference as context. A tool that grounds is **not** advertised as model-callable (grounding already
supplies the answer; dangling the tool in front of a small model just makes it emit malformed calls).
See [reliability.md](reliability.md).
