---
name: resources-tab-image-gatherer
description: "Resources GH tab + Image Gatherer component (2026-06-12) — ImageResource goo, path-only persistence, and the Eto/WPF GridView edit-commit gotcha that cost two crashes"
metadata: 
  node_type: memory
  type: project
  originSessionId: 9070a607-d646-44b4-a78e-a43dc097a34d
  modified: 2026-07-26T05:04:02.591Z
---

New **"Resources"** GH tab (created simply by passing `"Resources"` as the PhyBase `subCategory`). First component: **Image Gatherer** (`Components/Resources/ImageGatherer.cs`) — no inputs, single list output of a `GH_ImageSource` goo. Right-click → **Manage Images** opens `ManageImagesDialog` (Eto panel) with a GridView (path / editable alias / preview / red-✕ remove) + Add Image / Paste buttons.

- **Alias carried as real data**, not just a display string: Core record `ImageResource(string Alias, ImageSource Source)` in `Physalia.Core/ConvoInstruct/ImageResource.cs`. Goo `GH_ImageSource : PhyGoo<GH_ImageSource, ImageResource>` (TypeName "Image Source"); param `Param_ImageSource`. Images become `InlineImage(bytes, mime)`.
- **Persistence = file paths + aliases only** (component-level `Write`/`Read`; bytes re-read from disk on load via `File.ReadAllBytes`). Clipboard-pasted images have `FilePath = null` → NOT persisted; missing files on reopen → deferred warning surfaced in the next `SolveInstance`. Goo `Write`/`Read` are no-ops (component owns persistence).
- MIME map + unique-alias helpers are `internal static` on `ImageGatherer`, reused by the dialog. Alias uniqueness validated case-insensitively on cell-edit commit (revert + MessageBox on blank/dup).

**Eto/WPF GridView edit-commit gotcha (cost two crashes to find).** Rhino-Windows Eto is `Eto.Wpf`, so `GridView` wraps a WPF `DataGrid`:
1. Doing grid work synchronously inside the `CellEdited` handler re-enters the grid mid-commit and crashes — defer via `Application.Instance.AsyncInvoke`.
2. Even deferred, the row is STILL in a WPF `EditItem` transaction, and `GridView.ReloadData()` calls `CollectionView.Refresh()` → `InvalidOperationException: 'Refresh' is not allowed during an AddNew or EditItem transaction`. Fix: **never** call `ReloadData` to reflect an edited cell — make the row model implement `INotifyPropertyChanged` and raise it on the edited property; Eto's property binding refreshes just that cell with no collection Refresh. `ImageEntry` does this for `Alias`.

**First Eto.Forms usage in the repo.** Referenced via HintPath to Rhino's shipped `Eto.dll` (2.11) with `Private=False` (NOT a NuGet PackageReference) — deliberately matches the runtime to dodge the Eto 2.7-vs-2.11 CS1705 conflict ([[gh-code-editor-abandoned]]). `Eto.dll` holds both `Eto.Forms` and `Eto.Drawing`. Eto 2.11 `GridView.ReloadData()` has NO parameterless overload — pass `Enumerable.Range(0, rowCount)`. Mac `Eto.dll` HintPath is a guess; Eto UI surface untested on Mac ([[mac-todo]]).

Consumed by [[prompter-image-references]].
