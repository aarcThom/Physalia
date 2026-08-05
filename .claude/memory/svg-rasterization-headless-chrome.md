---
name: svg-rasterization-headless-chrome
description: How to rasterize an SVG to a transparent PNG on this machine — no magick/inkscape/rsvg installed; use headless Chrome.
metadata: 
  node_type: memory
  type: reference
  originSessionId: e79d0e95-1b27-406a-a875-97e4b416c93d
  modified: 2026-08-05T07:17:42.866Z
---

This dev machine has **no** ImageMagick, Inkscape, or rsvg-convert, and Python has no cairosvg/svglib/Pillow. Rasterize SVG → transparent PNG with headless Chrome at `C:\Program Files\Google\Chrome\Application\chrome.exe` (Edge at `C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe` works too).

Wrap the SVG in an HTML page whose `#host` div is the exact target pixel size (`html,body{margin:0;background:transparent}`, `svg{width:100%;height:100%;display:block}`), `fetch()` the .svg into it, then:

```
chrome.exe --headless=new --disable-gpu --allow-file-access-from-files --hide-scrollbars \
  --default-background-color=00000000 --force-device-scale-factor=1 \
  --window-size=W,H --screenshot=out.png "file:///.../render.html"
```

`--default-background-color=00000000` is the flag that yields a real alpha channel (verified Format32bppArgb, corner A=0). Match `--window-size` to the viewBox aspect or the render is letterboxed. Inline `<style>`/`class` fills in the SVG render fine — no need to flatten them.

Verify afterwards with `System.Drawing` in PowerShell: check `PixelFormat`, corner alpha, the ink colour, and the ink bounding box (a viewBox tight to the artwork gives bbox = full frame, so `FitCentred` needs no padding trim).

Used on 2026-08-04 to make the critter the project's only logo: `Images/phy_critter.svg` → `Resources/critter.png` (365x512) for the canvas widget, and inlined into `HappyFace.svelte` for the chat UI; the old jellyfish (`Images/logo.svg`, `Resources/logo.png`) was deleted. See [[chat-widget]].

Related: [[physalia-repo-gotchas]] (the `Resources\*.png` glob auto-embeds new icons — dropping a PNG in needs no csproj edit).

Known-unrelated build failure to expect: `dotnet build -c Release` fails at ILRepack with `Failed to resolve assembly: 'Grasshopper, Version=8.24...'`. **Pre-existing at HEAD 596f196** (verified by building a clean baseline worktree) — Release-only; the compile stage still succeeds and embeds resources correctly. Debug builds fine. Don't blame your change.
