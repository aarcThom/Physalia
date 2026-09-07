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

## 2026-09-07 — the in-Rhino pass, driven through the Rhino MCP's `run_python`

Rhino open with the branch `.gha` loaded. **B0, B1, B2, B3, C1 and A6 all PASS**; C2 passes its
stated assertions and turned up a NEW defect.

**B0 (blocker #1) is good, and the mechanism that had never been exercised now has been.** Forcing
`inner.Enabled = False` on the harness sub-document while the canvas sat on the host, the Timer went
on firing 4 → 8 and the flag came back True: `PipelineWake.Ready` really does re-enable it. Then with
`host.Enabled = False` (the user's own solver lock) it fired 9 → 14 and the host flag **stayed
False** — the lock is not overridden. Both ALSO-TEST cases covered.

**C1 (blocker #3) round-trips clean.** 27 components, guids preserved, and — the documented hazard —
**no param-order drift, no wire moved, no value changed**. Router's variable outputs came back still
named `declare`/`ask_human`/`state`; the Regex flag, Budget caps, `For Each` items, harness port
nicknames all restored; **all five triggers came back `off`**, including the two armed before saving.

**NEW DEFECT — `DocumentIds.MutateAll` does not recurse into a nested harness.** Placing a preset
twice re-issues ids for the document it is given (and the nested harness COMPONENT), but never for
that harness's own `InnerDocument`. Demonstrated: two placements, two distinct Timer components
inside the nested workers **sharing one `InstanceGuid`**, so a guid-addressed UI finds two. Confirmed
in the source — it walks `document.Objects` only. This is reachable by design, not a corner: a
Delegate links to a nested worker harness, so any delegation preset placed twice hits it. Wires and
within-harness links still work (nothing inside changed, so they stay self-consistent); what breaks
is anything keyed by `InstanceGuid` across the file — **Trigger Control addresses triggers by guid
precisely to avoid ambiguity**, and MemoryTool falls back to the guid for its local folder. Fix is to
recurse per harness with its own replacement dictionary.

**Rig-driving notes worth keeping.** `Instances.ActiveCanvas.Document` is read-only to Python but
`c.set_Document(d)` works (the property setter reached as a method), and `GH_DocumentServer` needs
`AddNewDocument()` — `AddDocument(GH_Document)` and `GH_Document.DisplayName` both refuse.
**`GH_DocumentIO.Copy`/`Paste` report True and do nothing from a script** (no canvas/undo context),
so C5's paste path is not scriptable — its substance is covered anyway, since arming cannot
serialize. Compare documents by `DocumentID`, never `is`: Python.NET hands out different proxy
objects for one .NET document, which read as a false negative. A **Panel's `UserText` is only the
user-typed field** — read `VolatileData` to see what arrived on the wire. And
`RhinoDoc.Objects.Count` keeps counting deleted objects held for undo, so it reads 501 after a
successful purge; iterate the table or ask `get_context` instead.

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

## 2026-09-07 later — both fixes re-verified, icons done, F3 is the last blocker

Rhino restarted onto the fixed build, and **A6 and C2 were re-run against it**: the state board
returns ONE item for a case-variant re-set (was two) and `Clear` no longer orphans an order entry;
two placements of a delegation preset gave **14 ids across two nesting levels with 0 duplicates**,
with each Delegate still linked to its own worker. A fix that is only "compiles clean" is worth
re-running — the whole reason this doc exists.

**Icons: 0 fallbacks** across 108 ribbon types (see [[component-icon-generation]]).

**A monitor now exists for F3**: `tools/overnight/Watch-OvernightRun.ps1`, tested against fixtures
on all three verdict paths (PASS / FAIL / NOTHING HAPPENED). It is **read-only by design** — asking
Grasshopper anything could expire a component, and a monitor that perturbs the run it measures is
worthless for this rig — so it tails `runs.jsonl` by byte offset and watches `conversation.json` and
folder growth. It knows a Budget Guard legitimately overruns by exactly ONE call. Use
`-StopAfterMinutes` so the verdict writes itself.

**Remaining ship blocker: F3 only.** Mac is deferred by decision (`planning/mac-port.md`).
