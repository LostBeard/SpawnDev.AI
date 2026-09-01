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
| `drive-chat-voice.cs` | The 🎤 button: records, transcribes in the worker, and lands an editable transcript in the composer. Asserts content words plus a 70% word-overlap floor. |
| `drive-hands-free.cs` | The 💬🔊 button, whole turn: **when** the loop stops listening (endpointing), when the reply lands, and whether the page **actually played audio** - `AudioBufferSourceNode.start` is hooked, so "it spoke" is a browser event, not a status string. Also prints the endpointer's ms/frame against its 32 ms realtime budget, and every transcription time across turns. |
| `drive-ai-imgtest.cs` | Direct SD-Turbo image generation, bypassing the LLM. |
| `drive-ai-model.cs` · `drive-ai-coreside.cs` | Model selection / core-side paths. |
| `check-webgpu-adapter.cs` | Which WebGPU adapter the browser actually gave us. |
| `build-index.cs` | Site index generation. |

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

## Useful flags

`--headed` to watch it · `--url http://localhost:5199/` to reuse a running server.

⚠️ `dotnet run` on the demo ignores `--urls` and takes the port from `launchSettings.json` (**5199**).

⚠️ Prefer INSTALLED Chrome (`Channel = "chrome"`). Playwright's bundled chromium exposes a SOFTWARE WebGPU
adapter, which reads as a hang rather than a config problem.
