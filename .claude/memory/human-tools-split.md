---
name: human-tools-split
description: "LLM Tools vs Human Tools taxonomy split (2026-07-23) — ToolDefinition→LlmToolDefinition renames, new HumanTool union + Human Tools tab (Geometry Snapshot, Add Image), Conversation Log 7-input reorder (accepted saved-doc break), image intake gated on Add Image"
metadata: 
  node_type: memory
  type: project
  originSessionId: e23fffd4-ac4f-41e0-a430-0d20d751f1c5
---

# LLM Tools vs Human Tools split (2026-07-23)

Thomas's design call: Geometry Snapshot was misfiled as grounding (contributes nothing to the prompt — it's a tool for the HUMAN, not the LLM). Programmatic separation introduced:

**Renames (LLM side)** — Core `ToolDefinition`→`LlmToolDefinition`, `GH_ToolDefinition`→`GH_LlmToolDefinition`, `Param_ToolDefinition`→`Param_LlmToolDefinition` (GUID + "Tool Definition"/"Tool" identity strings KEPT), `ToolComponentBase`→`LlmToolComponentBase`, folder `Components/Tools/`→`Components/LlmTools/`, ribbon subcategory "Tools"→"LLM Tools" (base + Router + Tools Present). Chat panel's Tools page relabelled "LLM Tools".

**New Human Tools** — Core union `HumanTool` (`Physalia.Core/HumanTools/HumanTool.cs`): `GeometrySnapshotTool(Message)` (migrated from deleted `GeometrySnapshotGrounding`, same DefaultMessage) + `AddImageTool` (marker). GH: `GH_HumanTool`/`Param_HumanTool` (param GUID 8C5E2D71-…), `HumanToolComponentBase : PhyBase` (passive emitter: no inputs, one Param_HumanTool output, subcategory "Human Tools"), `GeometrySnapshot` (GUID kept D5B8F2A6-…), `AddImage` (new GUID 4F7A9C25-…). Never touch the system prompt, never advertised to the model.

**Conversation Log reorder (SAVED-DOC BREAK, Thomas accepted)** — new input order: 0 System Prompt, 1 Prompt Signal, 2 Grounding, 3 Human Tools (list, optional), 4 Response Signal, 5 Feedback Signal, 6 LLM Tool Signal (renamed from "Tool Signal"; preset ghjson parameterNames updated to match). `ReadHumanToolInputs` beside `ReadGroundingInputs`; `HasGeometrySnapshotTool` (was HasGeometrySnapshotGrounding) + new `HasAddImageTool`. Snapshot override Write/Read keys unchanged.

**Image gating** — image intake FULLY disabled unless Add Image wired: Composer gates addImages choke point, drag-drop listeners detach reactively, paste file branch skipped, picker button disabled w/ tooltip, pending pills purged if unwired mid-composition; C# `SubmitJsonPayload` defensively drops images too. New state field `imageToolWired` (in BOTH Tick groundingSignature and state). Grounding panel kinds view gained a separated "Human Tools" section (snapshot pill moved there + read-only "Image attachments — Enabled" row).

Status: builds clean (slnx Debug, UI re-embedded), 300 tests green. Live Rhino test pending. Related: [[geometry-snapshot-grounding]], [[tool-calling-gh-loop]], [[signal-carrier-discipline]].
