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

Files: `HarnessComponent.cs`, `PhyDocuments.cs`, `HarnessResidency.cs`, `IHarnessArrow.cs`,
`Attributes/HarnessAttrib.cs`, `Widgets/HarnessReturnWidget.cs`.

## The local-vs-host document split — the core of it

`OnPingDocument()` inside a harness returns the **sub**-document. `PhyDocuments` resolves the rest:
`Host(doc)` / `Host(obj)` / `ActiveHost()` climb to the root; `ObjectsIncludingHarnesses(doc)` walks a
document plus the harnesses in it; `IsHarnessDocument(doc)`.

- **Stays local:** `ScheduleSolution`/`NewSolution`, `AddedToDocument`/`RemovedFromDocument`,
  Feedback→FeedbackCollector guid resolution, ToolsInUse's peer scan, `PromptPipelineView` wire walks.
  Every endpoint of those moves into the harness together.
- **Becomes `PhyDocuments.Host(this)`:** CanvasStateGrounder, ComponentTransmitter, GeometryReport,
  RuntimeHealthCheck, FidelityCheck, GeometryObservation, RhinoGeometryTool, MemoryTool,
  ConversationLog's canvas export, Chat's generated-geometry bounds, InterfaceLock's *script* lookup
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

## Arrows live on the proxy

Transmitters draw no arrow (`PyTransmitterAttrib` / `CompTxAttrib` deleted; both use plain
`PhyComponentAttributes`). `IHarnessArrow` is implemented by the **components** (`PyTransmitter`,
`ComponentTransmitter`), and `HarnessAttrib : ArrowAttributeBase` hosts the grip and forwards the drop,
showing it only when the harness holds exactly one implementer (`TryGetSoleArrow`). Required, not
cosmetic: the arrow's targets live on the host canvas and **a drag cannot cross two canvases**.
`ComponentTransmitter`'s placement offset is measured from the **proxy's** pivot (`ArrowAnchor`).
For the same reason `PyTransmitter` gained a **"Link to Script Component" menu picker** over the host
canvas's script components.

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
  `Feedback` (collector list), `InterfaceLock` (its transmitter), `ZoomGuid`, and `PyTransmitter` —
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
