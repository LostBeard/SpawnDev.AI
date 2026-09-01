# The voice-activity model

`silero_vad.onnx` — 643,854 bytes. This is what decides when you have stopped talking.

## Where it comes from

| | |
|---|---|
| Project | [snakers4/silero-vad](https://github.com/snakers4/silero-vad) |
| Licence | **MIT** |
| Variant | the 16 kHz model. The 8 kHz sibling is not shipped and `SileroVad` does not support it. |
| Copied from | `SpawnDev.ILGPU.ML/SpawnDev.ILGPU.ML.Demo/wwwroot/references/vad/silero_vad.onnx` |

## Why it is served from this app's own wwwroot

Whisper, SD-Turbo and ZipVoice arrive as lazy-hash torrents through the hub, because they are hundreds of
megabytes and want random access, OPFS caching and peer seeding. This is 643 KB and is needed before the
first word of the first turn — routing it through that machinery would add a hub round trip to the start of
every conversation to save nothing. It is a static asset, like the reference clip next door.

⚠️ It must stay reachable at `references/vad/silero_vad.onnx` from the app origin. `AiVadEngine` fetches it
relative to the worker's `HttpClient` base address, which is the app base URI.
