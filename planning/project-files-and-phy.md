# Project files, .phy packages, harness names, the harness panel

> Split out of `CLAUDE.md` (2026-09-08) to keep that file under its size limit. Content is verbatim; CLAUDE.md links here.

## Project files, `.phy` packages and harness names (built 2026-09-05)

A harness now has a NAME, a folder of its own, and a file format that carries both. The three are one
change: the name decides the folder, and the format exists because the name had nowhere to live.

### Four-word names (`FourWordKey`, Core/Naming)
A harness is called `curious-cake-soap-fun` by default — four words from a 256-word list, indexed by
the first four bytes of its `InstanceGuid`. **Derived, never randomised**, exactly like the master
group's name: nothing is generated, serialized or kept in step; it survives save/load for free; a
pasted harness is renamed automatically because Grasshopper issues the copy a new id; and a preset
placed twice yields two names because `DocumentIds.MutateAll` re-issues every id. That last one is
the one that matters — two harnesses sharing a name would share a project folder and overwrite each
other's downloads.
- **The word list is lower-case `[a-z]` only**, so the name survives folder sanitizing untouched and
  what is on the canvas is what is on disk. Words are short, unambiguous aloud, and share nothing
  with Grasshopper's vocabulary (no curve/point/mesh/tree/list/panel/plane…).
- **`IsGeneratedShape` is how an auto name is told from a chosen one**, and comparing against a
  freshly derived name is NOT a substitute: a pasted harness carries the name of the id it was copied
  from and no longer matches its own, which is exactly the case that has to be caught.

### The project folder (`ProjectPaths` in Core, `ProjectFolder` in GH)
`PROJECT_FILES/<harness name>/` **in the user's data folder** — `%LOCALAPPDATA%/Physalia` on Windows.
It was `Files/PROJECT_FILES/` beside the plug-in until 2026-09-09; see "Where the user's files live"
at the end of this document for why it moved and what moving it cost. Four spellings on every `Project Folder` input, told apart by
shape: **blank** = the harness's own; **no separator** = a NAME under `PROJECT_FILES`; **a separator**
= relative to the SAVED `.gh` file's folder (`PhyDocuments.Host()`, since a sub-document has no path);
**rooted** = verbatim. An unsaved document cannot resolve a relative path and is TOLD so rather than
redirected — quietly falling back is how a user loses track of where files went. Only the name
spellings are sanitized; that is also the containment guard.
- **A rename MOVES the folder.** The key is derived from the current name and never frozen, and only
  `_lastFolderKey` (+ the guid that owns it) is serialized. That makes it self-healing: an undone
  rename moves the folder back, and a move blocked by an open file is retried later while the OLD key
  stays in force so the pipeline keeps reading the folder its files are actually in. Never moves onto
  an existing folder (that is another project), and the move runs on `RhinoApp.Idle` — never from the
  `NickName` setter, which fires during layout, paste and archive reads.
- **`_folderKey` is stored WITH its owning `InstanceGuid` and dropped on read when they differ.** A
  pasted harness would otherwise deserialize the original's key and move the ORIGINAL's downloads
  into the copy's folder. One rule covers paste and preset load.
- `ProjectFolderInput` is the single resolver every node calls (grounder, Download File, Read File,
  Read PDF), so the model cannot be told about one folder while a tool reads another.
- **`Files/PDFS` is DELETED** and `PdfLocations` is down to `ListPdfs`. PDFs are project material, so
  they live in `<project>/PDF`; Read PDF keeps an optional `Reference Folder` for the one thing a
  project folder cannot express — an office-wide spec library shared across every job.

### `.phy` (`PhyPackage`, `PhyManifest`, `ZipSafety`, Core/Packaging)
An ordinary zip: `manifest.json` + `harness.gh` + `files/`. **The inner document is byte-identical to
what `PresetLibrary` already wrote as a `.gh`**, so a `.phy` can be unzipped and the definition opened
by hand — a format nobody can get their work back out of is not one a firm should standardise on.
**Format is decided by CONTENT (`PK`), never by extension.**
- **Deleting Harness Notes FORCED this.** A preset is the archive of a harness's SUB-document and the
  harness component is not in it, so once the notes stopped being a component sitting inside the
  pipeline there was nowhere in a plain `.gh` for the harness's own metadata. `ReadDescription`'s
  archive-chunk spelunking (matching `HarnessNotes.TypeGuid`) is gone with it; a legacy `.gh` preset
  simply has no description, and none of the shipped ones ever carried a notes component.
- **This is the one place a version field earns its keep** — unlike `mcp-servers.json` and
  `api-endpoints.json`, which are written and read by the same machine. A package is written by one
  person's Physalia and read by another's. A future format is REFUSED, not guessed at.
- **The package carries knowledge, not bytes, for anything re-fetchable.** `downloads.json`
  (`DownloadLedger`, in the project folder) records url → file → size; `ProjectPayload.Plan` bundles
  everything EXCEPT what the ledger accounts for, so a 400MB LiDAR tile costs a package ~200 bytes
  while a hand-added survey — which nothing can re-fetch — is carried in full. The size is shown
  before writing, because that is what decides whether a workflow is something anyone will send.
- **Nothing per-machine goes in**: no credentials, provider activations, MCP servers or API endpoints.
  The API catalog a pipeline needs already rides on its node, inside the document.
- **`ZipSafety` is shared with the download extractor** and is the only extraction path. Entry names
  are checked by the RESOLVED path (a name climbs out by many routes; only where it lands matters),
  and bytes are counted AS THEY LAND rather than taken from the header, because a zip bomb lies about
  its size. `nameFor` selects and renames in one step but is still contained — mapping is not a way
  round the guard.
- Importing applies the manifest NAME first (it decides which folder the files go into),
  `UniqueName` suffixes a collision, and nothing already in the folder is deleted first.

### The harness panel (`HarnessPanel`, `HarnessPanelHost`)
A real WinForms window, replacing `HarnessReturnWidget`, `HarnessMenuWidget` and `HarnessPill` (all
deleted). A `GH_Widget` is painted in device pixels and has no input controls of any kind, which was
fine for two pills and impossible for three text fields. It shows only inside a harness, carries
Name / Description / Chat text / Save as preset / Save .phy / Load / Back, and rolls up to its title
bar (remembered in `Instances.Settings`).
- **It is an owned top-level `Form`, NOT a child of the canvas** (changed 2026-09-06, after typing
  went to the Rhino command line twice). **A child of `GH_Canvas` cannot reliably HOLD keyboard
  focus.** Rhino routes keystrokes to its prompt unless the focused window is a text control, and
  `GH_Canvas` derives from `Control`, not `ContainerControl` — verified against the shipped assembly
  — so it breaks the chain WinForms uses to restore focus into a child: the containing `Form` walks
  `ContainerControl`s, finds a plain `Control`, and puts focus back on the canvas at every
  re-activation. Calling `Focus()` explicitly does NOT fix it, because getting focus was never the
  problem; keeping it is. **Grasshopper's own in-canvas editor concedes the same point rather than
  disproving it** — `GH_TextBoxInputBase` focuses its `TextBox` outright and then **hides itself on
  `LostFocus`**, so it is transient by design and never holds focus through anything. The chat window
  has always accepted typing because it has always been its own window.
- The form is borderless, `ShowInTaskbar = false`, `AutoScaleMode.None` (set BEFORE the children, or
  WinForms rescales the manual layout), `ShowWithoutActivation` (it appears when the canvas enters a
  harness, which is nobody asking to type), and **owned by `Instances.DocumentEditor`** — the owner
  is what keeps it above Grasshopper, drops it behind another application, and hides it when the
  editor minimises, all of which `TopMost` would break. Owned lazily as well as at attach, since
  `WidgetListCreated` fires while the editor is still being built.
- **What a window costs is position** — a child gets it from its parent for free. `HarnessPanelHost`
  repositions on the canvas's `LocationChanged`/`SizeChanged`/`ParentChanged`, hides the panel when
  its canvas is not visible (another document's tab showing), and disposes it with the canvas.
- **The host window is resolved from `canvas.FindForm()`, LAZILY, and not from
  `Instances.DocumentEditor`** (fixed 2026-09-06: moving Grasshopper left the panel behind). Lazily,
  because `WidgetListCreated` fires while the editor is still being built — subscribing at attach
  time subscribed to nothing, so no `Move` was ever heard. From the canvas, because
  `Instances.DocumentEditor` is the right window only while Grasshopper FLOATS: docked, the canvas is
  hosted in a Rhino panel and it is Rhino's window that moves it. Re-checked on each show, so
  docking or undocking mid-session re-points the panel instead of leaving it tracking a window the
  canvas has left.
- **It also FOLLOWS that window's visibility, which is the only way it can hear Grasshopper being
  closed** (fixed 2026-09-07: the panel sat on screen over a Grasshopper that had gone). Two
  independent reasons the obvious hooks are dead. (1) **Grasshopper never closes** —
  `GH_DocumentEditor.DocumentEditorFormClosing` sets `e.Cancel = true` and calls `Hide()` for every
  `CloseReason` but a real teardown, which is why reopening it restores the same documents. (2) **A
  WinForms control is never told an ANCESTOR was hidden** — `SetVisibleCore` flips `States.Visible`
  before raising, and `OnVisibleChanged` forwards to a child only `if (control.Visible)`, whose
  getter walks the parent chain and so already reads false; children get the internal
  `OnParentBecameInvisible()`, which raises nothing. So `canvas.VisibleChanged` (which
  `HarnessPanelHost` had been relying on) structurally cannot fire, and only the form whose own
  state changed raises anything. Owning the panel to that form does not cover it either — Windows
  hides an owned window when the owner is MINIMISED, not when it is hidden. The panel is HIDDEN, not
  disposed, and returns through `HarnessPanelHost.Refresh` rather than a bare `Show()`: the canvas
  comes back intact but may be pointed at a different document, or none.
- **It opens COLLAPSED**, and **Back to document is the LAST row and stays visible in both states**.
  Expanded it is a few hundred pixels square permanently over a working canvas, while its three
  fields are edited about twice in a harness's life and the exit is wanted constantly — so rolled up
  is the default, and the exit can never be behind the toggle (`ApplyCollapsed` excludes it along
  with the toggle itself; hiding it would strand anyone who collapsed the panel). Bottom placement
  is what puts it directly under the title strip when rolled up. Collapsed it is 260x79 at 100%.
- **It uses the CHAT WINDOW's palette (`HarnessTheme.Panel`), not the canvas one.** The colours
  above it draw a capsule among other nodes, where a hard black edge and a saturated fill are what
  make a node read as a node; the panel is chrome with text fields in it, sits on screen beside the
  chat window, and looked like a different application in aqua. `HarnessTheme.Panel` is the
  `--neu-*` tokens from `app.css` converted to sRGB — keep the two in step, since there is no way
  to share values across that boundary. Text boxes are `BorderStyle.None` with a soft rounded well
  drawn in `OnPaint`, because `FixedSingle` takes the system window-frame colour and cannot be
  softened; the panel's own corners are a `Region`, so the canvas shows through them.
- Attached from `WidgetListCreated` — not because it is a widget, but because that is the one static
  hook firing once per canvas with the canvas in hand. Held in a `ConditionalWeakTable`.
- **Every size in it is MEASURED, never a pixel constant** (fixed 2026-09-05 off a screenshot). The
  first cut hard-coded row heights and a panel width, which is only right at 100% scaling: at any
  other DPI the font grows and the boxes do not, so labels lost their descenders, the title ran into
  the button below it, and the action button read "Save as .p". Three traps behind that. **A
  single-line `TextBox` IGNORES an assigned Height** — WinForms derives it from the font — so
  advancing a row by the number it was told drifts further down the panel with every row; ask
  `PreferredHeight` and advance by the real `Height`. **Splitting a button row in half clips the
  longer label** however wide the panel is, so both action buttons take the width of the wider one
  and the panel is sized to fit two of those. And the panel must re-measure on `OnHandleCreated`
  (`DeviceDpi` is 96 until the handle exists, so the constructor's numbers are provisional),
  `OnFontChanged` and `OnDpiChangedAfterParent` (dragging Rhino to a monitor at another scaling).
  Same lesson the harness capsule learned when its outlet labels stopped being three fixed letters.
- Clicking a label focuses the field it names, and Escape hands the keyboard back to the editor.
  Both were kept from the attempt to fix the focus bug in place; both are worth having anyway.
- **The name field needs the `NickName` override**, since `GH_DocumentObject`'s setter raises nothing
  (see the GH custom-attribute traps). Committed on Leave/Enter, never per keystroke — the name is a
  folder name and renaming a directory once per typed character is not a thing to do to a disk.
- The three fields serialize on `HarnessComponent`, so they ship inside a `.phy`. `ChatText` is pushed
  to the chat window and **REPLACES the empty-conversation greeting** ("Physalia chat / Send a message
  to start the conversation") — both lines, not just the subtitle: a pipeline shared across a firm
  should open with its author's instructions, and the generic invitation underneath them would be the
  window talking over the person who set it up. Whitespace is preserved, so an author can write more
  than one line. It is deliberately NOT the composer placeholder as well; that would say the same
  thing twice on an empty conversation, and the placeholder is where the host's wiring hints live
  ("Add an LLM Call with a Model…"), which must not be displaced by a welcome message.

The **chat window goes with Grasshopper too — HIDDEN on the way out, restored on the way back in**
(fixed 2026-09-07). It hooked `Instances.DocumentEditor.FormClosed`, which — per the harness panel's
point (1) above — fires only on `Instances.CloseGrasshopper()`/Rhino shutdown, and `RhinoApp.Closing`
already covered that; the X-click reached nothing at all.
- **The two halves listen to different things, and each is the only thing that works for its
  direction.** Going away keys on the **gesture** (`FormClosing`, which fires whether or not the
  close is cancelled) and NOT on the editor's visibility: docked into Rhino, the editor is hidden by
  switching to another panel tab, and putting the conversation away over that click — denying its
  pending cards with it — is not what it meant. Coming back keys on the editor's `VisibleChanged`,
  because a cancelled close is undone by the editor simply being SHOWN again; there is no other
  event. The restore is guarded on having done the hiding, so the far more frequent visibility
  changes summon nothing.
- **Hidden, not closed**, so the conversation, the loaded page and the window position all survive —
  the same bargain Grasshopper strikes with the documents it was holding. `Visible = false` maps to
  WPF's `Window.Hide()` through `Eto.Wpf.Forms.WpfWindow`, so the HWND survives and the Win32
  ownership set by `OwnToGrasshopperEditor` still holds when it returns; `Show()` on an
  already-loaded Eto form is just `Visible = true`, so nothing is re-loaded.
- **`ChatWindow.CanAskUser` is what keeps hiding honest, and without it this change would have
  quietly broken every fail-closed gate.** `ToolApprovalBroker` and `HumanQuestionBroker` refuse
  immediately when there is nowhere to ask, and both keyed that on `Chat.ActiveWindow is null` — but
  a hidden window is still an open window, so a card would have been posted to a surface nobody
  could see and the model would have waited out the full five or ten minutes. Both now ask
  `is not { CanAskUser: true }`, `TryShowFetch` refuses (so a blocked download falls back to the
  standalone `BrowserFetchWindow`, which has no timeout to save it), and whatever was already
  pending is denied/abandoned at hide time exactly as a real close would have done it.

### Tool approval (`IToolApprover`, `ToolApprovalBroker`, `ApprovalCard.svelte`)
One seam, not a dialog per tool: downloading, unpacking and (later) running a script all want the same
question asked. **The question is a card in the chat window**, not a Rhino message box — an approval is
part of a turn (the model asked; the person is being asked whether it may have it), so it belongs where
the conversation is; and a modal dialog is a window, which can end up behind Rhino, on another monitor,
or over a canvas nobody was looking at.
- **Every edge denies.** No chat window open denies IMMEDIATELY rather than waiting out the timeout —
  there is nowhere to ask, and making the user wait five minutes to be told no is worse than telling
  them now. The window closing mid-wait denies (`Chat`'s Closed handler calls `DenyAll`), the round
  being cancelled denies, the timeout denies. Guessing "allow" does the thing nobody agreed to;
  guessing "deny" produces a tool result the model can react to, and only one is recoverable.
- **Five minutes**, the MCP sign-in's reasoning: a consent decision runs at human speed. Affordable
  only because an approval-gated tool sets `RunsAsync`, so no solution waits behind the card.
- `ToolApprovalBroker` is static (there is one window, and a call can be running against any harness),
  keyed by request id so two nodes asking at once queue rather than overwrite. Its `Changed` event
  pushes the card the moment the model asks; the window's 0.15 s tick is the safety net.
- The card renders **above the composer and OUTSIDE the `staticSurface` guard** — a tool can ask while
  the window is on Home or a setup page, and a question the user cannot see is a round that stalls.
  Only the OLDEST of a queue is shown ("2 more waiting"): stacking consent prompts is how people learn
  to clear them unread. The detail is shown verbatim, wrapping and selectable — the URL and the
  destination ARE the decision. An answered card disables its buttons, since the round is waiting and a
  live button invites a second click.
- Answers travel back as `phbridge://approve?id=…&allow=1|0`; anything but `allow=1` is a No on the
  host side too, so a lost or truncated navigation denies rather than permits.



---

### The library's fourth folder — `AI`, and the Experimental section (built 2026-09-09)

The 30 model-written harnesses sitting in `Files/PRESETS/Physalia/AI/` appeared nowhere: `Enumerate`
lists files directly inside a library folder and does not recurse, so a folder nested inside one is
invisible. `AI` is now a fourth entry in `PresetLibrary.Folders`, listed LAST, and
`DirectoryFor("AI")` resolves it to `Physalia/AI` — inside the shipped folder rather than beside it.

- **It is shipped, so it goes with the shipped root**, replaced wholesale by a package update like
  everything else in `Files/`. `IsShipped` is now the one predicate deciding both which root a folder
  resolves under and whether we create it, replacing two hand-written comparisons against
  `PhysaliaFolder`.
- **The nesting is load-bearing in the other direction too.** Because `Enumerate` does not recurse, a
  folder dropped inside a library folder is hidden until someone adds it to `Folders` — which is
  exactly the property that let 30 presets sit in the tree, committed and shipped, without being
  advertised. Keep it: it is the cheapest possible staging area.
- The wire value is `AI/<file>`, and `Resolve` still MATCHES against the enumerated library rather
  than composing the string into a path, so nothing about the extra level widens what a hostile wire
  value can reach. Nothing else in the codebase branches on a preset's folder.

The chat window does not put them in the gallery. They sit behind one pink **Experimental** button at
the bottom of the preset page, carrying a warning that everything inside is AI generated pending
human-written replacements, and while it is shut **those rows are not on the page at all** — a
warning you can scroll past is not a warning, and an opt-in that renders its contents anyway is not
an opt-in. It re-shuts on every visit. The pink is the existing `--neu-feedback` hue the auto-
generated feedback turns wear, at real chroma rather than a 12.5% tint, because here the colour is a
label to be read rather than a background wash.

Cover is `tools/uitest/test_preset_experimental.py`, which asserts the absent rows, the warning
verbatim, and the button's colour as PAINTED — `getComputedStyle` returns `lab()` for these oklch
tokens, so a regex over `rgb()` reports every colour as null and looks exactly like the
class-never-compiled bug it is there to catch.

## Where the user's files live (moved 2026-09-09)

**The problem, in one sentence: Rhino 8 installs each version of a package in a directory of its own
and updates installed packages silently at startup, so anything Physalia wrote beside its own
assembly was one update away from being stranded** — present on disk, in a folder nothing reads any
more, indistinguishable from data loss. The developer loop had the same bug and it had been biting
for months without being named: `CopyLibraryFiles` does `RemoveDir $(TargetDir)Files` before staging,
so every rebuild deleted whatever the last run had written into `bin`.

### The split
`Physalia.Core/Config/PhyData` is **the one place the user-or-package decision is made**. Nothing may
compose `Files/<X>` beside the assembly again; the five resolvers that each did (`ProjectFolder`,
`MemoryLocations`, `PresetLibrary`, `SystemPrompt`, `ClusterCatalogProvider`) all ask it now.

- **User-written → `%LOCALAPPDATA%/Physalia/`** (`~/.local/share/Physalia` elsewhere, the same root
  the credential store already used): `PROJECT_FILES`, `MEMORIES`, `PRESETS/User`,
  `PRESETS/Community`, plus `install.json`.
- **Shipped → the package's `Files/`**, read-only in practice: `SYSTEM_PROMPTS`, `CLUSTERS`,
  `PRESETS/Physalia`, `CHANGELOG.md`.

### Why an OVERLAY and not a copy-on-first-run
Where both roots hold the same folder, the user's is searched first and a file of the same name
shadows the shipped one (`PhyData.SearchPath`). The alternative — copy the shipped set into the data
folder on first run and read only from there — forces a choice with no right answer: refresh a
shipped preamble on update and you destroy the user's edit to it; never refresh and your own fix
never reaches them. The overlay has no such fork, and it costs one name-resolution order plus a union
listing in the two readers that enumerate. `ClusterCatalogProvider` merges the two `clusters.json`
manifests in REVERSE precedence order so the user's description wins per file name, and sorts the
merged catalog by name so which root a cluster came from is invisible downstream.

### `DataMigration` — the rules, each of which exists to avoid destroying something
- **Per ENTRY, not per folder.** A harness's project folder, one memory folder, one saved preset.
  A destination that already exists blocks that entry alone.
- **Never merge, never overwrite.** A blocked entry is reported and left exactly where it is.
- **Nothing is ever deleted**, even after everything around it moved: the package directory is not
  ours to tidy and the next update replaces it anyway.
- **Sibling package-version directories are scanned too**, newest first. This is the case that
  rescues somebody who updated BEFORE the migration shipped — their data is one directory over
  (`…/packages/8.0/Physalia/<other version>/Files`), somewhere the running install has never looked.
- **Claimed destinations are tracked while PLANNING**, not just read off disk: two legacy roots can
  hold the same harness's folder, and the second would otherwise be planned as a move onto a
  destination the first step has not created yet.
- **A failed entry does not stop the others**, and a cross-volume move falls back to copy-then-delete
  with the copy verified first — the plug-in installs on Rhino's drive while `%LOCALAPPDATA%` follows
  the user profile, and on plenty of workstations those are different drives.
- **`.gitkeep` and `README.md` are not user data.** They are copied into every build, so migrating
  one would move a file the next update puts back, and then report it as blocked forever.
- **Silence is the steady state.** `DataMigrationReport.Any` is false when only blocked entries were
  found, because a package that ships a demo project folder blocks it on every update, and saying so
  each time trains the user to ignore the line that matters.
- It runs from `PhyStartup`, **its own `GH_AssemblyPriority`** — deliberately not a line in
  `ChatWidgetPriority`, which is compiled `#if WINDOWS` and would have skipped a Mac user's memories.

### Telling the user an update happened (`InstallStamp`, `ReleaseNotes`, `UpdateNotice.svelte`)
`install.json` records the full four-part version that last ran here. Only ONE of the four cases is
news: **no stamp** is a first install (a "you have been updated" dialog at somebody's first launch is
simply a lie), **a higher stamp** is a downgrade or two Rhinos sharing the data folder, **an equal
one** is an ordinary restart, and **a lower one** is the update that notifies.

**The notice is cleared by ACKNOWLEDGEMENT, not by delivery.** Rhino often starts with no chat window
open, so a stamp written the moment the notice was computed would swallow the one notice the user was
owed. It waits in `PhyStartup.PendingNotice` for whichever window opens first; the dialog's dismissal
comes back as `phbridge://update-seen`, and `again=0` is the separate opt-out — a different decision
from "I have read this one", so a different message. Only then is `AcknowledgedVersion` written.

The dialog carries the release's own section of `Files/CHANGELOG.md` when it has one, because two
version numbers say THAT something changed and nothing about what. It is optional by design (a
changelog nobody wrote must not cost the user the notice) but a test compares the csproj `<Version>`
against the file, so a bump that forgets the section fails at the bump rather than a release later.

### What is still worth doing
- There is **no yak manifest in the repo yet**, and packaging must carry the `runtimes/**/native/`
  SkiaSharp and PDFtoImage binaries — the `.gha` stopped being self-contained on 2026-08-25, and a
  package missing them fails only inside Rhino, with a `DllNotFoundException` from a build that is
  healthy on the command line.
