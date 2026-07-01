---
name: document-units-grounding
description: Fourth grounding kind — Document Units — a wired grounder + chat-tab pill with an override dropdown
metadata: 
  node_type: memory
  type: project
  originSessionId: 7aa830b2-5f4e-49c5-b258-f0550a959b8b
---

Added a **Document Units** grounding kind (2026-06-30), mirroring the Cluster-grounding pattern end-to-end. Builds clean (`dotnet build src/Physalia.slnx -c Debug` 0 err), 135 Core tests green, svelte-check 0 err. **Live Rhino test pending** (see [[grounding-on-recorder]]).

Tells the LLM the active Rhino document's unit system so its numbers/geometry match. Wired-producer model (like Cluster Grounding), NOT intrinsic: wired ⇒ included, unwired ⇒ absent.

- **Core:** `DocumentUnitsGrounding(string Units)` record in `Grounding.cs` (empty section when blank).
- **Component:** `Components/Grounding/DocumentUnitsGrounder.cs` — subCategory "Resources", no inputs, reads `Rhino.RhinoDoc.ActiveDoc.ModelUnitSystem` (ToString(); None/Unset ⇒ empty) every solve, emits `GH_Grounding`.
- **Recorder:** `_unitsOverride` (string?, null = live doc units; config, survives Clear). Public `HasUnitsGrounding`/`DocumentUnits`/`UnitsOverrideOrNull`/`SetUnitsOverride`. Override swaps `DocumentUnitsGrounding.Units` in `BuildGroundedSystemPrompt`. Persisted via `UnitsOverrideSet`+`UnitsOverride` (Write/Read). **The override changes only the text sent to the model — never the Rhino document.**
- **ChatWindow:** state tick pushes `unitsWired/documentUnits/unitsOverride/unitOptions` (+`setunits` verb → `HandleSetUnits`, `UnitsOverridePayload{Reset,Units}`). `UnitOptions` = curated common unit names, merged with live doc value + override.
- **UI:** bridge.ts UiState fields + `UnitsOverridePayload`; `Grounding.svelte` gained a Document Units pill + a `view='units'` detail (dropdown-menu, not a select component — none exists; picking the doc's own value clears the override). App.svelte wires `setUnits` + a `groundingAvailable` derived so the Composer grounding button enables for ANY wired kind (also fixes clusters-only not enabling it).
- ghjson preset round-trip (`physalia.unitsOverride`) was left as optional/deferred — not implemented.
