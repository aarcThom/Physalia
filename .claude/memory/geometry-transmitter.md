---
name: geometry-transmitter
description: "Geometry Transmitter (GeoTx) — the geometry twin of Text Transmitter; how a data TREE is internalized into an arbitrary param, and why the change key is reference identity"
metadata: 
  node_type: memory
  type: project
  originSessionId: d228246d-2873-4ef9-a8d2-7387cdd284df
  modified: 2026-08-18T07:54:15.288Z
---

> **Superseded 2026-08-19.** Geometry Transmitter and Text Transmitter were merged into one
> **GH Data Transmitter** (`DataTx`, grip "data"), generic at tree access, so booleans, integers and
> colours ride through too — the split only ever reflected the param type each happened to declare.
> Everything below still describes how the delivery works; it is now that one component, living with
> [[harness-receiver-inlets]] under the **I/O** ribbon section. Two things the merge had to add:
> preview is supplied by hand (generic params tell GH nothing about geometry, so the viewport preview
> would have been silently lost), and a **signal is keyed by SEQUENCE, not reference identity** — a
> latched signal is re-wrapped in a fresh goo every solve, so identity would write to the canvas on
> every scheduled solve.

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

**Text targets, same day — the rule is PER ITEM, container preserved.** A text param needs no help:
`Param_String.Cast_Object` stringifies each goo on its own (probed outside Rhino — a `GH_Point`
casts to `"{1, 2, 3}"`), so the tree survives the ordinary `WriteTree` path. On top of that, an item
any param refuses outright is retried as a `GH_String` of its text form; a param that reads neither
refuses both and is counted, so the fallback only fires where it is right.

**The Panel is the one target that cannot hold data**, and it cost a wrong first attempt. Its only
storage is a single `_userText` string (`GH_Param`, not `GH_PersistentParam` — reflect its declared
members and there is nothing else). `CollectVolatileData_Custom` rebuilds its data from that string,
and I probed it directly by setting `_userText` and invoking it:

- `Multiline = false` → **one item per line, one branch**. So a LIST round-trips exactly.
- `Multiline = true`  → the whole text as one item.
- GH's own tree rendering (`{0;0}` headers, `0. item` lines) is **NOT parsed back** — the headers
  come back as data ITEMS.

So `ParamTargets.WritePanel` writes one item per line, forces `Multiline` off when there is more than
one, and collapses any newline inside an item's own text to a space (line count IS item count). My
first version rendered the GH tree display into the panel, which would have corrupted the data — a
multi-branch tree simply cannot live in a panel, so it is flattened and the component warns and
points at a Text parameter. `CanHoldOrDisplay` (Panel or persistent param) is the shared target test.

Also: `AddGeometryParameter` is `Param_Geometry` = `IGH_GeometricGoo`, which **excludes GH_Vector,
GH_Transform and GH_Interval** (GH_Plane, GH_Point, GH_Box and everything solid ARE included). That
was a deliberate trade for viewport preview and an honestly-typed wire.

The shared half of Text/Geometry Transmitter now lives in
`Components/Transmitters/ParamTargets.cs` — `PersistentSetter`, `CanHold`, `RefineDropTarget`,
`DeliveredCount`, `WriteTree`. TextTransmitter was refactored onto it (no behaviour change).

**Not yet run in Rhino.** No icon either — falls back to `brain.png` until one is cut in the
bead-line style ([[component-icon-generation]]).
