---
name: geometry-transmitter
description: "Geometry Transmitter (GeoTx) — the geometry twin of Text Transmitter; how a data TREE is internalized into an arbitrary param, and why the change key is reference identity"
metadata: 
  node_type: memory
  type: project
  originSessionId: d228246d-2873-4ef9-a8d2-7387cdd284df
  modified: 2026-08-18T07:54:15.288Z
---

Built 2026-08-18. `GeometryTransmitter` (`GeoTx`, grip "geo", magenta→gold gradient) is a harness
outlet that behaves as an ordinary wire: `Geometry In` → `Geometry Out` passthrough, plus a
deferred write of the same data into a linked input on the host canvas. Structurally a copy of
[[harness-subdocument]]'s `TextTransmitter` — `PhyBase` + `IHarnessOutlet` + `IGuidLinked`,
composed `TransmitterLink`, no signal lifecycle at all.

**Why:** so a pipeline can hand real geometry back to the user's canvas, not just text or code.

**Three things that were NOT obvious:**

1. **`SetPersistentData(params object[])` can only make ONE flat branch.** Internalizing a tree
   branch-for-branch needs the *other* overload, `SetPersistentData(GH_Structure<T>)`, and `T` is
   only known at runtime. New `ParamTargets.WriteTree<T>` walks the target's base chain to its
   constructed `GH_PersistentParam<T>`, `MakeGenericType`s a `GH_Structure<T>`, and appends. Items
   are cast with the param's own **protected `Cast_Object(object)`** — reflection-invocable, and it
   is the exact conversion the flat setter performs, so a Brep entering a Mesh input converts as it
   would through a wire. (Verified by reflecting over `Grasshopper.dll` and actually invoking
   `Cast_Object` on a live `Param_Point` outside Rhino — see [[inspecting-rhino-assemblies]].)
   Items the param refuses come back as a COUNT and are reported; silently dropping half a tree is
   the one failure a user cannot see.

2. **Change detection is reference identity, not value.** Key = tree shape +
   `RuntimeHelpers.GetHashCode` of every goo. GH re-mints goo objects only when their producer
   recomputes, so identical references prove nothing upstream ran. It can re-send geometry that
   recomputed to the same shape; it can never MISS a change. A bounding-box compare fails the other
   way (cheap and wrong), and an honest value compare costs a full Brep diff every solve.

3. **The tree handed to `SolveInstance` belongs to the input param and is cleared when that param
   next expires** — well before the `RhinoApp.Idle` callback runs. Queue a copy
   (`new GH_Structure<T>(tree, false)`, shallow) or the deferred write finds nothing.

**Text targets, added the same day.** A Panel is a `GH_Param` but NOT a `GH_PersistentParam`, so
it has no persistent data to write and takes `SetUserText` instead — it gets `ParamTargets.TreeText`,
the tree rendered the way a Panel shows one (plain lines for a single branch, `{path}` headers plus
indexed items for more). Separately, any item a target param refuses outright is offered again as a
`GH_String` of its own text form, so a text input that would have stringified geometry off a wire
still does. Neither is a special case in the component: a param that can read neither the geometry
nor a string refuses both and is counted, so the fallback only ever fires where it is the right
answer. `CanHoldOrDisplay` (Panel or persistent param) is now the shared target test for both
wire-like transmitters.

Also: `AddGeometryParameter` is `Param_Geometry` = `IGH_GeometricGoo`, which **excludes GH_Vector,
GH_Transform and GH_Interval** (GH_Plane, GH_Point, GH_Box and everything solid ARE included). That
was a deliberate trade for viewport preview and an honestly-typed wire.

The shared half of Text/Geometry Transmitter now lives in
`Components/Transmitters/ParamTargets.cs` — `PersistentSetter`, `CanHold`, `RefineDropTarget`,
`DeliveredCount`, `WriteTree`. TextTransmitter was refactored onto it (no behaviour change).

**Not yet run in Rhino.** No icon either — falls back to `brain.png` until one is cut in the
bead-line style ([[component-icon-generation]]).
