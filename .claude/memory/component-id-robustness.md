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

## Diagnosis status — root cause NOT fully pinned
Transcript id pattern (canvas id = placement position + 3, exact across 20 samples) proves the stable-id registry was EMPTY at the post-placement export — placement-time `RegisterStableIds` claims never landed. Static audit of the whole chain (capture → `GhJson.Fix` (defaults do NOT reassign ids; count-invariant) → `RestoreAuthoredIds` → `Put` (uses doc ids as-is; local ghjson-dotnet repo tag v1.1.1 == consumed nuget) → claims) reads CORRECT — something dynamic intervened (top suspect: guids in `PutResult.IdToGuidMapping` going stale vs the live objects). **The new diagnostics will name it on the next live run** — look for `[Physalia] Placement did not preserve...` in the Rhino command line (logs restored flag + verified/total claims).

## Fixes applied (all in `GhJsonBridge` partials unless noted)
1. **Claims from live objects**: `DeriveIdClaims(doc, putResult)` — when every component placed, pairs `doc.Components[i].Id` with `PlacedObjects[i].InstanceGuid` (CURRENT guids), fallback to library mapping. Used for registry claims AND the fidelity ledger, on both full-doc (`ExecutePut`) and patch-add (`ApplyAdds`) paths.
2. **Verify + loud failure**: after claiming, each pair is verified (live object exists + registry maps guid→that id). Any miss (or restore precondition failure) → `MarkPlacementNumberingLoss(doc)` + CompTx local warning + `RhinoApp.WriteLine` diagnostic.
3. **Model-facing renumber warning**: `ConsumePlacementNumberingLoss` (one-shot per doc) read by `ConversationLog.FreshCanvasStateGrounding` → new `CanvasStateGrounding.NumberingNote` init prop rendered after the provenance line ("your last placement could NOT keep the ids you authored… use ONLY the ids in THIS canvas state").
4. **Export-time second chance**: `AssignStableIds` pre-pass claims authored ids from the authored-placement ledger for objects the registry has never seen, before anything resolves fresh.
5. **Geometry Report + Runtime Health Check ids**: `TryGetStableId(doc, guid, out id)` peek (never mints) added to the registry; both `Label()` helpers now emit `Name 'Nick' (id N, <guid>)` and the report preamble explains id=endpoints / guid=match. Model no longer cross-references the full canvas export to map report entries to patch targets (exactly where it fumbled).
6. **Patch modify extension validation**: `ValidateModifyExtensions` in `ApplyModifies` — `gh.numberslider` on a non-slider / `gh.panel` on a non-panel now adds an `invalid_op` conflict with the corrective instruction (use `inputSettings.byParameterName` internalizedData) and skips the op, instead of the replace path silently rebuilding and confirming a no-op.

Builds clean, 300 tests green. Live test pending — check the Rhino command line for the numbering diagnostic on the next placement session. Related: [[iterative-canvas-editing]], [[geometry-snapshot-grounding]].
