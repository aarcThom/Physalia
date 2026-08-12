---
name: physalia-repo-gotchas
description: "Stale paths in CLAUDE.md (slnx in src/, primitives doc in planning/); the Files-folder build pipeline and its two MSBuild gotchas (stray src/Physalia.GH/Files, VS fast-up-to-date-check lag)"
metadata: 
  node_type: memory
  type: project
  originSessionId: bfb2ff92-c812-4e27-89de-9675e357c091
  modified: 2026-08-12T05:45:19.596Z
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
`$(TargetDir)Files` (= `bin/<Config>/<TFM>/Files`). `Files` is for USER-ALTERABLE content only
(presets, system prompts, schemas, API-key config) — it deliberately holds NO compiled plugin
components. The chat UI bundle does NOT ride this copy: as of 2026-06-29 it is embedded directly
into the `Physalia.GH` assembly (see chat-UI section below), so there is no `Files/UI/` folder.
There is also NO `src/Physalia.GH/Files` folder; it was a committed duplicate, removed from git
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

**Rhino holding the `.gha` fails the BUILD, not the COMPILE (seen 2026-08-11).** With Rhino 8 (or
VS) running with the plug-in loaded, `dotnet build` ends in
`error MSB3027/MSB3021: Could not copy obj\...\Physalia.GH.gha to bin\... locked by "Rhino 8"`.
The compile already SUCCEEDED — only the obj→bin copy failed. Don't read those two errors as a
broken change: filter for real diagnostics with
`dotnet build src\Physalia.GH\Physalia.GH.csproj -c Debug -f net7.0-windows -p:BuildUI=false -p:RepackGha=false 2>&1 | Select-String "error CS|warning CS|warning SA"`
and treat an empty result as clean. Close Rhino when the output in `bin` is actually needed.
- **Do NOT reach for `-t:CoreCompile` to dodge the copy.** On this project it detaches from
  reference resolution and fails with `CS0400: The type or namespace name 'System' could not be
  found` — a phantom error, nothing to do with the code. (It also needs an explicit `-f
  net7.0-windows`, since `TargetFrameworks` plural means the outer dispatch build has no
  `CoreCompile` target at all: `MSB4057`.)

**Chat UI bundle is EMBEDDED in the assembly (changed 2026-06-29).** `src/Physalia.UI` is a
no-assembly MSBuild wrapper around Vite; `BuildPhysaliaUI` runs `npm run build` → one
self-contained `src/Physalia.UI/dist/index.html` (~3.3 MB, gitignored). `Physalia.GH.csproj`'s
`EmbedChatHtml` target then embeds that file directly as the `Physalia.GH.chat.html` manifest
resource. At runtime `ChatWindow.TryExtractChatHtml` writes it to
`%TEMP%/Physalia/chat-<asmVersion>.html` (reused when present + same size) and `LoadUi` loads
it via `file://`. The bundle no longer touches `Files/` or `CopyLibraryFiles` at all.
- `EmbedChatHtml` hooks `BeforeTargets="SplitResourcesByCulture"`, NOT `CoreCompile`: it must
  add the `<EmbeddedResource>` BEFORE resource prep finalizes the list (resource prep is a
  CoreCompile dependency, so a `CoreCompile` hook is too late — the resource silently won't
  embed and the `.gha` stays ~0.5 MB instead of ~3.9 MB). The `Exists(dist/index.html)`
  condition is on the TARGET (not a static item) so the check is deferred until after the UI
  ProjectReference has built; a static `<EmbeddedResource Condition="Exists()">` evaluates at
  project-evaluation time (before UI builds on a clean build) and embeds nothing.
- `-p:BuildUI=false`: dist absent → resource not embedded → `GetManifestResourceStream` null →
  `ChatWindow` shows the "UI not found" page. Behavior preserved.

Visual Studio's fast up-to-date check (FUTDC) skips projects with no managed output, so in VS
the `BuildPhysaliaUI` target never ran and dist was never regenerated — while `dotnet build`
(no FUTDC) worked fine. Fixed 2026-06-22 by adding `<DisableFastUpToDateCheck>true</…>` to
`Physalia.UI.csproj`, and splitting npm restore into a lockfile-incremental `RestorePhysaliaUI`
target using `npm ci` (the old `!Exists(node_modules)` guard never reinstalled after a
dependency bump).

**`Physalia.GH.csproj` ALSO needs `<DisableFastUpToDateCheck>true</…>` (added 2026-06-23, still
required).** Forcing the UI project to build isn't enough: when only UI source changes (no `.cs`
edit) VS's FUTDC can deem GH up-to-date and SKIP it → CoreCompile never re-embeds the fresh
dist → Rhino loads stale UI. (The dynamically-added EmbeddedResource isn't in VS's static FUTDC
inputs, so VS can't see that dist changed.) Mirroring the flag makes VS invoke MSBuild every
build; CoreCompile then sees dist/index.html (a CoreCompile input via `@(EmbeddedResource)`) is
newer than the `.gha` and recompiles. `dotnet build` CLI never had this — verified 2026-06-29
that touching dist alone triggers a GH recompile + re-embed; CoreCompile stays incremental
otherwise (skips when neither `.cs` nor dist changed).
**How to apply:** if the chat window shows "UI not found" or stale UI, run
`dotnet build src/Physalia.slnx -c Debug` and confirm the `.gha` is ~3.9 MB and its manifest
includes `Physalia.GH.chat.html` (`[Reflection.Assembly]::LoadFile(...).GetManifestResourceNames()`).

**`npm run build` alone does NOT reach the assembly.** It only writes
`src/Physalia.UI/dist/index.html`. The propagation is MSBuild: `Physalia.GH` (ProjectReference)
embeds `dist/index.html` via `EmbedChatHtml`. So after any UI source edit, run
`dotnet build src/Physalia.slnx -c Debug` (or build in VS) — not just `npm run build` — or the
change is stranded in `dist/` and the chat window (reopen it to pick up a new build) shows the
old UI.
