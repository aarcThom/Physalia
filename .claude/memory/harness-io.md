---
name: harness-io
description: "Harness In and Harness Out — the two I/O components at the harness boundary, why inward is a real wire while outward is a side effect, and how a tree is internalized into an arbitrary param."
metadata: 
  node_type: memory
  type: project
  originSessionId: d239a690-60af-41ac-9249-d23231b49367
  modified: 2026-08-21T08:43:12.725Z
---

The harness boundary as the user meets it, finished 2026-08-19: two plainly named nodes under the
**I/O** ribbon section (`src/Physalia.GH/Components/IO/`), each with exactly one side.

- **Harness In** (`HarnessIn`) — no inputs, one generic tree output. Placing one inside a harness
  grows a real input on the LEFT edge of the proxy; what is wired in out there arrives here.
- **Harness Out** (`HarnessOut`) — one generic tree input, **no outputs**. An endpoint, not a
  passthrough: the pipeline ends there and the data is written into a target on the user's canvas
  through the drag grip the proxy hosts.

Both are generic at tree access, so geometry, numbers, text, booleans and colours all ride through.
Harness Out is the merge of the former Text and Geometry transmitters, which differed only in the
param type each declared and between them still refused booleans, integers and colours.
ComponentGuids were kept across every rename (Receiver → Harness In, GH Data Transmitter → Harness
Out), so old documents still load. Complements [[harness-subdocument]]; the layout and rename
mechanics it depends on are in [[gh-custom-attribute-traps]].

**Why the two sides are shaped differently.** What a pipeline *produces* is an edit to the canvas,
and GH has no mechanism for "a wire that writes" — so an outlet is a side effect on an arrow we
paint. What a pipeline *consumes* is data the canvas already computed, and GH hands us inward wires
for free — so an inlet is an ordinary input param with ordinary expiry and solve ordering. The
symmetric-looking alternative (a left-edge arrow that reaches out and pulls volatile data) was
rejected: it buys purity and inherits the [[script-io-grounder]] watch machinery with no ordering
guarantees.

## Rules that must not be eroded

- **Harness In is PASSIVE.** It mints no signal and starts no round; it latches its tree and re-emits
  on every sub-document solve. Two reasons, the second structural: a triggering inlet would fire an
  inference per slider tick, AND it would close a cycle GH's own detector cannot see (Harness Out
  writes canvas → canvas feeds Harness In → harness solves → writes again). Passive means nothing in
  the pipeline ACTS on inlet data alone, so the loop cannot close. The user explicitly refused an
  opt-in toggle for this.
- **An inlet param binds to its node by `InstanceGuid`, never by position.** The one place the outlet
  pattern must NOT be copied: an outlet's grip is an arrow we paint with no place in GH's graph, so
  reorder and rebuild it freely; an inlet's param is a real object other components' wires point AT.
  `Param_Inlet.InletId` persists the binding; `SyncInlets` reuses a live node's param and reorders by
  MOVING the param objects (`UnregisterInputParameter(p, isolate: false)` for a mover, `isolate:
  true` only for one leaving) so sources travel with them.
- **Param-set changes are idle-deferred** — mutating them inside a solution is illegal. Triggered by
  the sub-doc's `ObjectsAdded`/`ObjectsDeleted`, `AddedToDocument`, `Adopt`, and an order-drift check
  in `SolveInstance` (a MOVE raises no event anywhere; the solve is the only hook that runs often and
  runs for certain).
- **The push** is in the proxy's `SolveInstance`: hand each tree over and, when `TreeIdentity` says
  something changed, schedule ONE solution on the harness document with those nodes expired.
  Expiring inside a scheduled callback is the safe shape; never `NewSolution` from inside a solve.
- **One name per port, and Harness In's is two-way.** A Harness In's output nickname and its proxy
  input's nickname are the same name (both start "Data"), synced in both directions; a Harness Out's
  input nickname labels the grip the proxy paints, one-way only, because a painted label has no
  editor. Both inside ends are `Param_HarnessPort : Param_LinkedName`, which overrides the virtual
  `NickName` setter — see [[gh-custom-attribute-traps]] for why nothing else works. A cleared name is
  normalised back to "Data" rather than obeyed.

## Delivery: writing a tree into an arbitrary param (`ParamTargets`)

The hard-won part, and it survives unchanged from the Geometry Transmitter it started as.

1. **`SetPersistentData(params object[])` can only make ONE flat branch.** Internalizing a tree
   branch-for-branch needs the `SetPersistentData(GH_Structure<T>)` overload, and `T` is only known
   at runtime. `ParamTargets.WriteTree<T>` walks the target's base chain to its constructed
   `GH_PersistentParam<T>`, `MakeGenericType`s a `GH_Structure<T>`, and casts each item with the
   param's own **protected `Cast_Object`** — reflection-invocable, and the exact conversion the flat
   setter performs, so a Brep entering a Mesh input converts as it would through a wire. (Verified by
   invoking it on a live `Param_Point` outside Rhino — see [[inspecting-rhino-assemblies]].) Refused
   items come back as a COUNT and are reported; silently dropping half a tree is the one failure a
   user cannot see.
2. **Change detection is reference identity** (`TreeIdentity`: tree shape + `RuntimeHelpers.GetHashCode`
   per goo). GH re-mints goo only when its producer recomputes, so identical references prove nothing
   ran. Can over-send, can never MISS — a bounding-box compare fails the other way, and an honest
   value compare costs a full Brep diff every solve. **Exception: a signal is keyed by SEQUENCE**, as
   a latched signal is re-wrapped in fresh goo every solve and identity would write to the canvas on
   every scheduled solve.
3. **Copy the tree before the deferred write.** The one handed to `SolveInstance` belongs to the input
   param and is cleared when that param next expires — well before the `RhinoApp.Idle` callback runs.
   `new GH_Structure<T>(tree, false)`, shallow.
4. **Stringifying is PER ITEM, container preserved.** `Param_String.Cast_Object` stringifies each goo
   on its own (probed: a `GH_Point` → `"{1, 2, 3}"`), so a text param takes the tree through the
   ordinary path. On top of that, an item any param refuses outright is retried as a `GH_String` of
   its text form.
5. **A Panel is the one target that cannot hold data.** Its only storage is a single `_userText`
   string (`GH_Param`, not `GH_PersistentParam`). Probed `CollectVolatileData_Custom` directly:
   `Multiline = false` → one item per line, one branch, so a LIST round-trips exactly;
   `Multiline = true` → the whole text as one item; GH's own tree rendering (`{0;0}` headers) is **NOT
   parsed back** — the headers return as data ITEMS. So `WritePanel` writes one item per line, forces
   Multiline off, collapses newlines inside an item to a space, and a multi-branch tree is flattened
   with a warning pointing at a Text parameter. A first version that rendered GH's tree display into
   the panel would have corrupted the data.
6. **Delivery is deferred to `RhinoApp.Idle`** — it writes into and expires the HOST document from
   inside a harness solve, which cannot be done in-solution. A target with a wire into it warns, since
   internalized data loses to a wire.
7. **Preview is supplied by hand** (`IsPreviewCapable`/`ClippingBox`/`DrawViewport*` forwarding to any
   `IGH_PreviewData` on the input). Generic params tell GH nothing about geometry, so the merge would
   otherwise have silently lost the viewport preview the Geometry Transmitter had. Note
   `Param_Geometry`/`IGH_GeometricGoo` had EXCLUDED `GH_Vector`, `GH_Transform` and `GH_Interval`
   anyway — going generic gained those.

**Live status:** the inlet/rename/wire behaviour has been exercised in Rhino across several rounds of
fixes. Not yet confirmed: whether GH restores the archived `Param_Inlet` set on file reload (it
should — same machinery Merge Signal's variable inputs use, and `IGH_VariableParameterComponent` is
implemented for it); if it does not, wires into a harness are lost on reopen and the sync silently
rebuilds fresh params.
