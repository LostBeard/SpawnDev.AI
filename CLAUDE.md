# CLAUDE.md - SpawnDev.AI

Local LLM + image-generation serving for .NET - desktop and browser - on SpawnDev.ILGPU.ML.
Read `D:\users\tj\Projects\CLAUDE.md` first (global rules).

## Layering (strict, no upward references)

```
SpawnDev.BlazorJS → SpawnDev.ILGPU / SpawnDev.WebTorrent → SpawnDev.ILGPU.ML → SpawnDev.AI → Demo
```

- `SpawnDev.AI` - contracts only (chat, tools/`IAiTool`, transports, wire frames).
- `SpawnDev.AI.Server` - engines (chat/image), protocol router (OpenAI + Ollama + Anthropic +
  /v1/images/generations), worker server/client, model providers. WASM-compatible (no ASP.NET).
- `SpawnDev.AI.ServerHost` - the desktop HTTP host (Kestrel, :11434 Ollama drop-in).
- `SpawnDev.AI.Demo` - the Blazor WASM demo (GitHub Pages).

## Hard rules

- **NEVER trim, NEVER AOT the demo/host publishes.** ILGPU resolves methods (Math.Clamp etc.) via
  reflection at runtime; a trimmed publish kills the worker with `MissingMethodException` in
  `RemappedIntrinsics` (hit on the very first Pages deploy, 2026-07-04). `PublishTrimmed=false`,
  `RunAOTCompilation=false` - same standing rule as the ML demo.
- **One transport-free router.** Protocol logic lives in `AiApiRouter` against `IAiServerTransport`;
  hosts only implement transports (HTTP, worker MessagePort). Never fork protocol code per host.
- **Tools register once, serve three surfaces** (internal agentic loop, MCP, protocol clients) via
  `AiToolRegistry`. Binary outputs go through the artifact store, never through model context.
- **Per-kind model residency**: one LLM (ModelRegistry) + one image model (AiImageEngine) resident;
  each kind swaps within its own slot.
- **Kill gate servers + verify VRAM freed** (`nvidia-smi`) when done - resident models hold GBs.
- Deploys: `.github/workflows/deploy-to-github-pages.yml` (manual dispatch). All PackageReferences
  must resolve from nuget.org (CI has no local feed) - verify with a cold-cache nuget.org-only
  restore before triggering.

## Diagnostics / env levers

- `ML_NO_SESSION_CAPTURE=1` - disable pipeline graph captures (SD capture is default-off anyway
  pending `SpawnDev.ILGPU.ML/Plans/sd-capture-pool-priming.md`).
- `SDTURBO_CAPTURE=clip|unet|vae`, `SDTURBO_FORCE_CAPTURE=1` - capture scoping for that work.
- `SPAWNDEV_AI_PORT` - host port (default 11434).

## Verification

Rule 5 applies: gate before Captain sees it.

**The test suite: `SpawnDev.AI.TestRunner`** (Captain 2026-08-30 chose the
`SpawnDev.SpawnJS.WebWorkers` harness style over PMT, and this is a port of it, so the console
contract `READY:` / `TEST: name|result|ms|detail` / `RESULTS:` is identical).

```
dotnet run --project SpawnDev.AI.TestRunner                 fast suite, no model download
dotnet run --project SpawnDev.AI.TestRunner -- --heavy      + the model-backed chat tests
dotnet run --project SpawnDev.AI.TestRunner -- MultiTurn    filter by name
dotnet run --project SpawnDev.AI.TestRunner -- --headed     watch it
dotnet run --project SpawnDev.AI.TestRunner -- --url http://localhost:5199/   reuse a running server
```

Exit code is the number of failures, so it is usable as a gate. Tests live in
`SpawnDev.AI.Demo/Tests/`, run in the WINDOW scope only, and are reached with `?tests=1`
(`&heavy=1`, `&filter=`) so a normal visitor never triggers a model download. They drive
`AiWorkerClient` - the same client the UI uses - so the worker transport, GGUF decode and KV cache
are all real. Add a test class to `AiTestSuiteRunner.TestTypes`.

- ⚠️ Mark anything that loads a model `Heavy = true`; the first run pulls hundreds of MB.
- ⚠️ The runner prefers INSTALLED Chrome. Playwright's bundled chromium exposes a SOFTWARE WebGPU
  adapter, which reads as a hang rather than a config problem.
- ⚠️ A precondition the test is ABOUT must be asserted, never skipped. `SkipTestException` is for a
  genuinely absent capability only.

Other live gates: desktop HTTP (all 3 protocol surfaces + /v1/images/generations + the agentic
generate_image loop), and `tools/drive-ai-demo.cs` - a single-shot UI-level Playwright gate that
types into the composer and checks the answer. Keep it: it covers the Razor UI, which the suite
does not.
