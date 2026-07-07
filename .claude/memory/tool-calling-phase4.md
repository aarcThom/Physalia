---
name: tool-calling-phase4
description: Phase 4 (tool calling) keystone — provider contract now sends tool definitions; remaining GH visible-loop work
metadata: 
  node_type: memory
  type: project
  originSessionId: 31fd1665-ebf5-4a5f-8a28-914acda97340
---

Phase 4 of the robustness plan (research.md §5 "Tool calling") is the capstone. Its **keystone landed 2026-06-16**: the provider contract can now *send* tool definitions (previously it only parsed tool calls / round-tripped results).

**What shipped (Core, builds clean):**
- New `Physalia.Core/Common/ToolDefinition.cs` — `record ToolDefinition(string Name, string Description, string InputSchemaJson)`. `InputSchemaJson` is a JSON-Schema object string; blank/unparseable falls back to `{type:object,properties:{}}`.
- `ILlmProvider.StreamAsync` + `ProtocolProviderBase` (abstract) gained an `IReadOnlyList<ToolDefinition>? tools` parameter **before** `ct` (required, nullable; null/empty = send none). All 3 protocol providers + `ClaudeCodeProvider` updated; `ProtocolProviderBase.ParseToolSchema(string?)` is the shared schema-parse helper (returns `JsonNode`).
- Per-protocol serialization in each `BuildRequestBody` (gated `if (tools is { Count: > 0 })`): Anthropic `tools:[{name,description,input_schema}]`; OpenAI `tools:[{type:"function",function:{name,description,parameters}}]`; Gemini `tools:[{functionDeclarations:[{name,description,parameters}]}]`. `ClaudeCodeProvider` ignores tools (single-shot CLI).
- `LlmCall.cs:150` call site passes `tools: null` for now (no behavior change yet).
- **`tool_choice` deliberately NOT added** — auto is the provider default; defer until steering is needed.

**Increment 1 of the visible loop LANDED 2026-06-16 (builds clean, runtime untested in Rhino).** Topology the user chose: everything funnels through an explicit **Router**, cycle-free via the existing wireless `Feedback`→`FeedbackCollector` transport. Decisions: tool calls/results ride existing `ContentBlocks` (`ToolCallContent`/`ToolResultContent`) — **PhySignal unchanged**; dispatch **by Router output nickname == tool name**.
- **`RoutingComponentBase`**: opt-in 3rd output — `RegisterAdditionalOutputs` hook + `AuxOutputIndex` (default −1) + `AuxSignal` + `RoutingResult.Aux(PhySignal)`; latch branch latches quietly and stashes AuxSignal; `Emit` re-emits it. Zero impact on Schema Validator/Component Resolver/Canvas Observation/Transmitter.
- **LLM Call**: new optional **Tools** input (`Param_ToolDefinition`, list, index 2 → Cancel now 3, Signal 4) passed to `StreamAsync`; new **Tool Calls** output (index 2, `AuxOutputIndex=>2`); surfaces `chunk.ToolCalls`; `ReadSolve` returns `Aux` with a signal carrying `[TextContent?, ToolCallContent…]` when the model calls tools, else `Ok` (final-answer path unchanged — regression-safe).
- **Router** (`Components/Tools/Router.cs`): `StatefulComponentBase` + `IGH_VariableParameterComponent`. Inputs Tool Calls(0)+Results(1); variable tool outputs (rename nickname=tool name) + fixed **Feedback** output LAST (gating: insert `index<Count`, remove `index<Count-1`). Dispatches each `ToolCallContent` to the nickname-matched output; forwards the assistant request and the returned results to the Feedback output; per-nickname latched re-emit.
- **FeedbackCollector**: now aggregates `ContentBlocks` across the batch (was payload-string only) so `ToolResultContent` call-ids survive.
- **Conversation Log**: new **Tool** input (index 4 → Conversation now 5); `RecordToolSignal` logs `ToolCallContent`→assistant turn (quiet) and `ToolResultContent`→user turn (fires LLM Call, closing the loop).
- **ComponentSearch** (`Components/Tools/ComponentSearch.cs`, new): example tool node. Outputs `search_components` `ToolDefinition` + a Result signal; on dispatch, parses `{query}`, searches the wired `ComponentCatalog`, emits `ToolResultContent`.
- **New goo/param**: `GH_ToolDefinition` + `Param_ToolDefinition` (GUID A2D4F6B8-…).
- **Caveat:** adding LLM Call/Conversation Log inputs shifts param indices → old saved .gh canvases with those components may need re-wiring.

**Still TODO (later increments):** robust wait-for-all-results barrier (v1 leans on FeedbackCollector batching + sequence order); tool defs via System Prompt (v1 wires straight to LLM Call.Tools); more tools (Read-Errors/Query-Doc); `tool_choice`; visible iteration cap; Phase 5 (GhPatch delta). Design forks already resolved are in the approved plan file.

**Phase 5 (separate):** GhPatch-shaped delta + session stable-ID map in Transmitter v2.
