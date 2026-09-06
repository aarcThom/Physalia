---
name: api-call-tool
description: 2026-09-05 API Call tool — model reads a configured HTTP API; own plain store + shared credential store; catalog on the node; the tool walks paging itself and delivers one item per record; any tool is now pipeline-drivable; store-backed nodes reload on a file stamp
metadata: 
  node_type: memory
  type: project
  originSessionId: 71f196ff-e92a-4111-9164-b55cf9747339
  modified: 2026-09-06T00:45:11.940Z
---

**BUILT 2026-09-05 over one session, 75 new Core tests (746 total).** Lets a Physalia pipeline give
an LLM access to data over an API (the trigger case was Vancouver's Opendatasoft portal).

**Verified live but NOT in Rhino.** The Core half was driven against the real portal through a
throwaway console harness ([[core-console-harness]]) — URL composition, the paging walk, and the
records that reach the wire. Everything GH-side is unexercised: the Picker, the `Max Records` input,
a list-access Response feeding a Python component, a real Router round, and the setup page in the
WebView. Four things below were found only by USING it (staleness, paging, the page-guard bound, the
output shape), which is the pattern to expect for the rest.

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

**Paging, added after a live session hit it (2026-09-05).** A 145-record query delivered its last
100-record page to the canvas while the model reasoned about all 145 — nothing in a page's shape
reveals the difference. `ApiRequest.SendPagedAsync` now walks it; `Response` became LIST access, one
body per page. Five load-bearing rules: page size is MEASURED from the last page not assumed (a wrong
guess skips rows, silently); the style is endpoint config (`ApiPaging`, default `None`) because a
cursor API given offsets returns page one forever instead of failing; a mid-walk failure KEEPS what it
gathered; the summary describes the SET and shouts when partial (`IsPartial` also true when records <
matched — ending tidily is not being complete); and the 100-page guard is the runaway bound, not the
real one — at 50 it silently became the limit for small-page APIs. `max_records` is a tool argument
(defaults to one page) clamped by a `Max Records` node input: model judgement inside human budget.
**Verified live** via a throwaway console harness ([[core-console-harness]]): 811 records over 9
requests, 5929 over 60, partial reads reporting correctly, `None` doing exactly one request. The
keep-what-you-have-on-failure path is by construction only, never exercised live.

**Records on the wire, not pages (2026-09-05, same day, from a live session).** With `Response` as a
list of page BODIES the model did not accumulate them — and that was the design's fault, not the
model's: it had to unwrap each envelope, know the API's own rows key, and concatenate, and the shape
CHANGED between a one-page and a many-page answer, so a parser written against a small test query
broke on the real one. `ApiResponseSummary.ExtractRecords` now flattens to ONE ITEM PER RECORD,
already joined across pages. Free, because the pager already locates the rows to measure its stride;
envelopes are deliberately NOT merged (two disagreeing `total_count`s have no correct answer). No
record collection → falls back to one item per body, with the FIRST page deciding the shape so the
list is never a mixture. **The wording fix mattered as much as the code**: the shape is stated on the
Response param, in the tool description AND in the `GroundingDirective` — saying it once, in passing,
in only the multi-page branch, was the original bug. Verified live: 73 / 811 / 5929 records on the
wire, `asRecords` true, matching the pager's own counts exactly.

**An unset capability has to name itself (2026-09-05, third live finding).** An endpoint configured
BEFORE `ApiPaging` existed has no `paging` key, so it deserializes to `None` and the node makes one
request however large `max_records` is. Correct behaviour — but the model, told only that it got
fewer records than matched, concluded the NODE was capped at 100 and reported that the pipeline
needed rebuilding. It was one dropdown nobody knew was unset. **The default being safe is not the
same as the default being visible.** Now: with paging off the tool description says so and names the
remedy, and a short read from an unpaged endpoint adds "no paging configured … tell the user to set
Paging on the API calls page — the pipeline itself needs no change" (`ApiPagedResponse.CanPage`
carries it). Applies to any Physalia setting that silently degrades: say which setting, and who can
change it. Note there is no migration for existing entries — they must be re-saved once.

Deferred deliberately: `describe_dataset` / `search_datasets` for portals with too many datasets to
ground, and a Fetch button that seeds the Description box from the portal's own catalog.

Related: [[mcp-integration]], [[model-api-credentials]], [[settings-ownership]],
[[compaction-tool-pairing-fix]], [[memory-tool]].
