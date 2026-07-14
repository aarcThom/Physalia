---
name: balcony-session-debug
description: "2026-07-13 twisty-tower/balcony session debug — max_tokens truncation root cause, misleading validator errors on truncated JSON, appliedOps not model-facing, geometry report lacks tree topology"
metadata: 
  node_type: memory
  type: project
  originSessionId: ae343759-c086-4d8c-ab5f-dc5eff77c333
---

Debugged the 2026-07-13 tower+balcony live session (chat_log.txt / signal_trace.txt on Desktop, 59 signals).

**Findings:**
1. **Root cause of "took a few tries" + final stall: max_tokens truncation.** Anthropic `FallbackMaxTokens = 8192` (`AnthropicProtocolProvider.cs:30`); Sonnet with (adaptive/visible) thinking shares that budget. Responses 1–2 were cut mid-JSON; the final response (22:49:27, 102 s) was thinking-only with EMPTY text after `</think>` — Detect JSON quietly dead-ended it and the loop went silent. `ThinkingAnswerHeadroom` only bumps max_tokens on the manual-budget path, not adaptive.
2. **Truncated JSON → misleading schema errors.** JsonExtractor takes the LAST parseable block, so a truncated doc yields an inner component/connection object → validator says "property 'name'/'id'/'paramIndex' not allowed at document root" and tells the model to "fix ONLY the violations" — actively wrong guidance. LLM Call's truncation warning (LlmCall.cs:194) is canvas-only; nothing feeds the loop.
3. **Checksum distrust:** after the graft ghpatch, the geometry report body was byte-identical (still 169 breps) with a new checksum; CompTx's `AppliedOps` confirmation is a human-facing remark only (ComponentTransmitter.cs:205-215) — the model never receives "your modify landed", so it (wrongly, hallucinating a checksum "revert") concluded the patch was dropped.
4. **169 = 13×13 cross product at Ruled Surface** (Offset Curve output is branched {0;0..12}, Polygon flat {0}×13). Graft-both *should* fix it (13 branches vs 13 → 1:1). Statically verified the whole modify chain (fast-path eligibility, `GhPatchParameterSettingsEntryOp.set`, `GhJsonParameterSettings.dataMapping`, `ApplyParamModifiers`, ExpireSolution + `doc.NewSolution` at Idle) — all correct, yet 169 persisted in the fresh report. OPEN QUESTION: needs live check (is Graft ticked on BalconySurface Curve A/B?). If not ticked → silent drop somewhere runtime (param Name mismatch → replace path?).
5. Geometry Report gives item counts but no data-tree topology ("169x open brep", not "13 branches × 13") and no per-input graft state — the model had to guess tree structures.

**Fixes APPLIED 2026-07-13 (builds clean, 300 tests green, live test pending):**
1. Anthropic default max_tokens 8192→32768 in ALL THREE places it lived (`AnthropicConfig` record default, `AnthropicModel` GH input default+fallback, provider `FallbackMaxTokens`) — saved .gh docs keep their serialized 8192 until re-placed/edited. Manual thinking default budget decoupled (`DefaultManualThinkingBudget=8192`).
2. New `SupportsEffort` axis in `AnthropicModelDefaults`; adaptive thinking now sends `output_config:{effort:"medium"}` (tempers server default "high" = the over-reasoning).
3. LLM Call: truncated + thinking-only/empty response now routes **Fail** with corrective feedback (was Success w/ empty payload → silent stall).
4. `JsonExtractor.LooksTruncated` (brace-balance, ran-off-end + quote guard); Schema Validator replaces bogus root-level violations with a "response was CUT OFF mid-JSON" feedback.
5. CompTx Success payload appends `applied-op: ` lines (GhJsonBridge.AppliedOpLinePrefix) after GUIDs; GUID parsers skip them; Geometry Report lifts them into a "Patch confirmation — these ops DID land" section.
6. Geometry Report now prints data-tree shape per output ("13 branches × 1 item") and per-component input modifiers ("Curve A: graft") — makes cross-product bugs and graft state visible.
7. Runtime-message recording defaults ON (enabled in ChatWidgetPriority.PriorityLoad; RuntimeMessageTrace defers hookup via Instances.CanvasCreated when no canvas yet). Windows-only entry point (`#if WINDOWS`).

**Patch-add placement fix (2026-07-13, same day):** patch adds used authored pivots VERBATIM in absolute canvas space while the full graph was arrow-anchored + relaid out (`RelayoutLlmGraph`) — two frames, so the balcony landed a screen away from its tower. New `AnchorPatchAdds` in GhJsonBridge.Patch.cs: discards authored add pivots, relayouts the add-group from its internal wires (reuses RelayoutLlmGraph), anchors the group right of `LiveAuthoredBounds` (authored-placement ledger union) → fallback `ConnectedExistingBounds` (existing endpoints of the patch's connection adds) → whole-canvas bounds → arrow origin. Preamble (`Files/SYSTEM_PROMPTS/PREAMBLE/Node Graph.txt`) rewritten: placement is automatic in BOTH modes, emit placeholder grid pivots, never read existing pivots or reason about overlaps (old line 13 explicitly instructed collision avoidance — deleted).

**Still open:** why the graft-both patch left 169 breps in the live session — every static link verified correct; needs the live check (is Graft ticked on BalconySurface Curve A/B?). With fix 6, the next session's report answers this directly. Related: [[thinking-passthrough]], [[iterative-placement-robustness]], [[single-signal-output-rework]].
