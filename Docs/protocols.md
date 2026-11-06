# Protocol surfaces

`AiApiRouter` answers one method+path per route and writes through `IAiServerTransport`, so the same
routing code serves HTTP (desktop) and MessagePort frames (browser worker). A route marked in italics is only present
when the router has that engine: *(tools)* `Tools` (`AiToolRegistry`), *(images)* `Images` (`AiImageEngine`),
*(speech)* `Speech` (`AiSpeechEngine`), *(VAD)* `Vad` (`AiVadEngine`), *(voice)* `Voice` (`AiVoiceEngine`),
*(hub models)* `HubModels` (`HubModelProvider`), *(progress)* `Progress` (`AiProgressTracker`).

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
| `GET /ai/models` *(hub models)* | SpawnDev | The hub catalogue: name, size, description, and how much of each is already cached. |
| `GET /ai/progress` *(progress)* | SpawnDev | A snapshot of model downloads and loads in progress. |
| `POST /api/transcribe` *(speech)* | SpawnDev | Speech to text. `{ samples, sample_rate }` (mono PCM in [-1, 1]) -> `{ text, model, inference_ms }`. |
| `POST /api/vad` *(VAD)* | SpawnDev | Where an utterance starts and stops. `{ samples, reset?, flush? }` (16 kHz, continuing the stream) -> `{ speech_active, probability, spans: [{ start, length }], ... }`. Spans are offsets into the caller's own stream, counted from the last `reset`. |
| `POST /api/speak` *(voice)* | SpawnDev | Text to speech. `{ text, voice_id }` for a built-in or prepared voice, or `{ text, reference_samples, reference_text, sample_rate }` to clone -> `{ samples, sample_rate, model, inference_ms, duration_seconds }`. |
| `POST /api/voices` *(voice)* | SpawnDev | Prepare a cloned voice once: `{ voice_id, display_name, reference_text, reference_samples, sample_rate }`. `reference_text` must be the clip's exact transcript. |
| `GET /api/voices` *(voice)* | SpawnDev | The voice ids available to `/api/speak`. |
| `POST /api/warm` | SpawnDev | Load models ahead of use: `{ kinds: ["vad", "speech", "voice"] }` (the default). A kind that fails to warm is reported, not thrown, and loads lazily later. |

The audio routes carry PCM as JSON number arrays for now, which works over both transports; a binary frame is the
planned follow-up and will not change the routes.

## Client compatibility

Verified against the Ollama CLI/clients, OpenAI-compatible clients, and the Claude CLI (Anthropic
Messages). The Anthropic path streams text deltas live and emits `tool_use` blocks at the end; the
engine holds back tool-call markup so raw JSON never leaks as visible text.

## Sampling / options mapping

Each protocol's sampling fields map onto the neutral `AiGenerationOptions` (`MaxOutputTokens`,
`Strategy` greedy/top_p/top_k, `Temperature`, `TopP`, `TopK`, `RepetitionPenalty`, `Seed`, `Stops`).
Requested output tokens are clamped to the engine's `MaxOutputTokens` cap (agentic clients routinely
ask for far more than a small local model should produce).

## Timings

The final `/api/chat` and `/api/generate` frame carries Ollama's timing fields: `prompt_eval_count`, `eval_count`,
`total_duration`, `load_duration`, `prompt_eval_duration` and `eval_duration` (nanoseconds). A duration the engine did not
measure is omitted rather than sent as 0. Library callers get the same split as `AiChatResult.Timings` /
`AiChatUsage` (see [engines.md](engines.md#chat-aichatengine-modelregistry)).

## Tool calling on every surface

When a request carries no client tools, the engine injects the registered server tools, parses the
model's tool calls out of the generated text (ChatML `<tool_call>`, bare JSON, and ```` ```json ````
fences are all handled), executes them server-side, and continues the conversation with the results
(bounded by `MaxToolRounds`). See [tools.md](tools.md).
