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

## 1.0.0-preview.7 - LLM/image co-residence crash fix (one large GPU model resident per device)

- **Cross-kind GPU eviction.** The LLM (`ModelRegistry`) and the image pipeline (`AiImageEngine`) each held
  a multi-GB resident model on the SAME accelerator with no coordination, so an LLM + SD-Turbo (UNet 1.7 GB
  + VAE + text-encoder) co-residence blew past the WebGPU device budget → device loss → the browser page
  crashed. Reported symptoms: "the LLM can't do image-gen (crashes the page)" and "image-gen twice in a row
  crashes." Fix: each engine now evicts the OTHER kind before it loads/runs - an `EvictOtherKind` hook called
  gate-free at the start of `ModelRegistry.AcquireAsync` / `AiImageEngine.GenerateAsync`, plus an
  `EvictAsync()` on each, wired in `AiWorkerServer` - so only ONE large model is resident per device. Proven
  end-to-end on hardware WebGPU (real Chrome, `tools/drive-ai-coreside.cs`): chat → image-gen → image-gen →
  chat completes with NO page crash. Tradeoff: an LLM↔image switch reloads the incoming model (OPFS→GPU);
  letting a small LLM + tiled SD-Turbo co-reside when they actually fit is a follow-up optimization.

## 1.0.0-preview.10 - Index-aware grounding: any LostBeard repo, from one cached request

- **Pre-built digest (`spawndev-index.md`) + daily workflow.** `tools/build-index.cs` gathers every non-fork
  LostBeard repo (bug-repro/test/demo repos filtered out) into one markdown digest - SpawnDev libraries
  (primary) + the apps built with them (Anaglyphohol, AubsCraft, LostSpawns, rendusa, ...) + the crew, each
  with a description + README excerpt. `.github/workflows/build-spawndev-index.yml` refreshes it twice daily
  with the Actions GITHUB_TOKEN (5000/hr). The browser AI fetches this ONCE from `raw.githubusercontent.com`
  (CDN, cached in-session) instead of spending the user's anonymous `api.github.com` budget (60/hr per IP).
- **Grounding is now index-aware and works for ANY repo, via `IAiGroundingProvider`.** The grounding
  intent+resolution moved out of the engine into `GitHubTool` (which owns the digest): it recognizes any repo
  named in the digest - not just `SpawnDev.*` - plus crew/list/general "spawndev/lostbeard" questions, and
  returns the matching digest section (or a live overview if the digest lacks it). The engine just asks any
  registered `IAiGroundingProvider` for reference text and injects it. `github_lookup` itself is also
  index-first: list + repo-overview calls serve from the cached digest (0 api calls); only a specific file or
  a non-default-owner repo hits the live API.
- **Grounding tools are no longer advertised as model-callable.** Grounding already injects the answer as
  context, so dangling `github_lookup` in front of a small model just made it emit malformed calls (or try
  to "look up" the capital of France) instead of answering - any `IAiGroundingProvider` server tool is now
  excluded from the injected tool list. Crew questions inject only the crew section, not all ~70 repos.
  Verified on the 0.5B: 6/6 SpawnDev/app questions grounded correct (incl. Anaglyphohol from the digest),
  control ("capital of France") ungrounded and answered normally.
- Demo: "about SpawnDev 🧬" preset; system prompt reflects automatic grounding. Diagnostics: `probe-github`
  now covers a non-SpawnDev app (Anaglyphohol) and a control question.

## 1.0.0-preview.9 - GitHub lookup tool (ask about the SpawnDev libraries + crew)

- **`GitHubTool` (`github_lookup`).** A read-only, host-ALLOWLISTED GitHub tool so the chat model can answer
  questions about the SpawnDev libraries, their code/docs, and the crew. No args → lists all SpawnDev repos +
  descriptions; `{repo}` → that project's description + README (crew/credits preserved through truncation);
  `{repo,path}` → a specific file. Every request URL is built internally from a validated `owner/name`+path
  against `api.github.com` / `raw.githubusercontent.com` only (no SSRF; owner defaults to LostBeard), both
  CORS-friendly so it runs in the browser worker as-is. Anonymous, in-process cached. Verified directly:
  list/read/file all correct, 404 + path-traversal blocked.
- **GitHub grounding (`AiChatEngine.GroundGitHubOnIntent`, default on).** A small model NEVER calls the tool
  for SpawnDev questions (0/5 measured) and instead CONFIDENTLY HALLUCINATES ("SpawnDev.ILGPU enables OpenCL
  on Linux" - wrong). So when the message mentions "spawndev" (a zero-false-positive trigger; battery 0 FP/0
  FN over 9 cases), the engine pre-fetches the authoritative GitHub info and injects it as reference context.
  Result on the 0.5B: **5/5 grounded, answers now correct** - real crew list, "1,021 typed wrappers", the
  actual backend list - hallucination gone. Same shared-engine philosophy as the image forcing.
- **Demo:** default system prompt tells the model it can look up SpawnDev on GitHub; new "about SpawnDev 🧬"
  preset chip. Diagnostics: ServerHost `probe-github` (model call-rate), `probe-github-tool` (tool correctness).

## 1.0.0-preview.8 - Reliable image requests (pre-emptive tool forcing) + agent settings UI

- **The bug:** a small instruct model (qwen2.5-0.5b, the browser's default) REFUSES ~40% of plain image
  requests - "draw a cat" → *"I'm sorry, but I can't draw."* - because emitting the `generate_image` tool
  call is its own stochastic decision, and the refusal is the GREEDY ARGMAX (measured on the exact HF GGUF the
  browser streams AND the Ollama GGUF: 4/7 image, 3/7 refuse at both temp 0.3 and temp 0). No prompt or
  sampling tweak makes it reliable. This had been latent for a long time and only surfaced now that image
  generation itself works.
- **The fix (`AiChatEngine.ForceImageToolOnIntent`, default on).** When the latest user turn clearly asks to
  CREATE a visual, the engine bypasses the model's routing decision entirely: it prefills the assistant turn
  with the `generate_image` tool-call opener so the model can ONLY write the caption, then executes the tool
  itself. No refusal path exists. The model still authors the caption (greedy ~48-token pass); a deterministic
  strip of the user text is the fallback. Lives in the shared engine, so it fixes the desktop host, the
  browser worker, and every protocol surface at once. Measured on the browser's HF `qwen2.5-0.5b` GGUF:
  **7/7 image requests now generate (was 4/7), 0 refusals**, with clean model-authored captions ("draw a cat"
  → `a cat`). A precise intent detector (imperative draw/paint/sketch, or a create/show/want verb + a visual
  noun) scores **25/25 on a labeled battery, 0 false-positives / 0 false-negatives** - "tell me about the Mona
  Lisa painting", "who painted the Sistine Chapel?", "show me the code for a for loop" all correctly stay text.
  Control set through the full engine: 4/4 non-image prompts answer as TEXT with no spurious draws.
- **Caption source: deterministic first, model only as fallback (perf).** An LLM-prompted image was ~35s vs
  ~10s for the direct button because captioning with the model thrashed VRAM: it loaded the LLM (evicting the
  resident SD-Turbo), then image-gen evicted the LLM to reload SD-Turbo - two big loads per image, and every
  consecutive "draw X" repeated the thrash. The forced path now derives the caption by stripping the imperative
  off the user text ("draw a cat" → "a cat"; "draw a majestic dragon over a castle at sunset" keeps the full
  detail, which the 48-token model pass would truncate), touching NO model - so a resident SD-Turbo stays
  resident and consecutive image requests run ~10s warm like the direct button. The model caption pass remains
  only as a fallback for a subject-less request (bare "make a picture").
- **Demo: agent settings panel (⚙️).** The system prompt is now user-viewable/editable (with Reset to
  default), alongside Temperature and Max-response-tokens sliders. An empty system prompt is allowed (bare
  model). Model + image-model pickers already lived in the header.
- **Diagnostics:** `SpawnDev.AI.ServerHost probe-hub [model]` (loads the exact HF hub GGUF and measures
  tool-call reliability) and `probe-intent` (model-free intent-detector battery) - repeatable regression tools.
- Fixed the ServerHost's stale direct `SpawnDev.ILGPU` pin (4.17.2 → 4.17.6) that blocked its build against
  ML preview.13.
