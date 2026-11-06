# Engines

Everything in `SpawnDev.AI.Server` runs on one shared SpawnDev.ILGPU accelerator (CUDA, OpenCL or Velocity on the
desktop, WebGPU in the browser). Each engine follows the same shape: one resident model of its kind, a load gate,
`EnsureReadyAsync()` to load ahead of the first call, and `EvictAsync()` to free the GPU. Models stream through
`IModelSource` (HTTP into OPFS in the browser, the hub on the desktop) unless noted.

## GPU residency

`GpuResidency` decides which models may stay on the GPU together and evicts only when a new model genuinely does not
fit. It replaced an eviction ring in which every model kind evicted every other kind before loading (measured: three
interleaved image + chat turns spent ~130 s re-uploading an SD-Turbo UNet on a GPU with 10.6 GB free). It also keeps
headroom, because letting everything co-reside wedged a 12 GB card at 96% when Whisper joined an 8B LLM and the voice
cloner.

## Chat (`AiChatEngine`, `ModelRegistry`)

- **Two decoders.** `GgufEngine.Auto` (the default on `OllamaCacheModelProvider` and `HubModelProvider`) uses the batch
  decoder for qwen2 / qwen3 / llama on CUDA / OpenCL / WebGPU, and the session generator otherwise. llama3.1:8b on an
  RTX 4070 with a 3.7k-token prompt: 200 new tokens over a cached prefix reach the first token in 204 ms, a full prompt
  reads at 1,660 tok/s, decode runs at 45.5 tok/s. Tensor-core prefill on CUDA sm_75+. Q8_0 models (SpawnDev.ILGPU.ML 5.8.6):
  qwen3:1.7b-q8_0 on an RTX 4070 decodes at 166.9 tok/s on CUDA at a short prompt and 141.3 at 1,602 prompt tokens
  (ServerHost `chat-bench`); in Chrome on WebGPU at about 166 tok/s (measured by SpawnDev.ILGPU.ML). The batch decoder
  regroups Q8_0 weights in place at load (`GGMLType.Q8_0G`: same bytes, no extra memory), so code that inspects the
  decoder's weight types sees `Q8_0G`, not `Q8_0`.
- **Timings.** `AiChatResult.Timings` (`AiChatTimings`: load, prompt eval, eval, total ms) and `AiChatUsage`
  (`DecodeTokensPerSecond`). The Ollama routes report the same split; see [protocols.md](protocols.md#timings).
- **`PassObserver`** (`Action<AiChatPass>?`): every generation pass, each tool round included, with the exact
  `PromptTokenIds` sent (chat template applied), `RenderedPrompt` (those ids as text with special tokens kept),
  `GeneratedTokenIds`, `EndOfTurnTokenIds`, `EndTokenId` (the stop token that ended the pass; null on a length stop),
  `RawOutput` / `VisibleText`, token counts, reused prefix, timings and `AdapterLabel`. Prompt ids + generated ids + the
  end token is the inference sequence by construction, which is what training data for the same model should be. A null
  observer costs nothing; one that throws is reported through PerfLog and does not fail the chat.
- **`TokenizeAsync(model, text)` / `DecodeAsync(model, ids)`**: the tokenizer exactly as the chat template uses it.

### LoRA adapters

```csharp
await registry.SetAdapterAsync("llama3.1:8b", @"C:\adapters\my-lora");   // a PEFT adapter directory
await registry.SetAdapterAsync("llama3.1:8b", adapterSet, "my-label");    // a SpawnDev.ILGPU.ML LoraAdapterSet
await registry.SetAdapterAsync("llama3.1:8b", (string?)null);             // back to the base model
```

The adapter is remembered per model and applied at every load. On a resident batch-decoder model it is swapped in place
(no reload); otherwise the model reloads with it. Works on both decoders. `IAiModelProvider.LoadAsync` has a LoRA
overload whose default implementation loads the base model only.

## Memory (`AiMemoryStore`, `AiMemoryEngine`)

`AiMemoryStore` (in `SpawnDev.AI`) owns long-term memories (episodes, facts, lessons) and their rules: hard deletes that
cascade to whatever was derived from an entry, `ForgetSubject`, do-not-learn, `Supersede` history and `Compact`.
`AiMemoryEngine` embeds text (all-MiniLM-L6-v2 by default) and searches the store on the accelerator by meaning,
recency and importance; only the k best results leave the device. 50,000 entries: p95 5.6 ms on an RTX 4070 including
the embed. `ConsolidateAsync(chat, model, ...)` turns episodes into facts with provenance; conflicting facts are
superseded.

## Faces (`AiFaceEngine`)

YuNet finds faces, SFace embeds each one, and a registry of introduced faces says whose it is (`LookAsync`, `Enroll`,
`Registry.Forget`). A face nobody has named is filed under a provisional guest that expires (`GuestLifetime`). Face embeddings are personal data: the engine sends them
nowhere, and the registry can forget one person or everyone.

## Voices and speech

| Engine | Model | What it does |
|---|---|---|
| `AiSpeechEngine` | Whisper (default `whisper-tiny`) or Parakeet TDT 0.6B v3 (set `ModelRepo`) | Transcribes a finished utterance. Parakeet: 3.75% WER on 60 LibriSpeech clips at a 168 ms median (Whisper tiny: 11.53%). |
| `AiStreamingSpeechEngine` | NVIDIA Nemotron Speech Streaming 0.6B | Words while they are spoken: `BeginAsync()`, feed 16 kHz audio to the stream, `FinishAsync()`. 5.63% WER; a 160 ms chunk in ~70 ms. One stream at a time. |
| `AiVadEngine` | Silero | Where an utterance starts and stops; returns spans into the caller's own stream, not audio. |
| `AiTurnEngine` | Smart Turn v3.2 | How likely the speaker is finished, judged from the last 8 s of audio. |
| `AiSpeakerEngine` | ReDimNet2 | Who is speaking (`IdentifyAsync`, `ObserveAsync`, `EnrollAsync`). 99.2% closed-set identification, EER 1.2% on 40 LibriSpeech speakers. `ReleaseWorkingMemoryAsync()` frees its working pool (~0.7 GB with SpawnDev.ILGPU.ML 5.8.1+). |
| `AiVoiceEngine` | Kokoro (built-in voices), ZipVoice (cloning) | Text to speech. The voice id picks the model: Kokoro renders faster than realtime in a browser; ZipVoice speaks in a recorded voice but is slower than realtime. |

## Images (`AiImageEngine`)

SD-Turbo text-to-image in its own residency slot. Serves `/v1/images/generations`, the `generate_image` tool and MCP.

## Installing models (`OllamaModelInstaller`)

Installs Ollama models into an Ollama-layout cache (the one `OllamaModelStore` and `OllamaCacheModelProvider` read, and
Ollama itself reads) without Ollama, so an app can fetch its model on first run. Sources: models.spawndev.com, then
registry.ollama.ai. Each layer is hashed as it streams, a partial download resumes with a Range request, and the
manifest is written last, so an interrupted install is invisible rather than broken.

```csharp
var installer = new OllamaModelInstaller(http);              // the default Ollama models directory
var model = await installer.InstallAsync("qwen3:4b");
```
