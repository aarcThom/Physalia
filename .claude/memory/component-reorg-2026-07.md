---
name: component-reorg-2026-07
description: "2026-07-03 GH component reorg — new ribbon sections, folder moves, GH_Exposure ordering, 3 components deleted"
metadata: 
  node_type: memory
  type: project
  originSessionId: a6ed33db-33c4-4866-b922-defb86e3115f
---

2026-07-03: Reorganized all GH components in `src/Physalia.GH/Components/` into new sections (Thomas's canonical list). Builds clean (Debug, 0 errors), live Rhino test pending.

**Deleted** (+ dead code): `Prompter` (Core/Prompter.cs) and its `Attributes/PrompterAttrib.cs` — Chat is now the sole prompt entry point; `PythonShortcut`; `SchemaTranslator`. Also removed icons Prompter.png/SchemaTranslator.png. Stale `Prompter` comments across ~10 files rewritten to Chat. `PromptPipelineView` kept (shared, now Chat-only).

**Sections** = the GH **subCategory** string (the C# namespaces are flat `Physalia.GH.Components` — folders are just org). New folders + subCategories: `TokensCompaction`="Tokens & Compaction" (TokenEstimator, TokenizationTechniques, + the 6 compaction comps; note TokenThreshold & Summarizer set their own subcat, NOT via CompactionComponentBase), `Guardrails`="Guardrails" (Schema Validator, Component Resolver, Canvas Observation, Geometry Observation), `Pipeline` (was `Core`, renamed 2026-07-04), `Signals` (+ the 5 Compositors moved from Utility), `Models` (untouched), `Tools` (+ Router moved from Regulators; ToolsInUse/"Tools Present" retagged Grounding→Tools), `Grounding` (+ Component Catalog/Image Sources from Resources; the grounders retagged Resources→Grounding), `Regulators` (+ SignalLimiter, + DetectJson moved from Guardrails), `Transmitters` (ComponentTransmitter, PyTransmitter), `Extra` (ZoomGuid, Picker, Deserializer, Serializer). Emptied folders removed: Tokens, Compaction, Perception, GhPython, Resources, Utility, Serializers.

**Ordering:** forced via `public override GH_Exposure Exposure => GH_Exposure.<tier>` per component (Models left default). Tiers in order: primary, secondary, tertiary, `quarternary` (GH's real misspelling — extra r), quinary, senary, septenary, obscure. GH draws a separator line between each tier. **Tab (subcategory) order itself is NOT API-controllable — GH renders tabs alphabetically.**

Flattened the 2 stray sub-namespaces (`Picker`=.Utility, `PyTransmitter`=.GhPython) → flat; fixed usings in PickerAttrib, PyTransmitterAttrib, ComponentHelpers, GhJsonBridge.

Caveat: deleting the 3 component GUIDs is a hard break for any saved `.gh`/`.ghjson` referencing them. See [[signal-carrier-discipline]].
