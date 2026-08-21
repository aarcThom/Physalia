---
name: mac-todo
description: "Everything in Physalia that is Windows-only or unverified on Mac Rhino — the four #if WINDOWS files, the WinForms surface generally, and the GhPythonBridge DLL HintPaths to confirm"
metadata: 
  node_type: memory
  type: project
  originSessionId: 9070a607-d646-44b4-a78e-a43dc097a34d
  modified: 2026-08-21T00:00:00.000Z
---

Running list of what still needs a Mac Rhino 8 pass. Verify against a real install before claiming
any of it works. Facts below re-checked against the tree 2026-08-21.

**`#if WINDOWS` now guards FOUR files, not one.** The symbol is defined explicitly in the
windows-TFM `<PropertyGroup>` (alongside `UseWindowsForms`), not inherited from the SDK's implicit
define. On Mac these compile away and the feature is simply absent:
- `Components/Extra/Serializer.cs` — interactive export (select objects → Enter/Esc → SaveFileDialog
  → .ghjson) via `System.Windows.Forms.SaveFileDialog`, `Keys`/`KeyEventArgs` and a
  `GH_Canvas.KeyDown` hook. **To port:** swap to `Eto.Forms.SaveFileDialog`, replace the canvas
  `KeyDown` hook with an Eto keyboard equivalent, drop the guard. The HUD overlay
  (`CanvasPostPaintWidgets` + System.Drawing) and the GhJSON export calls are already
  cross-platform.
- `Widgets/SerializeWidget.cs` — the canvas widget that drives the above.
- `Panels/ChatWindow.cs` and `Widgets/ChatWidget.cs` — partially guarded; the chat window is the
  single biggest unverified surface on Mac ([[chat-window]], [[chat-widget]]).

`Components/Extra/Deserializer.cs` is **cross-platform, no guard** — inputs File Path + Run, no
WinForms (the path is a plain string), deferral via `Rhino.RhinoApp.Idle`. Runs on both TFMs as-is.

**WinForms is not confined to the guarded files.** 22 files under `Physalia.GH` import
`System.Windows.Forms` unguarded, and they compile on both TFMs. Most are `ContextMenuStrip`
right-click menus, which CLAUDE.md records as working on the GH canvas (`Eto.ContextMenu` does
not) — but nothing here has been exercised on Mac. The heavier ones to check first are the custom
attributes (`HarnessAttrib`, `PickerAttrib`, `ArrowAttributeBase`) since they own mouse capture and
drag, and `HarnessNotes`, which opens a Rhino edit box.

**GhPythonBridge — Mac DLL HintPaths to confirm.** The csproj has Mac ItemGroups with *guessed*
paths for three DLLs:
- `Rhino.Runtime.Code.dll` — used directly; `ParamType` lives here
- `RhinoCodePlatform.GH.dll` — used directly; `IScriptComponent`, `ScriptParamSpec`,
  `ScriptParamAccess` live here
- `RhinoCodePlatform.GH1.dll` — **confirmed 2026-08-21 to be imported by no code at all** (no
  reference to it, `ScriptTemplate` or `SetInputsArray` anywhere in the tree), despite the csproj
  comment claiming it supplies "concrete types". Kept for completeness; a candidate for removal.

Everything else in GhPythonBridge is platform-agnostic — `IsScriptComponent`, `Set/GetScript`,
`GetInputs/GetOutputs`, `GetErrors/GetWarnings`, `Expire`, `GetInput/OutputValues` are pure GH or
live in those referenced DLLs, and `SetInputs`/`SetOutputs` reach `UpdateInput/OutputParameters` on
`BaseScriptComponent` by reflection (resolves at runtime). But all of it needs those DLLs resolvable
**at compile time**, so the HintPaths gate the whole Mac build.

Also unverified on Mac: the Eto UI surface generally ([[resources-tab-image-gatherer]]).
