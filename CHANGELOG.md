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

## 1.0.0-preview.3 - Image generation + server-side tools (the agentic loop)

- **SpawnDev.AI**: `IAiTool` / `AiToolExecutionResult` / `AiToolArtifact` / `AiToolRegistry` - the
  server-side tool contracts (JSON-in/JSON-out; binary artifacts travel out of band via the bounded
  artifact store). `AiChatResult.Artifacts` carries tool-produced binaries to typed clients.
- **SpawnDev.AI.Server**:
  - `AiImageEngine`: image-model residency slot beside the LLM (per-kind residency), hub-streamed
    weights, serialized generation. Verified-first model list (sd-turbo, E2E-gated).
  - `GenerateImageTool`: the built-in image tool; PNG via the new dependency-free `PngEncoder`
    (works on desktop AND Blazor WASM).
  - **Agentic loop in `AiChatEngine`**: when the client sends no tools and server tools are
    registered, definitions are injected, the model's calls are EXECUTED server-side, results
    re-enter the conversation (bounded rounds), and artifact references are appended
    deterministically as `ai-artifact://{id}` markdown.
  - `AiApiRouter`: OpenAI-compatible `POST /v1/images/generations` (b64_json) +
    `GET /ai/artifacts/{id}`.
- VERIFIED LIVE (RTX 4070): /v1/images/generations produced a photorealistic fox (seed 7); the
  full agentic chain - "draw me a sailboat at sunset" through qwen2.5-coder-7B calling
  generate_image, SD-Turbo painting it, qwen describing it - produced a painterly sailboat,
  fetched by artifact id.
- Engine: SpawnDev.ILGPU.ML 4.0.0-preview.6-local.3.

## 1.0.0-preview.5 - Storage-quota runaway fix + browser storage management

- **Storage-quota browser-download runaway FIXED** (engine dep bump → SpawnDev.ILGPU.ML
  4.0.0-preview.7, which pulls SpawnDev.WebTorrent 3.2.12 + SpawnDev.BlazorJS 3.5.15). A browser
  model download whose OPFS piece store hit the quota used to re-request the same piece forever (169
  identical range GETs; origin ballooning) because a failing OPFS write leaked its swap file per
  attempt and the torrent re-requested the unflagged piece. Now the write aborts-on-throw (no swap
  leak) and the torrent classifies the failure and pauses with an `OnError` instead of hot-looping.
- **Browser storage management UI** (Seven): the chat footer shows OPFS usage/quota with a "clear"
  button - OPFS is invisible to Chrome DevTools ("Clear site data" doesn't touch it), so the app is
  its own storage manager. Plus a `?worker=dedicated` diagnostic switch (`PreferSharedWorker`) that
  forces a dedicated worker.

## 1.0.0-preview.6 - MCP tools surface

- **MCP (Model Context Protocol) surface** - `POST /mcp` on `AiApiRouter` (JSON-RPC 2.0, gated on a tool
  registry): `initialize` (echoes the client's protocol version), `tools/list` (each tool's `name` /
  `description` / `inputSchema` from its JSON schema), `tools/call` (executes the tool; returns `content`
  as text plus any image artifacts inline as base64 `image` content; tool errors in-band via `isError`),
  `ping`, and JSON-RPC notifications (202, no body). This is the third of the three surfaces the tool
  registry was designed for (internal agentic loop + OpenAI/Ollama/Anthropic protocols + MCP) - one
  `IAiTool` registration now also serves any MCP client (Claude CLI, agents). Verified end-to-end against
  the desktop HTTP host: initialize / tools/list / ping / notification / error paths, plus a real
  `generate_image` call returning a 512x512 PNG as inline MCP image content.
