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

Rule 5 applies: gate before Captain sees it. Live gates so far: desktop HTTP (all 3 protocol
surfaces + /v1/images/generations + the agentic generate_image loop), browser shared-worker chat
(Playwright driver: `tools/drive-ai-demo.cs`). A PMT harness is the right long-term fix - flagged.
