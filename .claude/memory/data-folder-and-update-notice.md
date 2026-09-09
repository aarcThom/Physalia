---
name: data-folder-and-update-notice
description: "Why the user's files moved to %LOCALAPPDATA%/Physalia, the overlay-not-copy rule for shipped content, and how a silent Rhino package update gets reported."
metadata: 
  node_type: memory
  type: project
  originSessionId: 5088ff96-f08c-4737-bf6d-d57d55713826
  modified: 2026-09-09T07:46:39.521Z
---

2026-09-09, built, headless-verified and committed (7 commits on `main`), **not yet run in Rhino**. Preparing Physalia for Rhino's
package manager forced this: **Rhino 8 updates installed plug-ins silently at startup and installs
each version in a directory of its own**, so everything Physalia wrote beside its assembly was one
update away from being stranded. Confirmed against McNeel's docs and forum: auto-update is on by
default, there is NO notification, and the only opt-out is
`Options → Advanced → Rhino.Options.PackageManager.CheckForUpdates` (which IT can force org-wide).

**The dev loop had the same bug the whole time and nobody had named it:** `CopyLibraryFiles` does
`RemoveDir $(TargetDir)Files` before staging, so every rebuild deleted whatever the last run wrote
into `bin` — memories, project folders, saved presets.

## What to remember
- **`Physalia.Core/Config/PhyData` is the ONE place the user-or-package decision is made.** Never
  compose `Files/<X>` beside the assembly again; five resolvers used to (`ProjectFolder`,
  `MemoryLocations`, `PresetLibrary`, `SystemPrompt`, `ClusterCatalogProvider`).
- **Overlay, not copy-on-first-run** — the design fork worth knowing. Where both roots hold a folder,
  the user's is searched FIRST and shadows the shipped file of the same name. Seeding the data folder
  instead forces a choice with no right answer: refresh a shipped preamble and you destroy the user's
  edit; never refresh and your own fix never reaches them.
- **Notice copy is short and positive on purpose** (asked for, second pass): title "Physalia
  updated!", one sentence with the version pair, then the ONE reassurance worth giving — where the
  user's own work is kept, with the data-folder path pushed from the host so it is named rather than
  described. The "Rhino's Package Manager does this on its own" explanation was cut; the changelog
  section already says what changed. The rig asserts the paragraph stays under 200 characters.
- **The notice is cleared by ACKNOWLEDGEMENT, not delivery.** Rhino often starts with no chat window
  open, so stamping when the notice is computed swallows the one notice owed. It waits in
  `PhyStartup.PendingNotice`; the dialog answers over `phbridge://update-seen`, `again=0` being the
  separate opt-out.
- **No stamp means a FIRST INSTALL, not an update** — and a higher stamp means a downgrade or two
  Rhinos sharing the data folder. Only a lower one notifies. Getting this wrong shows every new user
  a "you have been updated" dialog on their first launch.
- **`PhyStartup` is its own `GH_AssemblyPriority`** because `ChatWidgetPriority` is `#if WINDOWS` —
  a line added there would have skipped a Mac user's memories entirely.
- **The version number is load-bearing three times** (`Physalia.GH.csproj` `<Version>`, now `1.0.0`
  with the template's `Description of Physalia.GH` finally replaced): auto-update compares it,
  `Physalia_GHInfo` reports it, and a change between runs raises the notice. **A pre-release tag
  (`1.0.0-rc1`) hides the package behind "Include pre-releases" AND opts every user out of automatic
  updates.** A test compares the csproj version against `Files/CHANGELOG.md`, so a bump that forgets
  the section fails at the bump.
- Migration rules that each prevent a specific loss: per ENTRY not per folder, never merge, never
  delete, scan SIBLING version directories (that is what rescues anyone who updated before this
  shipped), track claimed destinations while PLANNING (two roots can hold the same harness's folder),
  cross-volume copy-then-delete with the copy verified first (the plug-in is on Rhino's drive while
  `%LOCALAPPDATA%` follows the profile). **Blocked-only is SILENT** — a shipped demo project folder
  blocks on every update forever, and saying so each time trains the user to ignore the real line.

## Still to do
- **Verify in Rhino.** Nothing here has run in Rhino: stamp the version down by hand and check the
  dialog appears once, put a memory/preset/project folder in the package `Files/` and check they
  arrive, then rebuild and confirm nothing is destroyed any more.
- **`Files/PRESETS/Physalia/AI/` is one level deeper than `PresetLibrary.Enumerate` looks**, so the
  30 presets sitting there do not appear in the gallery at all. Pre-existing, untouched — nested
  listing changes the wire value and `Resolve`, so it is its own job.
- **No yak manifest in the repo yet**, and packaging must carry `runtimes/**/native/` (SkiaSharp,
  PDFtoImage) or PDF rendering fails in Rhino only. See [[pdf-natives-verified]].

Detail: `planning/project-files-and-phy.md` ("Where the user's files live"). Rig:
`tools/uitest/test_update_notice.py`, 15 checks. Related: [[harness-names-and-phy-packages]],
[[model-api-credentials]], [[preset-conventions]], [[headless-chat-ui-testing]].
