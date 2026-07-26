---
name: mac-todo
description: "Everything in Physalia that is Windows-only or unverified on Mac Rhino — WinForms surfaces, the Serializer #if WINDOWS split, and the GhPythonBridge DLL HintPaths to confirm"
metadata: 
  node_type: memory
  type: project
  originSessionId: 9070a607-d646-44b4-a78e-a43dc097a34d
  modified: 2026-07-26T05:04:29.974Z
---

Running list of what still needs a Mac Rhino 8 pass. Verify against a real install before claiming any of it works.

**PrompterAttrib — WinForms surface untested on Mac.** `Attributes/PrompterAttrib.cs` compiles unguarded on both TFMs like the other attribs, but its WinForms surface — in-place `TextBox` overlay added to `GH_Canvas.Controls`, `System.Windows.Forms.Timer` (busy animation), `Keys`/`KeyEventArgs` in the Shift+Enter handler — has never run on Mac. Verify overlay focus/Leave behaviour; fall back to an Eto dialog if the canvas-hosted TextBox misbehaves. (Prompter was deleted in [[component-reorg-2026-07]]; the pattern survives in other attribs.)

**Serializers folder — opposite statuses for its two components.**
- `Serializer.cs` — **Windows-only (`#if WINDOWS`)**. Interactive export (select objects → Enter/Esc → SaveFileDialog → .ghjson) uses `System.Windows.Forms.SaveFileDialog`, `Keys`, `KeyEventArgs` and a `GH_Canvas.KeyDown` hook. WinForms exists only on `net7.0-windows`, so the **whole file** is wrapped — the Mac build compiles fine, the component is simply absent. The `WINDOWS` symbol is defined explicitly in the windows-TFM `<PropertyGroup>` (alongside `UseWindowsForms`), not relied on from the SDK implicit define. **To port:** swap to `Eto.Forms.SaveFileDialog`, replace the canvas `KeyDown` hook with an Eto keyboard equivalent, drop the guard. The HUD overlay (`CanvasPostPaintWidgets` + System.Drawing) and the GhJSON export calls are already cross-platform.
- `Deserializer.cs` — **cross-platform, no guard.** Inputs File Path + Run; no WinForms (path is a plain string). Deferral via `Rhino.RhinoApp.Idle` (RhinoCommon). Runs on both TFMs as-is.

**GhPythonBridge — Mac DLL HintPaths to confirm.** The csproj has placeholder Mac ItemGroups with *guessed* paths for three DLLs:
- `Rhino.Runtime.Code.dll` — used directly; `ParamType` lives here
- `RhinoCodePlatform.GH.dll` — used directly; `IScriptComponent`, `ScriptParamSpec`, `ScriptParamAccess` live here
- `RhinoCodePlatform.GH1.dll` — in csproj but no longer imported in code (kept for completeness)

Everything in GhPythonBridge is otherwise platform-agnostic — `IsScriptComponent`, `Set/GetScript`, `GetInputs/GetOutputs`, `GetErrors/GetWarnings`, `Expire`, `GetInput/OutputValues` are pure GH or live in those referenced DLLs, and `SetInputs`/`SetOutputs` reach `UpdateInput/OutputParameters` on `BaseScriptComponent` by reflection (resolves at runtime). But all of it needs those two DLLs resolvable **at compile time**, so the HintPaths gate the whole Mac build.

Also unverified on Mac: the chat widget and chat window ([[chat-widget]], [[chat-window]]) and the Eto UI surface generally ([[resources-tab-image-gatherer]]).
