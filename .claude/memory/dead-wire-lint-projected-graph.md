---
name: dead-wire-lint-projected-graph
description: "2026-07-26 — Required Input Check now lints the graph a ghpatch PRODUCES, flags orphaned value sources (unwired sliders), and flags operators fed one source twice."
metadata: 
  node_type: memory
  type: project
  originSessionId: a993e6a5-60b2-4736-b0de-458b42aef767
  modified: 2026-07-27T06:23:14.191Z
---

Diagnosed from a house-modelling session (chat + signal trace on the Desktop, 2026-07-26): the LLM wired `Addition 'Ridge Z'` to the centroid's **X** component instead of Ridge Height, so the "Ridge Height" slider was in **zero** connections from round 1. The graph solved clean, so nothing complained; the model then read Z=7000 as confirmation, and when the user edited House Length out-of-band the ridge moved (Z was reading length/2) and the model credited its own no-op slider patch. Its first fix wired Wall Height into **both** operands of the Addition. Three lint gaps, all now closed in `GhJsonBridge.Lint.cs`:

1. **Orphan rule no longer exempts zero-input components.** The old `inputs.Count > 0` guard existed to spare Panels but also spared every unwired slider. Exemption is now by KIND: `AnnotationObjectNames` (Panel/Scribble/Sketch/Image/Group/Legend) and `physalia.rhinoRef` params. Zero-input orphans get their own wording ("a value source nothing reads").
2. **Patch path lints the PROJECTED graph** (`LintPatch`, was `LintPatchAdds`): canvas + adds − removes, with connection add/remove applied. A connections-only patch used to get no review at all — that is how a rewire stranded `Ridge Point` unnoticed. Reuses `RemapCollidingAddIds` so the lint sees the same ids the apply will build.
3. **New rule: one source port into two inputs of the same operator** (`SelfCombinationDegenerate` — Addition/Subtraction/Division/Modulus/Minimum/Maximum/Line/comparisons). Multiplication and Power are deliberately EXCLUDED — A² is plausible intent and a false positive hard-rejects.

**Why the scoping matters (do not loosen):** findings are now typed (`LintFindingKind`) and the patch path filters per kind, because the canvas export drops Physalia objects **and their wires** — a native input fed by a Physalia output reads as unwired. So `RequiredInput` stays scoped to ADDS only; `Orphan` to adds ∪ stranded (the `from` of a removed wire, or a consumer that was removed) ∪ **modified** (the "you just changed a dead slider" case); `Endpoint` to ids the patch's own connections name. Widening any of these invites a phantom that hard-rejects a submission the model cannot fix.

Also added: `NormalizeEndpointIndices` fills paramIndex from paramName via signatures, so a removal authored by name matches the canvas export's index-addressed wire (and wire counting stops treating index/name as different ports). Preamble + SCHEMA rules gained matching bullets so the model avoids both defects rather than being rejected for them (`Files/SYSTEM_PROMPTS/{PREAMBLE,SCHEMA}/Node Graph.*`).

**Not yet done** from the same diagnosis — the report side, which is what let the model confirm success against contradictory numbers: the Geometry Report shows geometry but never the scalars driving it (a "slider = value → drives N inputs" line would have ended this at round 3), and `base_checksum_mismatch` says the canvas changed without saying WHAT, so a concurrent user edit gets attributed to the model's last patch. See [[iterative-canvas-editing]], [[component-transmitter]].

**Verification status:** builds clean, 305 Core tests pass, and the logic was traced by hand against every submission in that session — but the lint needs live Grasshopper introspection, so it has NOT been executed. Confirm in Rhino before trusting it.
