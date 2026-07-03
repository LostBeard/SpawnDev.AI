# SpawnDev.AI Changelog

Notable changes per release. Preview - APIs will change.

## 1.0.0-preview.1 - Initial extraction

- **SpawnDev.AI**: contracts - `AiChatMessage`/`AiGenerationOptions`/`AiChatRequest`/`AiChatResult`/
  `AiToolCall`/`AiStopKind`, `AiModelInfo`, `IAiChatService`, `IAiServerTransport` (+
  `AiEventStreamKind` SSE/NDJSON framing).
- **SpawnDev.AI.Server**: `OllamaModelStore` (Ollama on-disk cache reader, zero-copy blob
  resolution) and `ModelRegistry` (one-resident-model registry, serialized generation gate, WebGPU
  decode capture/replay enabled by default) extracted from
  `SpawnDev.ILGPU.ML/Examples/06.OllamaServer.Console`; new `AiChatEngine` (`IAiChatService` over
  the registry: chat templating, tool-call parsing, streaming tool-markup holdback) and
  `AiApiRouter` (the full protocol surface - OpenAI + Ollama native + Anthropic Messages -
  transport-free over `IAiServerTransport`, so HTTP hosts and browser-worker MessagePort hosts run
  the same code).
- **SpawnDev.AI.Blazor**: project scaffold (components land next).
- Engine: SpawnDev.ILGPU.ML `4.0.0-preview.6-local.1` (the WebGPU decode capture/replay stack -
  browser decode 1.5 -> 34 tok/s token-identical on qwen2.5-0.5b/RTX 4070).
