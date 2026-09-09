# Triggers, conditions, delegation, budget and records

> Split out of `CLAUDE.md` (2026-09-08) to keep that file under its size limit. Content is verbatim; CLAUDE.md links here.

## Events, conditions and delegation (built 2026-09-06)

Three tiers landed together, and they are one change: until this the plug-in could only *react* to a
person being present, could not say "only if", and had exactly one conversation to think in.

### Triggers — the event tier (`Components/Triggers/`)
A round could previously start three ways: a human typed in the chat, a Button drove Construct
Signal, or a Feedback loop re-entered. Every pipeline was therefore downstream of somebody sitting
there. `SignalSourceBase<TEvent>` is the source tier that fixes that — **Timer**, **Folder
Watcher**, **Rhino Changed**, **Data Changed** — and the base owns four things that are each
load-bearing.
- **Arming is session-only and is NEVER serialized.** A file that opened armed would start spending
  money on whatever machine opened it, a colleague's included. Same reasoning as the
  first-observation baselining that stops a stuck Toggle firing on load. A trigger always reopens
  `off`; the node's menu arms it, and the harness panel's **Disarm N triggers** button (visible in
  BOTH panel states, above Back) is the kill switch — `TriggerRegistry` is a weak registry so
  something outside a node can ask how many are armed and switch them all off.
- **Bursts are coalesced.** One copied folder is one file-system event per FILE; one saved file is
  several for that one file; a script adding 500 objects is 500 Rhino events. Events accumulate and
  a settle timer RESTARTS on each, so a burst is one signal whose length is the burst's.
- **`PipelineWake.Ready` is why a wake-up is not silently dropped.** GH drops scheduled solutions on
  a disabled document; a harness sub-document's `Enabled` is OUR invariant (the proxy re-asserts it
  every solve) but the proxy only solves when the host does, and an autonomous trigger fires when
  nothing has solved for hours. So the flag is re-asserted before scheduling — **for a harness
  document ONLY**, since on the user's file that same flag is Grasshopper's solver lock. Shared with
  `TaskIn`.
- **`PipelineFileWrites` breaks the download loop.** `download_file` writes into the project folder,
  the watcher sees it, the model is told a file appeared, and it fetches the next one — a loop with a
  bandwidth bill that no round or stall limit catches, because every round is genuinely different. So
  tool-driven writes register there and the watcher ignores them for 30s. **A browser fetch
  deliberately does NOT register** — that path exists *because* the watcher then hands the file to
  the model.
- Per-trigger notes: the Timer never fires on arming (arming is a switch, not a run button) and
  clamps below 1s. The Folder Watcher's `Changed Files` output is the point of it — a dropped LiDAR
  tile is to be imported, not read about — and removed files are kept off that wire. Rhino Changed
  subscribes all thirteen events once and filters at report time; **Watch Selection is the one to
  reach for**, since "move these" only resolves if a round starts when the selection changes. Data
  Changed is the ACTIVE counterpart of the passive Harness In and therefore **re-opens the cycle
  hazard Harness In avoids** — nothing can detect it, because the cycle runs through the user's
  canvas; Signal Limiter bounds it and the Budget Guard is the backstop.

### Watch Modelling — demonstrate once, repeat it (`ModellingWatch`, `Core/Recording/`)
Tick **Recording**, model the thing by hand, untick it: one signal carries the whole procedure.
- **Rhino already knows what you did, which is why this is small.** The obvious approach — diff the
  document against a previous state — needs a full before-snapshot kept between rounds, a round trip
  per edit, and it recovers geometry rather than intent. `Command.BeginCommand`/`EndCommand` give the
  intent directly (name + `CommandResult`), `Command.UndoRedo` gives `IsBeginUndo`, and
  `RhinoApp.CapturedCommandWindowStrings(clear)` gives the parameters as text. All verified against
  the shipped RhinoCommon before building.
- **It fires on DISARM, not per command** — hence `SignalSourceBase.FiresOnDisarm`, which suppresses
  the settle timer entirely: there is no pause length that tells thinking apart from finishing. The
  base's `SetArmed(bool)` (the `IArmableTrigger` / kill-switch form) drops the batch; the node's own
  menu calls `SetArmed(on, flush: true)`. **That asymmetry is deliberate** — somebody pressing the
  harness panel's disarm-everything button is not asking for a round to start.
- **The filtering IS the feature**, and it lives in Core so it is testable. What survives is judged
  by EFFECT (did the document change), which drops every view and selection command without needing
  their names; the name list is a second pass for commands that DO mutate but are not the
  demonstration (Save, Options, Grasshopper, RunPythonScript). **Undo pops the last surviving step
  and Redo pushes it back** — an Undo means the step never happened, and a recording containing both
  teaches nothing. Consecutive runs of one command fold into one step with a repeat count.
- **A selection is not a step.** Selecting is how a command is set up, so the selection at
  `BeginCommand` is recorded as that command's INPUT — which is what makes a step repeatable, since
  "Offset" alone says nothing about what was offset.
- **Direct edits are recorded**, because a gumball drag is a modelling step and a recording without
  the moves is a procedure with holes. Note it subscribes **`BeforeTransformObjects`, not
  `AfterTransformObjects`**: the after-event carries only a `TransformEventId`, while the before-event
  carries the `Transform`, the object count and `ObjectsWillBeCopied`. A pure translation is reported
  as the vector it was; anything else is left as "moved" rather than described in words the model
  would then act on.
- **`PipelineRhinoWrites` is the file-write suppression argument one document over.** `run_rhino_script`
  and geometry baking are not Rhino commands, so their object events arrive with no command open —
  indistinguishable from a gumball drag — and would be recorded as the user's own work. Both wrap
  their writes in a scope. Same reasoning as `PipelineFileWrites`: what the model did is already in
  the conversation.
- **The default closing instruction makes the model describe the procedure back and WAIT.** A
  demonstration arrives as a user turn and the natural next move is to start applying it, with
  parameters it inferred and nobody checked. Overridable on the node's `Instruction` input.
- **What it does not solve, and no component can:** repeating a demonstration on geometry that
  DIFFERS is the model generalising from one example. What helps is that every step reports its
  inputs as well as its outputs, and that the closing instruction puts the inference in front of a
  person while it is still cheap to correct.

### Trigger Control — where arming actually lives (`TriggerControl`, Human Tools)
A trigger's own right-click menu is the right home for arming ONE and stops being enough at three:
they are scattered inside a harness nobody is looking at, and arming is the act with a bill attached.
Until this, "what is switched on right now" had no answer short of visiting every node — the harness
panel could only say how many were armed and switch them all off, which is a fire alarm rather than a
control. So a human tool puts the list in the chat window: one switch per trigger, plus arm-all and
switch-all-off.
- **The list is READ LIVE, never stored.** Which triggers exist is not a setting; it is whatever is on
  the canvas now. `ConversationLog.LiveTriggers` scans its own document each time it is asked, and the
  window re-reads it on its 0.15s tick — because **arming changes no data and runs no solution**, so
  there is no event to push from and a cached list would go stale the moment a Timer was dropped in.
  The scan asks the components, not the solver (the `Router.InspectConnection` lesson).
- **Two arming verbs, and conflating them would make one case silently wrong.** `IArmableTrigger`
  gained `SetArmedAndHandOver` beside `SetArmed`: a switch aimed at ONE named trigger does what that
  trigger's own menu does — so switching a Watch Modelling off from the list **sends the recording** —
  while **Switch all off** stays the kill switch and discards, matching the harness panel's button,
  because somebody stopping everything is not asking for a round to start. The page says so on the
  row, and the warning appears only while a recorder is actually armed.
- **Addressed by `InstanceGuid`, never by nickname.** Folder Watcher's default nickname is `Watch` and
  so is Watch Modelling's, so a name-keyed switch would flip whichever it found first — the same
  mistake the old name-keyed tools selection made. Verified headlessly
  (`tools/uitest/test_trigger_send.py`): the row click sends `armtrigger?id=<guid>&on=…` and no name
  ever crosses the bridge.
- `HandsOverOnDisarm` is exposed on the interface purely so the UI can label the row. A page offering
  a switch has to know that "off" is not merely "stop" for a recorder; telling somebody afterwards
  that their demonstration went nowhere is not a recoverable message.
- The node reports a **Remark when the pipeline has no triggers**, because otherwise the failure is
  silent and off-canvas: the tool is wired, the button appears, and the page says "no triggers" with
  nowhere obvious to look. Same reasoning as Token Count reporting a missing link.
- `window.location` cannot be redefined in Chrome, so the send path is verified from OUTSIDE over the
  DevTools Protocol (`Log.enable` reports the blocked custom-scheme navigation and carries the URI).
  Worth knowing for any future bridge test — a page-side interception of `location` does not work.

### Conditions — `SignalRelayBase` (`Components/ControlFlow/`)
Every branch in Physalia used to be a specialist: Detect JSON knows only about JSON, Stall Guard only
about repeated failures, a guardrail's Success/Fail pair only about its own run. Four relays now sit
on one base: **Signal Gate** (decides now: Passed/Blocked), **Hold Signal** (waits: Released/Timed
Out), **Signal Switch** (matches the payload text; regex is a context-menu toggle), **Signal
Throttle** (one per interval, newest wins).
- **The ORIGINAL signal is forwarded, never re-minted.** A signal carries Instructions on the
  Conversation Log→LLM Call hop, plus content blocks and an origin trail; minting a replacement
  would have a gate placed inline silently strip the conversation it was gating. The sequence
  travels too, so a signal does not become "newer" by being held.
- **One signal per solve with a follow-up scheduled**, because a relay may HOLD and the held one must
  not be jumped. Nothing is scheduled for signals queued *behind* a hold — `Route` asks for that
  solve when the hold clears, and asking every solve would be a busy loop wearing a timer's clothes.
- Two hold policies cover everything built on it: `KeepOldest` for a wait, `KeepNewest` for a
  throttle (an overtaken event is stale by definition).
- **Hold Signal's `Recheck` also expires the components wired into `Release`**, and it has to:
  expiring this node re-reads nothing, because GH recomputes only what it expired. That is the whole
  mechanism by which a wait on something outside the data graph can ever end.
- **Outcome routing needs no node**: Deconstruct Signal already hands out a `Success` boolean, so
  that into a Gate is the exact form — worth knowing because a Merge Signal's combined outcome is
  otherwise unreachable.
- **For Each** walks a list one item at a time (`Next` is wired from the END of the per-item work).
  Strictly sequential, and that is a rule: the pipeline downstream has ONE Conversation Log, so
  twelve items at once would interleave into one conversation. `Index` is what carries anything that
  is not text — a List Item on the far end picks the matching geometry. The list is **snapshotted at
  Start**, or a pipeline that edits the canvas extends the very list it is iterating. An empty list
  is DONE, not broken.

### Declare and Ask Human — the two directions of intent
- **Declare** (`declare`) lets the MODEL pick a route the pipeline offers, instead of the graph
  inferring intent from prose. Routes are typed on the node (so they ship in a preset) and generated
  into the schema as an enum, so advertised and accepted cannot drift. Branch on it exactly:
  `Route` → an equality test → a Signal Gate's `Open`. **It is only the second tool ever to override
  `GroundingDirective`** (Memory is the first) and for the documented reason — a model not told it
  must declare simply answers in prose, which is the thing the node exists to stop the pipeline
  interpreting. **The signal fires in the same solve as the tool result**, because that is the only
  moment the node is awake and "the round finished" is not observable from inside a tool; a
  declaration wired into a Prompt Signal therefore joins the tool-result turn, which
  `MergeIntoLastUserMessage` already handles.
- **Ask Human** (`ask_human`) is the inverse, and before it the only question the model could ask was
  yes-or-no. `IHumanAsker` / `HumanQuestionBroker` / `QuestionCard.svelte` are the **third sibling**
  of `ToolApprovalBroker` and `BrowserFetchOffers`, and the differences are why they are not one
  class: an approval blocks a call and must fail CLOSED; a fetch offer blocks nothing so has no
  timeout; a question blocks a call but **has no safe answer to invent**, so every edge returns
  *unanswered* and the model is told so in as many words. Ten minutes, not five: answering means
  going and looking. `expect: "rhino_selection"` is the case that justifies the seam — the answer to
  "which ones" cannot be typed — and the selection is read **at the moment the button is pressed**,
  host-side, then handed back both to the model and onto the `Selection` output. Answers ride the
  SUBMIT channel under `kind: "human-answer"` because a typed answer can be a pasted paragraph.

### Delegation — a harness as a tool of another harness (`Components/Delegation/`, `IO/`)
A pipeline has exactly ONE Conversation Log, so every subtask ever asked stays in that context.
**Delegate** (grip-linked to a harness, `DelegateAttrib`) hands a task to another harness and waits;
**Task In** and **Task Out** are that harness's entry and exit. Chosen over a self-contained
sub-agent node deliberately: a black box would be the one part of Physalia the user could not see,
edit, validate or ship.
- **The two ends are paired on the INNER DOCUMENT** (`DelegationBroker`), not by a wire — no wire
  crosses a harness boundary. Same device as the PDF registry, the spend ledger and the state board.
- **Task In is ACTIVE where Harness In is passive**, and has no Armed switch: it fires only when
  another pipeline calls it, so the caller's own budget and triggers already bound it.
- **Task Out answers with the whole signal**, so a sub-pipeline that LOOKED at something hands the
  image back — as a tool attachment, the machinery a tool already has for answering with non-text.
  Reaching it with nobody waiting is a Remark, not an error: a callable harness is still an ordinary
  pipeline someone runs by hand while building it.
- **One task at a time per inner harness.** One conversation, one solve state: two tasks would
  interleave and neither answer would be trustworthy. It is also the guard that stops most
  recursion, alongside an explicit self-reference check.
- **An unlinked or undescribed node advertises NOTHING** — the ApiCall rule, for the ApiCall reason:
  a tool that fails every call reads to the model as broken rather than unconfigured. Tool names are
  namespaced `delegate__<name>`, since two delegates in one pipeline is the normal case and the
  Router dispatches on the name.

### Budget Guard — what bounds an unattended session
Signal Limiter caps one loop's rounds and Stall Guard catches a loop repeating itself; neither bounds
a SESSION, and until a timer could start a round a session was bounded by a person being present.
**A pipeline with any trigger armed and no Budget Guard has no upper bound on its bill.**
- `SpendPolicy` (Core, pure, tested) + `SpendLedger` (per local document, session-only). The LLM Call
  records; the guard reads; **no wire between them**.
- **Checked BEFORE a call, against what is already spent**, so a pipeline can overrun by up to one
  call. The cost of a call is not knowable until it is made, and a runaway loop is stopped just as
  dead one call late.
- **A call with no usage reported still counts as a call**, which is what makes `Max Calls` the cap
  that works for a CLI provider on a subscription — it reports no token figure at all.
- Over budget it refuses (reason on Fail Signal, so a loop can react) **and reuses
  `ToolApprovalBroker`** to offer another slice. Reused rather than reimplemented because the
  question genuinely IS an approval — may this spend more of your money — and every edge failing
  closed is exactly what a budget wants. `Extension = 0` means the budget is final and no card
  appears.

### Conversation persistence and the run log (`Core/Recording/`)
- **`ConversationArchiver` + `ConversationTranscript`**: `<project>/conversation.json` plus
  `conversation-images/`. Project folder, not the `.gh` — a transcript is project material, so it
  ships in a `.phy`, and a `.gh` is copied and emailed far more casually. **Autosaved every turn**
  (crash-safe; images written once, since keys are derived from position in an append-only history)
  and **resumed only on request** — loading on open would hand a shared pipeline its author's
  conversation, paid for on the next call. The offer is a button in the chat window's empty state
  (`resumeTurns` on `UiState`), shown only while the conversation is empty, which is also the only
  time resuming has an unambiguous meaning. Images go BESIDE the JSON, a newer format version is
  REFUSED, an unknown block type is dropped with the turn kept, and consecutive same-role turns are
  MERGED rather than refused (somebody's transcript beats no transcript). This replaced two
  "not yet implemented" menu stubs.
- **`RunLog` + `RunLedger`**: `<project>/runs.jsonl`, one line per inference call. **JSONL because a
  log is a stream** — an append needs no read, and a truncated last line costs one record; that is
  the opposite of `downloads.json`, which is a LEDGER read whole. Per CALL, not per round: a round is
  a boundary nobody would agree on, a call is what costs money. Failures are logged too, and **a
  write failure is swallowed on purpose** — a log that cannot be written must not cost the answer it
  was recording.

### Pipeline State — structured state the graph can branch on
`state` (set/get/list/clear) + `StateStore`, keyed per local document, session-only, capped at 64
keys and 8KB a value. **Not the Memory tool**: memory is prose files the model writes for its future
self and the pipeline never looks inside them; this puts a value on a WIRE — into a comparison, into
a Signal Gate. Before it, the only structured state the graph could act on was the Build Plan
tracker's. Its `Instruction` input rides in the prompt (an author who does not say which keys matter
gets keys the model invented, and a gate watching one nobody set), but unlike Memory's directive it
does NOT make calling mandatory — a pipeline that does not branch on state has no use for it.

### Undo Last Placement
On the Component Transmitter's menu. Everything it needed was already there: `_placedGuids` tracks
the last placement so a re-placement can replace itself, and removing them is the same operation.
**It removes what was ADDED and says so** — a ghpatch also MODIFIES existing components and nothing
recorded what they looked like before, so a full graph is undone completely and a patch as far as its
additions go. Deferred to idle, like placement itself.

---

