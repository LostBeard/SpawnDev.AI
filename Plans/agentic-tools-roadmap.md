# Agentic tools roadmap (TJ's ideas, 2026-07-13)

Captured from TJ during the tool-calling fix. All build on the existing `IAiTool` / `AiToolRegistry`
contract: ONE registration serves THREE surfaces (internal agentic loop, MCP `tools/list`+`tools/call`,
and protocol clients). Binary outputs go through the artifact store, never model context. The tools run
where the engine runs - the browser WORKER on the demo, the desktop host otherwise - so browser CORS /
permission / OPFS semantics apply on the WASM path.

**Cross-cutting reliability note:** small local models are unreliable tool-routers (see the image-tool
fix - a 0.5B refuses/omits ~40% of the time, and the refusal is the greedy argmax). Every tool below
needs the same treatment we gave `generate_image`: either a bigger default model for agentic flows, or
deterministic intent-detection + forced/guided invocation, or an explicit "tools mode" the user turns on.
Do NOT ship a tool that only works when the model spontaneously decides to call it. See memory
`feedback-small-model-toolcalling-unreliable-force-on-intent`.

## 1. HTTP fetch tool → ask about SpawnDev libraries + the crew  ✅ SHIPPED (preview.9/.10)
Done: `GitHubTool` (`github_lookup`) - read-only, host-allowlisted GitHub access (api.github.com +
raw.githubusercontent.com), works for any LostBeard repo. Grounding (not model tool-calling) makes it
reliable on the 0.5B: the engine pre-fetches authoritative info and injects it as context. A daily
GitHub-Actions-built digest (`spawndev-index.md`, fetched once from the CDN) is the single-request core
source; `IAiGroundingProvider` tools ground only (never advertised as callable to the small model).
Original design notes below, kept for reference.

Let the LLM answer questions about our libraries/team by fetching from GitHub.
- Tool: `web_fetch` (or scoped `spawndev_docs`) - args `{ url }` or `{ repo, path }`.
- Source: `api.github.com` (repo metadata, README, releases, issues), `raw.githubusercontent.com`
  (file contents), the LostBeard org repos. README crew section is the "team" answer.
- **Security = allowlist.** Restrict to an explicit host allowlist (github.com, api.github.com,
  raw.githubusercontent.com) to prevent SSRF / arbitrary-URL abuse. In-browser `fetch` is sandboxed +
  CORS-gated (GitHub APIs send permissive CORS), which helps, but the allowlist is the real gate. No
  auth token in the browser build; anonymous GitHub rate limits apply (cache responses in OPFS).
- Output: text into model context (small - summaries/snippets), or an artifact for large files.
- Nice follow-on: a tiny local retrieval index over our READMEs/CHANGELOGs cached in OPFS so answers
  don't burn a fetch every turn.

## 2. OPFS notes / memory / chat-history tool
Let the AI persist and recall across sessions - the browser is the runtime, so give it a filesystem.
- Reuse the SAME storage layer the model cache uses: `IAsyncFS` /
  `AsyncFSFileSystemDirectoryHandle` (OPFS via `FileSystemDirectoryHandle`). Bytes stay JS-side (Rule 4).
- Tools: `save_note {name, content}`, `read_note {name}`, `list_notes`, `append_memory {content}`,
  plus chat-history save/restore (persist the transcript + resume it on load).
- Chat history: serialize `_messages` to an OPFS file per conversation; a session list UI to resume.
  "Memory" = an append-only notes file the system prompt is told to consult (or we inject relevant
  notes into context).
- Guardrail: sandbox all paths under one app dir (e.g. `/spawndev-ai/`); no `..` traversal.

## 3. User-selectable external folders (later)
Extend #2 beyond OPFS to real directories the user picks.
- `showDirectoryPicker()` → `FileSystemDirectoryHandle`; persist the handle in IndexedDB; re-request
  permission (`queryPermission`/`requestPermission`) on return visits (needs a user gesture).
- Same `IAsyncFS` abstraction already models a directory handle, so the tool code is storage-backend
  agnostic - OPFS vs picked-folder is just which handle backs it.
- This is what turns "keeps notes" into "reads/writes MY project files."

## 4. Coding help (varying degrees)
Builds on #3: once the AI can read/write a picked folder, it can read code, propose edits, write files.
- Degrees: (a) read + explain, (b) suggest diffs shown to the user, (c) apply edits to the folder with
  confirmation. Keep a confirm-before-write gate; never silent-overwrite (mirrors our own rules).
- Realistically wants a stronger model than the 0.5B for useful edits; pairs with a model-picker default
  bump when "coding mode" is on.

## Sequencing suggestion
#1 (self-contained, high demo value, showcases the crew) → #2 (unlocks persistence/memory) →
#3 (external folders) → #4 (coding). Each is one `IAiTool` + registration; the registry + MCP surface
already carry them to every client.
