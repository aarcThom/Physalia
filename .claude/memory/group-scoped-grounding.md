---
name: group-scoped-grounding
description: "2026-07-28 — master 'Physalia' group + group-scoped canvas grounding, plug-ins unchecked by default, verbosity sweep (branch feat/group-based-context). Run live same day: worked end-to-end once the checksum frame prefix was replaced by frame MATCHING."
metadata: 
  node_type: memory
  type: project
  originSessionId: d0d67dd2-c77a-46f3-9c36-7c583ec99c0a
  modified: 2026-07-28T08:22:52.637Z
---

Built 2026-07-28 on `feat/group-based-context` because LLMs get confused by pre-existing canvas nodes. Builds clean, 339 Core tests green. **Run live in Rhino the same day** (White House staged build, chat + trace triaged): enrollment, scoped export/grounding, id preservation, and the full 5-stage loop all worked — the model finished in prose with correct massing. But the first cut's checksum frame prefix cost 2 wasted rounds per stage:

**HARD LESSON — a frame marker cannot live inside the checksum string.** The first cut minted scoped checksums as `sha256-group-<hex>`. Physalia's own schema passed it, but the GhJSON LIBRARY's ghpatch schema (reference-only NuGet, validated by GH Definition Validator) regex-rejects any non-`sha256-<hex>` shape — and its error flattening then co-reported bogus "All values fail against the false schema" errors on perfectly valid internalizedData entries (branch-knockout noise, same family as the 2026-07-25 `not`-branch bug). The model coped by STRIPPING `group-`, which silently dropped every patch into the full frame; every one of the 5 stages burned the same 3-round dance (regex reject → strip prefix → mismatch → full-frame checksum → apply). **Fix: both frames use plain `sha256-…` and the frame is resolved by MATCHING** — `GhJsonBridge.ResolveBaseSnapshot(doc, carried)` tries the active frame's export first, then the other frame; used by `ApplyPatchToCanvas` AND `LintPatch`. Neither matches → real drift, mismatch feedback in the active frame. Identical content across frames → identical checksum → choice immaterial.

**Other session notes:** stage-1 full placement logged `id claims verified: 20/24` on a FRESH canvas with the scoped grounder — new data point for [[component-id-robustness]] (ledger pre-pass restored the authored numbering, so no harm downstream). A user-doc Fidelity Check had its Definition input miswired (payload starting with '#'); the component's own remedy message handled it — no code change. `claude_code_incremental.ghjson` preset now wires **Physalia Group Components** instead of Canvas State.

**Master "Physalia" group** (`Generation/GhJsonBridge.GroupScope.cs`):
- Identity = a `GH_Group` with NickName `GhJsonBridge.MasterGroupName` ("Physalia"); the NAME is the contract (rename detaches, naming your own group adopts it). Faint teal, created at the first LLM placement (bounds are its members', so it materializes at the transmitter tip).
- `EnrollPlaced(doc, placedComponents, modelGroups?)` runs on BOTH LLM paths — `LoadAndPlaceJson` (infers model groups = groups whose members ⊆ placed set) and `ApplyPatchToCanvas` (gets created-group guids from `ApplyGroupOps`, which now returns them). Model sub-groups are enrolled as whole members; uncovered components directly. Already-covered objects are never touched.
- The master group is INFRASTRUCTURE, excluded everywhere: both export frames (`TryExportCanvasState` skips `IsMasterGroup`), `Layout.cs` area enumeration (else the whole placement fuses into one rigid body), and `RegisterPlacedGroupIds` live candidates (else a single-group placement could set-match it and steal the claim).

**Group-scoped canvas frame:**
- `TryExportCanvasState(doc, groupScope)` — scoped guid set = `MasterGroupScope` (BFS through nested groups, live objects only). `CanvasStateSnapshot` gained `GroupScoped`.
- **Frame resolution is by checksum MATCHING, never a prefix** (see the hard lesson above): `ResolveBaseSnapshot` compares the carried `patch.base.checksum` against each frame's export; `CanvasStateSnapshot.GroupScoped` says which frame won.
- **Active frame registry**: ConvLog's `FreshCanvasStateGrounding` calls `RecordActiveFrame(doc, scoped)` (CWT, session-only); every guardrail hands back checksums via `GhJsonBridge.CurrentBaseChecksum(doc)` (SchemaValidator, RuntimeHealthCheck, GeometryReport, Fidelity) so the model never sees mixed-frame checksums.
- New component `PhysaliaGroupGrounder` ("Physalia Group Components", GUID 7C3E9A15-…) subclasses `CanvasStateGrounder` via `protected virtual bool GroupScope` + a protected identity ctor — shares the whole debounce/rescan machinery. `CanvasStateGrounding.GroupScoped` (Core) renders the visibility contract (auto-enrollment, hidden canvas, user opts components IN by moving them into the group). ConvLog: any wired scoped grounding wins (`_groupScopedCanvasGrounding`).

**Plug-ins unchecked by default (selection-level, NOT catalog-level)**: user revised the first cut (a hard `includePlugins` filter in `BuildFromServer` + grounder toggle — reverted). Plug-in components stay in the catalog and the grounding tree; the DEFAULT is a null ConvLog selection now meaning `ComponentCatalog.NativeSelection()` (leaves with ≥1 core-library entry; mixed panels included whole) instead of include-all. `EffectiveGroundingSelection` on ConvLog is what the chat UI renders (never null-as-all), so plug-in tabs show unchecked and are opt-in per pipeline. "Reset to all" in the chat window now materializes an EXPLICIT `GroundingSelection.All(tree)` — null no longer means all, and the window re-checks every leaf locally on reset, so the host must match. No Svelte changes were needed.

**Verbosity sweep** (what was cut — don't re-inflate):
- `base_checksum_mismatch` no longer embeds a second full canvas JSON (the ConvLog re-export at fold time already puts the fresh state in the same request) — the biggest failure-round saving.
- Schema `rules` deduped against the preambles (both files, 20→14 and 23→17 rules): pivot/layout, GROUPS, PANELS, rhinoRef, GHPATCH FEEDBACK, required-*, self-combination, nickName rules now live ONLY in the preamble; schema keeps document-format facts. Root `description`s point at the MODE rule. **The preamble+schema always travel together (SystemPrompt joins the pair), which is what makes single-homing safe.**
- Catalog grounding header rewritten ("These are the ONLY Grasshopper components you may place…", ~830→~330 chars) — also fixed the now-false "native and plug-in alike" claim.
- Canvas grounding prose, three preflight rejection headers (SchemaValidator ×3, GhDefinitionValidator, RequiredInputCheck), lint violation lines, RuntimeHealthCheck headings, GeometryReport header all tightened; semantics kept (nothing-placed / same-kind / fix-only-listed all survive).
- Deliberately NOT touched: Build Plan digest + Geometry Report closing instructions (proven live per [[incremental-staged-building]]), StallGuard, the "Current base checksum — copy this verbatim" line format (StallGuard fingerprint-strips it by prefix).

Related: [[iterative-canvas-editing]], [[grouping-and-panel-placement-fixes]], [[component-id-robustness]], [[incremental-staged-building]]
