# SpawnDev.AI Documentation

Detailed reference for SpawnDev.AI. The top-level [README](../README.md) is the concise overview; the
factual details live here.

| Doc | Covers |
|---|---|
| [protocols.md](protocols.md) | Every HTTP/worker endpoint and which client each is compatible with (OpenAI, Ollama, Anthropic, MCP, image generation). |
| [hosting.md](hosting.md) | Running the server on the desktop (Kestrel, `:11434`) and in the browser (shared worker on WebGPU). The Blazor demo. |
| [tools.md](tools.md) | The server-side tool system: `IAiTool`, `AiToolRegistry`, the built-in `generate_image` and `github_lookup` tools, the artifact store, and the MCP surface. |
| [reliability.md](reliability.md) | How small in-browser models are made reliable: pre-emptive image-tool forcing and GitHub grounding (RAG) over a daily-built repository digest. |

All four are transport-neutral: the same `AiApiRouter` / `AiChatEngine` code runs over HTTP on the
desktop and over a MessagePort in a browser worker.
