# The Harness — the plug-in's base unit

> Split out of `CLAUDE.md` (2026-09-08) to keep that file under its size limit. Content is verbatim; CLAUDE.md links here.

## The Harness — the plug-in's base unit (`src/Physalia.GH/Harness/`)

A **Harness** (`HarnessComponent`) holds its own `GH_Document`. The user's canvas carries only the
proxy node; the entire Physalia pipeline — Chat included — lives inside it. Right-click →
**"Edit Harness"** points the canvas at the inner document, the canvas return widget comes back,
double-click opens the chat window on the Chat inside. **Dataflow crosses INWARD only**, and the
asymmetry is load-bearing: what a pipeline *produces* is an edit to the canvas (placement, a pushed
script), and GH has no mechanism for "a wire that writes" — so outputs are side effects carried by the
proxy's drag arrows (outlets). What a pipeline *consumes* is data the canvas already computed, and GH
hands us wires pointing inward for free — so a **Harness In** inside grows a real input param on the
proxy's LEFT edge (inlets). See the I/O row below.

- **A harness is where a pipeline belongs, not where it is forced to be.** Placing a Physalia
  component straight onto the user's canvas is legal (the `HarnessResidency` guard, which used to
  delete strays on the next idle pass, is **deleted** — 2026-08-17). Nothing needs repairing for
  that case: `PhyDocuments.Host()` on a canvas-resident component returns the canvas itself,
  `PhyDocuments.Harness()` is nullable everywhere it is consumed, `MasterGroupName(null)` falls back
  to the unsuffixed `"Physalia"` group, and the chat switcher already sorts harness-less Chats ahead
  of the rest. What a stray gives up is the harness's own affordances — the proxy's icon row, presets,
  the Edit-Harness canvas, group-scoped grounding keyed per pipeline.
- **A transmitter outside a harness hosts its own drag arrow.** The arrow normally lives on the proxy
  because a drag cannot cross two documents; standing on the canvas there is only one document and no
  proxy, so `OutletArrowAttrib` (an `ArrowAttributeBase` adapting `IHarnessOutlet`) puts the grip back
  on the node — bottom-centre, since the right edge already carries the Signal outputs. One attribute
  covers both cases and reads residency **live** per layout/frame (`OwnsArrow`), because attributes are
  built before the component reaches a document; inside a harness it draws no grip, expands no pick
  region and starts no drag. Used by `TransmitterComponentBase` and by `HarnessOut`.
- **`OnPingDocument()` inside a harness returns the SUB-document.** Use `PhyDocuments.Host(this)` /
  `ActiveHost()` for anything meaning "the user's canvas" (grounding, placement, reports, memory
  scope); keep `ScheduleSolution`/`NewSolution` and co-resident peer lookups on the local document.
  The GhJSON library resolves its own target from the active canvas, so its writes are wrapped in
  `PhyDocuments.OnHostCanvas(...)` and its reads replaced by `GhJsonBridge.SerializeByGuids`.
- **Ownership is ours, not `GH_Document.Owner`** (a `ConditionalWeakTable` in `HarnessComponent`).
  Setting `Owner` makes Grasshopper paint its own cluster icon whose menu disposes the document.
- **The proxy wears its Chats' emoji as its icon** — one per Chat inside, in the same order as the
  chat window's switcher row (by pivot, left-to-right then top-to-bottom, matching
  `ChatWindow.CompareChats`), so the node and the row of circles read as the same list. The capsule
  widens to fit the row (`HarnessComponent.Chats` → `HarnessAttrib.ContentWidth`). A harness holding
  no Chat keeps the plug-in's own mark; nickname display mode is untouched.
- **A harness has one OUTLET per transmitter inside it** — its only kind of output, since no dataflow
  crosses. `IHarnessOutlet` (implemented by `TransmitterComponentBase`) is that type: a short label
  drawn beside the grip (`"node"`, `"py"`), its own wire gradient, settled endpoints, and the drop.
  `HarnessComponent.Outlets` orders them by pivot INSIDE the harness (top-to-bottom, then left-to-right
  — stable, nothing serialized, re-ordered by moving the nodes), and `HarnessAttrib` composes one
  `ArrowGrip` per outlet down the right edge, growing the capsule taller to fit. Adding a transmitter
  inside expires the proxy layout via the sub-document's `ObjectsAdded`/`ObjectsDeleted`. New
  transmitter kinds (IronPython, VB) derive from `TransmitterComponentBase`, or from
  `ScriptTransmitterBase` when they push into an existing component on the canvas (that tier owns the
  linked guid, its persistence, the picker menu and `ResolveTarget`; supply `TargetKind` +
  `IsLinkTarget`). **`OutletLabel` is fixed text on the script/component transmitters but LIVE on
  `HarnessOut`, which returns its input's nickname** — so the grip is labelled with whatever the user
  called the wire inside. `DrawOutletLabels` reads it every frame so no push is needed; what a rename
  does need is `OnOutletRenamed` → `ExpireProxyLayout`, because the right-edge label strip is
  MEASURED now (`TextRenderer` + `GH_FontServer.Standard` — the unadjusted font, since layout runs in
  canvas units and does not re-run on zoom) rather than the old fixed 30u for three-letter tags. The
  capsule is sized from its PARTS — input column + gap + centre + gap + label column — and no longer
  floors on GH's own `bounds.Width` once there are inputs: GH's layout reserves an icon region of its
  own, this class adds another, and taking the larger left a hole between the icon and the outlet
  labels with all the slack on one side. The centre is measured in BOTH display modes
  (`CentreStripWidth`): the emoji row (or the plug-in mark, for a harness with no Chat) under icons,
  the nickname under `GH_FontServer.Large` otherwise — unadjusted, same canvas-units reason as the
  outlet labels — so the two modes size and centre identically.
- **A harness has one INLET per Harness In inside it** — its only kind of input, and the mirror of the
  outlets. `IHarnessInlet` (implemented by `HarnessIn`) is that type; `HarnessComponent.Inlets` orders
  them by pivot INSIDE the harness exactly as `Outlets` does, and the proxy grows one `Param_Inlet`
  (hidden generic param, **tree access**, optional) per node, sharing ONE nickname with that
  node's OUTPUT parameter — both start "Data", and renaming either end renames the other (the
  node's own nickname is not involved and stays free to say what the node is). **Bound by `InstanceGuid`, never by position** — and this is where the outlet pattern must
  NOT be copied: an outlet's grip is an arrow we paint, with no place in GH's graph, so it can be
  reordered and rebuilt freely; an inlet's param is a real object other components' wires point AT, so
  rebuilding one drops its wire and re-binding by index silently swaps one node's data for
  another's. `SyncInlets` therefore REUSES a param whose node still lives and reorders by moving
  the param objects (sources travel with them); `Param_Inlet.InletId` persists the binding through
  save/load, and `HarnessComponent` implements `IGH_VariableParameterComponent` (both `Can*Parameter`
  false — no zoom +/- icons; the set is derived) so an archived param set is restored rather than
  discarded. Sync is deferred to `RhinoApp.Idle` (it mutates the param set, which must not happen
  inside a solution) and is triggered by the sub-document's `ObjectsAdded`/`ObjectsDeleted`, by
  `AddedToDocument`, by `Adopt`, and by each node's own `ObjectChanged` — a **rename** and a
  **move** change what the proxy must show and reach no solution anywhere, the same class of problem
  Script I/O has. The proxy's `SolveInstance` hands each inlet's tree to its node and, when
  anything changed (`TreeIdentity`), schedules ONE solution on the harness document with those
  nodes expired — deferred, because the harness is a different document with its own solver.
  `HarnessAttrib` must LAY OUT and DRAW the input rows itself: it composes its capsule by hand and
  never reaches `GH_ComponentAttributes`'s render, so the params would otherwise be wireable and
  invisible; and because GH sizes the capsule from the params *before* the class grows it for the
  outlets, the rows are re-centred by pure translation (`ShiftInputParams` — Bounds and Pivot both, so
  the input grip moves with them). **Two traps, both found live (2026-08-18).** (1) Composing the
  Objects channel by hand means `base.Render` was never called *at all*, so GH's own render — which
  draws the wires ARRIVING at the inputs — was skipped; invisible while a harness had no inputs, and it
  looks like "data transfers but no wire is drawn", since delivery is the solver's business and has
  nothing to do with what is painted. Every non-Objects channel must fall through to `base.Render`.
  (2) **`GH_DocumentObject`'s `NickName` setter raises NOTHING** — verified against the shipped
  assembly, the setter body is a bare field assignment; only the right-click name box announces a
  rename (`Menu_NameItemTextChanged`/`Menu_NickNameChanged`), so an F2 or properties-panel rename
  reaches no handler anywhere, and nothing at all is raised for a MOVE. Worse, **`PerformLayout` is
  called from a bare handful of places and the paint loop is not one of them**, so reconciling at
  layout time sits unfired indefinitely — an `ExpireLayout` is not a promise that `Layout()` runs
  (layout is performed on SOLUTION, not on paint). The hook that works is **overriding the virtual
  `NickName` setter** (declared on `GH_InstanceDescription`), which both ends inherit from the shared
  `Param_LinkedName` base: `Param_HarnessPort` (the inside end) relabels the input via
  `OnInletRenamed`, and `Param_Inlet` renames it back via `RenameInlet` — one name, either end
  editable, the recursion cut by an equality guard, a cleared name normalised back to "Data" rather
  than obeyed. Order drift from a MOVE has no hook at all, so it is checked in `SolveInstance` and
  handed to the idle sync.
  **Do not build a rename watch on `ObjectChanged`, do not assume `ExpireLayout` will get `Layout()`
  called, and if the name is editable at both ends make the sync two-way — a derived-only name silently
  reverts what the user typed on the proxy.** **Every Rhino 8 script component wears the same `IScriptComponent`** — only its
  `LanguageSpec` tells Python 3 from C# from IronPython — so a script transmitter's `IsLinkTarget`
  MUST test the language (`GhPythonBridge.IsPython3Component` / `IsCSharpComponent`), or it will
  cheerfully push Python into the C# component next door.
- **Presets are stock `.gh` files** in `Files/PRESETS`, each one a harness's worth of pipeline —
  exactly what saving from inside a harness produces. Loading one adds a NEW harness holding it.
  The library is split three ways (`PresetLibrary`): **`Physalia/`** (shipped), **`User/`** (saved by
  the user), **`Community/`** (reserved, empty). Nothing outside those folders is listed. Wire values
  are library-relative (`User/mine.gh`) and resolved by MATCH against the enumerated library, never by
  composing a path. **Save Harness as Preset…** writes to `User/` — on the proxy's right-click menu and
  on the harness panel; it refuses a harness with no Chat, since the loader would reject it.
  **Save .phy…** (`HarnessComponent.SavePackage`) is the same write to a destination the user PICKS —
  the way a workflow leaves this machine — and it differs in one thing: **the project folder is
  carried WHOLE, ledger-accounted downloads included** (`ProjectPayload.Plan(carryDownloads: true)`),
  because a preset is placed on the machine that wrote it, where a re-fetch is something the pipeline
  knows how to do, while an exported package is a file somebody sends somewhere and is worth its size
  if it opens with no network and no URL that has since moved. `ProjectPayloadPlan.Downloads` still
  means "what is NOT in the package" under both settings, so carrying everything leaves only the
  ledger entries whose file has since been deleted — otherwise the import message sends the user off
  to download files sitting in their own project folder. The destination is EXCLUDED from its own
  payload (`Excluding`, applied before the size is quoted): saving twice into the project folder would
  otherwise carry the previous package inside the new one and double the file on every save. It uses
  the WinForms `SaveFileDialog` for its explicit `OverwritePrompt` — `Rhino.UI.SaveFileDialog`
  exposes no such property, and this one writes anywhere the user can reach. The same two menus
  carry its reverse, **Load Harness from .gh File…** (`HarnessComponent.LoadFromFile`), which reads ANY
  `.gh` — not just one in the library — and REPLACES this harness's contents with it: the file is read
  exactly as a preset is (fresh ids, host targets cleared), one carrying no Chat is refused, and a
  non-empty harness asks first, because the pipeline going out takes its conversation and solve state
  with it and none of that is on the undo stack. The swap adopts the new document FIRST (so anything
  reacting to the old one being dismantled already sees the replacement), re-points the canvas when you
  are standing inside, and then RETIRES the old document — `RemoveObjects` + `Dispose`, so every
  `RemovedFromDocument` runs and warm CLI sessions and host-document subscriptions are actually
  released. The chat window is put back on this harness only if it was watching it (`ChatWindow.IsViewing`,
  reached through `Chat.ActiveWindow`); on Home it stays on Home.
- **Reading a preset re-issues every instance id** (`DocumentIds.MutateAll`): an archive carries the ids
  it was saved with, so the same preset placed twice would otherwise put duplicate `InstanceGuid`s in one
  file. Wires and groups are Grasshopper's own problem; a guid held in one of OUR fields is not — any
  component storing another object's `InstanceGuid` must implement **`IGuidLinked.RemapLinks`** and
  replace **only** guids the map contains (a link may point outside the document, as PyTransmitter's
  does). A normal file load (`HarnessComponent.Read`) deliberately preserves ids.
- **A document may hold any number of harnesses** — one per line of work. Nothing is ever replaced or
  swept: each placement mints its own Chat (except the first, which adopts the window's detached one),
  drops the proxy at the first free spot right of the window (`PlaceHarness` steps down past anything
  already there), and switches the window to the new Chat. The switcher row is the way back.
- Nothing is placed automatically: the chat window's **Home** screen offers "Place predefined harness"
  and "Place empty harness", and the header menu carries the same two ("Add preset" / "Add empty
  harness") for once a conversation is under way.
- **Home** is the chat window's entry screen — a house icon leading the switcher row, always present,
  always divided off from the chat dots. It is a window state (`ChatWindow._home`), not a Chat. The
  canvas widget always opens on Home; double-clicking a harness opens on the first Chat inside it.

Detail: memory note `harness-subdocument`.

---

