---
name: signal-relay-conditionals
description: "2026-09-06 — SignalRelayBase (Gate / Hold / Switch / Throttle) plus For Each; a relay must forward the ORIGINAL signal, never re-mint it."
metadata: 
  node_type: memory
  type: project
  originSessionId: 87baa99a-bc2a-4051-b3ac-90fef7c24c3a
  modified: 2026-09-07T04:16:24.008Z
---

Every branch in Physalia used to be a specialist — Detect JSON knows only about JSON, Stall Guard
only about repeated failures, a guardrail's Success/Fail pair only about its own run — so "only carry
on if…" had nowhere to live. `SignalRelayBase` (`Components/ControlFlow/`) is the conditional layer:
**Signal Gate** (decides now), **Hold Signal** (waits), **Signal Switch** (payload text; regex is a
context-menu toggle), **Signal Throttle** (one per interval, newest wins). Plus **For Each**
(`ForEachSignal`), which is not on that base. BUILT, **not run in Rhino**.

**Why:** the trigger tier makes rounds start on their own, and rounds that start on their own need a
way to be refused, delayed and rate-limited.

**How to apply:**
- **Forward the ORIGINAL signal object. Never re-mint.** A signal carries `Instructions` on the
  Conversation Log→LLM Call hop plus content blocks and an origin trail; a re-mint would have a gate
  placed inline *silently strip the conversation it was gating*. The sequence travels with it, so a
  held signal does not become "newer" than one behind it. Same discipline as Signal Limiter, which
  already forwarded rather than re-minting.
- **One signal per solve, with a follow-up scheduled** — a relay may HOLD, and the held one must not
  be jumped. Do NOT schedule for signals queued *behind* a hold: `Route` asks for that solve when the
  hold clears, and asking every solve is a busy loop wearing a timer's clothes (hit while writing it).
- Two hold policies cover every case: `KeepOldest` (a wait), `KeepNewest` (a throttle — an overtaken
  event is stale by definition).
- **Hold Signal's `Recheck` must also expire the components wired into `Release`.** Expiring the
  relay itself re-reads nothing, because GH recomputes only what it expired — so a condition computed
  from data that never changes on its own would be re-read forever and answer the same forever.
- **Outcome routing needs no new node**: Deconstruct Signal already exposes a `Success` boolean, so
  that into a Gate is the exact form. Worth remembering because a Merge Signal's combined outcome is
  otherwise unreachable — the branches lost their own Success/Fail outputs by then.
- **For Each is strictly sequential and that is a rule, not a limitation**: the pipeline downstream
  has ONE Conversation Log, so twelve items at once interleave into one conversation. `Next` is wired
  from the END of the per-item work. `Index` is what carries anything that is not text. The list is
  **snapshotted at Start** — a pipeline that edits the canvas can otherwise extend the list it is
  iterating. An empty list is DONE, not broken. For genuinely independent per-item work, use
  [[harness-delegation]] instead — that is the actual fix for a shared context.

Related: [[trigger-tier]], [[intent-tools-declare-ask-human]], [[signal-carrier-discipline]].
