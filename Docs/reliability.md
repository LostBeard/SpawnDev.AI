# Small-model reliability

The in-browser default is a tiny model (qwen2.5-0.5b) so the demo runs on modest hardware. Tiny
instruct models are **unreliable tool routers**: they refuse or forget to call a tool, or hallucinate
about a niche corpus. Two engine mechanisms make the common cases work regardless of model, without
relying on the model's own tool-calling decision. Both live in the shared `AiChatEngine`, so they apply
to the desktop host, the browser worker, and every protocol surface. Both are on by default and can be
disabled.

## Pre-emptive image-tool forcing

`AiChatEngine.ForceImageToolOnIntent` (default `true`).

A 0.5B refuses ~40% of plain image requests ("draw a cat" -> "I'm sorry, I can't draw"), and the
refusal is the greedy argmax - no prompt or sampling tweak makes it reliable. So when the latest user
turn clearly asks to **create a visual**, the engine bypasses the model's routing decision:

1. A conservative intent detector (`HasImageIntent`) matches an imperative draw/paint/sketch, or a
   create/show/want verb + a visual noun. It does not fire on ordinary chat that merely mentions a
   picture ("tell me about the Mona Lisa painting", "show me the code" both stay text).
2. The caption is **derived deterministically** from the user text (strip the imperative wrapper:
   "draw a cat" -> "a cat"). This touches no model, so a resident SD-Turbo stays resident and
   consecutive image requests run warm. Only a subject-less request ("make a picture") falls back to a
   short model caption pass.
3. `generate_image` runs; there is no refusal path.

Set `ForceImageToolOnIntent = false` for pure model-driven routing.

## GitHub grounding (RAG)

`AiChatEngine.GroundGitHubOnIntent` (default `true`), backed by the `github_lookup` tool.

A small model never calls `github_lookup` for a SpawnDev question and instead answers inaccurately from
memory (reductive or invented). So the engine grounds: it asks the `IAiGroundingProvider` for
authoritative reference text and injects it as context before generating. The GitHub tool recognizes
**any** LostBeard repo named in the digest, plus crew/list/general "spawndev/lostbeard" questions, and
returns the matching section (a compound "what is X and who is the crew" injects both). Grounding tools
are excluded from the callable tool list while grounding is on.

### The digest (one cached request, not the user's API budget)

Anonymous `api.github.com` is 60 requests/hour per IP - in the browser that's the *user's* IP. So the
authoritative core info is a single pre-built markdown digest, `spawndev-index.md`, fetched once from
`raw.githubusercontent.com` (CDN, cached in-session) rather than many live API calls:

- `tools/build-index.cs` gathers every non-fork LostBeard repo (bug-repro/test/demo repos filtered
  out) - SpawnDev libraries + the apps built with them - each with a description + README excerpt, plus
  the crew section.
- `.github/workflows/build-spawndev-index.yml` regenerates it twice daily using the Actions
  `GITHUB_TOKEN` (5000/hr), committing only when it changes.
- `github_lookup` is index-first: list + repo-overview calls serve from the cached digest (0 API
  calls); only a specific file, or a repo not in the digest, hits the live API.

Set `GroundGitHubOnIntent = false` to restore model-driven tool use; turning it off also makes
`github_lookup` model-callable again.

## Caveat

Grounding always supplies the correct data; a very small model may still not weave every part of a
compound answer into one response. This is a model-tier limit, not a grounding gap - a more capable
model uses the injected reference fully.
