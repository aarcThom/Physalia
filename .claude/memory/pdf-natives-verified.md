---
name: pdf-natives-verified
description: "The PDF natives DO resolve inside Rhino (proved 2026-09-07, both directly and through the Read PDF node), plus the folder layout the node actually wants and how to read a tool call's result at all."
metadata:
  node_type: memory
  type: project
---

Done 2026-09-07. CLAUDE.md's compatibility note says a green `dotnet build` proves nothing about
`PdfNativeLibrary` and demands "place a Read PDF tool and render one page". Both halves now done, in
Rhino, on the merged Debug `.gha`.

**Directly:** `PdfNativeLibrary.Install()` returned **null** (resolvers in place), and
`PdfPageRenderer.TryRender` succeeded twice against a hand-built two-page PDF — full page at 150 DPI
→ **1568x1108, `Downscaled=True`** (the documented 1568px delivery cap), and a title-block region at
600 DPI → **1568x369**. I LOOKED at both PNGs rather than trusting byte counts: the page came out
with correct colour and geometry, and the region crop landed exactly on the title block and was
legible. That last part incidentally confirms two invariants at once — the **top-left** origin
convention (an inverted flip would have cropped the opposite corner) and that
**`DpiRelativeToBounds`** is honoured, since otherwise the crop is stretched across a page-sized
canvas.

**Through the node:** a placed Read PDF answered `render` with `IsError=False` and **one
`ImageContent` attachment carrying an `InlineImage` of 32360 bytes** — byte-identical in size to the
direct call, as it should be. `text` returned the page's real words (PdfPig), `list` produced the
descriptor, and the **control** — `render` page 99 — returned `IsError=True`, "Page 99 does not
exist — sheet-test.pdf has 2 page(s)". Without a call that must fail, a reported success means
nothing; run one.

Nice incidental: the descriptor's **sheet-number heuristic works**. On a synthetic A1 sheet it
reported `2 page(s), mixed sizes (first page 841x594mm (A1 landscape))` and
`Sheet numbers (best guess, read from each page's title-block corner): p1 SHEET A-101 SCALE 1:100 REV C`.

## Two things that cost time

**The node reads `<project folder>/PDF`, not the project folder itself.** `OnSolveTick` does
`Path.Combine(resolved.FullPath, ProjectPaths.PdfSubfolder)`, and `PdfLocations.ListPdfs` is
`TopDirectoryOnly`. A PDF at the root of the folder you typed is invisible, `Available PDFs` comes
back empty, and nothing says why. Note the input is called **`Project Folder`** (plus an optional
`Reference Folder`) — CLAUDE.md still calls it "PDF Folder", which is the older name.

**A manual Construct Tool Call batch cannot show you what a call returned.** By design it mints no
Result signal, so the answer only ever reaches the canvas through the node's OWN outputs — and Read
PDF's single extra output is `Available PDFs`, which says nothing about a call. `State` is no help
either: `LlmToolComponentBase` runs its own `_busy`/`_doEmit` machinery and never touches
`StatefulComponentBase.State`, so it sits at `Empty` through a perfectly successful call. **To
observe a result, invoke `ExecuteCallAsync` by reflection** and read `Content` / `IsError` /
`Attachments`. Construct Tool Call remains the right tool for proving a SIDE EFFECT (it is how the
Blender cube was made — [[blender-mcp-preset]]), just not a returned value.

## Reflection notes for driving this from IronPython

- **`ToolCallResult` is a protected nested record STRUCT**, and IronPython cannot touch the box:
  even `res.GetType()` throws *"Can only unbox from an object or interface type to a value type"*.
  Never touch the value from Python — take the type from
  `method.ReturnType.GetGenericArguments()[0]` and read members with that `Type`'s `PropertyInfo`.
- **Read `Task.Result` off the DECLARED return type.** The runtime type is the internal
  `AsyncStateMachineBox<ToolCallResult, …>`, so both attribute access and its own `GetProperty`
  fail; `exec_method.ReturnType.GetProperty("Result")` works.
- `ToolCallContent(Id, Name, InputJson)` constructs fine via `Activator.CreateInstance`.
- **Log to UTF-8 bytes.** `str()` on a .NET string forces ascii and an em-dash in the descriptor
  kills the script — which then leaves the modal exception dialog that blocks every later run.
