# HTTP APIs — the model reads live data

> Split out of `CLAUDE.md` (2026-09-08) to keep that file under its size limit. Content is verbatim; CLAUDE.md links here.

## HTTP APIs — the model reads live data (built 2026-09-05)

An **API Call** node lets the model read from an HTTP API the user configured. Three rules shape it,
and each was a fork with a worse branch.

- **The model supplies a path and a query, never a URL and never a header.** That is the whole
  security posture. `ApiRequest.ComposeUri` ENFORCES it rather than trusting relative resolution,
  because `new Uri(baseUri, "https://elsewhere/")` quietly returns the other host — so the composed
  URI is checked back against the base for scheme, authority AND a path still beneath it (`..`
  climbs the path while staying on the host). A protocol-relative `//other-host/x` is caught by two
  different mechanisms depending on platform: on Windows .NET parses it as an absolute `file://` UNC
  URI so the whole-URL check refuses it; elsewhere it is not absolute and the leading-slash trim
  makes it an ordinary path segment. **GET only** — a model-authored request body is a much larger
  surface than a query string, and a write API belongs behind a node the human wires deliberately.
- **The answer goes two ways, and that is the point of the node.** The data lands on the
  **Response** output — LIST access, **one item per RECORD**, already unwrapped from the envelope and
  joined across pages (`ApiResponseSummary.ExtractRecords`); what goes BACK to the model is
  `ApiResponseSummary` — record count, *total* matched, field names, one sample record. A blind truncation hands the model the first few rows and no hint that more exist, which is
  how it concludes a query returned everything when it returned one page. Non-JSON degrades to
  truncation rather than refusing, since an API answering CSV or prose is still readable.
- **The tool walks the paging itself** (`ApiRequest.SendPagedAsync` → `ApiPagedResponse`), because a
  100-record page against a 145-record query otherwise delivers a fifth of the data to the canvas
  with nothing saying so. Five rules hold it together. (1) **The page size is measured, never
  assumed** — the next offset strides by what the last page actually returned, so a cap of 100, 50 or
  20 all walk with nothing configured; assuming a size either refetches rows or skips them, and
  skipping is silent. (2) **The style is endpoint config** (`ApiPaging`, default `None`), not
  detected: a cursor API handed offsets returns page one forever rather than failing, so guessing
  wrong is not a no-op. (3) **A failure part way through KEEPS the pages already gathered** and says
  why it stopped; only a failure on the first page is an error. (4) **The summary describes the SET,
  not the last page**, and a partial read says `THIS IS NOT THE WHOLE RESULT SET` with the numbers —
  `IsPartial` is true when anything stopped it *or* when fewer records came back than matched, since
  a walk ending tidily is not the same as a walk being complete. (5) The 100-page guard is the
  **runaway** bound, not the real one — `max_records` is; at 50 it silently became the limit for any
  API with a small page and reported stopping for a reason unrelated to what was asked.
  `max_records` is a tool argument (defaulting to one page — paging spends someone's quota, so it is
  opted into per call) clamped by a **`Max Records`** input on the node: the model's judgement about
  this question, bounded by the human's budget for all of them.
- **Records on the wire, not pages — and the model is told so in three places.** Handing over the raw
  bodies made the consumer unwrap each envelope, know which key *that* API nests its rows under, and
  concatenate; worse, the shape CHANGED with the result size, so a script written against a one-page
  test query broke on the real multi-page one. Observed live: the model simply did not accumulate.
  `ExtractRecords` flattens instead, which costs nothing because the pager already has to locate the
  rows to measure its stride — and it does NOT merge envelopes, since two disagreeing `total_count`
  values have no correct resolution. A body with no record collection (a single document, or non-JSON)
  falls back to one item per body, and the **first** page decides the shape for the whole call so the
  list can never be a mixture of records and bodies. Saying it once was the original mistake: the
  shape is now stated on the Response param, in the tool description, and in the `GroundingDirective`
  — the last of those because it is what a script author needs to know *before* writing the parser.
- **Not a third store, and not an extension of `ProviderCatalog`.** A provider is one of a handful of
  endpoints the plug-in speaks the protocol of — a fixed table, one vocabulary. A user's REST API is
  a discovered third-party integration, open-ended, exactly like an MCP server, so
  `%LOCALAPPDATA%/Physalia/api-endpoints.json` (`ApiEndpointStore`) is shaped like
  `mcp-servers.json` and `ProviderCatalog` was left alone entirely. **Plain, not encrypted**: an
  entry is a URL, a header name and possibly the NAME of an environment variable. The one secret —
  the key — goes in the SHARED credential store under `ApiEndpoint.CredentialId` (`api:<name>`), so
  there is still exactly ONE encryption seam in the repo. `CredentialStore` validates no ids, which
  is what makes that free.
- **`ApiKeyResolver` has the same two sources in the same order as the model providers** —
  environment variable named on the entry, then the store — with the environment lookup injected for
  the reason it is there: reading the real one makes the order untestable. **No activation gate**,
  deliberately: a provider can be found already configured on a machine, which is why availability
  had to be separated from consent there; nothing discovers an API endpoint, so typing it in IS the
  opt-in.
- **The catalog lives on the NODE, not in the store** — the `Description` input, ordinary
  internalized param data, so it is saved in the `.gh` and **ships inside a preset**. The store is
  per-user and per-machine; a pipeline shared without this arrives with its wiring and none of its
  knowledge. Same reasoning as MemoryTool's `Memory Folder` and ReadPdf's `PDF Folder`.
- **The description rides in the PROMPT, via `GroundingDirective`** — not in the tool definition.
  A tool description is read once the model is already weighing that call; a prompt is read before it
  decides there is anything to call. There is no token argument either way (tool definitions ride
  `Instructions.Tools` on every request, same as the system prompt); it is purely about when it is
  read. Same ruling as the Memory tool's standing instruction.
- Tool names are namespaced `api__<endpoint>` and sanitized, so two API nodes cannot collide on one
  Router key — same rule as `McpServer`. A node with no endpoint picked advertises **nothing**
  (`Definitions` empty), because a tool that fails every call reads to the model as a broken API
  rather than an unconfigured node.
- **The node re-reads the list when the FILE changes, not just when it holds nothing** — and this is
  shared with `McpServer`, which had the same defect. Both used to reload only `if (_library.Count
  == 0)`, so editing an entry mid-session left the node on the definition it loaded at startup while
  the setup page showed the new one; the only visible sign of the disagreement was the node's Status
  output. `FileRevision.Stamp` (write time + length — a coarse file-system clock can put two quick
  saves on the same tick) is exposed as `RevisionStamp` on both stores, and the ChatWindow push
  methods use it too, so there is ONE definition of "has this file changed". Note the asymmetry that
  made this confusing to hit: the KEY already refreshed live, because saving calls
  `PhyCredentials.Invalidate()` and `ApiKeyResolver` reads through the credential cache. **On
  `McpServer` a reload additionally resets discovery — but only when the PICKED server's
  `Identity` changed**, the same key the connection pool uses; a stamp change from editing a
  *different* entry must not drop a live session's tool list.
- The chat window's **API calls** page (Home screen and header menu) owns setup: name, base URL, auth
  form, optional key, optional env var, plus **Test** (a GET at the base URL, writing nothing).
  **The key is never pushed to the page** — only `hasKey`/`keySource` — so a blank key box on save
  means "leave the stored one alone", and clearing is its own *forget* verb. Deleting an endpoint
  also drops its stored key; an orphaned secret for an unreachable endpoint is a surprise, not a
  safeguard.

### A tool can be driven by the pipeline, not just the model
`LlmToolComponentBase` reads its calls from the dispatched signal's content blocks and does not care
who put them there — so **Construct Tool Call** (`Signals/`) mints a signal carrying a
`ToolCallContent` and runs any tool node directly. What that costs is the ANSWER:
`ToolResultContent` must echo an id the assistant actually emitted, and a provider rejects the whole
request when it does not (the same failure compaction's tool pairing exists to prevent). So
`ManualToolCall` marks such calls with a `manual:` id prefix — no provider issues an id containing a
colon — and a manual batch emits **no Result signal at all**; what it produced reaches the canvas
through the node's own outputs. Decided on the calls in `StartAsyncBatch`, not at latch time, since
the latch runs a solve later and a second batch may have started. A MIXED batch is treated as
model-driven: the model's calls still get answered, where the alternative is a round that never
completes. **Relying on the user not to wire the Result output is not a design** — the model path
requires that wire.

---

