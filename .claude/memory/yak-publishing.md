---
name: yak-publishing
description: Facts verified about publishing Physalia to Rhino's package manager — yak commands and tags, the name being free, and the 294MB of native runtimes a Windows package must not carry.
metadata:
  type: reference
---

2026-09-09, **researched and measured, nothing pushed**. The plan is
`planning/yak-publishing.md`; this is what was verified rather than assumed.

## The tool, on this machine
- `C:\Program Files\Rhino 8\System\Yak.exe` — **Yak 0.15.2**, Rhino **8.34** installed. (Mac:
  `/Applications/Rhino 8.app/Contents/Resources/bin/yak`.)
- `yak spec` writes a starter `manifest.yml` from the ASSEMBLY attributes — which is why
  `Physalia.GH.csproj`'s `Title`/`Authors`/`Company`/`Description`/`Version` were worth fixing.
- `yak build [--platform win|mac|any] [--version X]`, `yak login` (browser, token good ~30 days),
  `yak push [--source https://test.yak.rhino3d.com] <file>.yak`, `yak search [--prerelease]`,
  `yak yank`.
- **`yak search physalia` returns nothing, released or pre-release — the name is FREE**, and the
  first account to push 1.0.0 owns it. Worth claiming early on the real server.
- **A version can never be deleted or overwritten.** `yank` only unlists. The test server
  (`--source https://test.yak.rhino3d.com`) is wiped nightly and is where mistakes belong.

## Package shape
- A `.yak` is a zip: `manifest.yml` at the ROOT, the `.gha`/`.rhp` at the root too (or inside a
  framework folder — `net48/`, `net7.0/` — for a multi-targeted package). Other subfolders are
  allowed, which is what lets `Files/`, `Bridge/` and `runtimes/` ride along.
- Distribution tag is `<app>_<major>_<minor>-<platform>`, e.g. `rh8_24-win`; the rh part comes from
  the referenced RhinoCommon (ours pins Grasshopper/RhinoCommon **8.24.25281.15001**), the platform
  from `--platform`.
- Manifest name may only hold letters, numbers, dashes, underscores; version is semver 2.0.0 or
  four-digit; `icon` is a small PNG/JPEG **inside the package** (`icon_url` is obsolete). Keywords
  should include the Grasshopper plug-in GUID so Grasshopper's package restore can find it —
  `862C53A2-69A1-4B56-A133-26E0BCEDE789` (`Physalia_GHInfo.Id`).

## The measurement that changes the packaging
`bin/Debug/net7.0-windows` is **321 MB, of which `runtimes/` is 294 MB** — SkiaSharp natives for 21
RIDs, including every Linux musl/arm variant. A Windows package needs **win-x64 only** (18.6 MB;
win-arm64 and win-x86 are another 33 MB and Rhino 8 Windows is x64). `Bridge/` is 3.3 MB, `Files/`
0.7 MB, and there are 7 loose DLLs beside the `.gha` (the JSON stack and the two P/Invoke shims,
both deliberately denylisted from ILRepack). **Prune the RIDs, then re-verify a PDF page renders in
Rhino from the INSTALLED package** — `PdfNativeLibrary` probes `runtimes/<os>-<arch>/native/`, and
the failure mode is a `DllNotFoundException` in Rhino only. See [[pdf-natives-verified]].

## Two things that are ship blockers, not packaging details
- **F3, the unattended overnight run**, is still the only outstanding item in
  `planning/pre-ship-testing.md` — an armed pipeline that overspends its budget is worse than no
  trigger tier at all. Drive it with `tools/overnight/Watch-OvernightRun.ps1`.
- **The 30 presets in `Files/PRESETS/Physalia/AI/` do not appear in the gallery**, because
  `PresetLibrary.Enumerate` lists files directly inside the three library folders only. Shipping a
  package whose whole teaching library is invisible is worse than shipping without it.

Related: [[data-folder-and-update-notice]], [[preset-conventions]], [[pre-ship-testing-pass]].
