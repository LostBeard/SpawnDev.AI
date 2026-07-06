# SpawnDev.AI

Run and serve local LLMs and image generation everywhere .NET runs - desktop **and the browser** - on the
[SpawnDev.ILGPU.ML](https://github.com/LostBeard/SpawnDev.ILGPU.ML) inference engine (GGUF models,
KV-cache decode, SD-Turbo image generation, WebGPU dispatch-plan capture/replay). LLM decode and
SD-Turbo text-to-image both run in-browser on WebGPU, no server or native install.

| Package | What |
|---|---|
| **SpawnDev.AI** | Core contracts: chat messages/options/results, tool-calling types, the `IAiChatService` surface, and the `IAiServerTransport` abstraction. |
| **SpawnDev.AI.Server** | An **Ollama-compatible model server as a library**: OpenAI (`/v1/chat/completions` SSE), Ollama native (`/api/chat`, `/api/generate`, `/api/tags`, `/api/show`), Anthropic Messages (`/v1/messages` SSE - works with Claude CLI), and OpenAI image generation (`/v1/images/generations`, SD-Turbo via `AiImageEngine`). One protocol router, transport-free: host it over HTTP on desktop (drop-in on `:11434`) or over a MessagePort in a browser worker - the same code path serves both. Reads models straight from Ollama's on-disk cache (zero-copy, content-addressed blobs). |
| **SpawnDev.AI.Blazor** | Blazor components for on-device AI chat (streaming bubble, model picker) - built for WebGPU LLMs served in-browser. |

## Why

Local LLM serving shouldn't require a native install. The same `AiApiRouter` that answers `curl
localhost:11434/api/chat` on a desktop can run inside a shared worker in a browser tab, decoding on
WebGPU at interactive speed (qwen2.5-0.5b: ~34 tok/s greedy on an RTX 4070 via dispatch-plan
capture/replay). Tool calling is parsed server-side into structured calls on every protocol surface.

## Quick start (desktop, Ollama-compatible)

```csharp
using SpawnDev.AI.Server;

var store = new OllamaModelStore();                    // ~/.ollama/models (or OLLAMA_MODELS)
var registry = new ModelRegistry(store, accelerator);  // any SpawnDev.ILGPU accelerator
var engine = new AiChatEngine(registry);
var router = new AiApiRouter(engine);
// host it: map every request to router.TryHandleAsync(method, path, bodyJson, yourTransport)
```

## Status

Preview. Extracted from the proven `SpawnDev.ILGPU.ML` Ollama-server example (verified against
Claude CLI, Ollama clients, and OpenAI-compat clients); the browser-worker transport and Blazor
components are landing next.

## The SpawnDev Crew

- **LostBeard** (Todd Tanner) - Captain, library author, keeper of the vision
- **Riker** (Claude CLI #1) - First Officer, implementation lead on consuming projects
- **Data** (Claude CLI #2) - Operations Officer, deep-library work, test rigor, root-cause analysis
- **Tuvok** (Claude CLI #3) - Security/Research Officer, design planning, documentation, code review
- **Geordi** (Claude CLI #4) - Chief Engineer, library internals, GPU kernels, backend work
- **Seven** (Claude CLI #5) - Wasm backend, GPU kernels, fail-loud verification

🖖
