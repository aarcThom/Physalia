# Publishing Physalia to Rhino's package manager

Written 2026-09-09. Everything below marked **verified** was checked on this machine or against
McNeel's own docs; everything marked **decide** or **verify** has not been.

The order matters: two of these stages are irreversible on the public server, and one of them
(claiming the name) is worth doing early precisely because it is.

---

## 0. Before any of this — the two real blockers

Neither is a packaging problem, and both would ship as a broken first impression.

1. **F3, the unattended overnight run** — the last outstanding item in `planning/pre-ship-testing.md`.
   An armed pipeline that overspends its budget is worse than no trigger tier at all, and the trigger
   tier is in the box. Drive it with `tools/overnight/Watch-OvernightRun.ps1`.
2. **The 30 presets in `Files/PRESETS/Physalia/AI/` do not appear in the gallery.**
   `PresetLibrary.Enumerate` lists files directly inside `Physalia`, `User` and `Community`, one
   level shallower than they sit. Shipping a package whose entire teaching library is invisible is
   worse than shipping without one. Either flatten the folder or teach `Enumerate`/`Resolve` a nested
   path — the latter changes the wire value the chat window hands back (`Physalia/AI/01 - ….phy`), so
   it is its own piece of work with its own headless check.

Also worth finishing first, because it is what a package listing is judged on: this repo's
`README.md` is what the manifest's `url` will point at.

---

## 1. Decisions (**decide**)

| Question | Recommendation | Why |
|---|---|---|
| Package name | `physalia` | **Verified free** — `yak search physalia` returns nothing, released or pre-release. Letters/numbers/dashes/underscores only; the case of the first upload is kept. |
| First version | `1.0.0` | Already in `Physalia.GH.csproj`. **Not** `1.0.0-rc1`: a pre-release tag hides the package behind the manager's *Include pre-releases* checkbox AND opts every user out of the automatic updates the whole last week of work was about. |
| Platforms | `win` only, for now | The GH project builds `net7.0-windows` on Windows and the mac port is deferred (`planning/mac-port.md`). |
| Minimum Rhino | whatever `yak build` derives from the RhinoCommon reference — expect `rh8_24` | We pin Grasshopper/RhinoCommon `8.24.25281.15001`. **Verify** the tag on the built file name and only override deliberately: claiming a lower SR than we compile against is how a package installs and then fails to load. |
| Who owns it | the account that pushes 1.0.0 | Ownership is first-come and cannot be transferred by us. If Physalia is ever to be a StructureCraft-published package rather than a personal one, that decision has to be made **before** the first push. |

---

## 2. Trim the payload (**the one measured surprise**)

`src/Physalia.GH/bin/Debug/net7.0-windows` is **321 MB**, and **294 MB of that is `runtimes/`** —
SkiaSharp natives for 21 RIDs, Linux musl and ARM variants included, because `PDFtoImage`/`SkiaSharp`
bring every one they support.

| RID | Size | Needed in a Windows package? |
|---|---|---|
| `win-x64` | 18.6 MB | **Yes** — this is the one Rhino 8 for Windows uses |
| `win-arm64`, `win-x86` | 33 MB | No. Rhino 8 Windows is x64; there is no 32-bit or native-ARM Rhino to load us |
| `linux-*` (12 RIDs), `osx-*`, `browser` | ~240 MB | No |

So: restrict the RIDs in `Physalia.GH.csproj` (a `RuntimeIdentifiers`/`ExcludeAssets` narrowing, or a
delete step in the existing `TrimNativePdbs` neighbourhood, which already prunes native `.pdb`s
including the ~89 MB `libSkiaSharp.pdb`). Target: **a package around 25 MB rather than 320 MB.**

**Then re-verify PDF rendering in Rhino, from the installed package** — not from `bin`, and not on
the command line. `PdfNativeLibrary.Resolve` probes `runtimes/<os>-<arch>/native/`, then
`runtimes/<os>/native/`, then a flat copy beside the assembly; prune the wrong thing and the symptom
is a `DllNotFoundException` in Rhino only, from a build that is healthy everywhere else. See
`planning/pdf-tools.md` and the compatibility note in `CLAUDE.md`.

Also **verify** whether the package server has a size limit at all — unknown, and irrelevant once
the above is done, but worth knowing before the first push.

---

## 3. The package folder

Build `-c Release` and package from `src/Physalia.GH/bin/Release/net7.0-windows`. What belongs:

```
manifest.yml                 <- written here, kept in the repo, copied in at build time
icon.png                     <- 64x64, from Images/phy_critter.svg
Physalia.GH.gha              <- the plug-in; MUST be at the package root
*.dll  (7)                   <- the JSON stack + PDFtoImage/SkiaSharp shims, deliberately NOT
                                merged by ILRepack (type identity / P-Invoke; see CLAUDE.md)
runtimes/win-x64/native/     <- after stage 2
Bridge/                      <- Physalia.McpBridge, net8.0, launched as a subprocess. McpServer
                                resolves it as "Bridge" beside the assembly, so the folder NAME is
                                load-bearing. Rhino 8 already carries the .NET 8 runtime it needs.
Files/                       <- CHANGELOG.md, SYSTEM_PROMPTS, CLUSTERS, PRESETS/Physalia
LICENSE, README.md           <- AGPL-3.0-or-later; see the licence note below
```

What must **not** go in:

- `Files/MEMORIES`, `Files/PROJECT_FILES`, `Files/PRESETS/User`, `Files/PRESETS/Community` — the
  user's own work. As of the 2026-09-09 data-folder move these are empty in the build output except
  for a README explaining where they went, so this is now automatic rather than a rule to remember.
  **Verify it stayed that way** before pushing: a package that shipped somebody's conversation
  history or API-shaped project files would be a disclosure, not a bug.
- `Physalia.GH.pdb`, `Physalia.GH.xml` (the doc XML is generated to VALIDATE the comments, not to
  ship), `Physalia.GH.csproj.user`.
- `Physalia.GH.deps.json` / `.runtimeconfig.json` are inert for a `.gha` loaded by `Assembly.LoadFrom`
  — harmless either way; drop them for tidiness if a test install still loads.

Single framework, so the `.gha` sits at the package root rather than in a `net7.0/` subfolder. The
`net48/` + `net7.0/` layout only earns its place when the same version ships for both.

---

## 4. `manifest.yml` (**verified field rules**)

Keep it in the repo — `tools/packaging/manifest.yml` — and copy it into the staging folder, rather
than letting `yak spec` regenerate it each release. `yak spec` is worth running **once**, to see what
it reads off the assembly (that is why `Title`, `Authors`, `Company`, `Description` and `Version`
were corrected on 2026-09-09; they were still the project template's).

```yaml
name: physalia
version: 1.0.0
authors:
  - Thomas Gaudin
description: >
  AI pipelines inside Grasshopper. Physalia wires an LLM to your Rhino document, your canvas and
  your project files as a visual node graph: it can build definitions, write and push scripts, read
  drawings and live APIs, call MCP servers, and drive itself from triggers under a spend cap.
url: "https://github.com/aarcThom/Physalia"
icon: icon.png
keywords:
  - ai
  - llm
  - claude
  - openai
  - gemini
  - mcp
  - automation
  - scripting
  - 862C53A2-69A1-4B56-A133-26E0BCEDE789
```

- **The GUID keyword is not decoration.** It is `Physalia_GHInfo.Id`, and it is how Grasshopper's
  package restore finds the package for a definition that needs it. A user opening somebody else's
  `.gh` gets offered the install instead of a canvas full of red.
- `icon` must be a file **inside** the package (`icon_url` is obsolete). PNG or JPEG, small — 64x64.
  Render it from `Images/phy_critter.svg`, the project's only logo.
- `version` supports a `$version` placeholder if the build should substitute it; with `--version` on
  the command line available too, prefer keeping the literal in step with the csproj and let the
  changelog test catch the drift.
- **Licence.** AGPL-3.0-or-later means whoever receives the binary must be able to get the
  corresponding source. There is no `license` field in the manifest, so `url` pointing at the public
  repo plus `LICENSE` in the package is what discharges it. Do not push a package built from a dirty
  worktree — the source that URL serves must be the source that built the binary.

---

## 5. Dry run on the test server (**do this first, always**)

```
cd <staging folder>
& "C:\Program Files\Rhino 8\System\Yak.exe" build --platform win
& "C:\Program Files\Rhino 8\System\Yak.exe" login
& "C:\Program Files\Rhino 8\System\Yak.exe" push --source https://test.yak.rhino3d.com physalia-1.0.0-rh8_24-win.yak
```

The test server is wiped nightly, so this is the only place a mistake is free. Then install FROM it,
which is the step that proves the package rather than the file:

```
& "C:\Program Files\Rhino 8\System\Yak.exe" install --source https://test.yak.rhino3d.com physalia
```

`yak login` opens a browser and the token lasts about 30 days — so it is a step a release script
cannot do unattended.

---

## 6. Verify the INSTALLED package in Rhino

Not `bin`, not the command line. The install lands in
`%APPDATA%\McNeel\Rhinoceros\packages\8.0\physalia\1.0.0\`, and Rhino loads new packages on its next
start. Check, in this order, because each one has a known way of failing only here:

1. **The ribbon loads** — all 108 components, no load error in `_PlugInManager`.
2. **PDF rendering** — place a Read PDF tool and render a page. This is the RID pruning's only real
   test.
3. **The MCP bridge** — configure a remote server and round-trip `tools/call`. Proves `Bridge/`
   arrived and that `McpExecutable.Resolve` still finds `npx` from an installed location.
4. **The preset gallery lists the shipped presets** and one loads onto the canvas (see blocker 2).
5. **The chat window's entry screen shows `v1.0`** with `Physalia 1.0.0.0` on the tooltip.
6. **A fresh install shows NO update notice**, and `%LOCALAPPDATA%\Physalia\install.json` appears
   with `version: 1.0.0.0`.
7. **Then fake an update**: edit that file's version down to `0.9.0.0`, restart Rhino, and the notice
   should appear once, name 0.9 → 1.0, carry the changelog section, and not come back after "Got it".
8. **Nothing of the user's is in the install directory** — `Files/MEMORIES`,
   `Files/PROJECT_FILES` and `Files/PRESETS/User` hold only their READMEs, and a memory written by
   the model lands in `%LOCALAPPDATA%\Physalia\MEMORIES` instead.
9. **Uninstall** (`yak uninstall physalia`, restart) and confirm the data folder survives it. That is
   the whole point of the move, and the only way to see it is to do it.

---

## 7. Publish (**irreversible**)

```
& "C:\Program Files\Rhino 8\System\Yak.exe" push physalia-1.0.0-rh8_24-win.yak
& "C:\Program Files\Rhino 8\System\Yak.exe" search physalia
```

A successful push prints nothing. **A version can never be deleted or overwritten** — `yak yank`
only unlists it, and the bytes stay resolvable for anyone who already depends on them. So the first
push is the one to be slow about; everything in stages 5 and 6 exists to make it boring.

Then tag the commit (`v1.0.0`) so the source that URL serves can be matched to the binary.

---

## 8. After the first release

- **Every release is now two edits, and a test enforces the pair**: bump `<Version>` in
  `Physalia.GH.csproj` and add a `## <version>` section to `Files/CHANGELOG.md`.
  `ReleaseNotesTests.TheShippedChangelogHasASectionForTheShippedVersion` fails at the bump rather
  than a release later. That section is what every user sees in the update dialog, so it is written
  for them, not for us.
- **Users will be auto-updated silently and will not be asked.** That is the package manager's
  behaviour, not a choice we get to make — which is why the notice exists, and why a release that
  changes behaviour needs its changelog entry to say so plainly.
- Optional: a GitHub Actions release job (build `-c Release`, prune RIDs, stage, `yak build`) that
  attaches the `.yak` to a GitHub release. It cannot `yak push` unattended without a stored token, so
  leave the push manual, or store the token as a secret with eyes open about the 30-day expiry.
- The mac distribution (`--platform mac`, a second `.yak` under the same version) waits on
  `planning/mac-port.md`: `ISecretStore` is the one platform seam, and `SkiaSharp`'s osx RIDs are
  already in the dependency graph.
