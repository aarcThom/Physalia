---
name: mcp-bridge-chunked-body
description: "Why Adobe Illustrator's MCP server failed through Physalia.McpBridge — chunked request bodies and a 404 on the optional GET stream — and the three fixes."
metadata: 
  node_type: memory
  type: project
  originSessionId: d99ac3f2-6497-4903-b063-925646c7cd5c
  modified: 2026-09-04T13:07:41.056Z
---

Debugged live 2026-09-04 against Adobe Illustrator's built-in MCP server
(`http://localhost:18412/v1/mcp`, static `Authorization: Bearer ilst_…`, 47 tools). Three separate
defects, only one of them Illustrator's fault. All fixed in `src/Physalia.McpBridge/Program.cs`.

**1. The SDK sends the POST body CHUNKED, and Illustrator cannot read a chunked request.** This was
the real one. `HttpClient` frames a content of unknown length with `Transfer-Encoding: chunked`;
Illustrator's server parses the raw framing *as the body*, so it answered
`-32700 JSON parse error: at line 2, column 1: unexpected '{'; expected end of input` — the hex
chunk-size on line 1 is itself a valid JSON number, so the server read that as the whole message and
found an object on line 2 it had no use for. The transport reduced all of this to **"Streamable HTTP
POST response completed without a reply to request with ID: 1"**, which sounds like silence from a
server that was complaining in detail. Fix: `ContentLengthHandler` calls
`request.Content.LoadIntoBufferAsync()` so the request goes out with a `Content-Length`.

**2. A 404 on the optional standalone GET stream kills the whole session.** The spec makes the
server→client GET stream optional and says a server without one answers **405**; Illustrator answers
**404** (`{"error":"Not found. Use POST /v1/mcp"}`). The SDK ends the session either way, so the relay
died the instant it connected. Fix: probe `GET <endpoint>` at startup and set
`EnableStandaloneGetStream` from the answer — anything that is not an event-stream 200 turns it off.
Left ON when the probe cannot reach the endpoint, since a server that does offer the stream needs it.

**3. Our own bridge reported none of it.** `Task.WhenAny(inbound, outbound)` then `return 0` meant a
transport dying before relaying anything looked identical to a clean shutdown — exit 0, nothing on
stdout, and Physalia could only say "the server closed the connection" and echo stderr. Fix: name
which side ended, surface the fault, exit 1 when nothing was ever relayed, and a **`--trace`** flag
that logs the whole HTTP exchange to stderr (credentials redacted).

**Method note, and the reason this took as long as it did.** Four hypotheses were wrong before the
right one — OAuth clobbering the header, connection reuse/retry, response-body buffering, SDK version
drift — and one experiment was actively **misleading**: a harness passing its own `HttpClient`
"disproved" the OAuth theory, but supplying a client is exactly what leaves the SDK's OAuth handler
out of the pipeline, so that test could not have failed. What finally isolated it was noticing
`--trace` made it WORK, then reducing the trace handler to its one side effect. **A/B the harness
against the real component with one variable at a time, and distrust any probe that differs from the
component in more than the thing under test.**

Also true and worth keeping: `McpServer.ConfigPath` is next to the ASSEMBLY, so the running plug-in
reads `bin/Debug/net7.0-windows/Files/MCP_SERVERS.YAML` — not the repo's `Files/`. And the bridge is
a **subprocess**, so a bridge-only fix needs no `.gha` rebuild; retrying the connection picks it up
even with Rhino open and holding the `.gha` lock.

Related: [[mcp-integration]], [[core-console-harness]].
