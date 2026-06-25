---
name: chatbox-emoji-identity
description: Each Chatbox gets a random ocean emoji as its canvas icon + switcher-row dot
metadata:
  node_type: memory
  type: project
  originSessionId: 709e481c-1f44-4e56-9f70-49eb73fe5ebc
---

Each `Chatbox` is assigned a stable, randomly-chosen sea/ocean emoji as its visual
identity, so users can pair a switcher-row dot in the chat window with the component on
canvas. Landed 2026-06-24, builds clean (`dotnet build src/Physalia.slnx -c Debug`); live
Rhino test of the colour icons pending.

- `Chatbox.cs`: `OceanEmoji` palette (~21 single-glyph emojis, `🌊` first), `_emoji` field
  seeded random in ctor, deduped against canvas siblings in `AddedToDocument` (skipped when
  `_loaded`, i.e. restored from file). Persisted via `Write`/`Read` key `"ChatboxEmoji"`.
  `Emoji` public getter.
- **Icon = bundled colour PNG, NOT a drawn font glyph.** GDI / `System.Drawing` (and the GH
  canvas, which paints objects to an off-screen GDI buffer) can only render the *monochrome
  base layer* of a colour emoji font — `TextRenderer`/`DrawString` both give black outlines,
  even on the live canvas. So we bundle Noto Emoji colour PNGs (Apache-2.0) in
  `src/Physalia.GH/Resources/emoji/emoji_u<codepoint>.png` (+ `NOTICE`), embedded via a csproj
  `EmbeddedResource Include="Resources\emoji\*.png"` glob (resource name
  `Physalia.GH.Resources.emoji.emoji_u<hex>.png`). `Icon` getter resolves the resource from the
  emoji at runtime: `char.ConvertToUtf32(emoji,0):x` → resource name, load, scale to 24×24
  (HighQualityBicubic). Ribbon proxy (no document) shows `OceanEmoji[0]` (🌊); brain fallback if
  a resource is missing. `ResetEmojiIcon` nulls the cache + `DestroyIconCache()` (real
  GH_DocumentObject method); called on emoji change and in `AddedToDocument`.
- `ChatWindow.MaybePushChatboxes` adds `emoji = cb.Emoji` to the pushed JSON; `bridge.ts`
  `UiChatbox.emoji`; `App.svelte` switcher row renders the glyph (HTML → colour native) instead
  of a coloured dot (active = raised neu ring `--neu-shadow-sm`, no-history = `opacity-40`).
- Dead end tried first: rendering the emoji with `TextRenderer.DrawText` (Segoe UI Emoji) into a
  bitmap *and* live on the canvas DC — both monochrome. To colour-render a system emoji font you
  need DirectWrite/Direct2D, SkiaSharp, or pre-made images; we chose pre-made images.

Related: [[chatbox-switcher-row]], [[collapsible-harness]], [[chat-window]].
