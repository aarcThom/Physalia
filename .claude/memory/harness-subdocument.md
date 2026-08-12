---
name: harness-subdocument
description: "The Harness is a real owned GH sub-document and the plug-in's base unit — every Physalia component lives inside one"
metadata: 
  node_type: memory
  type: project
  originSessionId: 8ba6eaa2-c1c1-4bb2-9487-ac5379251e1f
  modified: 2026-08-09T06:19:21.849Z
---

Built 2026-08-08 (replaces the in-place "collapsible harness" entirely). Related: [[chat-window]],
[[chat-widget]], [[chatbox-switcher-row]], [[ghjson-library-reference-only]].

**Live-run status:** first Rhino run 2026-08-09 — the window opens and the preset gallery lists the
bundled `.gh` files (that took the push-gate fix in [[chat-window]]; the gallery was empty until the
window was closed and reopened). **Still unconfirmed live: the `ScheduleSolution` spike below, and
whether a placed harness's pipeline actually runs.**

## What it is

A **Harness** is a `HarnessComponent` (`src/Physalia.GH/Harness/`) holding its own `GH_Document`. The
user's canvas carries only the proxy node; the whole Physalia pipeline — Chat included — lives inside.
Right-click → **"Edit Harness"** points the canvas at the inner document; **double-click opens the chat
window** on the Chat inside (the proxy stands in for that Chat, so it answers the Chat's gesture).

**No cluster input/output hooks are needed.** `Chat` has zero inputs and one output, and a Physalia
pipeline never exchanges *dataflow* with the user's canvas — it only *scans* it (grounders, guardrails)
and *writes to it by side effect* (placement). So nothing crosses the boundary as a wire.

Files: `HarnessComponent.cs`, `PhyDocuments.cs`, `HarnessResidency.cs`, `IHarnessOutlet.cs`,
`Attributes/HarnessAttrib.cs`, `Widgets/HarnessReturnWidget.cs`,
`Components/Transmitters/{TransmitterComponentBase,ScriptTransmitterBase,TransmitterLink,TextTransmitter}.cs`.

## The local-vs-host document split — the core of it

`OnPingDocument()` inside a harness returns the **sub**-document. `PhyDocuments` resolves the rest:
`Host(doc)` / `Host(obj)` / `ActiveHost()` climb to the root; `ObjectsIncludingHarnesses(doc)` walks a
document plus the harnesses in it; `IsHarnessDocument(doc)`.

- **Stays local:** `ScheduleSolution`/`NewSolution`, `AddedToDocument`/`RemovedFromDocument`,
  Feedback→FeedbackCollector guid resolution, ToolsInUse's peer scan, `PromptPipelineView` wire walks.
  Every endpoint of those moves into the harness together.
- **Becomes `PhyDocuments.Host(this)`:** CanvasStateGrounder, ComponentTransmitter, GeometryReport,
  RuntimeHealthCheck, FidelityCheck, GeometryObservation, RhinoGeometryTool, MemoryTool,
  ConversationLog's canvas export, Chat's generated-geometry bounds, ScriptIO's *script* lookup
  (its PyTransmitter lookup stays local).
- **Becomes `PhyDocuments.ActiveHost()`:** the 13 `Instances.ActiveCanvas?.Document` sites in
  `GhJsonBridge*`. **The dangerous ones** — while the user edits *inside* a harness the active canvas
  document IS the pipeline. `GhJsonBridge`'s `??=` fallbacks host-resolve whatever they are handed, so
  a missed caller still behaves.
- `RuntimeMessageTrace` goes the OTHER way: it scans *for* pipeline components, so it hooks
  `SolutionEnd` on the host **and every harness inner doc**, re-syncing the watch set each solve.

## Ownership: deliberately NOT GH_Document.Owner

`HarnessComponent.Owners` is a `ConditionalWeakTable<GH_Document, HarnessComponent>`; `PhyDocuments.Host`
follows it and falls back to `doc.Owner?.OwnerDocument()` so real GH clusters still resolve. Reasons,
verified by decompiling `GH_Canvas`:

- `Owner != null` makes GH paint its **own hard-coded cluster icon** top-left (`_regionCluster`,
  `Res_GUI.OpenCluster_Empty_63x56`), drawn directly in the canvas paint pass and hit-tested in
  `GH_Canvas_MouseDown` ahead of widgets — **not a widget**, so it cannot be removed from the widget
  list or reliably painted over. Its menu runs "Save and Return" → `RemoveDocument` → **Dispose**.
- Bonus: `Owner`'s setter does `m_renderQueue.Enabled = (value == null)`, so leaving it null keeps
  Rhino previews working from inside a harness.
- The inner doc is also **not registered with the DocumentServer**: the canvas setter only calls
  `PromoteDocument`, which no-ops for an unregistered document. Staying out keeps the harness off the
  document dropdown, out of the exit-time save sweep, and out of reach of `RemoveDocument`.
- Residual: File > Save while inside a harness saves the *inner* doc (Save As dialog, stray file).
  Non-destructive; the return widget is the intended exit.

Other decompiled facts that shaped it: `GH_Canvas.Document` is settable and the ONLY in-memory way to
show a document (`GH_DocumentEditor` has no `SetDocument`); its setter disables the **outgoing**
document, so `ReturnToHost` re-enables the inner one and `SolveInstance` re-asserts it every solve;
GH's own `GH_Cluster.EditClusterAsSeparateDocument` edits a **duplicate** and merges back, which
Physalia cannot do because signals/conversation/solve state are session-only and non-persisted — **we
edit the live document**; and there is **no breadcrumb or back button in GH**, hence
`HarnessReturnWidget` (registered in `ChatWidget.cs`'s `AddWidgets`).

Persistence: `writer.CreateChunk("HarnessDocument")` + `GH_Document.Write/Read` — the plug-in's only
nested-archive persistence. Saving the host file writes the harness and its contents; saving *while
inside* a harness writes just that harness's contents, which is what makes `.gh` presets authorable.

## The GhJSON library resolves its own document

`CanvasReader.GetActiveDocument()` is literally `Instances.ActiveCanvas.Document`. `PutOptions` has no
document field, `CanvasPlacer` is `internal`, and there is no `Put` overload taking one — so
host-resolution at our call sites does NOT reach it. The library is reference-only, so:

- **Writes** (`Put` ×3, `Delete` ×1) are wrapped in `PhyDocuments.OnHostCanvas(...)`, which points the
  canvas at the host for the call and restores it in a `finally`. GH's canvas setter saves/restores
  each document's viewport target and zoom, so the user's position inside the harness survives.
- **Reads** must NOT swap — `GetByGuids` runs on every canvas-state export and swapping per solve would
  thrash. Replaced by `GhJsonBridge.SerializeByGuids(doc, guids)` over the public
  `GhJsonGrasshopper.Serialize(objects, options)`. Keep document order (it feeds id assignment) and set
  `IncludeSelectedState = false` — the one default where `SerializationOptions` (true) disagrees with
  `GetOptions` (false). This was a **grounding** bug as much as a placement one: inside a harness the
  model was handed an empty canvas.

## Residency: Physalia components must live in a harness

`HarnessResidency`, hooked from `PhyBase.AddedToDocument` (every subclass override already called base).
A `PhyBase` that is not a `HarnessComponent` and lands outside a harness is **removed on the next idle
pass**, reason written to the Rhino command line. Deliberately not undoable — an undo would re-add it
and trip the guard again. Four exemptions:

1. `HarnessComponent` itself.
2. Anything already in a harness document.
3. `GhJsonBridge.IsImporting`.
4. **Anything added to a document that is not the canvas document** — this is how a file load is told
   apart from a user placement (GH reads every object in before handing the document to the canvas).
   *`GH_Document.Context` looks like the natural signal but its own SDK docs say it is a setter, not a
   getter — do not use it for this.*

The idle deferral also re-checks each queued component, so anything swept into a harness meanwhile is
spared. Pre-harness files load untouched; migrate by opening a harness and pasting the pipeline in.

## Arrows live on the proxy — one grip per transmitter (variable outlets, 2026-08-10)

Transmitters draw no arrow (`PyTransmitterAttrib` / `CompTxAttrib` deleted; both use plain
`PhyComponentAttributes`). Required, not cosmetic: the arrow's targets live on the host canvas and
**a drag cannot cross two canvases**. `ComponentTransmitter`'s placement offset is measured from the
**proxy's** pivot (`ArrowAnchor`, now on `TransmitterComponentBase`). For the same reason the script
transmitters carry a **"Link to Script Component" menu picker** over the host canvas.

**A harness has as many outputs as it has transmitters.** `IHarnessOutlet` (was `IHarnessArrow`) is
the base transmitter output type: `OutletLabel` (the short tag drawn beside the grip — `"node"`,
`"py"`) + `OutletGradient` (its own wire colour) + endpoints/drop. `HarnessComponent.Outlets`
(replaced `TryGetSoleArrow`) lists them **ordered by pivot inside the harness**, top-to-bottom then
left-to-right — stable across sessions, nothing serialized, and re-orderable by moving nodes inside.

`HarnessAttrib` therefore derives from `BottomGripAttributes` and **composes one `ArrowGrip` per
outlet** (private `OutletHandle : IArrowHost`) instead of deriving from `ArrowAttributeBase`, which
bakes in exactly one. Grips spread evenly down the right edge; the capsule grows to
`n * RowHeight(20)`; labels are right-aligned against the edge and dropped below zoom 0.6.
`ArrowStyles.Proxy` is deleted — colour now comes from each outlet.

**The proxy's icon is its Chats' emoji (2026-08-10).** `HarnessComponent.Chats` lists them in the
chat window's switcher-row order — pivot X then Y, matching `ChatWindow.CompareChats`, NOT the Y-then-X
order `Outlets` uses — so the node and the row of circles are visibly the same list; `FindChat` now
takes the first of these. `HarnessAttrib` holds the Chats (not their bitmaps) and asks each for
`Icon_24x24` at paint time, so a Chat that re-rolls its emoji is right on the next frame. The capsule
widens via `ContentWidth` (emoji strip + label column + insets) as a FLOOR under the 3× default width,
so it only grows once the Chats need it. No Chat inside → the plug-in's own mark, as before.

**Labels are drawn from a measured POINT, never into a rectangle** — a rect clips, which is what
rendered "node" as "nod". Measure with the same `StandardAdjusted` font at draw time (it follows the
canvas zoom, and Layout does not re-run on zoom), same `GenericTypographic` format for measure and
draw. The column Layout carves out of `m_innerBounds` for them is a FIXED 30 units — measuring it
with the unadjusted font reserved a third of the node at high zoom and left a visible hole between
the emoji and a four-letter tag. Nothing needs it exact: the labels place themselves at paint time
and cannot clip against it. `WidthFactor` is 2.35 (was 3) — trimmed once the emoji row gave the node
a reason to be as wide as it is.

Because the proxy's layout depends on the CONTENTS of a document GH doesn't know is connected to it,
`Adopt` subscribes to the inner document's `ObjectsAdded`/`ObjectsDeleted` and expires the proxy
layout — otherwise a newly placed transmitter has no grip until some unrelated relayout.

Transmitter class tree (built for IronPython / C# / plain text to slot in):
`RoutingComponentBase<string>` → `TransmitterComponentBase` (IHarnessOutlet, `TryGetData` off the
signal payload, plain attributes, `ArrowAnchor`) → `ScriptTransmitterBase` (`IGuidLinked`: linked
guid + its persistence under the unchanged `"LinkedGuid"` key, link/unlink undo, picker menu, wire
endpoint under the target, `ResolveTarget`; subclass supplies `TargetKind` + `IsLinkTarget`) →
`PyTransmitter` / `TextTransmitter`. `ComponentTransmitter` derives from the first tier (free-point
drop, no target).

**TextTransmitter (2026-08-10) — the outlet that is NOT a routing component.** Grip `"text"`,
silver→black wire. Two rewrites got here; the shape below is the one the user specified.

**It is a plain `PhyBase`: one generic `Data In`, one generic `Data Out`, nothing else.** Whatever
arrives passes through untouched — a signal leaves as the SAME `PhySignal` (same sequence, so
downstream consume-once still holds and the pipeline reads as if it were not there), text leaves as
text. No Success/Fail pair, no latch, no state machine, because it decides nothing. What it
TRANSMITS is the text form of whatever arrived: a signal's payload, or the text itself.

**Why the first two cuts failed:** built on `RoutingComponentBase`, which only ever runs on a
consumed signal — so a text-only wiring never solved at all, and a single output was impossible.
The lesson generalizes: *the outlet contract is `IHarnessOutlet`, not the routing base.* A
transmitter that is not signal-driven implements the interface directly.

- **`TransmitterLink` (new, composed)** now owns "how a transmitter is linked": target guid +
  persistence (`"LinkedGuid"`, unchanged), undo'd `Set`, settled wire endpoint, grip-drop, picker
  menu, `Resolve`, `Remap`. `ScriptTransmitterBase` delegates to it (built LAZILY — it is wired from
  the subclass's `TargetKind`/`IsLinkTarget`, so it cannot be built in the ctor); `TextTransmitter`
  composes its own. This works because `GH_DocumentObject.Menu_AppendItem` is a public static and
  `RecordUndoEvent` is public — a non-component helper can drive both.
- **The grip connects like a standard GH output, to ANY input.** Delivery is `GH_Panel` →
  `SetUserText`, everything else → `SetPersistentData(params object[])` **found by reflection**
  (`GH_PersistentParam<T>` has no non-generic interface and T is runtime-only) — which casts the value
  into whatever the param holds, exactly as an incoming wire's data is cast, so a number input takes
  "3.5". Target resolution mirrors GH's: nearest **input grip** within 12u, else the row under the
  cursor, else the node's first input; a Panel or floating param links directly. `TransmitterLink`
  hit-tests nodes twice (exact bounds, then +8u) because a grip sits just off the capsule edge, and
  `Endpoints` lands the wire ON `Attributes.InputGrip` when the target has one. **A drop on empty
  canvas does nothing** — it must never create a target. Note the picker menu can only list top-level
  objects, so component inputs are reachable by drag only.
- **Delivery is deferred to `RhinoApp.Idle`** — it writes into and `ExpireSolution`s an object on the
  HOST document from inside the HARNESS document's solve, which in-solution is the classic silent
  no-op (prime suspect for the original "does not transmit").
- **Keyed once-per-change**: signals by `#sequence`, everything else by value. The link's `Changed`
  callback clears the key, or a freshly linked target would sit empty forever.
- Delivered-but-overridden (the target has a wire into it) warns rather than fails — internalized
  data loses to a wire, so the text would vanish with no explanation. Warnings reach the node via a
  one-shot `ExpireSolution` from the Idle handler, guarded on the message actually changing.
- No icon yet — falls back to `brain.png`.

## Presets are stock .gh files

**Library split three ways + save-from-harness (2026-08-09).** `Harness/PresetLibrary.cs` owns
`Files/PRESETS`, now divided into `Physalia/` (shipped), `User/` (saved by the user) and `Community/`
(reserved, empty — `.gitkeep` files keep User/Community in git AND get them staged into bin, since
`CopyLibraryFiles` copies files, not empty dirs). Nothing in the PRESETS root is listed any more.
- **Wire values are library-relative** (`User/mine.gh`) and `Resolve` MATCHES them against the
  enumerated library instead of composing a path — traversal-proof by construction, not by sanitising.
- **`ReadDocumentFile` re-issues every instance id (`DocumentIds.MutateAll`, 2026-08-09).** An archive
  carries the ids it was saved with, so placing the same preset twice used to put two objects with the
  SAME `InstanceGuid` in one file — the chat window's switcher row collapsed their circles, and anything
  else keyed by id (signal trace, GhJSON) had to guess. GH re-issues on paste for the same reason.
  Order matters: `DestroyProxySources()` first, which is `MutateAllIds`'s documented prerequisite.
  **Wires need no help** (sources are object references, not ids) and groups are GH's own problem, but a
  guid in one of OUR fields is opaque to it — those components implement **`IGuidLinked.RemapLinks`**:
  `Feedback` (collector list), `ScriptIO` (its transmitter), `ZoomGuid`, and `PyTransmitter` —
  whose target script component lives on the HOST canvas, so its id is absent from the map and the link
  is correctly left alone. The rule for any new implementer: **only replace a guid the map contains.**
  `HarnessComponent.Read` deliberately does NOT re-issue — a file load must round-trip its own ids.
  Unaffected: `ComponentGuid` (static type ids), live `InstanceGuid` uses, and the memory tool's
  document key (derived from `document.FilePath`).
- The switcher row still carries `key`/`ordinal` alongside the guid, and resolves clicks by position
  with a guid cross-check. Not redundant: a file SAVED before the re-issue landed can still hold
  duplicate ids, and `HarnessComponent.Read` preserves them.
- **`HarnessComponent.SaveAsPreset()`** — on the proxy's right-click menu and on the new
  `HarnessMenuWidget`. Prompts via `Rhino.UI.Dialogs.ShowEditBox`, confirms overwrite via
  `ShowMessage`, writes with `GH_Archive.AppendObject(doc, "Definition")` +
  `WriteToFile(path, overwrite: true, rememberPath: FALSE)` — `rememberPath: false` is the point: it
  leaves no stamped FilePath and no recent-files entry on the live sub-document, the same reason
  `ReadDocumentFile` avoids `GH_DocumentIO.Open`. **Refuses a harness with no Chat**, because
  `HandlePlacePreset` rejects such a preset at LOAD time — better to explain while a user is present.
- The gallery groups by folder off the host's order (`UiPreset.folder`/`.name`) — no sorting in the UI,
  or page and library would disagree on precedence. `MaybePushPresets`'s tick signature means a
  just-saved harness appears within 0.15 s, no refresh action needed.
- **Harness Notes (2026-08-10)** — `Components/Pipeline/HarnessNotes.cs` + `HarnessNotesAttrib`. A
  panel-shaped, param-less annotation in the harness livery: title strip with the nickname, wrapped body,
  double-click → multi-line `ShowEditBox` (a dialog, not an in-place WinForms text box — that is what made
  the old Prompter panel awkward on Mac). Width is persisted and user-facing; height follows the wrapped
  text, measured against a throwaway 1×1 `Bitmap` because `Layout` has no `Graphics`. `Layout` does NOT
  call base — there are no params to place and the base would size a component capsule.
  **Its text is the preset's description.** `PresetLibrary.ReadDescription` walks the archive as DATA —
  `Definition/DefinitionObjects` → `ObjectCount` + indexed `FindChunk("Object", i)`, whose `GUID` item is
  the **TYPE** id, then its `Container` chunk holds what the component wrote — so no component is ever
  instantiated (which would also fire placement hooks). `HarnessNotes.TypeGuid` and `NotesKey` are
  therefore an **archive contract**: changing either orphans every saved preset's description. Read only
  when the library signature changes, never on the 0.15 s tick.
- **Proxy size, grip side + selection (2026-08-10).** The harness has NO parameters, so GH's layout made
  it one of the smallest nodes on the canvas when it stands for a whole pipeline; it is now **×3 wide**,
  normal height. The resize goes through a new `BottomGripAttributes.AdjustVisualBounds` seam, called
  between GH's layout and the grip-strip measurement — **not** by assigning `Bounds` afterwards, which
  would leave the grip, the wire origin and the pick region disagreeing with what is drawn. Grows from
  the left edge. `HarnessAttrib.Layout` then re-centres `m_innerBounds` on the widened capsule, or the
  icon/nickname would sit in a corner of it.
- **The proxy's grip moved to the right edge** (standard GH output position), via two more seams on
  `BottomGripAttributes`: `GripOrigin` (defaults to `BottomCentre`, harness returns `RightCentre`) and
  `ExpandForGrip` (defaults to a downward strip, harness widens rightwards). `ArrowAttributeBase
  .ArrowOrigin` now follows `GripOrigin`, so drawing, hit-testing (`GripBounds`) and the wire all move
  together — the three must never be set independently. Every other grip component is untouched.
- **No component-count tag.** `HarnessComponent` no longer sets `Message` (and `Count`, which existed
  only to feed it, is gone); `HarnessAttrib` also drops its `RenderMessage` call, so GH's black caption
  cannot reappear under a harness even if something sets `Message` later.
  **Selected now uses `GH_Skin.palette_normal_selected`** (the standard green) instead of the livery — a
  node with a private palette that ignores selection looks broken next to every other one. The pink rim
  is drawn in BOTH states: it is the signature, and the body colour already answers "am I selected".
- **Canvas background wash inside a harness (2026-08-10)** — `Widgets/HarnessCanvasTint.cs`, a diagonal
  **pink → lilac → blue** gradient over the whole window so a secondary screen never looks like the file
  you came from. Stops are pale on purpose (components sit on top of it): the pink is `Glow` lightened,
  the middle is `HarnessTheme.Lilac` (the original Physalia panel's title highlight — `Ink` is too near
  black to lighten into a purple, it just goes grey), the end is `HarnessTheme.Aqua`. **`Fill` does not
  work as the blue end** — it is so near white that behind translucency it vanishes, so the sweep read as
  an all-pink canvas fading out; hence `Aqua` (the old panel's entry-section blue) plus stops weighted
  `0 / 0.32 / 1` to give blue most of the ramp, pink being much the loudest of the three. Three stops means
  `InterpolationColors`, which supersedes the two ctor colours; the brush rect is inflated 1 px because
  GDI+ samples a gradient's first row/column from the far end of the ramp and leaves a stray edge line.
  **Not a widget**: widgets paint at the END of the pipeline, over the components; a background must go
  under them. Hangs off **`GH_Canvas.CanvasPaintBackground`** ("raised after the background has been
  drawn" — i.e. grid down, groups/wires/objects still to come). That event is an INSTANCE event
  (`(GH_Canvas sender)`), while `WidgetListCreated` is the only STATIC hook handing over a new canvas —
  hence `Attach` is called from `AddWidgets`, made idempotent by `-=` then `+=` on a static method so a
  rebuilt widget list cannot double-subscribe. Painted in DEVICE space (ResetTransform +
  `ClientRectangle`), like the pills. GH's own cluster tint is unusable: it keys on `GH_Document.Owner`,
  which we never set.
- **`HarnessTheme`** now holds the one copy of the family's look (fill / edge / ink / glow + `DrawGlow`);
  `HarnessAttrib`, `HarnessPill` and `HarnessNotesAttrib` all draw from it. The pill outlines in ink
  rather than black — at 30 px a hard black edge reads worse.
- **Widgets stack in one column**: `HarnessPill` holds the shared geometry (row index → Y) and palette
  for `HarnessReturnWidget` (row 0) and `HarnessMenuWidget` (row 1), so the two cannot drift apart.
  Both force `Visible => true` and draw only inside a harness. **`GH_FontServer` is in
  `Grasshopper.Kernel`, not `Grasshopper.GUI`** — cost a build iteration.
- **Dev-build hazard:** `CopyLibraryFiles` does `RemoveDir` on `$(TargetDir)Files` every build, so
  user presets saved into `bin/.../Files/PRESETS/User` are WIPED by the next `dotnet build` — same
  trap as `API_KEY_CONFIG.YAML`. Fine for an installed `.gha`; copy anything worth keeping into the
  repo's `Files/PRESETS/` tree.



`Files/PRESETS/*.gh`, each holding one harness's worth of pipeline. `HandlePlacePreset` reads the
archive (`HarnessComponent.ReadDocumentFile` — `GH_Archive` + chunk `"Definition"` +
`DestroyProxySources`, **not** `GH_DocumentIO.Open`, which stamps FilePath and pollutes GH's
recent-files MRU), wraps it in a NEW harness (`HarnessComponent.CreateWith` → `DropPresetHarness`) and
re-points the window at the Chat inside. Refuses a preset carrying no Chat. The gallery lists file
names only — a `.gh` has no readable description. The whole GhJSON preset splice is deleted:
`LoadAndPlaceAnchored`, `RewireAnchor`, `RewireRequest`, `TryReadMetadataDescription`.

## Placement is user-driven only, and repeatable

`MaybePlaceComponent` is gone — the widget/window place nothing on their own. The connect screen has
exactly two options plus LLM setup: **"Place predefined harness"** (gallery) and **"Place empty
harness"** (`placeemptyharness` → `HandlePlaceEmptyHarness`). "Connect a Conversation Log" and its verb
are deleted. An empty harness holds only a Chat, so `connected` stays false and the connect screen
correctly remains.

**Both options stay on offer for good — a document may hold many harnesses** (2026-08-08; the original
build allowed exactly one). A harness exchanges no dataflow with anything outside itself, so there is
no reason to cap it at one, and the two single-harness mechanisms are gone:

- `HandlePlaceEmptyHarness` no longer early-returns on "already placed", and `HandlePlacePreset` no
  longer swaps an existing harness's contents — `ReplaceInnerDocument` was its only caller and is
  **deleted**. Presets accumulate; nothing running is ever destroyed.
- `DropHarness` mints a **new `Chat`** per harness, consuming the window's own only when it is still
  detached (the widget-created first placement), then `SetActiveComponent`s onto it. Taking the viewed
  Chat would leave the earlier harness with nothing driving it.
- `PlaceHarness` + `MoveTo` + `Overlaps` replace the fixed anchor: lay out, then step down a row at a
  time (`PlacementGap`, capped at `MaxPlacementRows`) until the proxy clears everything on the host.
  `GH_Group`s are skipped as obstacles — they are containers drawn behind their members, so the master
  "Physalia" group would otherwise push every harness far down the canvas.
- `HostForPlacement` resolves through `PhyDocuments.Host`, so placing while *inside* a harness lands on
  the user's canvas rather than nesting.
- The `harnessPlaced` wire field is **gone** from `UiState`/`App.svelte`/`ConnectOptions`; it survives
  only as a Tick local picking the status-line wording. The header menu gained **"Add empty harness"**
  beside "Add preset" — the connect screen is hidden once a conversation starts, so without it the
  empty-harness path would be unreachable after the first one.

**Deleting a harness fires NO `RemovedFromDocument` for anything inside it (2026-08-09).** Removing the
proxy takes the whole sub-document out of the file, but the objects in it are untouched and still
report that inner document from `OnPingDocument()` — so a Chat inside a deleted harness looks perfectly
placed. The chat window sat frozen on its conversation because of exactly this. There is no per-object
hook to add; the window instead runs a liveness check each tick (`IsViewedChatLive`) and falls back to
Home: climb with `PhyDocuments.Host` and you end at a **harness** document only when a proxy up the
chain is no longer placed, because `Host` stops at the first owner that is not on a document. Testing
the Chat's own document would always answer "live". Anything else that must react to a harness leaving
the file needs the same reachability test, not an event.

**Corollary — never resolve the host document from a possibly-orphaned component.** `Host(orphanedChat)`
returns the DEAD sub-document (non-null!), so `ObjectsIncludingHarnesses` over it happily enumerates the
deleted harness's contents: the switcher row kept showing the deleted harness's chat dot, and
"Clear all" would have swept components no longer in the file. `ChatWindow.LiveHost()` gates the
`Host(_component) ?? ActiveHost()` idiom on `IsViewedChatLive()` and falls back to the canvas otherwise.

`ChatWidget.FindChat`, `ChatWindow.EnumerateChats` and `HandleClearAll` walk `ObjectsIncludingHarnesses`.
`Chat.MoveSelectionIntoHarness` / `HarnessComponent.CreateFromSelection` were removed with the
"Add to Harness" menu item.

## UNVERIFIED — the spike that gates everything

Does an owned, `Enabled = true` document run its own `ScheduleSolution` timer while it is NOT the canvas
document? The entire signal lifecycle is scheduler-driven. If it does not, the fallback is small: route
`StatefulComponentBase.ScheduleAt` (the single funnel) to `PhyDocuments.Host(this)` and have
`HarnessComponent.SolveInstance` forward with `_inner.NewSolution(false)`.
