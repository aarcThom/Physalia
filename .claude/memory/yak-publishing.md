---
name: yak-publishing
description: Physalia 1.0.0 is PUBLISHED on Rhino's package manager — what shipped, the rh8_0 tag we could not fix and why, the packaging toolkit, and the verification trap that makes an installed-package test meaningless.
metadata:
  type: reference
---

**Published 2026-09-09: `physalia 1.0.0`, `rh8_0-win`, 19.7 MB, on `https://yak.rhino3d.com/`,
owned by the personal account `thomas@aarc.io`.** A version can never be deleted or overwritten;
`yak yank` only unlists it. Plan: `planning/yak-publishing.md`. Toolkit: `tools/packaging/`.

## The toolkit
`tools/packaging/` holds `manifest.yml`, `icon.png` (64x64, rendered from `Images/phy_critter.svg`
with headless Chrome — see [[svg-rasterization-headless-chrome]]) and `Stage-YakPackage.ps1`, which
copies `bin/Release/net7.0-windows`, prunes, asserts, and optionally runs `yak build`.
**322 MB in, 19.7 MB out.** The prune is in the SCRIPT, not the csproj: narrowing
`RuntimeIdentifiers` on a library changes restore and output layout for every dev build and would
fight the deferred mac port, while deleting from a staged copy is reversible.

Its checks are all "healthy on disk, broken once installed" cases: the `runtimes/<rid>/native/`
paths `PdfNativeLibrary` probes, `Bridge/` and the net8.0 exe's `deps.json` + `runtimeconfig.json`
(only its `.pdb` goes), a manifest version matching the assembly, and a `## <version>` changelog
section. It walks EVERY `runtimes/` folder — the bridge carries its own.

## The rh tag we could not fix
**`yak build` derives the distribution tag from the LOWEST RhinoCommon reference in the merged
`.gha`**, and `GhJSON.Grasshopper 1.1.1` has `RhinoCommon 8.0.23304.9001` compiled into it.
`GhJSON.Core` is clean and `Physalia.Core` emits no Rhino reference at all, so **no csproj pin
reaches it** — a direct 8.24 `PackageReference` in Core was tried and reverted as inert. The only
lever is `<RepackDenyList Include="GhJSON.Grasshopper" />`, shipping it loose beside the `.gha` the
way the JSON stack and PDFtoImage already do. Deferred to 1.0.1.

So 1.0.0 advertises a Rhino **8.0** minimum for a plug-in whose GH half compiles against 8.24.
Related, and worse in principle: `Rhino.Runtime.Code` and `RhinoCodePlatform.GH` are `HintPath`s
into `C:\Program Files\Rhino 8\System`, so the real floor for `run_rhino_script` **follows whatever
Rhino the build machine has** (8.34 here). Yak ignores them. Pin them deliberately some day.

## The trap that makes verification meaningless
`%APPDATA%\Grasshopper\grasshopper_kernel.xml` has `Assemblies:Folders` pointing at
`bin\Debug\net7.0-windows`. **The installed package supplies a second `.gha` with the same plug-in
GUID**, so unless that folder is cleared (`_GrasshopperDeveloperSettings`, with Rhino CLOSED — GH
rewrites the file on exit) you restart Rhino, see everything work, and have tested the DEV BUILD.
Always confirm the loaded location first:
`AppDomain.CurrentDomain.GetAssemblies()` → `Physalia.GH` → `.Location` must be under
`%APPDATA%\McNeel\Rhinoceros\packages\8.0\physalia\<version>\`.

## Verified live from the INSTALLED package (2026-09-09)
Library GUID `862c53a2-…` matching the manifest keyword; **124 proxies, 0 missing icons**;
`PdfPageRenderer.TryRender` producing a real 937x625 PNG (the RID prune's only true test — the
natives resolved); the bridge starting far enough to print its own usage and exit 2 (proving the
net8.0 runtime and `deps.json` survived); **39 presets enumerated, 30 in AI**; `PhyVersion.Display`
→ `1.0`; and nothing of the user's in the install directory.

**NOT verified, and now shipped:** the update notice read from the package directory, uninstall
leaving `%LOCALAPPDATA%\Physalia` standing, and **F3** (the unattended overnight run — still the
open item in `planning/pre-ship-testing.md`, shipped by an explicit decision).

## Preset 29 nearly shipped broken
A `.gh` preset cannot carry a payload the way a `.phy` carries `files/`, so the ComfyUI preset's
SDXL graph ships at `Files/PROJECT_FILES/comfy-render/img2img-sdxl.json` and **`DataMigration`
delivers it** into `%LOCALAPPDATA%\Physalia\PROJECT_FILES\`, where the preset's Project Folder input
(`comfy-render`, no separator) resolves it. The first staging script pruned it as stray user content.
It is now an explicit `$shipped` exception WITH an assertion, so a future prune change fails the
stage instead of silently breaking that preset. Confirmed delivered on this machine.

## Mechanics worth not relearning
- `yak login` opens a browser, stores `%APPDATA%\McNeel\yak.yml`, lasts ~30 days — no release script
  can do it unattended. From Claude Code's `!` prefix it is BASH, so `& "C:\…"` is a syntax error.
- Test server (`--source https://test.yak.rhino3d.com`) is wiped nightly; install FROM it, which is
  what proves the package rather than the file.
- `yak build` takes only `--platform` and `--version`. **There is no tag override.**
- Yak adds its own normalised `guid:<lowercase>` keyword beside ours; both are in the listing.
- `[warn] Content version doesn't match manifest: '1.0.0.0' != '1.0.0'` is cosmetic and accepted.

Related: [[data-folder-and-update-notice]], [[pdf-natives-verified]], [[preset-conventions]],
[[pre-ship-testing-pass]], [[comfy-render-preset]], [[ilrepack-release-double-merge]].
