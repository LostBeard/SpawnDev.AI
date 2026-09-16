# tools

UI-level gates for the demo. The test suite (`SpawnDev.AI.TestRunner`, tests in `SpawnDev.AI.Demo/Tests/`)
drives `AiWorkerClient` and covers the worker transport, GGUF decode and KV cache; these cover the **Razor
UI**, which the suite does not.

Run from the repo root (`SpawnDev.AI/`):

```
dotnet run tools/<name>.cs -- [url]
```

| tool | answers |
|---|---|
| `drive-ai-demo.cs` | Types into the composer and checks the answer - the single-shot UI gate. |
| `drive-ai-room.cs` | Group chat: makes two characters, sets the scene, adds both to the room, sends one message, asserts BOTH answered under their own names. Deletes what it created. |
| `drive-ai-reachy.cs` | HARDWARE gate: a character whose body is the real Reachy Mini. Reads the daemon's actual head pose out of band (baseline taken AWAKE, since wake_up alone lifts it ~0.5 rad) and requires a real classified gesture. Always parks in a finally: home, sleep, motors off. |
| `drive-chat-voice.cs` | The 🎤 button: records, transcribes in the worker, and lands an editable transcript in the composer. Asserts content words plus a 70% word-overlap floor. |
| `drive-hands-free.cs` | The 💬🔊 button, whole turn: **when** the loop stops listening (endpointing), when the reply lands, and whether the page **actually played audio** - `AudioBufferSourceNode.start` is hooked, so "it spoke" is a browser event, not a status string. Also prints the endpointer's ms/frame against its 32 ms realtime budget, and every transcription time across turns. |
| `tap-shared-worker.cs` | **Reads a SHARED worker's console**, which never reaches `page.Console`. Start Chrome with `--remote-debugging-port=9222`, open the app, then tap it. CDP lists shared workers as their own targets, so their logs were always readable - we just never asked. This is the instrument that found a 626-second model load. |
| `check-ui-layout.cs` | **Is the app still usable?** Message-box width against the composer, the model picker naming the model actually selected, a default avatar on the stage, and no horizontal scroll - at 1040px AND at 420px. Every check is a defect Captain found by LOOKING, that every functional gate passed. |
| `drive-ai-imgtest.cs` | Direct SD-Turbo image generation, bypassing the LLM. |
| `drive-ai-model.cs` · `drive-ai-coreside.cs` | Model selection / core-side paths. |
| `deploy-space.cs` | **Publishes the demo and deploys it to the Hugging Face Space** (`LostBeard/spawndev-ai`) - the only place the Reachy WebRTC path works, since an HTTPS page cannot reach the robot's LAN daemon. Takes no arguments, so right-click → "dotnet run script" works; `--dry-run` stages without pushing. Verifies the live page afterwards. See below. |
| `check-abi-drift.cs` | **Does the shipped IL still agree with the assemblies it loads next to?** Resolves every member reference between the SpawnDev assemblies in an output folder. Catches the one failure a build, a restore and a publish are all blind to - see below. Exits 1 on any break, and `run-ai-gate.cmd` runs it before the suite. |
| `check-webgpu-adapter.cs` | Which WebGPU adapter the browser actually gave us. |
| `build-index.cs` | Site index generation. |
| `serve-published.cs` | Serves a `dotnet publish` output statically with PMT's COOP/COEP headers - **the only correct way to measure the demo**, see below. |

## 🔴 The thing that hid two real bugs for a day: every gate runs `?worker=dedicated`

Sync access handles are **dedicated-worker only**. So SpawnDev.WebTorrent's `createWritable`/Blob fallback
runs in exactly the configuration a normal visitor gets - a SHARED worker - and a shared worker's console
does not reach `page.Console`. Every gate here passes `?worker=dedicated` **in order to be able to read
anything at all**, which means the gates systematically select the configuration that HIDES any
shared-worker-only defect.

Three real ones lived there while every gate was green: a content file sized by how much had arrived
rather than its true length (a half-downloaded model was just a shorter file, and the ONNX parser threw
`Unknown wire type: 6`), a cached Blob snapshot going stale mid-read (reported by the browser as
"permission problems ... after a reference to a file was acquired"), and a `getFile()` per read while
downloading that turned a model load into **626 seconds**.

Use `tap-shared-worker.cs` to read that console, and `AsyncFSFileStore.ForceWritableFallback` to exercise
the fallback path in a dedicated worker where a gate can assert on it.

## Deploying to the Hugging Face Space

```
dotnet run tools/deploy-space.cs              # or right-click -> "dotnet run script"
dotnet run tools/deploy-space.cs -- --dry-run # build and stage, push nothing
```

The Space exists because **Reachy only works from there**. The robot's daemon speaks plain HTTP on the
LAN, so an HTTPS page cannot reach it at all - the browser blocks it as mixed content - and the supported
route from a hosted page is WebRTC through Hugging Face's signalling Space. GitHub Pages cannot do the
sign-in half; a Space can, because `hf_oauth: true` gets it a registered OAuth app for free.

🔴 **`tools/space/README.md` and `tools/space/.gitattributes` are the Space's own files, versioned here.**
The deploy copies them in over the published output. They are not decoration:

- The README's frontmatter carries **`hf_oauth: true`**, which is what makes Hugging Face inject
  `OAUTH_CLIENT_ID` into the page. That id is the ONLY thing "Connect my Reachy" can sign in with -
  passing an invented client id fails with *"Not authenticated - call login() or pass a token"*, which
  reads like a token problem and is not. The deploy REFUSES to push a README without it (red-checked).
- `.gitattributes` is what puts `.wasm`/`.dat`/`.wav`/`.onnx` into LFS. Hugging Face rejects a push whose
  binaries are not in LFS; deleting that file once cost two rejected pushes.

⚠️ **The tool finds the repo from `[CallerFilePath]`, not from the working directory or
`AppContext.BaseDirectory`** - a single-file `dotnet run` builds into `%TEMP%\dotnet\...`, so anything
relative to the assembly lands nowhere near the repo, and Explorer's right-click sets an arbitrary
working directory. Verified by running it from `C:\`.

⚠️ It verifies AFTER pushing that the Space still serves the page and still injects `OAUTH_CLIENT_ID`. A
green push is not a working page, and the two things a bad deploy silently loses are exactly those.

## 🔴 The thing no build, restore or publish can see: a source-compatible, binary-BREAKING bump

SpawnJS 2.1.7 had `FileSystemWritableFileStream.Seek(ulong)`. 2.1.17 made it `Seek(long)`. C# recompiles
against either, so **the change is invisible to every build of every project**. But SpawnDev.WebTorrent
4.2.7 was compiled before the bump and its shipped IL still calls the `ulong` overload, while NuGet's
nearest-wins resolution loads 2.1.17 beside it. The result is a `MissingMethodException` in the browser,
on the one code path that calls it, with nothing reported anywhere earlier.

It cost a day: it surfaced during the ML 5.2.14 -> 5.2.15 gate, so it read as an ML regression, and the two
failing tests were written off as pre-existing and unrelated.

```
dotnet run tools/check-abi-drift.cs -- SpawnDev.AI.Demo/bin/Release/net10.0
```

It resolves **every** member reference between the SpawnDev assemblies that will sit in the folder
together - not just the ones some test executes, which is the point, since a bad reference only throws when
its path runs. Running it over the fixed graph found a SECOND break nobody had hit yet (`Truncate(ulong)`),
on the same real piece-write path.

⚠️ Point it at `bin/Release/net10.0`, not at a publish. Publish output is webcil-wrapped `.wasm`, which
Cecil cannot read; the `.dll`s in the build output carry the same IL.

⚠️ The fix is always **rebuild and republish the consuming package**, never a downstream workaround
(Rule 2). A rebuild against the current version is the entire fix - there is no source change to make.

## Two things that will cost you an hour otherwise

⚠️ **THE APP DOES NOT START ITSELF.** Until `StartAsync` runs, `_ready` is false and the page shows only a
**"Start the AI server"** button - the composer is not in the DOM at all. Waiting for it waits forever on a
page that looks alive and logs nothing. Every gate here clicks
`button:has-text("Start the AI server")` first, then waits for `.composer textarea`, whose appearance IS
the signal that the worker and WebGPU came up. `drive-ai-demo.cs` has always done this; copy from it rather
than rediscovering it.

⚠️ **Chrome's fake audio device produces digital silence on this machine** (measured in plain browser JS -
see `SpawnDev.ILGPU.ML/tools/probe-fake-mic.cs`). Frames still arrive and counters still advance, so an
audio gate can report "9 seconds captured" with every sample zero. `drive-chat-voice.cs` therefore replaces
`getUserMedia` before boot with a looping `BufferSource` of a known-transcript WAV
(`wwwroot/test-audio/librivox-public-domain.wav`, transcript in its `PROVENANCE.md`). The page's real
capture path runs unchanged; only the sound source is ours.

## A third thing that will cost you an hour

⚠️ **A `BufferSource` that ENDS is not a quiet room.** When it finishes it stops feeding its
`MediaStreamDestination`, so the page's capture simply stops receiving frames - which no microphone ever
does. `drive-chat-voice.cs` loops its clip and never notices; an ENDPOINTING gate must not, because the
silence after the talker stops is the whole thing being tested. Put the silence **inside the buffer**
(clip + N seconds of zeros, `loop = false`). Measured with the source merely ending: the demo's sample
counter froze at 4.0 s, even the 30 s fixed window never elapsed, and the gate reported a 75 s hang that a
real microphone could not have produced.

## 🔴 The thing that costs THREE DAYS otherwise: measure a PUBLISHED build

**A performance number taken under `dotnet run` is not a number.** `dotnet run` starts WasmAppHost, which
serves `bin/<cfg>/net10.0/wwwroot` - the BUILD output. PlaywrightMultiTest publishes. The two are not the
same app.

MEASURED 2026-09-04, same commit, same clip, same browser, only the build/serving path different:

| | `dotnet run -c Release` | `dotnet publish -c Release` + static |
|---|---|---|
| transcribe | 11,099 ms | **3,669 ms** |
| Whisper decode step | 947 ms | **328 ms** |
| speak | 60,299 ms | **26,199 ms** |
| warm-up to mic open | 12.2 s | **3.1 s** |

⚠️ **`-c Release` is not enough** - the slow run was already Release. It is BUILD output vs PUBLISH
output. Publish relinks and `wasm-opt -O2`s the runtime (the trees ship a different
`dotnet.native.*.wasm`, 3,128,737 B vs 3,006,472 B), and **SpawnDev.ILGPU transpiles .NET IL into GPU
shaders**, so the build configuration changes the generated WGSL as well as host-side speed.

The "demo is 3.5x slower than PMT" gap was blamed on Chrome WebGPU flags, then on window-vs-worker
execution, then on a stale NuGet package. It was this.

```
SpawnDev.AI.Demo/_buildRelease.bat                 # dotnet publish -c Release -o bin/PublishRelease
dotnet run tools/serve-published.cs -- SpawnDev.AI.Demo/bin/PublishRelease/wwwroot 5299
dotnet run tools/drive-hands-free.cs -- http://localhost:5299
```

The static server must send `Cross-Origin-Embedder-Policy: credentialless` and
`Cross-Origin-Opener-Policy: same-origin` (SharedArrayBuffer), which is what PMT's `StaticFileServer`
sets. `drive-hands-free.cs` now prints the runtime wasm it actually loaded, and shouts if pointed at
:5199.

⚠️ **A different port is a different ORIGIN, so OPFS starts EMPTY.** The first run on a new port pays
every model download and compile again (chat first token 274 s on the cold origin). Warm it before
reading any load-sensitive number.

## Useful flags

`--headed` to watch it · `--url http://localhost:5199/` to reuse a running server.

⚠️ `dotnet run` on the demo ignores `--urls` and takes the port from `launchSettings.json` (**5199**).

⚠️ Prefer INSTALLED Chrome (`Channel = "chrome"`). Playwright's bundled chromium exposes a SOFTWARE WebGPU
adapter, which reads as a hang rather than a config problem.
