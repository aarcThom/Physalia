---
name: trigger-tier
description: "2026-09-06 — Physalia's event tier (Timer / Folder Watcher / Rhino Changed / Data Changed); arming is session-only, PipelineWake stops dropped wake-ups, PipelineFileWrites breaks the download loop."
metadata: 
  node_type: memory
  type: project
  originSessionId: 87baa99a-bc2a-4051-b3ac-90fef7c24c3a
  modified: 2026-09-07T04:16:07.512Z
---

Physalia had **no event sources** until 2026-09-06: a round could only start because a human typed in
the chat, a Button drove Construct Signal, or a Feedback loop re-entered — so every pipeline was
downstream of somebody sitting there. `SignalSourceBase<TEvent>` (`Components/Triggers/`) is the
source tier, with five concrete sources: **Timer**, **Folder Watcher**, **Rhino Changed**, **Data
Changed** and **Watch Modelling**. **Watch Modelling has been run live in Rhino and works**
([[watch-modelling]]) — which also exercised the base's arming, caption and fire path. The other four
are BUILT and compile but have **not been run in Rhino**.

**Why:** an "adaptable harness" that can only react to a person present is not a harness, it is a
chat window. The Rhino Changed one is the character change — it turns Physalia from a generator into
a participant in an ongoing model.

**How to apply:**
- **Arming is session-only and MUST NOT be serialized.** A file that opened armed would spend money
  on whatever machine opened it. Same family of reasoning as first-observation baselining. The kill
  switch is `TriggerRegistry` (weak registry) + the harness panel's "Disarm N triggers" button, which
  is visible in BOTH panel states because the panel opens collapsed.
- **Arming lives in the chat window now, via the `TriggerControl` human tool** (added 2026-09-06,
  UI verified headlessly). A node's own menu is right for ONE trigger and useless for finding the
  three that are armed inside a harness. Two things about it generalise:
  **(a) there are TWO arming verbs and conflating them makes one case silently wrong** —
  `SetArmedAndHandOver` for a switch aimed at one named trigger (what its own menu does, so a
  recorder SENDS its batch) and `SetArmed` for switch-everything-off (the kill switch, which
  discards). **(b) address a trigger by `InstanceGuid`, never by nickname** — Folder Watcher and
  Watch Modelling BOTH default to the nickname "Watch", so a name-keyed switch flips whichever it
  finds first. The list is read live off the document every tick because arming changes no data and
  runs no solution, so there is no event to push from.
  **Both verbs verified live 2026-09-06**: an individual switch-off produced a NEW signal from an
  armed recorder, while switch-all-off discarded a provably non-empty batch (4 events pending, read
  off `PendingCount` rather than inferred) and minted nothing. Test the discard case by CHECKING
  something was pending first — "no signal appeared" is otherwise consistent with there having been
  nothing to discard, which is how a weak pass slips through.
- **A recorder's caption did not tick up** (found live, fixed, NOT yet verified): a recorder mints
  nothing while running, so the pending count on the node is the only sign it is catching anything —
  and `Message` is only rewritten on a state transition, which a recorder makes none of until it
  stops. `ReportEvent` now refreshes it when `FiresOnDisarm`.
- **`PipelineWake.Ready(component)` before any scheduled solution from an external event.** GH drops
  scheduled solutions on a disabled document; a harness sub-document's `Enabled` is our invariant
  re-asserted by the proxy's solve, and the proxy only solves when the host does. Re-enable **only a
  harness document** — on the user's file that flag is Grasshopper's solver lock. Symptom without it:
  a trigger that looks armed and does nothing, silently. Shared with `TaskIn`.
- **Coalesce bursts with a RESTARTING settle timer**, not a fixed window. One copied folder is one
  event per file; one saved file is several for that file; 500 objects is 500 Rhino events.
- **`PipelineFileWrites` (30s, path-keyed) is not optional.** `download_file` writes into the project
  folder → the watcher wakes the model → it fetches the next file. No round or stall limit catches
  that, because every round is genuinely different. A **browser fetch must NOT register** — that path
  exists precisely so the watcher hands the file to the model.
- **Data Changed re-opens the cycle hazard [[harness-io]] avoided** by making Harness In passive.
  Nothing can detect it: the cycle runs through the user's canvas and a deliberate re-run looks
  identical to a runaway. Signal Limiter bounds it; the Budget Guard is the backstop.

Related: [[signal-relay-conditionals]], [[session-budget-and-records]], [[harness-io]],
[[signal-lifecycle-summary]].
