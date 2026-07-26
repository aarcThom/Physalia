---
name: grouping-and-panel-placement-fixes
description: "2026-07-25 White House transcript triage — group-add schema deadlock, documentation-panel placement, and three patch-loop error sources, all fixed"
metadata: 
  node_type: memory
  type: project
  originSessionId: 9070a607-d646-44b4-a78e-a43dc097a34d
  modified: 2026-07-26T05:02:49.352Z
---

Triage of `chat.txt` (White House session, 2026-07-25). Three user-reported symptoms, all traced to code/schema defects — none were model flakiness.

**1. The model could not group components — two unsatisfiable deadlocks.**
- Full documents: `Files/SYSTEM_PROMPTS/SCHEMA/Node Graph.json` `fullDocument` had `additionalProperties:false` over only schema/components/connections, and a rule saying "Omit document metadata and groups". No group could ever appear.
- Patches: Physalia's `groups.add` allowed ONLY name/color/members (`additionalProperties:false` → `id` forbidden), while the GhJSON library's `groupAdd` = `allOf[ghjson#groupData, not{required:[instanceGuid]}]` and `groupData.anyOf` requires `(instanceGuid+members)` OR `(id+members)` → with instanceGuid banned, `id` is MANDATORY. Literally no document satisfied both validators.
- **Library bug amplifying it:** `PatchValidator.FlattenDetails` (ghjson-dotnet) suppresses failing `anyOf`/`oneOf` branches but NOT failing subschemas under `not`. So a *correct* add (no instanceGuid) makes the inner `required` fail — which is what makes the `not` pass — and that inner failure is emitted verbatim as `Required properties ["instanceGuid"] are not present`. Comply by adding one, and `ValidateNoInstanceGuidInAdds` says "must not specify instanceGuid". Unwinnable.
- **Fixes:** shared `group` def in Physalia's schema requiring `id`+`members` and forbidding `instanceGuid`; `groups` added to `fullDocument`; new schema rules + preamble guidance on grouping; `DescribePatchError` in `GhJsonBridge.Validity.cs` strips `instanceGuid` from required-property lists reported against `*/add/*` paths and drops the error when nothing else was missing. Verified with jsonschema: library-compliant form now VALID, id-less form INVALID, instanceGuid still INVALID, full-doc groups VALID.
- `ApplyGroupOps` already handled `groups.add` correctly all along (reads name/color/members, ignores id) — only validation blocked it. It now also claims authored group ids in the stable-id registry.

**2. Panels landed in a block off to the right.** `AnchorPatchAdds` discards authored pivots by design (the preamble tells the model not to position anything) and anchors all adds as one block right of the graph, laid out from their *internal wires*. Documentation panels have zero wires → nothing to lay out, whole block dumped past the right edge. **Fix (design choice: group membership is the association):** `ExtractGroupAnchoredAnnotations` pulls unwired adds that a patch group makes a member of out of the wired block and pivots them above that group's LIVE member bounds; `LiftGroupAnnotationsAboveLayout` does the full-document equivalent (band above the layout, X-aligned with the group's leftmost wired member). Also fixed the schema rule that contradicted the preamble by telling the model to read existing pivots.

**3. Patch errors — three separate causes.**
- The instanceGuid/group deadlock above (~3 wasted rounds).
- Dangling endpoints 186/187 → partial apply: `LintPatchAdds` passed `endpointIdsMustResolve:false`, so patch endpoint ids were NEVER cross-checked against the canvas. Now it reads the canvas ids via `TryExportCanvasState` and resolves against adds ∪ canvas (`authoredIds` → `resolvableIds`, new `existingIds` param).
- False "receives 2 wires but consumes ONE item": the model emitted the SAME connection twice (`50→46[B]`); `LintRequiredInputs` counted raw connections with no dedup. GH treats a repeat as a no-op. Now deduped via `ConnectionIdentity` before counting. The bad advice had pushed the model into inventing a `Multiplication ×2` node.

**Unexplained, worth watching:** `Main Block Width` read 51000 in the first two geometry reports and 51852 in the last, with no patch touching that slider. Either a user nudge or something in the panel-add path rewriting slider state. Not diagnosed.

Builds clean (`dotnet build src/Physalia.slnx -c Debug`), 305 Core tests pass. **Live Rhino test pending** — group placement on the full-document path and annotation anchoring both need a real canvas run.

Related: [[iterative-canvas-editing]], [[component-id-robustness]], [[system-prompt-preambles]], [[iterative-placement-robustness]]
