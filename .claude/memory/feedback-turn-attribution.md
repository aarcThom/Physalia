---
name: feedback-turn-attribution
description: 2026-08-17 — feedback turns in the chat window are collapsed by default behind a header naming the producing component (icon + nickname); provenance travels as PhySignal.Origins through aggregators.
metadata:
  type: project
---

A feedback turn in the chat window (the pink, machine-generated user turn — geometry reports,
validation dumps) now renders **collapsed by default** behind a header carrying the **Grasshopper
icon and canvas nickname** of the component that produced it. Expanding it works like the Thinking
section. Sketch-driven request, 2026-08-17.

**Why it needed Core work at all.** `PhySignal` already carried `SourceId`/`SourceName`, but every
aggregator re-mints under its OWN identity, so a report routed through a Merge Signal or a Feedback
Collector reached the Conversation Log as "Merge Signal" — the producing node was gone. So:

- New `Physalia.Core.Common.ComponentOrigin(Guid Id, string Name)`.
- `PhySignal.Origins` + `OriginTrail` (the trail, or the emitter itself when empty). **This is
  provenance, sitting with `SourceId`/`SourceName`/`Timestamp` — NOT a fourth carrier field.** The
  carrier discipline (Payload / ContentBlocks / Instructions) is untouched; see
  [[signal-carrier-discipline]]. Nothing branches on it.
- `SignalAggregation.Combine` returns the deduped combined trail — so both aggregators get it for
  free ([[merge-signal-join]]). Stall Guard passes it on its escalation re-mint.
- `ConversationMessage.Sources` (presentation-only, session-only, exactly like `IsFeedback`);
  `ConversationLogBuilder` stamps `signal.OriginTrail` onto every recorded turn.

**Two merge sites had to learn the same rule.** `Conversation.MergeIntoLastUserMessage` previously
DROPPED `IsFeedback` outright; it now takes `incomingIsFeedback` + `incomingSources` and keeps
`prev && incoming` (matching `CompactionInvariants.MergeConsecutiveSameRole`, which also unions
sources now). The `&&` is load-bearing: a human prompt merged onto a feedback turn must not be
presented as machine-generated.

**The icon.** `IGH_DocumentObject.Icon_24x24` IS on the interface (verified by reflection), so
`ChatWindow` resolves each origin guid in the viewed Chat's own document — inside a harness that is
the sub-document the whole pipeline lives in — and PNG-encodes the icon to a `data:` URI, cached per
guid per window (the history is rebuilt on every conversation change). Nickname is read LIVE; the
trail's recorded name is only the fallback for a deleted node, so a turn keeps its attribution.

UI: `lib/chat/FeedbackTurn.svelte` (bits-ui Collapsible, `--neu-feedback*` palette on both header
pill and body), and `App.svelte` now renders the user-turn body from a shared `userBody` snippet so
plain and feedback turns cannot drift.

**Not yet run in Rhino** — builds clean (`dotnet build src/Physalia.slnx -c Debug`, UI embedded) and
469 Core tests pass, 7 of them new (`Physalia.Core.Tests/Signals/SignalOriginTests.cs`).
