# Hosting

The protocol logic (`AiApiRouter` + `AiChatEngine`) is transport-free. A host only implements
`IAiServerTransport` (how to write a response) and provides an accelerator + a model provider.

## Desktop (HTTP, Ollama drop-in)

`SpawnDev.AI.ServerHost` is a thin Kestrel host listening on `:11434` (override with
`SPAWNDEV_AI_PORT`). It picks the best accelerator (CUDA > OpenCL > CPU) and reads models straight from
Ollama's on-disk cache (content-addressed blobs, zero-copy) via `OllamaCacheModelProvider`.

```csharp
var store = new OllamaModelStore();                     // ~/.ollama/models (or OLLAMA_MODELS)
var registry = new ModelRegistry(new OllamaCacheModelProvider(store), accelerator);
var engine = new AiChatEngine(registry);
using var images = new AiImageEngine(webTorrent, http, accelerator);
var tools = new AiToolRegistry();
tools.Register(new GenerateImageTool(images, tools));
tools.Register(new GitHubTool(http));
engine.Tools = tools;
var router = new AiApiRouter(engine) { Images = images, Tools = tools };
// map each HTTP request -> router.TryHandleAsync(method, path, body, new HttpAiServerTransport(ctx))
```

Point any Ollama / OpenAI / Anthropic client at `http://localhost:11434`.

## Browser (shared worker, WebGPU)

`AiWorkerServer` runs the identical router inside a (shared) web worker, owning the WebGPU accelerator
and the model registry; window-side code talks to it through `AiWorkerClient`, which speaks the same
protocol surface over marshalled callback frames instead of sockets. Models stream from the SpawnDev
hub (WebTorrent + HuggingFace CDN) straight onto the GPU and cache in OPFS via `HubModelProvider`. One
large GPU model is resident per device: the LLM and SD-Turbo evict each other before loading, so they
never co-reside (co-residence exceeded the WebGPU device budget).

Register in DI in **all** scopes (the same `Program.cs` runs in window and worker; only the worker
instance touches the GPU):

```csharp
builder.Services.AddSpawnDevAI(options =>
{
    options.Models.Add(new HubModelOption("qwen2.5:0.5b-instruct-q8_0",
        "Qwen/Qwen2.5-0.5B-Instruct-GGUF", "qwen2.5-0.5b-instruct-q8_0.gguf"));
});
```

## The demo

`SpawnDev.AI.Demo` is a Blazor WebAssembly app (GitHub Pages) that starts the shared-worker server on
one click: LLM chat + image generation, 100% on the visitor's GPU. It exposes a model picker, an
agent-settings panel (editable system prompt, temperature, max-tokens), and lets the chat model both
paint images and answer questions about the SpawnDev libraries (see [reliability.md](reliability.md)).

**Publishes must not trim or AOT.** ILGPU resolves methods (e.g. `Math.Clamp`) via reflection at
runtime; a trimmed publish kills the worker with `MissingMethodException`. Keep `PublishTrimmed=false`
and `RunAOTCompilation=false`.
