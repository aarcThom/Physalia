---
name: chat-window-placement-fixes
description: "Two still-live fixes rescued from the old preset-placement note: full-name ExpireLayout, and centring the chat window over the GH editor"
metadata: 
  node_type: memory
  type: project
  originSessionId: 8ba6eaa2-c1c1-4bb2-9487-ac5379251e1f
  modified: 2026-08-09T06:19:48.017Z
---

Two fixes from 2026-06-23 that outlived the preset machinery they shipped with (the GhJSON preset
splice is gone — see [[harness-subdocument]]). Both are still in the code.

## Full param names on GhJSON-placed components need ExpireLayout

`ComponentHelpers.ExpandToFullName` set `param.NickName = param.Name` but never invalidated layout.
Components placed by GhJSON `Put` were already laid out with the short JSON nicknames, so the capsule
stayed narrow and GH truncated the longer full name (`Si…`, `C…`). Fix: `obj.Attributes?.ExpireLayout()`
at the end of `ExpandToFullName`, so the next layout pass (from the following `NewSolution`/refresh)
recomputes capsule widths. Only runs when `CentralSettings.CanvasFullNames` is on. Fresh single-object
placements were already fine — their first layout used the expanded names.

## The chat window opens centred over the GH editor (multi-monitor)

The window had no position set and was owned by the Rhino main window, so on multi-monitor it opened on
Rhino's monitor — off the canvas — which broke anchored placement (`AnchorRightOfWindow` maps the
window's screen rect onto canvas world coords). `ChatWindow.PositionOverGrasshopperEditor()`
(Windows-only, called in `Shown` just before `OwnToGrasshopperEditor()`) reads the GH editor + chat
window rects natively via `GetWindowRect` — device px, consistent with the existing anchor math, no
Eto/WPF DPI surprises — and centres the window with `SetWindowPos`
(`SWP_NOSIZE|SWP_NOZORDER|SWP_NOACTIVATE`). No-op if either HWND is unavailable. `_ghEditor` is already
set by `HookHostClose()` earlier in `Shown`.

`AnchorRightOfWindow` still matters: it is where a harness proxy lands when placed from the connect
screen.
