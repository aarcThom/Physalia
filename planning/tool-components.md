# Tool Components — Research

> Research deliverable (2026-06-27). Catalog of LLM function-calling tool components worth building
> for Physalia. Not yet implemented — this is the spec.

## How tools work here

A tool is a `ToolComponentBase` subclass (`src/Physalia.GH/Components/Tools/`). It advertises a
`ToolDefinition(Name, Description, InputSchemaJson)` on its **Tool** output (collected by `ToolsInUse`
→ `Reasoner.Tools`); receives dispatched `ToolCallContent` blocks on its **Signal** input from a
`Router`; implements `ExecuteCall(ToolCallContent)` → `ToolCallResult.Ok(string)` / `.Error(string)`,
returning one result string per call; and may override `OnSolveTick` to read a per-solve context input
once (as `ComponentSearch` reads its wired Catalog). Existing: `ComponentSearch` (`search_components`);
`OutputSnapshot` (signal-driven viewport capture, not a tool). Reusable bridges: `GhJsonBridge`
(place/validate/resolve graphs), `GhPythonBridge` (drive a Python Script component).

**Key design principle.** Physalia already has a full deterministic build pipeline (Reasoner → Auditor
→ Transmitter → Receiver) that places whole graphs as the primary output. Tools should therefore be
mostly **read/sense + targeted action + verify** — how the model gathers grounding *before* it emits
its structured graph, and inspects *after* — not a second whole-graph builder competing with the
pipeline. Categories borrowed from Claude Code (Read/Write/Edit/Glob/Grep/Bash/WebSearch) and the
Rhino/GH MCP servers: sense state → search → act narrowly → run code → inspect after → reference docs.

## Catalog (prioritized)

### Priority 1 — sensing (build first; read-only, can't corrupt the doc)

1. **`get_document_summary`** — *low.* Compact inventory of the canvas (component name, nickname,
   short GUID, category, error/warning counts; optional param names; doc totals + units). The model
   must see what exists before it builds. Reuses `OnPingDocument().Objects` + `RuntimeMessages`.
2. **`inspect_component`** — *low.* One component's inputs, outputs, current values, and runtime
   messages (by instance GUID or nickname). Turns a red component into a self-correctable observation.
   Reuses `GhPythonBridge`'s value/message readers, generalized to any `IGH_Component`.
3. **`get_selection`** — *low.* Which GH components / Rhino objects are currently selected — bridges
   vague human pointing ("make these taller") to concrete GUIDs.
4. **`query_geometry`** — *medium.* Measure a component output's geometry: bounds, area, length,
   volume, validity (`IsValidWithLog`), count, centroid. The model's only way to numerically "see"
   geometry it produced. Pure RhinoCommon over `VolatileData`.

### Priority 2 — action & code (need verify-after; defer mutation to `RhinoApp.Idle`)

5. **`place_graph`** — *medium; maximal reuse.* Place a small GhJSON sub-graph as a tool call (surgical
   incremental edits mid-conversation). `GhJsonBridge.ResolveComponentNames` + `LoadAndPlaceJson`
   already validate, fix, resolve names, place, and report `PlacedGuids` + `UnfixedIssues`. Needs a
   wired Catalog input.
6. **`set_parameter`** — *medium.* Drive a slider / panel / boolean on an existing component without
   rebuilding. Set `PersistentData` / `GH_NumberSlider.SetSliderValue` then `ExpireSolution`. Reject
   wired inputs with a clear error; only persistent-data params are safely settable.
7. **`run_python`** — *medium-high; gate behind explicit opt-in.* Execute a RhinoCommon Python snippet
   and return stdout + result (the universal escape hatch — the most-used tool in every Rhino MCP
   server). Runs via Rhino's embedded CPython (same path as PyValidator). Arbitrary code execution —
   gate like agent harnesses gate Bash.

### Priority 3 — knowledge & reference

8. **`search_docs`** — *medium.* Web / RhinoCommon docs lookup. Reuses the `HttpClient` plumbing
   (`ProtocolProviderBase` / `HttpErrorMapper`). A local RhinoCommon-XML mode is lower-risk + offline;
   ship that first.
9. **`read_file`** — *low; path-allow-listed to `Files/`.* Read a bundled reference / skill / schema /
   preset into context. Claude Code's `Read`, scoped to Physalia's Files tree.
10. **`calculate`** — *low.* Arithmetic + **unit conversion** via `RhinoMath.UnitScale` against the
    doc's `ModelUnitSystem` ("12 ft to mm" in a millimeters document). Removes a constant friction point.

### Priority 4 — vision (mostly built; wrap it)

11. **`capture_viewport`** — *medium; most exists.* Let the model request a fresh screenshot and
    receive it as an inline image observation. `OutputSnapshot` already zooms, captures, bounds to
    1568 px, and emits an `ImageContent` block; `PhySignal.ContentBlocks` carries images. Caveat:
    `ToolCallResult` is text-only today — image-returning tools need a richer result shape or a
    content-block convention.

### Lower priority / future
`delete_component` / `clear_canvas` (need undo story); `connect`/`disconnect` (subsumed by
`place_graph`); `list_categories` / `get_component_type_info` (browse catalog by category — reuses
`ComponentCatalog`); `run_solution` / `expire_component`; `get_rhino_scene` / `create_rhino_object`
(operate on baked Rhino geometry — a whole second surface).

## Recommended first five

Build the read → act → verify loop, leaning on existing bridges:

1. **`get_document_summary`** — the model must see the canvas. Foundational, pure read.
2. **`inspect_component`** — read a node's values + errors; reuses `GhPythonBridge` readers.
3. **`query_geometry`** — numerically "see" produced geometry; pure RhinoCommon.
4. **`place_graph`** — the core action tool; mostly wiring `ExecuteCall` to `GhJsonBridge`.
5. **`run_python`** — the escape hatch covering everything else; behind an enable toggle.

`search_components` (already built) supplies names. Vision (`capture_viewport`) is the strong sixth
once `ToolCallResult` can carry an image block.

## Implementation gotchas
- **Result shape:** `ToolCallResult` is text-only today — image-returning tools need a richer result.
- **Deferred mutation:** any tool that mutates the doc or solves must defer to `RhinoApp.Idle`
  (`GhJsonGrasshopper.Put` and `ExpireSolution` both kick `NewSolution`; `OutputSnapshot` sets the pattern).
- **Context inputs:** tools needing the catalog or files read them in `OnSolveTick` once per solve.
- **Safety:** `run_python` (and future delete/run-command tools) is arbitrary execution — gate behind
  explicit opt-in; `read_file` must be path-allow-listed to `Files/`.

## Key files
`Components/Tools/ToolComponentBase.cs`, `ComponentSearch.cs`, `ToolsInUse.cs`,
`Components/Regulators/Router.cs`, `Core/Common/ToolDefinition.cs`, `Generation/GhJsonBridge.cs`,
`Generation/GhPythonBridge.cs`, `Components/Perception/OutputSnapshot.cs`, `Core/Catalog/`,
`planning/physalia-primitives.md`.

## Sources
- Claude Code Tools reference (code.claude.com/docs/en/tools-reference).
- RhinoMCP (jingcheng-chen/rhinomcp); rhino-grasshopper-mcp (dongwoosuk).
