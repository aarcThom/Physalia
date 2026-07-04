---
name: obsolete-component-guid-validation
description: Why the LLM placed obsolete components (e.g. colour Multiplication) and the GUID-validation fix in the Component Resolver path
metadata: 
  node_type: memory
  type: project
  originSessionId: 00ee13c0-3eff-4019-9373-fd6e2e350fcd
---

Symptom (2026-06-30): the LLM kept placing the **obsolete colour "Multiplication"** (`035bf8a7-b9e0-4e37-b031-4567bc60d047`, Vector > Colour) instead of the live math one (`ce46b74e-00c9-43c4-805a-193b69ea4a11`, Maths > Operators).

Root cause was NOT the catalog filter. Grasshopper registers 5 components named "Multiplication"; four are `obsolete=True` and already excluded by `ComponentCatalogGrounder.BuildCatalog`'s `proxy.Obsolete` check, so the catalog (and thus name-matching) only ever contained the correct `ce46b74e`. The leak was `GhJsonBridge.ResolveComponentNames`, which **trusted any `componentGuid` the model emitted without validation** — and LLMs reproduce memorized GUIDs from old `.ghx`/GhJSON training data, including the obsolete colour one. That GUID skipped resolution and was instantiated directly, bypassing every catalog filter.

First fix (Component Resolver path): an incoming `componentGuid` is trusted only if `ComponentCatalog.ContainsGuid(guid)` is true (new lazy `HashSet<Guid>` in Core). Stale GUID fails, gets nulled, re-resolves by name.

**BUT that only runs inside the Component Resolver component. The real leak (confirmed via screenshot 2026-06-30) was placement WITHOUT a Component Resolver.** Decompiled `GhJSON.Grasshopper` 1.0.0: `Put` creates GUID-first, then falls back to `CreateByName`, which walks `Instances.ComponentServer.ObjectProxies` and returns the **FIRST name match with NO obsolete filter** — so an unstamped "Multiplication" node instantiates the obsolete colour twin (first in enumeration). No fork needed.

Real fix: `GhJsonBridge.PlaceDocument` now calls `StampComponentGuids(doc)` before `GhJson.Fix`/`Put` — makes placement self-sufficient (mirrors the existing cluster-extraction "for pipelines with no Component Resolver" pattern). For each non-cluster node: if it already carries a live non-obsolete GUID (`server.EmitObjectProxy(guid)?.Obsolete == false`) leave it (user/preset files untouched); else re-resolve by name against the obsolete-free catalog and stamp the GUID → library takes CreateByGuid. Catalog-building extracted from `ComponentCatalogGrounder.BuildCatalog` into shared `Generation/ComponentCatalogProvider.BuildFromServer(includeLegacy=false)` so Component Catalog and placement share one filter/de-dupe policy.

Decisions: **obscure-exposure components are KEPT** (e.g. Mass Multiplication `e44c1bd7`, exposure 65544 = obscure) — demoted but genuinely useful; an earlier obscure-exclusion experiment was reverted. Also added (retained) per-name de-dupe in `BuildCatalog` (native + lower exposure wins) so the prompt/search tool don't list same-named twins. See [[grounding-on-recorder]].

Files: `Physalia.Core/Grounding/Components/ComponentCatalog.cs` (ContainsGuid), `Physalia.GH/Generation/GhJsonBridge.cs` (ResolveComponentNames validation + `StampComponentGuids` in PlaceDocument — the operative fix), `Physalia.GH/Generation/ComponentCatalogProvider.cs` (new, shared catalog builder; Component Catalog delegates to it), `Physalia.GH/Components/Resources/ComponentCatalogGrounder.cs` (de-dupe, now via provider). Builds clean; **live-Rhino verified 2026-06-30** — Multiplication/Subtraction now place as Maths ▸ Operators, colour twins gone.
