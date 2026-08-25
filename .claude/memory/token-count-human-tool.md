---
name: token-count-human-tool
description: Token Count human tool (2026-08-24) — the chat window's token counter moved off the Token Estimator onto its own grip-linked human tool.
metadata:
  type: project
---

**2026-08-24 — new human tool "Token Count" (`Components/HumanTools/TokenCount.cs`).**
Counting and displaying the count are two components now, on the same separation-of-concerns
ethos as [[settings-ownership]].

- **Grip-links to a `TokenEstimator`**, copying the Script I/O pattern exactly
  (`TokenCountAttrib : GripLinkAttrib`, `ArrowStyles.TokenCount` = spring green → slate blue,
  `IGuidLinked.RemapLinks` so it survives a preset load). See [[script-io-grounder]].
- **The link is the ONLY resolution path.** `PromptPipelineView.GetDownstreamTokenCount` — the old
  "first Token Estimator downstream of the Conversation Log" walk — is **deleted**. Decided with
  the user against an auto-find fallback: a pipeline may hold several estimators (a cheap local one
  gating a compactor, an exact API-backed one for the display) and "whichever the wires reached
  first" is a silent wrong answer. Unlinked → warning on the node, no counter.
- Consequence, accepted: **an estimator on its own now shows nothing.** No shipped preset carried a
  Token Estimator (checked), so nothing migrated.
- The count is read **live** off the estimator's output via `ConversationLog.LinkedTokenCountOrNull`
  (an `Owners<TokenCount>` walk, same façade discipline as every other setting owner) — the chat
  window asks on its own 0.15 s tick and nothing re-solves the tool when the estimator recounts.
- **`HumanToolComponentBase` grew `OnSolveEnd()`** — the base seals `SolveInstance` (a human tool
  emits its type and nothing else), so a tool that must report state of its own had nowhere to call
  `AddRuntimeMessage`. Named to match `LlmToolComponentBase.OnSolveEnd`.
- UI: `tokenCountToolWired` rides the grounding payload for the Human Tools row in the grounding
  panel; the corner counter itself still keys purely on `setTokenCount` being non-null, so no gating
  logic was added to the page.
- **Icon `TokenCount.png` was DRAWN IN CODE**, not generated — a lone regenerated sheet cell comes
  back at a different bead size, and at 24 px the bead texture is not resolvable anyway. Window
  frame (the SignalTrace/ReadUrl shape) + a gauge arc with a cyan needle. Recipe in
  `planning/component-icon-prompts.md`; see [[component-icon-generation]].
- Built clean (`dotnet build src/Physalia.slnx -c Debug`, UI re-embedded). **Not yet run in Rhino** —
  the grip drag, the warning, and the counter appearing/disappearing are unverified live.
