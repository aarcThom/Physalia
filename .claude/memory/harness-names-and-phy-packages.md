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
- Format is decided by content (`PK`), never extension. Legacy `.gh` presets still load.
- The harness panel is a WinForms control PARENTED to `GH_Canvas`, not a `GH_Widget` — widgets have no
  input controls. Its name field needs the `NickName` override, per [[gh-custom-attribute-traps]].

Not run in Rhino. Related: [[harness-subdocument]], [[settings-ownership]], [[project-file-tools]].
