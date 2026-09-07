# Pre-ship testing — the harness passes

**Status:** written 2026-09-06, for the `events-and-delegation` branch and everything before it.
**Last pass:** 2026-09-07 on `final-pass` — A1–A6, B0–B3, B7 (page half), C1, C2 done; two defects
found and fixed (see *What the 09-06/07 pass found*). B4 onwards, D, E and F still open.
**Scope:** the last full pass before a release. Every rig here is driven from a harness on a real
canvas in a real Rhino, because that is the only place several of these features exist at all.

This document exists because a green build and a passing Core suite have now twice said nothing
useful about whether a feature works. `Physalia.Core` has 964 tests and they are worth having, but
every defect found in the last two days was found by *running it*: a shared Rhino buffer that ate
another plug-in's stdout, a caption that never refreshed, a wire that Grasshopper refused outright, a
component that silently solved twenty times. None of those was reachable from a unit test.

---

## 0. How to use this

Work the passes **in order**. A is free and deterministic, F costs money and needs a person watching.
Failing early in A means B–F are wasted effort.

Each rig states **BUILD**, **RUN**, **EXPECT** and **FAILS AS** — the last of those is the point. A
test whose failure signature you cannot recognise is a test you will mark as passing.

Mark each rig in the sign-off table at the end. "Partially" is a legitimate answer and better than a
tick you cannot defend.

### Ground rules — read before starting

| Rule | Why |
|---|---|
| **Restart Rhino to deploy a build.** Rhino loads the `.gha` from `bin` and holds a lock, so the obj→bin copy fails while it is open. The COMPILE still succeeds — do not read `MSB3021`/`MSB3027` as a compile failure. | `physalia-repo-gotchas` |
| **Close the Rhino MCP server for any test involving Watch Modelling's transcript.** It reads its own stdout from `RhinoApp`'s shared command-window buffer, and two consumers of one destructive global means a partial transcript at best. | §B4 |
| **Arming NEVER persists.** Every trigger reopens `off`. If a trigger appears armed after a file load, that is a defect, not a convenience. | §B0 |
| **A rig does not need a harness unless it needs a Chat, a Conversation Log, or delegation.** Pass A runs on a bare canvas with Construct Signal and Deconstruct Signal: deterministic, instant, free. | §A |
| **If you script a rig, make the persistent-data helper wrap unconditionally.** `SetPersistentData` APPENDS to the registered default, and a bare string resolves to `IEnumerable<char>` — one item per character, and the component solves once per character. Both traps have landed more than once. | `building-harnesses-programmatically` |
| **Write scripted-probe progress to a file.** stdout dies with the process, and Rhino has crashed mid-call at least once without reproducing. | ibid |

### What is NOT a test, and must be fixed regardless

- **Icons — DONE 2026-09-07.** All 29 are generated, split and installed; the audit now reports
  **0** components falling back. Four sheets, first generation, no rework — see the third pass in
  `planning/component-icon-prompts.md`, including why the apparent line-weight drift was measured
  and found not to exist.

  The original count was **29**, taken on 2026-09-06 by matching every type declaring
  `override Guid ComponentGuid` against the `.gha`'s embedded `Physalia.GH.Resources.<TypeName>.png`
  names — the shipped artifact's count rather than a tally of what felt new, which is what caught
  that the figure had been carried as 18. The `Param_*` types are exempt throughout: `PhyParam` sets
  `GH_Exposure.hidden`, so they never reach the ribbon. **Re-run that audit after any pass** — it is
  four lines of script and it is the only thing that answers the question.
- **Mac.** Not testable on this machine. `McpServer.BridgeExecutable()` still hardcodes a `.exe`
  (memory: `mac-port-mcp-gaps`), and the new WinForms surfaces are Windows-only. Decide whether the
  release is Windows-only and say so, or schedule the port.

---

## Pass A — bare canvas, no model, no harness

Free, instant, repeatable. **All of A is already verified once** (2026-09-06); re-run it after any
change to `SignalRelayBase`, `StatefulComponentBase` or `PhySignal`, because it is the cheapest
regression net in the project.

### A1. The forward-the-original contract

> This is the single most important test in the document. If it fails, a gate placed before an LLM
> Call silently strips the conversation, and nothing downstream reports a problem.

- **BUILD** — `Construct Signal` → `Signal Gate` → `Signal Switch` → `Deconstruct Signal` ("AT END").
  A second `Deconstruct Signal` ("AT SOURCE") straight off Construct Signal. Payload a recognisable
  sentence; Gate `Open` true; Switch `Pattern` a substring of the payload.
- **RUN** — press the Button once.
- **EXPECT** — the **same `Sequence` number** at AT SOURCE and AT END, the same payload, and both
  signals reporting `from Construct Signal`.
- **FAILS AS** — a different sequence at the end, or a source reading `from Signal Gate`. That means a
  relay is re-minting, and `Instructions` are being dropped on the hop that carries them.

### A2. Gate and Switch routing

- **RUN** — set `Open` false, press. Then `Open` true with a `Pattern` that cannot match, press.
- **EXPECT** — `Blocked` receives the new signal while `Passed` still holds the older one (latched
  signals persist; consume-once is what stops a re-fire). Then `No Match` receives it. Captions
  `shut · 1/2`, `text · 1/2`.
- **FAILS AS** — `Passed` cleared when a signal is blocked, or both outputs carrying the same signal.

### A3. Hold Signal, and its self-poll

- **BUILD** — `Construct Signal` → `Hold Signal` (`Release` false, `Timeout` 0, `Recheck` 0) → two
  `Deconstruct Signal`s off `Released` and `Timed Out`.
- **RUN** — (a) press; (b) set `Release` true; (c) set `Release` false, `Timeout` 2, `Recheck` 0.5,
  press, wait 3s.
- **EXPECT** — (a) caption `waiting Ns`, nothing out. (b) `Released` gets it, **sequence unchanged**.
  (c) `Timed Out` gets it.
- **FAILS AS** — (c) never firing. That means `Recheck` is not re-solving, and a timeout can only ever
  be noticed by an unrelated solve — i.e. never, in the case the feature is for.
- **ALSO TEST** — a `Release` fed by something outside the data graph (a script that reads a file).
  With `Recheck` 0 it must never release; with `Recheck` 1 it must. That is the whole reason `Recheck`
  expires its own sources.

### A4. Throttle, by payload identity

- **BUILD** — `Construct Signal` → `Signal Throttle` (`Interval` 3) → `Deconstruct Signal`.
- **RUN** — three presses inside one second, with payloads `FIRST`, `MIDDLE`, `NEWEST`.
- **EXPECT** — `FIRST` immediately; then `NEWEST` when the window elapses; **`MIDDLE` never appears**.
  Caption `2 through, 1 overtaken`.
- **FAILS AS** — `MIDDLE` arriving (queueing instead of superseding), or `NEWEST` never arriving
  (the poll not re-arming). Checking sequence numbers alone will NOT catch a payload mix-up — assert
  on the payload.

### A5. For Each

- **BUILD** — `Construct Signal` → `For Each.Start`; `Items` = three named strings;
  `Item Signal` → `Feedback` ⤳(grip)⤳ `Feedback Collector` → `For Each.Next`; `Done Signal` →
  `Deconstruct Signal`.
- **RUN** — press once and let the scheduled solutions run (do not force solves — the point is that
  it advances on its own).
- **EXPECT** — caption `1 / 3` immediately, then `done · 3`; `Index` 3; `Item` empty;
  `All 3 items are through.`
- **FAILS AS** — stalling at `1 / 3` (the wireless self-feed is not reaching `Next`); or Grasshopper
  refusing the graph at build time, which means someone wired `Item Signal` straight into `Next` —
  that is a self-dependency and it is *supposed* to be refused.
- **ALSO TEST** — an **empty** `Items` list must fire `Done` immediately, not stall. And pressing
  `Start` mid-loop must restart from the top.

### A6. Pipeline State, graph side

- **BUILD** — `Pipeline State` with `Key` = `stage`, its `Value` output → a text equality → a
  `Signal Gate.Open`; `Construct Signal` → that Gate.
- **RUN** — set the board from a script (`StateStore.Set`) or via the tool in Pass F; press.
- **EXPECT** — the Gate opens only when `stage` matches. `Keys`/`Values` list in last-set order.
- **ALSO TEST** — 65 keys (the 65th must be **refused**, not evict one); a 9KB value (truncated to
  8KB); `clear` with no key emptying the board.

---

## Pass B — triggers

Each trigger needs arming, and several need waiting. **None of B has been run except Watch
Modelling.** Do B inside a harness, not on a bare canvas — the harness case is where the hard bug
lives (B0).

### B0. The wake-up that gets dropped — do this FIRST

> `PipelineWake` exists for exactly this and it has never been exercised. If it is broken, every
> trigger looks armed and does nothing, in silence, and only inside a harness.

- **BUILD** — a harness containing `Timer` (Interval 10) → `Conversation Log.Prompt Signal`, plus the
  rest of a minimal pipeline. A `Panel` on a `Deconstruct Signal` off the Timer so firing is visible
  from outside.
- **RUN** — arm the Timer, then **leave the harness** (Back to document) and touch nothing on the
  host canvas for a full minute.
- **EXPECT** — the Timer keeps firing; the panel keeps updating.
- **FAILS AS** — firing stops the moment you leave the harness, or never starts. That is a disabled
  sub-document swallowing the scheduled solution. Check `PipelineWake.Ready` is being called and that
  it is re-enabling a *harness* document only.
- **ALSO TEST** — the same rig with the harness proxy **on a locked/disabled host document**. The
  solver lock on the user's own file must NOT be overridden.

### B1. Timer

- **RUN** — arm at Interval 5. Then: set Interval 0.2; disarm; save and reopen the file.
- **EXPECT** — fires every ~5s and **not on arming** (arming is a switch, not a run button); a warning
  and a clamp to 1s at 0.2; caption `every 5s · N`; disarm stops it immediately; **reopens `off`**.
- **FAILS AS** — a fire the instant you tick Armed; or armed after a reload, which is the safety
  property that matters most on this whole branch.

### B2. Folder Watcher

- **BUILD** — inside a harness with a `Project Folder` grounder, so the folder resolves the same way
  the model is told about it. `Changed Files` → a `Panel`.
- **RUN** — arm, then from Explorer: (a) drop one small file in; (b) copy a **large** file (500MB+) in;
  (c) delete a file; (d) create a subfolder and drop a file in it, with `Subfolders` off then on;
  (e) create then immediately delete a temp file.
- **EXPECT** — (a) one signal naming the file, absolute path on `Changed Files`. (b) **exactly ONE
  signal**, when the copy finishes — this is the settle window's whole job. (c) the payload says
  removed and the path is **NOT** on `Changed Files`. (d) ignored, then reported. (e) nothing at all
  (appeared-and-vanished is a temp file).
- **FAILS AS** — a signal per write during a large copy, which is the defect the restarting settle
  timer exists to prevent; or a deleted file's path on the wire, which is a trap for anything
  downstream that tries to open it.
- **THEN THE LOOP TEST** — with the watcher armed on the project folder, have the model run
  `download_file` into it. **EXPECT: the watcher does NOT fire.** A fire here is the
  fetch-one-becomes-fetch-everything loop, and no round or stall limit catches it.
- **AND THE INVERSE** — trigger a Cloudflare block (`webtransfer.vancouver.ca` has served for this
  before), take the browser-fetch offer, and confirm the watcher **DOES** fire on the file the browser
  saved. That path exists precisely so the watcher closes the loop.

### B3. Rhino Changed

- **RUN** — arm with `Geometry` only. (a) draw a curve; (b) change the selection; (c) enable
  `Selection` and change it again; (d) run a script adding 500 objects; (e) open a different file with
  `New File` off, then on.
- **EXPECT** — (a) fires. (b) **does not** fire. (c) fires, payload naming the selection change and
  carrying the counts. (d) **exactly ONE signal**. (e) nothing, then fires.
- **FAILS AS** — 500 signals for (d). Also watch for a signal on every *gumball frame* during a drag.

### B4. Watch Modelling — the transcript, which has never been seen working

> The only part of the recorder still unverified, because the Rhino MCP server clears the shared
> buffer it reads from. **Close the MCP server before this test.**

- **RUN** — arm with `Capture Command Line` ON. Model something with real parameters: an `Offset` with
  a typed distance, a `Fillet` with a radius, an `ExtrudeCrv` with a height. Untick Recording.
- **EXPECT** — the steps carry `>` transcript rows showing `Distance=…`, `Radius=…`. That text is the
  ONLY place a command's parameters exist, and without it the model can name the operations but not
  reproduce them.
- **FAILS AS** — no `>` rows at all (something else is clearing the buffer — check what else is
  loaded), or rows belonging to the *previous* command (the high-water mark is wrong).
- **ALSO RE-TEST** — with the MCP server running, confirm MCP stdout still works while armed. That is
  the regression guard on the non-destructive read.

### B5. Watch Modelling — the rest

- **RUN** — (a) two identical commands in a row; (b) a command then Undo; (c) a command, Undo, Redo;
  (d) a cancelled command (Escape); (e) a **gumball drag**; (f) a dragged **copy** (Alt-drag);
  (g) a control-point pull; (h) arm, then run `run_rhino_script` that adds geometry.
- **EXPECT** — (a) folded to `(x2)`. (b) the step is gone and `1 undone` is reported. (c) back. (d)
  counted as discarded, not taught. (e) a `Direct edit` step with `moved by x, y, z`. (f) the same,
  noted as a copy. (g) a `Direct edit` step. (h) **the script's geometry is NOT in the recording** —
  that is `PipelineRhinoWrites`, and a failure here teaches the model its own actions back.
- **ALSO** — the caption must **tick up** as events arrive (`recording · 4`). Fixed but unverified.

### B6. Data Changed, and its documented hazard

- **RUN** — wire a slider, arm, and drag it for several seconds.
- **EXPECT** — ONE signal after the drag settles, not one per frame.
- **THEN, DELIBERATELY** — wire it downstream of something the pipeline itself writes (a `Harness Out`
  target). **EXPECT a runaway**, bounded by a `Signal Limiter` and then the `Budget Guard`. The point
  is to confirm the documented hazard behaves as documented and that the bounds actually hold — not
  to discover it in the field.

### B7. Trigger Control, in Rhino

The host half is verified by script; this is the UI in place.

- **BUILD** — a harness with `Trigger Control` → `Conversation Log.Human Tools`, plus a `Timer` and a
  `Watch Modelling`.
- **RUN** — open the chat window. Arm from the page; switch one off; switch all off. Remove every
  trigger and reopen the page.
- **EXPECT** — the rail button appears and is **tinted while anything is armed**; the page lists both
  in canvas order with the node's own captions; switching **one** off an armed recorder **sends the
  recording**; **Switch all off discards** it; the discard warning shows only while a recorder is
  armed; with no triggers the page says so and the node carries a Remark.
- **FAILS AS** — a switch flipping the wrong trigger (two default to the nickname "Watch" — this is
  why it is keyed on `InstanceGuid`); or switch-all-off producing a round.

---

## Pass C — persistence, save/load, and distribution

Cheap, and the most likely place for a silent regression, because this branch added serialized state
to several components and a new file format under the project folder.

### C1. The everything harness, round-tripped

- **BUILD** — one harness containing **every new component**: all five triggers, all four relays, For
  Each, Budget Guard, Declare, Ask Human, Delegate (linked to a nested worker harness), Pipeline
  State, Task In/Out inside the worker, Trigger Control, plus a working Chat/System
  Prompt/Conversation Log/LLM Call/Model spine. Set a non-default value on everything that has one.
- **RUN** — save the `.gh`. Close Rhino. Reopen it.
- **EXPECT, item by item** —
  - every param in its original position, **no wires moved** (an input inserted before a
    base-appended Signal shifts saved layouts — the documented hazard);
  - the `Router`'s extra tool outputs restored, still named after their tools;
  - `Delegate`'s `LinkedHarness` intact and its caption naming the worker;
  - `Signal Switch`'s **Regex** flag as saved (default off — reading a literal pattern as a regex
    changes what a definition does);
  - `Budget Guard`'s caps; `Memory`'s folder; `Pipeline State`'s `Instruction`; the `Picker` values;
  - **every trigger `off`**;
  - the `Advertise To The Model` flag on each tool as saved.
- **FAILS AS** — a wire on the wrong param after reload. Check it explicitly; it does not announce
  itself.

### C2. Preset, placed twice

- **RUN** — Save Harness as Preset… from the harness panel. Place it **twice** on a fresh document.
- **EXPECT** — two harnesses with **different four-word names** and therefore different project
  folders; each `Delegate` linked to **its own** nested worker (`IGuidLinked.RemapLinks`); each
  `Trigger Control` listing only its own triggers; no duplicate `InstanceGuid`s anywhere.
- **FAILS AS** — the second harness's Delegate pointing at the FIRST harness's worker. That is a
  remap failure and it will look like a working pipeline that quietly runs somebody else's.

### C3. `.phy` round trip

- **RUN** — save as `.phy`; import it on a fresh document; import it again.
- **EXPECT** — name, description and chat-window opening text all restored (these live on the proxy,
  which is why a plain `.gh` cannot carry them); `UniqueName` suffixing the second import; project
  files carried except what `downloads.json` accounts for.

### C4. Conversation autosave and resume

- **RUN** — hold a conversation including an **image** and a **feedback turn**. Confirm
  `<project>/conversation.json` and `conversation-images/` exist. Close Rhino. Reopen.
- **EXPECT** — the Conversation Log is **Empty**, and the chat window's empty state offers
  *"Resume saved conversation (N turns)"* with the right N. Pressing it restores the history with the
  image and with the feedback turn still **badged with its producing node**.
- **FAILS AS** — auto-resuming without being asked (a shared pipeline would arrive carrying its
  author's conversation, paid for on the next call).
- **ALSO TEST** — hand-edit `"version"` to `99` → refused with a clear message, not a partial load.
  Delete `conversation-images/` → the turn survives, the image block is dropped.
  Corrupt the last line of `runs.jsonl` → the earlier records still read.

### C5. Copy and paste

- **RUN** — arm a trigger, then copy/paste the whole harness. Also copy a single armed trigger.
- **EXPECT** — the pasted copy is **disarmed**; the pasted harness has a new four-word name and its
  own project folder; a pasted `Delegate`'s link is remapped or cleared, never left pointing at the
  original's worker.

---

## Pass D — regression on everything this branch touched

Not new features — the existing ones that now run through modified code. Skipping this is how a
release breaks the thing everybody actually uses.

| Surface | What changed | Test |
|---|---|---|
| `LlmCall` | two ledgers added on the success path | a plain chat round still streams, still records usage, `runs.jsonl` gains a line, cancel still abandons cleanly |
| `ConversationLog` | autosave, resume, trigger façade | a plain conversation records normally; the chat window's grounding/tools/units pages all still work |
| `ChatWindow` | 3 new dispatch cases, 2 new pushes | every existing page opens: Home, setup, presets, grounding, MCP, API; approval and fetch cards still appear |
| `DownloadFile` | `PipelineFileWrites` | a download still works, still reports the path, still unpacks, still offers a browser fetch on a block |
| `RhinoGeometryTool` | write scope | still bakes and still drops a referencing param |
| `RhinoScriptRunner` | write scope | `run_rhino_script` still runs, still prints, still one undo step |
| `ComponentTransmitter` | Undo Last Placement menu item | a normal placement and a normal ghpatch still work |
| `HarnessPanel` | the disarm-all row | opens collapsed; **Back to document visible in both states**; measured layout correct at 100% **and 150%** DPI; the disarm row appears only when something is armed and does not clip |
| `SignalSourceBase` | `FiresOnDisarm`, two arming verbs | all five triggers still arm and disarm from their own menus |

### D1. Undo Last Placement

- **RUN** — a full-graph placement, then the menu item. Then a **ghpatch** that both adds and modifies,
  then the menu item.
- **EXPECT** — the full graph is removed entirely; the patch's **additions** are removed and its
  modifications are **not**. The menu says so — confirm the wording is honest about that, because it
  is the one thing a user could reasonably misread as a full undo.

---

## Pass E — the model in the loop, single turn

Costs tokens. Use a cheap model except where noted. **Everything here is unverified.**

### E1. Declare

- **BUILD** — `Declare` (Routes `done, needs_input, failed`) → Router; `Route` → text equality →
  `Signal Gate.Open`; the Gate driving a visible action.
- **RUN** — ask for a two-step task and tell the model to declare when finished.
- **EXPECT** — the prompt contains the standing directive (check via `Deconstruct Signal` →
  `Instructions`); the tool definition carries the routes as an **enum**; `Route` and `Note` populate;
  the Gate branches; the declaration's signal joins the **tool-result turn** rather than producing a
  role-alternation error.
- **ALSO** — force an off-list route (a hand-made `Construct Tool Call` with `{"route":"bogus"}`) →
  an error that **re-lists the legal routes**.
- **FAILS AS** — a 400 from the provider about consecutive same-role turns. That means the
  declaration is being recorded as its own user turn instead of merging.

### E2. Ask Human — all three modes

- **RUN** — (a) a text question; (b) a question with `choices`; (c) `expect: "rhino_selection"`.
- **EXPECT** — the card appears **above the composer**; (b) shows the options as buttons **and still
  accepts typed text**; (c) shows a Send-selection button, and the selection is read **at the moment
  you press it** — select the objects *after* the card appears, and confirm those are the ids that
  come back on `Selection`.
- **THEN THE EDGES, all four** — Skip (the model is told nobody would say, distinctly from an empty
  answer); an empty answer; **close the chat window mid-wait** (→ unanswered); and with **no window
  open at all** (→ unanswered *immediately*, not after ten minutes).
- **ALSO** — raise a question, then navigate to **Home**. The card must still be visible and
  answerable; a question you cannot see is a stalled round.
- **FAILS AS** — any edge producing a plausible-looking answer. The model must never be told a person
  agreed to something.

### E3. Budget Guard

- **RUN** — `Max Calls` 2, `Extension` 0.5. Run three rounds.
- **EXPECT** — round 3 refused; the reason on `Fail Signal`; a card offering another slice; **Allow**
  extends and the round proceeds; **Deny** stops. Then `Extension` 0 → no card at all. Then close the
  window and exceed the budget → it simply stops.
- **ALSO, ON A CLI PROVIDER** — confirm `Max Tokens` never trips however long the session runs, and
  that `Max Calls` does. A CLI reports the *delta* it was sent, not the prompt; `runs.jsonl` showed
  `inputTokens: 6` against a 5,650-character prompt. If this surprises a reviewer, the node should say
  it — consider that a finding, not a pass.

### E4. Pipeline State, model side

- **RUN** — with an `Instruction` telling the model which keys to keep, run a multi-stage task.
- **EXPECT** — the directive appears in the prompt; `set`/`get`/`list`/`clear` all work; the graph
  gates on `Value`.

---

## Pass F — the model in the loop, whole workflows

The end-to-end cases a user would actually hit. Slow, expensive, and the only tests that can find an
interaction defect.

### F1. Watch and repeat — the flagship

- **BUILD** — `Files/PRESETS/User/Watch and Repeat.gh`, plus a `Trigger Control`.
- **RUN** — model one instance of something by hand with real parameters. Untick Recording. Let the
  model describe the procedure back. Then ask it to apply it to other geometry, selecting the targets
  when it asks.
- **EXPECT** — the recording arrives as one turn; the model **describes and waits** rather than
  acting; `ask_human` with a Rhino selection resolves "these ones"; `run_rhino_script` replays;
  the result is checkable.
- **THE REAL QUESTION** — does it work when the targets **differ** in size or orientation? That is the
  model generalising from one example and no component fixes it. Record what actually happens; if it
  fails, the honest answer may be a note in the preset's description rather than a code change.

### F2. Delegation with a real sub-model

The echo worker is verified; a *thinking* worker is not.

- **BUILD** — a worker harness with its own System Prompt, Conversation Log, LLM Call and a
  `Take Snapshot`, ending at `Task Out`. A `Delegate` in the caller, described properly.
- **RUN** — ask the caller a question that warrants delegating.
- **EXPECT** — the model chooses to delegate; the worker runs its own conversation; the answer comes
  back as a tool result; **an image the worker produced arrives as an attachment on the answering
  turn**; the caller's conversation does **not** contain the worker's reasoning.
- **THEN THE GUARDS, all four** — call the same worker twice concurrently (**refused as busy**); link
  a Delegate to its own harness (**refused as self-reference**); remove the worker's `Task Out`
  (**refused up front**, not after a timeout); set `Timeout` 5 and give the worker something slow
  (**times out with the pages so far**, not silently).

### F3. An unattended overnight run

The case the whole trigger tier exists for, and the one with a bill attached.

- **BUILD** — `Folder Watcher` or `Timer` → `Signal Throttle` → `Conversation Log`, with a
  `Budget Guard` (**both** caps set) and a `Signal Limiter`. `Trigger Control` wired.
- **RUN** — arm it and leave it for several hours with intermittent input.
- **EXPECT** — it wakes, works, and **stops at the budget**; `runs.jsonl` accounts for every call;
  `conversation.json` is current; nothing has run away; the harness panel's disarm button shows the
  armed count throughout.
- **FAILS AS** — anything that spent more than the cap allowed. Treat a single unexplained call as a
  blocking defect: this is the pass that decides whether the trigger tier is safe to ship.

### F4. Two harnesses at once

- **RUN** — two harnesses in one file, both with triggers armed, both with budgets, one delegating.
- **EXPECT** — spend ledgers, state boards, PDF registries and delegation sessions are **per harness**
  and do not bleed; the chat switcher moves between them; `Trigger Control` in each lists only its own.
- **FAILS AS** — one harness's budget being spent by the other's calls (the ledger is keyed on the
  local document — this is what proves it).

---

## Sign-off

| Pass | Rig | Status | Notes |
|---|---|---|---|
| A | A1 forward-the-original | ✅ verified 09-06 | seq preserved through Gate+Switch |
| A | A2 Gate/Switch routing | ✅ verified 09-06 | |
| A | A3 Hold + self-poll | ✅ verified 09-06 | timeout fired at 2s with Recheck 0.5 |
| A | A4 Throttle by payload | ✅ verified 09-06 | FIRST through, MIDDLE overtaken, NEWEST out |
| A | A5 For Each | ✅ verified 09-06 | empty-list and restart cases still to do |
| A | A6 Pipeline State (graph) | ✅ verified 09-07 | caps + last-set order pass; 65th key refused with nothing evicted, 9000 chars → 8192. Found a DEFECT: a case-variant re-set (`stage` then `STAGE`) put two items on `Keys`/`Values`. **Fixed**; re-run after a Rhino restart to confirm the fix rather than the bug |
| B | B0 **wake-up in a harness** | ✅ verified 09-07 | the mechanism was genuinely exercised: with `inner.Enabled` FORCED false the Timer kept firing 4→8 and the flag came back True. Then with `host.Enabled` false it fired 9→14 and the host flag **stayed false** — the user's solver lock is not overridden |
| B | B1 Timer | ✅ verified 09-07 | a FRESH timer arms to `every 1m` with no count, so nothing fires on arming — check a fresh one, a re-armed timer keeps its old count and reads as if it had; 0.2s → clamp warning; disarm immediate; reopens `off` |
| B | B2 Folder Watcher + loop | ◐ verified 09-07 | all five file cases pass — 160MB flushed in 160 chunks gave **exactly one** signal, a removed file's path stayed **off** `Changed Files`, create-then-delete gave nothing. The download-loop and browser-fetch halves still need a model |
| B | B3 Rhino Changed | ◐ verified 09-07 | (a)–(d) pass: **500 objects = one signal**, and the selection payload carries its counts ("501 objects in the document, 7 selected"). (e) New File not tested |
| B | B4 **transcript with parameters** | ☐ blocked | needs the Rhino MCP closed, and that MCP is the only way this session can drive Rhino. Must be done by hand |
| B | B5 Watch Modelling, the rest | ◐ | folding/undo/discard verified; drags and script-suppression not |
| B | B6 Data Changed + hazard | ☐ | |
| B | B7 Trigger Control in Rhino | ◐ verified 09-06/07 | page half measured headlessly: rail button tinted while armed, rows carry counts + captions, discard warning only with a recorder armed, and every send is `armtrigger?id=<guid>` — no name crosses the bridge. The UI in Rhino not |
| C | C1 everything harness round-trip | ✅ verified 09-07 | 27 components: guids preserved and — the documented hazard — **no param-order drift, no wire moved, no value changed**. Router's outputs came back named `declare`/`ask_human`/`state`; Regex flag, Budget caps, For Each items, port nicknames all restored; **all five triggers `off`**, including two armed before saving |
| C | C2 preset placed twice | ◐ verified 09-07 | stated assertions PASS — different four-word names, and each Delegate linked to its **own** worker. But ids inside the **nested** harness were not re-issued: two placements gave two Timers sharing one `InstanceGuid`. **Fixed** (`MutateAll` now descends); re-run after a Rhino restart |
| C | C3 `.phy` round trip | ☐ | Core half already pinned (`PhyPackageTests`, incl. future-format refusal); the import-twice path is not |
| C | C4 autosave + resume | ◐ | autosave verified on disk; the three ALSO-TEST edges are already pinned by the Core suite (`ANewerFormatIsRefused_NotGuessedAt`, `AMissingImageFileLosesTheBlockAndKeepsTheTurn`, `AHalfWrittenLastLineCostsOneRecord_NotTheFile`) — only the resume BUTTON is untested |
| C | C5 copy and paste | ◐ verified 09-07 | the arming half is settled: nothing in the trigger tier overrides `Write`/`Read` **at all**, so arming cannot serialize, and C1 confirmed it live. The paste path itself is untested — `GH_DocumentIO.Copy`/`Paste` return true and do nothing from a script |
| D | D regression sweep | ☐ | |
| D | D1 Undo Last Placement | ☐ | |
| E | E1 Declare | ☐ | |
| E | E2 Ask Human, all modes and edges | ☐ | |
| E | E3 Budget Guard refusal + card | ☐ | |
| E | E4 Pipeline State (model) | ☐ | |
| F | F1 watch and repeat | ☐ | |
| F | F2 delegation with a real sub-model | ◐ | echo path verified; thinking worker and all four guards not |
| F | F3 unattended overnight | ☐ | **ship blocker** |
| F | F4 two harnesses at once | ☐ | |
| — | icons for 29 components | ✅ done 09-07 | all 29 generated, split and installed; audit reports **0** fallbacks across 108 ribbon types |
| — | Mac decision | ☐ | Windows-only, or schedule the port |

### What the 09-06/07 pass found

Two defects, both fixed on `final-pass`, and both worth recording because of *how* they were found.

1. **`StateStore.All()` duplicated a key on a case-variant re-set.** `Board.Values` is keyed
   `OrdinalIgnoreCase`; `Board.Order` was a plain `List<string>` whose `Remove` is case-SENSITIVE. So
   `set stage` then `set STAGE` left ONE board entry and put TWO items on the `Keys`/`Values`
   outputs, both carrying the newest value — which shifts anything downstream matching by index.
   Reproduced live on the shipped `.gha` (§A6). `Clear` had the same mismatch, masked, because
   `All()` filters the order log through `Values` and so hid the orphaned name rather than showing
   it. **A masked copy of a bug is the one that survives a test pass; look for it every time.**
2. **`DocumentIds.MutateAll` did not descend into a nested harness.** A preset placed twice
   re-issued ids for the document it was handed and for the nested harness COMPONENT, but never for
   that harness's own `InnerDocument` — so two placements gave two Timers sharing one
   `InstanceGuid`, which is exactly what Trigger Control's guid addressing exists to prevent. Not a
   corner case: a Delegate links to a worker harness, so every delegation preset is that shape.
   **C2's stated assertions all PASSED** — the names differed, `RemapLinks` sent each Delegate to its
   own worker — so the rig as written would have been ticked. It was caught only by also asserting
   "no duplicate `InstanceGuid`s **anywhere**", which meant walking two levels down.

Neither was reachable from `Physalia.Core`, which stayed at 964/964 green throughout. That is now
the third time this document's premise has held.

### Blocking for a release

1. ~~**B0**~~ — **verified 09-07**, including the `PipelineWake` re-enable that had never been
   exercised and the host-solver-lock case.
2. **F3** — an unattended run that overspends its budget is worse than no trigger tier at all.
   Still the open blocker.
3. ~~**C1**~~ — **verified 09-07**, clean round trip with no param or wire drift. **C2** passed its
   own assertions but exposed the nested-id defect above; re-run it against the fix.
4. ~~**Icons**~~ — **done 09-07**, 0 fallbacks.

### Not blocking, but decide before shipping

- Whether `Budget Guard` should say on the node that a token cap is meaningless on a CLI provider.
- Whether `Data Changed` should ship at all, or ship behind a warning, given its cycle hazard.
- Whether the Watch-and-Repeat preset should be a `.phy` so it carries its own opening text.
- Whether a trigger's fire count should **reset when it is re-armed** (noticed in §B1). It does not
  today, so a re-armed Timer immediately reads `every 10s · 14` and looks as though it fired on
  arming — which is the one thing B1 exists to check. Cosmetic for a user, actively misleading for a
  tester, so the test note matters more than the change: **check a FRESH trigger**.
