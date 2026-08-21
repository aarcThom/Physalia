---
name: harness-receiver-inlets
description: "The Receiver component (2026-08-18) — harness inlets, why inward dataflow is a real GH input while outward stays a side effect, and why an inlet param must bind by InstanceGuid."
metadata: 
  node_type: memory
  type: project
  originSessionId: d239a690-60af-41ac-9249-d23231b49367
  modified: 2026-08-19T04:52:44.384Z
---

Built 2026-08-18: **Receiver** (`Rx`, section "Receivers"), the inverse of a transmitter. No inputs,
one generic tree output; placing one inside a harness grows a real Grasshopper input on the LEFT edge
of the harness proxy. Intended use: passing geometry and goal
conditions in for LLM tools to reason against. Complements [[harness-subdocument]] and
[[geometry-transmitter]].

**Why:** the harness's founding claim was "a pipeline never exchanges dataflow with the canvas". That
is now half true, deliberately. What a pipeline *produces* is an edit to the canvas and GH has no
mechanism for "a wire that writes", so outlets stay side effects on painted drag arrows. What a
pipeline *consumes* is data the canvas already computed and GH hands us inward wires for free — so an
inlet is an ordinary input param, with ordinary expiry and solve ordering. The symmetric-looking
alternative (a left-edge drag arrow that reaches out and pulls a param's volatile data) was rejected:
it buys purity and inherits the Script I/O watch-hell, with no expiry guarantees.

**How to apply:**
- **Passive by design, and it must stay that way.** A Receiver mints no signal and starts no round; it
  latches its tree and re-emits on every sub-document solve. Two reasons, and the second is
  structural: a triggering inlet would fire an inference per slider tick, AND it would close a cycle
  GH's own detector cannot see (transmitter writes canvas → canvas feeds receiver → harness solves →
  transmitter writes). Passive means nothing in the pipeline ACTS on inlet data alone, so the loop
  cannot close. The user explicitly refused an opt-in toggle for this.
- **Bind an inlet param to its Receiver by `InstanceGuid`, never by position.** This is the one place
  the outlet pattern must NOT be copied. An outlet's grip is an arrow we paint — no place in GH's
  graph, so reorder and rebuild it freely. An inlet's param is a real object other components' wires
  point AT: rebuild it and the wire dies, re-bind by index and one Receiver's data silently becomes
  another's. `Param_Inlet.ReceiverId` persists the binding; `SyncInlets` reuses a live Receiver's
  param and reorders by MOVING the param objects, so sources travel with them
  (`UnregisterInputParameter(p, isolate: false)` for a mover, `isolate: true` only for one leaving).
- **`HarnessAttrib` had to grow layout AND render for the inputs.** It composes its capsule by hand
  and never reaches `GH_ComponentAttributes`'s render, so params would be laid out, wireable and
  completely invisible. And GH sizes the capsule from the params *before* the class grows it for the
  outlets — so the rows are re-centred by pure translation of Bounds+Pivot (the input grip is derived
  from them, so it moves along). Also: the ×2.35 WidthFactor must not apply once there are inputs, or
  three short labels give a node half a canvas wide.
- **Sync triggers.** Idle-deferred (mutating the param set inside a solution is illegal), fired by the
  sub-doc's `ObjectsAdded`/`ObjectsDeleted`, `AddedToDocument`, `Adopt`, and — as a shortcut only —
  each Receiver's `ObjectChanged`. Renames and moves reach no solution anywhere, the same class of
  problem [[script-io-grounder]] has, but see finding 2 below: the events are unreliable, and
  `RefreshInlets()` at layout time is the actual mechanism.
- The push itself is in the proxy's `SolveInstance`: hand each tree over, and when `TreeIdentity` says
  something changed, schedule ONE solution on the harness document with those Receivers expired.
  Expiring inside a scheduled callback is the safe shape; never `NewSolution` from inside a solve.
- `TreeIdentity` (new, `Components/TreeIdentity.cs`) is GeoTx's shape+reference-identity key extracted
  and shared. Over-sends, never misses.

**Two bugs found on the first Rhino run (2026-08-18), both now fixed and both worth remembering:**

1. **The incoming wire was not drawn, though the data transferred.** `HarnessAttrib` composes the
   Objects channel by hand and so never called `base.Render` *at all* — and GH's own render is what
   draws the wires arriving at a component's inputs. Invisible for as long as the proxy had no inputs.
   The symptom is diagnostic: painting and delivery are unrelated, so "data arrives but no wire" always
   means a render path, never a solver one. Any attribute that hand-composes one channel must still
   fall through to `base.Render` for the others.
2. **A rename did not propagate.** `GH_DocumentObject`'s `NickName` setter raises NO event — verified
   by reading the IL of the shipped Grasshopper.dll: the setter body is a bare field assignment with no
   calls. The only members that raise `ObjectChanged(NickName)` are the right-click name box handlers
   (`Menu_NameItemTextChanged`, `Menu_NickNameChanged`, `Menu_NameItemKeyDown`), so an F2 or
   properties-panel rename reaches nothing; and **nothing raises anything for a MOVE**.
   It took THREE attempts, and the two dead ends are the lesson:
   - *Reconcile at layout* — failed. `PerformLayout` is called from about a dozen places in
     Grasshopper.dll and the paint loop is not one of them, so `ExpireLayout()` is not a promise that
     `Layout()` will ever run. Layout is performed on SOLUTION, not on paint.
   - *Draw the label live off the Receiver in `Render`* — fixed the symptom and broke the other
     direction: the name became derived-only, so renaming the input on the proxy silently reverted.
     A name editable at both ends needs a two-way sync, not a one-way read.
   What works: **override the virtual `NickName` setter** (declared on `GH_InstanceDescription`, and
   virtual — which is the only reason any of this is reachable) at BOTH ends, via the shared
   `Param_LinkedName` base. One name, either end editable, recursion cut by an equality guard, a
   cleared name normalised back to the default rather than obeyed. A MOVE has no hook whatsoever, so
   order drift is checked in `SolveInstance` — the one thing that runs often and runs for certain —
   and handed to the idle sync.
   **This also casts doubt on [[script-io-grounder]]'s rename watch**, which is built on
   `ObjectChanged` alone and has never been run live.

3. **And the linked pair was the wrong pair.** Two rounds went into syncing the Receiver COMPONENT's
   nickname with the proxy input. What the pipeline actually wants linked is the Receiver's **output
   parameter** nickname with the proxy input's nickname — one name on the wire inside and on the grip
   outside, both defaulting to "Data". The component's own nickname stays free to say what the node is
   ("Rx"). Lesson for next time: when a request says "nickname", ask WHICH object's — a GH component
   and each of its parameters all have one, and they are all visible on the canvas.

**Otherwise not yet run in Rhino.** Builds clean, 482 Core tests pass. The one thing to watch on first launch:
whether GH restores the archived `Param_Inlet` set on file reload (it should — same machinery Merge
Signal's variable inputs use, and `IGH_VariableParameterComponent` is implemented for it); if it does
not, wires into a harness are lost on reopen and the sync silently rebuilds fresh params.
