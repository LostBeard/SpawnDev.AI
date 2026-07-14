# Protocol surfaces

`AiApiRouter` answers one method+path per route and writes through `IAiServerTransport`, so the same
routing code serves HTTP (desktop) and MessagePort frames (browser worker). Routes marked *(tools)* /
*(images)* are only present when the router is given an `AiToolRegistry` / `AiImageEngine`.

## Endpoints

| Method + path | Surface | Notes |
|---|---|---|
| `GET /` , `HEAD /` | - | `"Ollama is running (SpawnDev.AI)"` liveness. |
| `GET /api/version` | Ollama | Server version string. |
| `GET /api/tags` | Ollama | List models. |
| `POST /api/show` | Ollama | Model metadata + capabilities. |
| `POST /api/chat` | Ollama native | NDJSON stream (default) or buffered. |
| `POST /api/generate` | Ollama native | Single-prompt completion. |
| `GET /v1/models` | OpenAI | List models. |
| `POST /v1/chat/completions` | OpenAI | SSE stream or buffered; `tool_calls` in the response. |
| `POST /v1/messages` | Anthropic Messages | SSE stream (works with Claude CLI); `tool_use` blocks. |
| `POST /v1/messages/count_tokens` | Anthropic | Prompt token count. |
| `POST /v1/images/generations` *(images)* | OpenAI Images | DALL-E-compatible; `b64_json` PNG (SD-Turbo). |
| `POST /mcp` *(tools)* | MCP (JSON-RPC 2.0) | `initialize` / `ping` / `tools/list` / `tools/call`. |
| `GET /ai/artifacts/{id}` *(tools)* | SpawnDev | Base64 fetch of a tool's binary output (generated image). |
| `GET /ai/image-models` *(images)* | SpawnDev | Default + available image models. |

## Client compatibility

Verified against the Ollama CLI/clients, OpenAI-compatible clients, and the Claude CLI (Anthropic
Messages). The Anthropic path streams text deltas live and emits `tool_use` blocks at the end; the
engine holds back tool-call markup so raw JSON never leaks as visible text.

## Sampling / options mapping

Each protocol's sampling fields map onto the neutral `AiGenerationOptions` (`MaxOutputTokens`,
`Strategy` greedy/top_p/top_k, `Temperature`, `TopP`, `TopK`, `RepetitionPenalty`, `Seed`, `Stops`).
Requested output tokens are clamped to the engine's `MaxOutputTokens` cap (agentic clients routinely
ask for far more than a small local model should produce).

## Tool calling on every surface

When a request carries no client tools, the engine injects the registered server tools, parses the
model's tool calls out of the generated text (ChatML `<tool_call>`, bare JSON, and ```` ```json ````
fences are all handled), executes them server-side, and continues the conversation with the results
(bounded by `MaxToolRounds`). See [tools.md](tools.md).
