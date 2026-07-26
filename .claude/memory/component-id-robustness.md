---
name: component-id-robustness
description: "2026-07-23 overnight fixes for the White House session's id confusion — authored-id preservation hardened + renumber warning to model, canvas ids in Geometry Report / Runtime Health Check, patch modify extension-type validation"
metadata: 
  node_type: memory
  type: project
  originSessionId: 0051c9fe-ee20-44ce-86cd-9cb93ae1cfd0
---

# Component-id robustness fixes (2026-07-23, overnight autonomous run)

Driven by the White House transcript (`physalia-chat-20260723-0017.txt`): the model's authored ids (10–67) came back renumbered (~4–54) in the next canvas state despite the system prompt promising id stability, and a `gh.numberslider` modify on a Construct Domain was confirmed "applied" twice while doing nothing.

## ROOT CAUSE FOUND AND FIXED (2026-07-25)
The 2026-07-25 23:19 session logged `id claims verified: 1/48` and named it. **`GhJsonGrasshopper.Put` calls `NewSolution` internally; `CanvasStateGrounder.SolveInstance` exports canvas state on EVERY solve; that export calls `StableIdRegistry.Resolve`.** So by the time a placement reaches `RegisterStableIds`, every object it just created already holds an *export-order* id — and the old `Claim` refused a guid it already knew (`_byGuid.ContainsKey(guid) → return`). Worse, the interim ids OVERLAP the authored ones being claimed (48 objects holding 5..52 while asking for 1..50), so even an authoritative single-shot claim refuses every time: the id wanted is held by another object in the same batch. This is also the "+3 / +4 offset, exact across 20 samples" pattern from 2026-07-23 — export order, not authored order. The earlier audit read correct because the interference is dynamic and comes from a *different component's solve*.

**Fix:** `StableIdRegistry.ClaimBatch` — release the interim ids of every guid in the batch FIRST (safe: those guids were created moments ago by this placement, and the export the model actually reads happens after), then claim. Ids belonging to anything outside the batch are never touched, so no number the model has seen can move or be reassigned. Plus `ReleaseRetiredStableIds` on the full-graph path (a graph placed then deleted used to burn its numbers for the session, so the *second* full placement could never keep its authored ids), and a preamble rule to number above the canvas-state max when the canvas already holds the user's own components. Simulated: OLD 0/48 → NEW 44/48, and 48/48 once the model numbers above the existing ids (the 4 misses are the user's own components legitimately holding 1..4).

## Original diagnosis status (superseded by the above)
Transcript id pattern (canvas id = placement position + 3, exact across 20 samples) proves the stable-id registry was EMPTY at the post-placement export — placement-time `RegisterStableIds` claims never landed. Static audit of the whole chain (capture → `GhJson.Fix` (defaults do NOT reassign ids; count-invariant) → `RestoreAuthoredIds` → `Put` (uses doc ids as-is; local ghjson-dotnet repo tag v1.1.1 == consumed nuget) → claims) reads CORRECT — something dynamic intervened (top suspect: guids in `PutResult.IdToGuidMapping` going stale vs the live objects). **The new diagnostics will name it on the next live run** — look for `[Physalia] Placement did not preserve...` in the Rhino command line (logs restored flag + verified/total claims).

## Fixes applied (all in `GhJsonBridge` partials unless noted)
1. **Claims from live objects**: `DeriveIdClaims(doc, putResult)` — when every component placed, pairs `doc.Components[i].Id` with `PlacedObjects[i].InstanceGuid` (CURRENT guids), fallback to library mapping. Used for registry claims AND the fidelity ledger, on both full-doc (`ExecutePut`) and patch-add (`ApplyAdds`) paths.
2. **Verify + loud failure**: after claiming, each pair is verified (live object exists + registry maps guid→that id). Any miss (or restore precondition failure) → `MarkPlacementNumberingLoss(doc)` + CompTx local warning + `RhinoApp.WriteLine` diagnostic.
3. **Model-facing renumber warning**: `ConsumePlacementNumberingLoss` (one-shot per doc) read by `ConversationLog.FreshCanvasStateGrounding` → new `CanvasStateGrounding.NumberingNote` init prop rendered after the provenance line ("your last placement could NOT keep the ids you authored… use ONLY the ids in THIS canvas state").
4. **Export-time second chance**: `AssignStableIds` pre-pass claims authored ids from the authored-placement ledger for objects the registry has never seen, before anything resolves fresh.
5. **Geometry Report + Runtime Health Check ids**: `TryGetStableId(doc, guid, out id)` peek (never mints) added to the registry; both `Label()` helpers now emit `Name 'Nick' (id N, <guid>)` and the report preamble explains id=endpoints / guid=match. Model no longer cross-references the full canvas export to map report entries to patch targets (exactly where it fumbled).
6. **Patch modify extension validation**: `ValidateModifyExtensions` in `ApplyModifies` — `gh.numberslider` on a non-slider / `gh.panel` on a non-panel now adds an `invalid_op` conflict with the corrective instruction (use `inputSettings.byParameterName` internalizedData) and skips the op, instead of the replace path silently rebuilding and confirming a no-op.

Builds clean, 300 tests green. Live test pending — check the Rhino command line for the numbering diagnostic on the next placement session. Related: [[iterative-canvas-editing]], [[geometry-snapshot-grounding]].
