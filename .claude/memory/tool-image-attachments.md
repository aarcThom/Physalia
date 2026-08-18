---
name: tool-image-attachments
description: "2026-08-18 how an LLM tool answers with an IMAGE — the attachment path added through ToolBatchRunner/Router/ConversationLogBuilder, and the Take Snapshot tool that needed it"
metadata: 
  node_type: memory
  type: project
  originSessionId: 13e6b92d-5e4a-4e96-afb2-c30c6aab719e
  modified: 2026-08-18T07:14:24.115Z
---

**2026-08-18: new LLM tool `take_snapshot`** (`Components/LlmTools/TakeSnapshot.cs`), plus the pipeline
path that let a tool answer with a picture at all. Camera stands at the wired `Current Location`, aims
anywhere over the full sphere, captures, and hands the image back to the model.

**Why:** the load-bearing discovery is that **a tool result is TEXT on every provider.**
`ToolResultContent.Content` is a string, OpenAI's `role:tool` message and Gemini's `functionResponse`
have nowhere to put an image, and only Anthropic accepts image blocks nested inside a `tool_result`.
So "return an image from a tool" is not a tool_result feature — the image has to ride the SAME
answering user turn as a sibling block.

**How to apply:**
- The path is `ToolCallResult.OkWith(text, blocks)` → `ToolCallOutcome.Attachments` → `ToolBatchRunner`
  → `Router` → `ToolDispatchRound.CombineResults` → `ConversationLogBuilder.RecordToolSignal`.
- **Attachments MUST sort after every `ToolResultContent`** — Anthropic requires tool_result blocks to
  lead the user message answering a tool_use turn. Enforced twice on purpose: the runner orders within
  one tool node, the Router orders across nodes.
- **It was dropped in TWO places before this**, both silent: `Router.CollectResults` filtered to
  `OfType<ToolResultContent>()`, and `ConversationLogBuilder.RecordToolSignal` only recorded non-result
  blocks when a `ToolCallContent` was present (so an image with no tool_use vanished). Fixing one
  without the other gets you nothing.
- **No provider change was needed** — verified by reading all three: Anthropic emits tool_result and
  image blocks side by side; the OpenAI protocol ALREADY splits such a turn into `role:tool` messages
  plus a `role:user` message; Gemini emits functionResponse + inlineData parts. `ToolPairing` only
  reports problems, never strips. Compaction's `Reassemble` keeps non-tool blocks via its `default`
  arm, so an image survives even when its tool result is compacted away as an orphan.

**Take Snapshot specifics:**
- **`RunsAsync => true` is not about speed.** Posing a viewport is illegal inside a Grasshopper
  solution and must happen on the UI thread, so the async path lets the dispatch solve finish and the
  capture is marshalled onto `RhinoApp.Idle` (the Geometry Observation deferral) and awaited from the
  background batch — with a timeout, or a never-idle Rhino hangs the round with its tool id unanswered.
- **The user's viewport is borrowed, not spent:** `ViewportSnapshot.TryCaptureFromCamera` wraps the
  pose in Rhino's own `PushViewProjection`/`PopViewProjection` with the pop in a `finally`. Reflected
  the API rather than guessing — `ViewCaptureSettings.SetViewport` takes a **RhinoViewport**, not a
  `ViewportInfo`, so there is no way to capture from a synthetic camera without touching a real view.
- **35mm lens (~54° horizontal FOV), and the model is TOLD that number** — knowing what is off-camera
  is what stops it reading "not in frame" as "not in the model". 50mm is the other literal answer to
  "human" but crops too tight indoors to see a room.
- **`Snapshot Directions` is a TREE, one branch per VISIT** (a revisit opens a new branch), so branch
  order equals walk order. Downstream can always collapse branches by point; it could never re-split
  them if merged here.
- **One compass across both tools, structurally:** azimuth lives in `SpaceNavigator.Aim` /
  `BearingLabel` beside the cone table it inverts, so azimuth 90 aims exactly where [[move-in-space-tool]]
  walks `right`. Round-tripped in the harness for all 8 bearings. Azimuth is WRAPPED (450→90, a
  readable intent) but elevation is CLAMPED (past vertical the camera is upside down, not aimed
  elsewhere).

**Verification:** 476 Core tests green (7 new: attachment ordering in the runner sync+async, in
`CombineResults`, the recorder's results+image turn, and a regression test that a tool_use-bearing
signal still puts non-result blocks on the ASSISTANT turn). 61 checks green in the console harness
including the aim/cone round-trip. **Not yet run in Rhino** — the Idle capture, the camera restore, and
the tree output are unexercised live. No icon (`TakeSnapshot.png` absent → `brain.png` fallback).
