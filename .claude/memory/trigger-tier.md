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
source tier, with four concrete sources: **Timer**, **Folder Watcher**, **Rhino Changed**, **Data
Changed**. BUILT, full solution compiles, **not run in Rhino**.

**Why:** an "adaptable harness" that can only react to a person present is not a harness, it is a
chat window. The Rhino Changed one is the character change — it turns Physalia from a generator into
a participant in an ongoing model.

**How to apply:**
- **Arming is session-only and MUST NOT be serialized.** A file that opened armed would spend money
  on whatever machine opened it. Same family of reasoning as first-observation baselining. The kill
  switch is `TriggerRegistry` (weak registry) + the harness panel's "Disarm N triggers" button, which
  is visible in BOTH panel states because the panel opens collapsed.
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
