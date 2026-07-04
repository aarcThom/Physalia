---
name: tools-in-use-component
description: "Tools In Use GH component — scans doc for tool nodes wired to a Router, emits their definitions as one list into LLM Call.Tools (replaces manual per-tool fan-in)"
metadata: 
  node_type: memory
  type: project
  originSessionId: f7bcfc55-648d-4e1a-a389-c7f91f9456cd
---

New GH component **Tools In Use** (`src/Physalia.GH/Components/Tools/ToolsInUse.cs`, `: PhyBase`, subcategory `Tools`, GUID `E3A7C612-9F84-4B0D-A5E1-7C2D8F61B934`) replaces the clunky fan-in where each tool node's `Tool` output was wired into `LLM Call.Tools` by hand. Now one wire: `Tools In Use.Tools → LLM Call.Tools`.

**What it does:** no inputs; one `Param_ToolDefinition` list output. `SolveInstance` scans `doc.Objects.OfType<ToolComponentBase>()` and keeps only tools whose **Signal input (index 0) has a source owned by a `Router`** (`IsDispatchedFromRouter`) — that is the "in use" criterion (chosen over auto-scope-to-pipeline and all-tools-on-canvas). Stray/unwired tool nodes are excluded.

**Freshness:** subscribes `document.SolutionEnd` in `AddedToDocument`/`RemovedFromDocument` (same pattern as [[component-transmitter]]'s sibling Router). Re-solves itself via `OnPingDocument()?.ScheduleSolution(1, _ => ExpireSolution(false))` **only when the in-use signature changes** — signature = sorted `InstanceGuid:N + AdvertisedDefinition.Name` per in-use tool. The signature comparison is what makes it converge instead of looping (a wire change doesn't re-solve the source component, hence the SolutionEnd hook; Router does the same but only renames, which is display-only).

**Supporting change:** `ToolComponentBase` (`Components/Tools/ToolComponentBase.cs`) gained `public ToolDefinition AdvertisedDefinition => Definition;` so the scanner reads each tool's definition without depending on the node having solved.

**Unchanged:** LLM Call (`Tools` is already a `GH_ParamAccess.list` input; manual per-tool wires still work alongside Tools In Use — additive). Router (still reads each tool's `Tool` output volatile data for its output-name sync, so that output stays).

**Known limitation:** with two independent pipelines on one canvas, a single Tools In Use merges all Router-wired tools across both (auto-scoping was explicitly declined). Fine for single-pipeline use.

Builds clean (`dotnet build src/Physalia.slnx -c Debug`, 0 errors). **Live Rhino test still pending** as of 2026-06-25: confirm the list tracks Router-wired tools, excludes an unwired tool node, drops a tool on Signal-wire disconnect with no runaway re-solving, and that the tool-calling loop still completes.
