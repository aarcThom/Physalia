---
name: mac-todo
description: "Everything in Physalia that is Windows-only or unverified on Mac Rhino — the four #if WINDOWS files, the WinForms surface generally, and the GhPythonBridge DLL HintPaths to confirm"
metadata: 
  node_type: memory
  type: project
  originSessionId: 9070a607-d646-44b4-a78e-a43dc097a34d
  modified: 2026-08-21T00:00:00.000Z
---

**SUPERSEDED 2026-09-07 by `planning/mac-port.md` in the repo** — the Mac release was deliberately
deferred a few weeks and the whole surface was re-audited, because everything below predates the
harness panel, the browser fetch, the DPAPI credential store and the MCP bridge. Read the planning
doc first; keep this note only for the GhPythonBridge HintPath detail at the end.

Three things the re-audit established that are worth having here too:

1. **Two blockers were found FROM WINDOWS**, before anyone touches a Mac. The non-Windows TFM fails
   restore on a `System.Drawing.Common` downgrade (`GhJSON.Grasshopper` wants >= 8.0.11, the project
   pins 7.0.0 — it does not bite on `net7.0-windows`), and the `Grasshopper` NuGet package's only
   non-net48 asset is the **Windows** build: `Grasshopper.dll` references `RhinoWindows`, `Eto.Wpf`,
   WPF and `System.Windows.Forms`. So the fix is to repoint Grasshopper/GH_IO/RhinoCommon at the Mac
   app bundle, the way the csproj already does for Eto and Rhino.Runtime.Code. **Force the other TFM
   with `dotnet build -p:TargetFrameworks=net7.0` — it costs ten minutes and answers questions that
   would otherwise cost an afternoon in the dark.**
2. **The WinForms surface is 29 files but only FOUR real items.** 25 of them touch nothing but
   `ToolStripDropDown`/`ContextMenuStrip`, i.e. Grasshopper's own menu API that we are obliged to
   override. Whether those need any work at all depends on one question — does the Mac reference set
   give a compilable `System.Windows.Forms`? — which falls out the moment the TFM compiles. The real
   items are `HarnessPanel` (a genuine top-level `Form`, the biggest single job), plus
   `OpenFileDialog`, `Clipboard` and `Cursor`.
3. **`Physalia.Core` has no `System.Windows.*` reference of any kind**, and DPAPI is runtime-guarded
   by `OperatingSystem.IsWindows()` rather than compiled in. The boundary rule is why this is a UI
   job and not a rewrite. **Do not weaken Windows encryption to make the platforms match** — the Mac
   answer is one Keychain `ISecretStore` plus one line in `SecretStores.For`.

Also fixed 2026-09-07: `McpServer.BridgeExecutable()` hardcoded `Physalia.McpBridge.exe`, so on macOS
it returned null and every REMOTE MCP server reported the bridge missing while local stdio servers
kept working — a platform fault disguised as a server-specific one. It now probes both apphost
spellings. A package built on Windows still carries no macOS apphost.

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
