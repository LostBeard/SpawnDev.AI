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

## 1.0.0-preview.2 - Model providers + the in-browser worker server

- **IAiModelProvider**: model sources abstracted - `OllamaCacheModelProvider` (desktop: Ollama's
  on-disk cache, zero-copy) and `HubModelProvider` (browser: GGUF streamed from the SpawnDev hub
  via WebTorrent/HF straight onto the GPU). `ModelRegistry` is now provider-independent;
  `LoadedModel.Info` replaces the file-coupled `Meta`.
- **AiWireFrame** (contracts): the message-boundary response frame (json/text/start/event/raw/end/
  error) mirroring IAiServerTransport writes 1:1.
- **AiWorkerServer / IAiWorkerApi**: the in-browser AI server - lives in a (shared) web worker,
  lazy-inits WebGPU + hub registry on first request, answers the full protocol surface over
  marshalled callback frames (SpawnDev.BlazorJS.WebWorkers expression dispatch).
- **AiWorkerClient**: window-side handle - attaches the shared worker (name "SpawnDevAI", dedicated
  fallback), `RequestJsonAsync` for buffered calls, `ChatStreamAsync` streaming message deltas over
  the Ollama-native NDJSON surface.
- **AddSpawnDevAI(options)**: one DI call registers server + client in all scopes.
- Desktop regression gate re-run after the provider refactor: /api/generate on CUDA still answers
  correctly through OllamaCacheModelProvider.
