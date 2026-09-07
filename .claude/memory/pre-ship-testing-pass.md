---
name: pre-ship-testing-pass
description: "What of planning/pre-ship-testing.md can be run WITHOUT Rhino, and the two findings that came out of doing it (2026-09-06)"
metadata: 
  node_type: memory
  type: project
  originSessionId: f9444f96-8bd3-4d18-8dab-e1a4ded642eb
  modified: 2026-09-07T06:44:58.546Z
---

Ran everything in `planning/pre-ship-testing.md` that does not need a live Rhino (2026-09-06, Rhino
was not running). Passes A–F all need a canvas; what is reachable off-canvas is the build, the Core
suite, the headless chat-UI rigs, the icon audit, and static audits of the safety properties.

**All green:** `dotnet build src/Physalia.slnx -c Debug` (0 errors), Core suite **964/964**, and all
**11** headless UI rigs in `tools/uitest` (see [[headless-chat-ui-testing]]). The embedded
`Physalia.GH.chat.html` resource is byte-identical to `src/Physalia.UI/dist/index.html`, which is
what makes a headless UI result a claim about the bundle Rhino would actually load — check that
before trusting one.

**Two findings the doc did not have:**

1. **`StateStore.All()` duplicates a key on a case-variant re-set** — A6's `Keys`/`Values`
   assertion is exactly what catches it. `Board.Values` is a `Dictionary` with
   `StringComparer.OrdinalIgnoreCase`, but `Board.Order` is a plain `List<string>` whose `Remove`
   is case-SENSITIVE. `set stage` then `set STAGE` leaves ONE board entry and TWO items on the
   outputs, both carrying the newest value. Proved with a standalone repro, not by reading.
   **FIXED on branch `final-pass`** — `Order.RemoveAll(... OrdinalIgnoreCase)` in both `Set` and
   `Clear`; the repro confirms one pair out and last-set order still preserved. `Clear`'s copy of the
   bug was masked (All() filters the order log through Values) so it only leaked stale names, which
   is why it would have accumulated unnoticed. There is no unit test: `StateStore` is `internal` to
   `Physalia.GH` and keyed on a `GH_Document`, so A6 in Rhino is still the only test of it.
   `McpServer.ToggleTool` already had the correct idiom — a scan of all 46 `OrdinalIgnoreCase` files
   found StateStore to be the ONLY site with the mismatch.
2. **29 ribbon components fall back to the brain placeholder, not the 18 the doc says.** Audited by
   matching every type declaring `override Guid ComponentGuid` against the `.gha`'s embedded
   `Physalia.GH.Resources.<TypeName>.png` names. The `Param_*` types are exempt — `PhyParam` sets
   `GH_Exposure.hidden`, so they never appear in the ribbon.

**Already pinned by the Core suite, so do not re-run them by hand in Rhino** — C4's three "ALSO
TEST" edges (`ANewerFormatIsRefused_NotGuessedAt`, `AMissingImageFileLosesTheBlockAndKeepsTheTurn`,
`AHalfWrittenLastLineCostsOneRecord_NotTheFile`), C3's `.phy` round trip and future-format refusal,
and most of B5's recorder cases. What is left in those rigs is the in-Rhino half only: does the
resume BUTTON appear and restore, does a `.phy` import into a fresh document and suffix on the
second.

**Statically verified, which is stronger than a test here:** nothing under
`src/Physalia.GH/Components/Triggers/` overrides `Write` or `Read` **at all**, so arming cannot
serialize — B1's "reopens off" and C5's "pasted copy is disarmed" hold by construction. And
`PipelineWake.Ready` is called from exactly two places (`SignalSourceBase.Wake`, `TaskIn`), so all
five triggers share the one path. Note `StatefulComponentBase.ScheduleAt`'s early-flush **re-arm
does not go back through `Ready`** — harmless today because the proxy only ever re-asserts `Enabled`
true, but it is the seam to look at first if B0 fails intermittently rather than outright.

Still open and NOT checkable off-canvas: B0 itself, F3, C1/C2. `McpServer.BridgeExecutable()` still
hardcodes `.exe` ([[mac-port-mcp-gaps]]).
