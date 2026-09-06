---
name: api-call-tool
description: 2026-09-05 API Call tool — model reads a configured HTTP API; own plain store + shared credential store; catalog on the node; tools now drivable by the pipeline
metadata: 
  node_type: memory
  type: project
  originSessionId: 71f196ff-e92a-4111-9164-b55cf9747339
  modified: 2026-09-06T00:45:11.940Z
---

**BUILT 2026-09-05, compiles and 48 new Core tests pass — NOT yet run in Rhino.** Lets a Physalia
pipeline give an LLM access to data over an API (the trigger case was Vancouver's Opendatasoft
portal).

**The shape, and the forks that decided it:**

- **A tool, with a secondary data output** — not a plain wired node. The credential concern
  constrains the tool's SCHEMA (the model sends a path and a query, never a URL or a header), not
  which tier the node sits in. `ApiRequest.ComposeUri` is the boundary and it verifies containment
  rather than trusting `new Uri(base, rel)`, which silently returns another host for an absolute
  input. GET only.
- **Two answers from one call.** Full body → `Response` output (what the definition actually wants);
  summary → the model (`ApiResponseSummary`: count, TOTAL matched, field names, one record). Blind
  truncation is what makes a model think a page was the whole result set.
- **Its own plain store, `api-endpoints.json`, NOT an extension of `ProviderCatalog`.** I proposed
  extending the catalog first and reversed it: a provider is a protocol the plug-in speaks (fixed
  table, one vocabulary); a user's REST API is a discovered integration (open-ended) — that is an
  MCP-server-shaped thing. The KEY still goes in the shared `credentials.dat` under `api:<name>`, so
  there is one encryption seam. `CredentialStore` validates no ids, which made that free.
- **No activation gate.** "Availability is not consent" exists because a provider can be found
  already configured on the machine. Nothing discovers an API endpoint — typing it in IS the opt-in.
- **The catalog/field list lives on the NODE (`Description` input), never in the store**, because a
  store is per-machine and a setting is only useful if it ships inside a preset. Same reasoning as
  MemoryTool's `Memory Folder`. It reaches the model via `GroundingDirective` → the PROMPT, not the
  tool definition: a tool description is read once the model is already weighing that call. There is
  no token argument either way — both ride every request.
- **The key is never pushed to the UI**; the page gets `hasKey`/`keySource`, so a blank key box means
  "keep the stored one" and clearing is a separate *forget* verb.

**The generalizable half — any tool can now be driven by the pipeline.** `LlmToolComponentBase`
already read its calls out of the signal's content blocks, so a hand-minted signal runs a tool for
free. The cost is the ANSWER: a `ToolResultContent` must echo an id the assistant actually emitted or
the provider 400s the request (same class as the compaction tool-pairing bug). So `ManualToolCall`
marks them `manual:` and a manual batch emits **no Result signal**; data leaves via the node's own
outputs. New `Construct Tool Call` node mints them. **Not relying on wiring discipline was the
point** — the model path REQUIRES the Result wire, so "just don't wire it" was never available.

**Config staleness, fixed for BOTH nodes (same session).** `ApiCall` and `McpServer` reloaded their
list only `if (_library.Count == 0)`, so editing an entry mid-session left the node on the startup
definition while the setup page showed the new one — the disagreement visible only on the node's
Status output. Now keyed on `FileRevision.Stamp` (write time + length; a coarse FS clock can put two
quick saves on the same tick), exposed as `RevisionStamp` on both stores and reused by the ChatWindow
push methods, so "has this file changed" has one definition. The confusing part was the asymmetry:
the KEY already refreshed live, because saving calls `PhyCredentials.Invalidate()`. On `McpServer` a
reload also resets discovery, but ONLY when the picked server's `Identity` changed — a stamp change
from editing a different entry must not drop a live session's tool list.

Deferred deliberately: `describe_dataset` / `search_datasets` for portals with too many datasets to
ground, and a Fetch button that seeds the Description box from the portal's own catalog.

Related: [[mcp-integration]], [[model-api-credentials]], [[settings-ownership]],
[[compaction-tool-pairing-fix]], [[memory-tool]].
