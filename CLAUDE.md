# CLAUDE.md - SpawnDev.AI

Local LLM + image-generation serving for .NET - desktop and browser - on SpawnDev.ILGPU.ML.
Read `D:\users\tj\Projects\CLAUDE.md` first (global rules).

## Layering (strict, no upward references)

```
SpawnDev.BlazorJS → SpawnDev.ILGPU → SpawnDev.ILGPU.ML → SpawnDev.AI → Demo
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
dotnet run --project SpawnDev.AI.TestRunner -- --dev        dev BUILD, not a publish (fast loop)
dotnet run --project SpawnDev.AI.TestRunner -- --url http://localhost:5199/   reuse a running server
```

🔴 **The runner PUBLISHES the demo and serves it (default since 2026-09-14). A `dotnet run` build is not
the app.** Blazor WASM is only relinked and wasm-opt'd on publish, and this suite asserts on TIMINGS.
MEASURED, same card, same utterance, the voice test's warm synthesis: **9,089 ms against a dev-server
build, 4,493 ms published** - so every performance number this runner produced before it published was
pessimistic by 2x or more, which is worse than no number: it reads as a product regression rather than as
the harness measuring the wrong artifact. `--dev` opts back into the fast loop and says so in its output.
PMT has always published for the same reason.

⚠️ **A number from this runner is still not PMT's number.** Published, the same utterance takes 4,493 ms
in the demo WORKER against 1,837 ms in PMT's page with nothing else resident - a 2.4x gap that publishing
does NOT explain and that would apply to every model the demo runs. Unexplained as of 2026-09-14; do not
quote a worker timing as the engine's.

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

**Release gate for SpawnDev.AI.Server** (what every 1.4.x shipped on, RTX 4070, card quiet - ask Tuvok first):

```
(delete bin/obj of SpawnDev.AI, .Server, .ServerHost, .Demo, .TestRunner after any SpawnDev.ILGPU.ML version change)
dotnet run --project SpawnDev.AI.TestRunner -c Release -- --heavy --verbose      89 tests incl. browser DecodeRate
dotnet run --project SpawnDev.AI.ServerHost -c Release -- pass-test               PassObserver, 15 checks
dotnet run --project SpawnDev.AI.ServerHost -c Release -- lora-test cuda          LoRA adapters, 10 checks (9/10 since ML 5.8.7: syn-30 near-tie, below)
CHATBENCH_GREEDY_OUT=out.json dotnet run --project SpawnDev.AI.ServerHost -c Release -- chat-bench qwen3:1.7b-q8_0 cuda 0
                                                                                   (and contextWords 1300) decode rate + greedy text
dotnet run --project SpawnDev.AI.ServerHost -c Release -- lora-ab cuda smollm2:360m-instruct-q8_0 out.json
                                                                                   adapter over a Q8_0 base, ids for cross-version diff
```

- Compare the `CHATBENCH_GREEDY_OUT` / `lora-ab` JSON against the previous ML version's: identical is the expectation; an
  ML change that alters float order (Q8 vector GEMV, Q8_0G regrouping) can flip a near-tie, and ML exposes `GGUF_*`
  switches to turn those paths off for an exact comparison - ask Tuvok which ones the release touched.
- `lora-test opencl`: the session engine's base phase is 23/24 since ML 5.8.4 (syn-30 step 3, a near-tie: logits
  11.40754 vs 11.39705, flipped by a tree-summed RMSNorm). Not a gate failure.
- `lora-test cuda`: the session engine also flips syn-30 at step 3 since ML 5.8.7+ (FP contraction, explicit FMAs): '.' 11.4022
  vs '0' 11.4019, a margin of 3e-4, against an engine-to-engine logit gap of 5e-3 at that step (the batch decoder: '0' 11.4069
  vs '.' 11.3970). A near-tie, so CUDA is 9/10 with that one line; 24/24 on the batch decoder. Measured by Tuvok with ML's
  `Bench_OllamaGreedyDivergence_Margin` (GREEDY_DIV_MODEL=smollm2:360m, GREEDY_DIV_PROMPT_IDS=<syn-30 prompt_ids>,
  GREEDY_DIV_STEPS=9). Any OTHER miss is a real failure: `lora-test` prints each miss's line id and first differing step.
- Then a COLD nuget.org-only restore of Server, ServerHost, Demo and TestRunner (empty `NUGET_PACKAGES`, a config with only
  nuget.org). Pass `--no-cache` if an earlier restore ran before the new ML version was indexed: NuGet's HTTP cache keeps
  the old version list (~30 min) and the restore says "Nearest version" is the previous one.
- ⭐ `lora-test` is what caught ML 5.8.4's stale CUDA-graph replay after an in-place adapter clear (2/24) - keep it in.
