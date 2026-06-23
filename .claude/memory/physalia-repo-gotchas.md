---
name: physalia-repo-gotchas
description: "Stale paths in CLAUDE.md (slnx in src/, primitives doc in planning/); the Files-folder build pipeline and its two MSBuild gotchas (stray src/Physalia.GH/Files, VS fast-up-to-date-check lag)"
metadata: 
  node_type: memory
  type: project
  originSessionId: bfb2ff92-c812-4e27-89de-9675e357c091
---

CLAUDE.md has stale paths:
- The solution file is `src/Physalia.slnx`, not repo-root `Physalia.slnx` — build with
  `dotnet build src/Physalia.slnx`.
- `docs/physalia-primitives.md` and `docs/ghjson-implementation.md` actually live in
  `planning/` (`planning/physalia-primitives.md`).

**Why:** following CLAUDE.md verbatim fails (MSB1009) or misses the doc.
**How to apply:** use the `src/` and `planning/` paths; consider fixing CLAUDE.md when touching it.

**Files-folder build pipeline (single source of truth = repo-root `Physalia\Files`):**
`Physalia.GH.csproj`'s `CopyLibraryFiles` target (`AfterTargets=Build`) includes
`$(MSBuildProjectDirectory)\..\..\Files\**\*` (= repo-root `Files`) and copies it into
`$(TargetDir)Files` (= `bin/<Config>/<TFM>/Files`). The UI's `chat.html` reaches bin through
this same copy (UI target writes repo-root `Files/UI/chat.html` first — see below). There is
NO longer a `src/Physalia.GH/Files` folder; it was a committed duplicate, removed from git
2026-06-23 (commits `1e2b7e8` + `32ab1be`). The old "empty-diff EOL noise on
`src/Physalia.GH/Files/PhySchema.json`" gotcha is gone with it.

- **Stray `src/Physalia.GH/Files` regenerating every build (FIXED `32ab1be`):** because the
  project uses `TargetFrameworks` (plural), MSBuild runs an OUTER multi-targeting dispatch
  build where `$(TargetFramework)`/`$(TargetDir)` are EMPTY. `CopyLibraryFiles` fired there too,
  so `$(TargetDir)Files` resolved to a RELATIVE `Files` under the project dir → spawned
  `src/Physalia.GH/Files` on every build. Fix: guard the target with
  `Condition="'$(TargetFramework)' != ''"` so it only runs in the inner per-TFM build (where
  `$(TargetDir)` correctly points at bin). Diagnose empty TargetDir with
  `dotnet msbuild <proj> -getProperty:TargetDir` (returns "" on a `TargetFrameworks` project).
  Related: [[trigger-state-machine-status]].

Svelte chat UI build (`src/Physalia.UI`, a no-assembly MSBuild wrapper around Vite that
emits `Files/UI/chat.html`, gitignored): Visual Studio's fast up-to-date check (FUTDC) skips
projects with no managed output, so in VS the `BuildPhysaliaUI` target never ran and
chat.html was never generated/copied to the GH debug folder — while `dotnet build` (no
FUTDC) worked fine. Fixed 2026-06-22 by adding `<DisableFastUpToDateCheck>true</…>` to
`Physalia.UI.csproj`, and splitting npm restore into a lockfile-incremental
`RestorePhysaliaUI` target using `npm ci` (the old `!Exists(node_modules)` guard never
reinstalled after a dependency bump).

**`Physalia.GH.csproj` ALSO needs `<DisableFastUpToDateCheck>true</…>` (added 2026-06-23).**
Forcing the UI project to build wasn't enough: the bin copy is `Physalia.GH`'s
`CopyLibraryFiles`, so when only UI source changed (no `.cs` edit) VS's FUTDC deemed GH
up-to-date and SKIPPED it → CopyLibraryFiles never ran → `bin/.../Files/UI/chat.html` lagged
ONE build behind (Rhino kept loading the old UI; you had to build twice). VS evaluates GH's
up-to-date status BEFORE the build, using the stale repo-root chat.html timestamp, while the
UI ProjectReference regenerates chat.html DURING the build — an inherent one-build lag the
`UpToDateCheckBuilt` pairing can't close. `dotnet build` CLI never had this (always runs the
Build target). Mirroring the UI flag on GH makes VS invoke MSBuild every build so
CopyLibraryFiles always stages the fresh chat.html; CoreCompile stays incremental (Csc still
skips when no `.cs` changed), so the only added cost is the MSBuild pass.
**How to apply:** if the chat window shows "UI not found" or stale UI, confirm chat.html
exists in `Files/UI/` and matches `bin/<Config>/Files/UI/` (sha256). A no-output wrapper
skipped by VS, OR `Physalia.GH` itself skipped by VS when only UI changed, is the usual cause.

**`npm run build` alone does NOT reach `bin`.** It only writes `src/Physalia.UI/dist/index.html`.
The propagation is MSBuild: the `BuildPhysaliaUI` target copies `dist/index.html` → repo
`Files/UI/chat.html`, and `Physalia.GH` (ProjectReference) stages that into
`bin/<Config>/Files/UI/`. So after any UI source edit, run `dotnet build src/Physalia.slnx -c Debug`
(or build in VS) — not just `npm run build` — or the change is stranded in `dist/` and the
chat window (loads `chat.html` via `file://`; reopen the window to pick it up) shows the old UI.
