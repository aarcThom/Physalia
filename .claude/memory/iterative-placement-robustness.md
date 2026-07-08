---
name: iterative-placement-robustness
description: "2026-07-08 fixes from the failed house session — resolver was destroying ghpatches, extractor took the first of multiple JSON blocks, Canvas Observation feedback was anonymous"
metadata: 
  node_type: memory
  type: project
  originSessionId: 4ca7fbb5-1637-4471-a862-1ff8f0cf7404
---

Fixes landed 2026-07-08 (working tree) after diagnosing the "Placement failed: The GhJSON file contains no components to place." session (export `physalia-chat-20260708-0010.txt` on Desktop):

1. **Component Resolver destroyed ghpatches** (the reported error): `ResolveComponentNames` parsed any payload with `GhJson.FromJson` — a ghpatch binds to a `GhJsonDocument` with `Components = null`, `kind`/`patch` drop, and `ToJson` re-serializes an empty full document that CompTx then rejects with the misleading error. Fix: `ComponentResolver` branches on `GhPatchDetector.IsGhPatch` → new `GhJsonBridge.ResolvePatchComponentNames` resolves only `patch.components.add` (via shared `ResolveComponentList` helper) and re-serializes with `GhJson.PatchToJson`; a patch with no adds passes through byte-identical.
2. **JsonExtractor first-match**: a self-correcting model emitting several ```json blocks in one response had its FIRST (abandoned) attempt placed. Fix: `ExtractJson` collects all fenced blocks and returns the LAST that parses (fallback: nearest earlier parseable block for truncated output, else last outright). Follow-up same day: unfenced text now uses a balanced-brace scan (string-literal aware, steps past unclosed openers) collecting bare `{...}`/`[...]` candidates with the same last-parseable policy — covers multiple bare attempts and stray prose braces; old outermost-span behavior kept as final fallback. 10 new tests in JsonExtractorTests.
3. **Canvas Observation anonymous feedback**: warning/error/dead lines carried only `Name` — five identical "Construct Point: ..." lines, model had to guess. Fix: `Label()` = Name + 'NickName' + (InstanceGuid), matching the canvas-state export identity so the model can patch by instanceGuid; scoped scans also get a sharper header ("The graph from your last response was placed...").
3b. **Canvas Observation delta-only scope on patch turns** (follow-up, same day): CompTx's patch Success payload is only added+modified GUIDs, so cascade breakage in earlier-placed components was never scanned, and a remove-only patch (empty payload) was dropped by TryGetData — no scan at all. Fix: CanvasObservation now ACCUMULATES every GUID ever received (`_watchedGuids`, session-only, cleared in OnCleared, never serialized) and scans the whole watched graph each turn; removed components pruned at scan time; locked ones stay watched but are excluded (they never solve — would jam IsReadReady); blank payload accepted once GUIDs are watched; whole-doc standalone-probe fallback only when nothing is watched.
4. **Preambles** (Node Graph.txt + Python3 Script.txt): added "Emit exactly ONE JSON document per response — never include drafts or abandoned attempts."

**Why:** ghpatch mode landed 2026-07-07 but Component Resolver was never made patch-aware — the second turn of every conversation is a ghpatch, so the whole iterative loop was broken.

**How to apply:** live Rhino verification still pending: (a) multi-block response places the last block, (b) modify-only ghpatch applies through the full pipeline WITH a Component Catalog wired into the Resolver, (c) feedback lines carry nickname+guid. Related: [[iterative-canvas-editing]], [[detect-json-gate]].
