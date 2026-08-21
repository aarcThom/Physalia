---
name: slider-nicknames
description: "LLM-placed sliders now get meaningful canvas nicknames — schema/preamble instruct it, PhySchema carries it, placement preserves it"
metadata: 
  node_type: memory
  type: project
  originSessionId: 7aa830b2-5f4e-49c5-b258-f0550a959b8b
---

Made LLM-generated Number Sliders (and other input sources) get meaningful canvas nicknames instead of the default "Number Slider" label (2026-06-30). Builds clean, 135 Core tests green. **Live Rhino test pending.**

> **STALE BELOW (the PhySchema half).** The whole PhySchema path — `SCHEMA/PhySchema.json`, its
> minified twin, `Generation/PhySchema.cs`, `SerializePhySchema` and the `SchemaTranslator`
> component — was deleted (`d053d6f`, `8300690`). The model emits GhJSON directly now, and the
> schemas live in `SYSTEM_PROMPTS/SCHEMA/` as `Node Graph.json` / `Incremental Node Graph.json` /
> `Python3 Script.json` / `C# Script.json`. Only the GhJSON and placement-preserve notes below still
> apply; the slider-naming RULE survived into `Node Graph.json`.

At the time, two model-facing schemas drove canvas building: **PhySchema** (`SCHEMA/PhySchema.json` + `(Minified)` — the `Node Graph` preamble; LLM emits it, `SchemaTranslator` → `GhJsonBridge.SerializePhySchema` → GhJSON → place) and **GhJSON** (`SCHEMA/GhJSONSchema.json` — full-GhJSON mode, placed directly). Both were missing slider-naming guidance; PhySchema was also missing the `nickName` field entirely (its component object is `additionalProperties:false`, so the LLM literally couldn't emit one).

Changes:
- **PhySchema.json + minified:** added a `nickName` component property + a rule ("give every Number Slider a nickName naming what it controls") + nicknamed the example sliders.
- **GhJSONSchema.json:** already had `nickName` (component-level); added the same naming rule + strengthened the field description.
- **Preambles** `Node Graph.txt` + minified: bullet instructing to nickname every slider.
- **`Generation/PhySchema.cs`:** `PhySchemaComponent` gained `NickName` (JsonPropertyName `nickName`).
- **`GhJsonBridge.SerializePhySchema`:** maps `component.NickName` → `GhJsonComponent.NickName` (lib applies it to the placed object on Put; confirmed `set_NickName` in GhJSON.Core/Grasshopper).
- **Placement preserve (the subtle bug):** a Number Slider is a floating `IGH_Param`, and `ComponentHelpers.ExpandToFullName` (run by `ApplyNickNameDisplay` when GH "Draw Full Names" is ON) set `floatingParam.NickName = Name` → clobbered the slider's label back to "Number Slider". Fixed: `ApplyNickNameDisplay` now takes an optional `ISet<Guid> preserveNickNames`; `ExecutePut` builds it from doc components with a non-empty NickName (via `PutResult.IdToGuidMapping`, since RegenerateInstanceGuids remaps guids) so the floating-param branch skips them. Component param-grip expansion is unaffected. See [[obsolete-component-guid-validation]] for the sibling placement-path concern.
