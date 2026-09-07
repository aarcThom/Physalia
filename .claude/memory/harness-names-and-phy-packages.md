---
name: harness-names-and-phy-packages
description: "2026-09-05 — harnesses get derived four-word names, a project folder that follows a rename, and a .phy package format; Harness Notes deleted"
metadata: 
  node_type: memory
  type: project
  originSessionId: 41930f90-4aec-4ebd-8c9e-359924617843
  modified: 2026-09-06T05:31:34.125Z
---

Built 2026-09-05. Harnesses are named `curious-cake-soap-fun` by default, own a folder under
`Files/PROJECT_FILES/<name>/`, and save as `.phy` (a zip: `manifest.json` + `harness.gh` + `files/`).
`HarnessNotes`, `HarnessReturnWidget`, `HarnessMenuWidget`, `HarnessPill` and `Files/PDFS` are all
DELETED. Full detail in CLAUDE.md, "Project files, `.phy` packages and harness names".

**Why:** the three changes are one change and could not be done separately. A preset is the archive
of a harness's SUB-document, and the harness component is not in it — so the moment the notes stopped
being a component sitting inside the pipeline, harness-level metadata had nowhere in a plain `.gh` to
live. That is what forced the package format, not a wish for one. And the name is what decides the
project folder, so it had to be settled first.

**How to apply:**
- Names are DERIVED from `InstanceGuid`, never randomised — that is what makes a pasted harness and a
  twice-placed preset rename themselves for free (`DocumentIds.MutateAll` re-issues ids). Use
  `FourWordKey.IsGeneratedShape` to tell an auto name from a chosen one; comparing against a freshly
  derived name is wrong precisely for the pasted case.
- A rename MOVES the project folder. `_folderKey` is serialized WITH the guid that owns it and dropped
  on read when they differ — without that, a pasted harness moves the ORIGINAL's downloads into its
  own folder. That was the one data-loss bug in the design.
- A failed move (file open, scanner) keeps the OLD key in force and retries; never assume the move
  happened. It runs on `RhinoApp.Idle`, never from the `NickName` setter.
- A `.phy` records re-fetchable downloads (`downloads.json` / `DownloadLedger`) instead of carrying
  them, so a 400MB tile costs ~200 bytes. Hand-added files are carried in full — nothing can re-fetch
  those.
- **Save .phy… (added 2026-09-07) inverts that one choice and nothing else.** Same package, same
  writer, destination picked in a file dialog — but `carryDownloads: true`, so the project folder goes
  whole. The split is right for a preset (placed on the machine that wrote it, where a re-fetch is a
  thing the pipeline does) and wrong for a file somebody sends somewhere. `ProjectPayloadPlan.Downloads`
  keeps meaning "what is NOT in the package" either way, which is what stops the import message telling
  the user to download files sitting in their own folder — and it is why the two modes share one code
  path instead of one having its own manifest rule. The destination is excluded from its own payload
  BEFORE the size is quoted, or a second save into the project folder carries the first package inside
  it and the file doubles every time.
- Format is decided by content (`PK`), never extension. Legacy `.gh` presets still load.
- The harness panel is its own owned top-level WinForms `Form` (it was parented to `GH_Canvas` until
  2026-09-06), not a `GH_Widget` — widgets have no input controls. Its name field needs the `NickName`
  override, per [[gh-custom-attribute-traps]]. Every size in it is MEASURED: adding the third action
  button meant re-deciding the row split (the two save verbs share a row at the width of the wider,
  Load… went full-width below), because an even split clips the longer label however wide the panel is.
- `Rhino.UI.SaveFileDialog` exposes **no `OverwritePrompt`** (checked by reflection against the shipped
  RhinoCommon) and its base is internal, so whether it warns is unknowable from outside. Use
  `System.Windows.Forms.SaveFileDialog` wherever a silent overwrite would cost the user a file —
  `Serializer.PromptForSavePath` was already the precedent.

Not run in Rhino. Related: [[harness-subdocument]], [[settings-ownership]], [[project-file-tools]].
