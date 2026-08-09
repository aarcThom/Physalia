---
name: gh-nopreview-hidden-palette
description: "Recoloring a non-preview GH component capsule via GH_Skin must swap the Hidden palette, not Normal"
metadata: 
  node_type: memory
  type: reference
  originSessionId: 810a75de-1e1b-41a1-87ef-d15080b000b0
---

To tint a Grasshopper component's capsule, the standard trick is to swap `GH_Skin.palette_normal_standard`/`palette_normal_selected` with a custom `GH_PaletteStyle(fill, edge, text)` around `base.Render(...)` (restore in `finally`). This preserves the icon, grips, nickname, and message caption for free.

**Gotcha (cost one build iteration):** `GH_ComponentAttributes.RenderComponentCapsule` (decompiled from Grasshopper 8.24) does:
```csharp
var p = GH_CapsuleRenderEngine.GetImpliedPalette(Owner); // Normal unless Warning/Error runtime msg
if (p == GH_Palette.Normal && !Owner.IsPreviewCapable) p = GH_Palette.Hidden;
```
So a component that produces **no geometry** (e.g. Physalia's Chat / signal-only nodes) is **not preview-capable** and GH forces its capsule onto the **Hidden** palette. Swapping only `palette_normal_*` then has zero visual effect. Fix: also swap `palette_hidden_standard`/`palette_hidden_selected` (or just swap all four with the same tint).

Warning/Error runtime messages still override to the Warning/Error palette — the swap only affects the no-message state.

**Who carries a tint (2026-08-09):** only `HarnessAttrib`. `ChatAttrib` was stripped back to Grasshopper's own `GH_Skin.palette_{normal,locked}_{standard,selected}` — the Chat is an ordinary pipeline node now, with no double-click gesture either; the harness proxy is the only door onto the chat window. It KEEPS the custom `RenderSmoothCapsule` regardless, for the two structural reasons below (jagged edge + forced Hidden palette), just driven by the stock style.

`ChatAttrib.cs` (and `HarnessAttrib.cs`, which still carries the tint) ABANDONED the skin-swap and custom-render the capsule (`RenderSmoothCapsule`), a near-verbatim replica of `GH_ComponentAttributes.RenderComponentCapsule` driven by a private `GH_PaletteStyle`. Reason: it also needed to round both edges (`capsule.SetJaggedEdges(false, false)` — GH forces a jagged "no inputs" left edge) Replica notes: `MessageRectangle` is `internal` (can't set cross-assembly — just discard `RenderMessage`'s return); `RenderComponentParameters` is `public static` (reuse for param name + tags); `IsIconMode` is `protected static` (replicate inline: `mode==icon || (mode==application && CentralSettings.CanvasObjectIcons)`); `capsule.Render(g, style)` draws grips+bg+highlight+outline. Light-blue fill `(218,243,245)`, black edge, dark-purple text `(47,8,87)`, plus a pink→white inner gradient outline (CompoundArray `{0.5f,1f}` inner-band trick). See [[harness-subdocument]].
