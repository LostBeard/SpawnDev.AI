---
title: SpawnDev.AI
emoji: 🤖
colorFrom: blue
colorTo: purple
sdk: static
app_file: index.html
pinned: false
hf_oauth: true
---

# SpawnDev.AI

Local LLM chat, speech and image generation running **entirely in your browser** on the GPU, with no
server doing the inference - built on [SpawnDev.ILGPU.ML](https://github.com/LostBeard/SpawnDev.ILGPU.ML),
which transpiles C# to WebGPU compute shaders.

## Reachy Mini

Sign in with Hugging Face and press **🤗 Connect my Reachy** to give a character a body. The connection
goes over WebRTC through the central signalling Space, so it reaches **your own** robot - the one
registered to the account you signed in with.

- Wireless Reachy Mini only. The signalling server does not serve the Lite.
- Your robot must be signed in to Hugging Face and registered to your account.

Everyone else in the scene stays on screen.
