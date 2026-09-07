# Teaching presets

A set of `.phy` harnesses meant to be read, not just run. Each one is a working pipeline with
every stage numbered, headed with a blue panel, and explained in a yellow one — in plain English,
on the canvas, where you are looking when you have the question.

They are designed to be worked through in order. Each assumes the ones before it and says so.

## How to use them

These files live outside the preset library, so the chat window's gallery does not list them yet.
There are two ways in, and **there is no drag-and-drop onto the canvas** — that was checked.

**Either put them in the library.** Copy the `.phy` files into `Files/PRESETS/User/` beside the
plug-in (the folder is `.../Physalia.GH/bin/<config>/net7.0-windows/Files/PRESETS/User` when running
from a build). They then appear in the chat window's **Home → Place predefined harness** gallery,
each with the one-line description out of its manifest. Verified: the library enumerates `.phy` as
well as `.gh`, and reads the description back.

**Or load one into a harness you already have.** Chat window **Home → Place empty harness**, then
right-click that Harness node and choose **Load Harness from .gh File…**. It accepts a `.phy` from
anywhere on disk. Note it REPLACES that harness's contents — conversation, solve state and all — and
asks you first.

Once it is on the canvas:

1. Right-click the Harness node and choose **Edit Harness** to go inside and read it.
2. Double-click the Harness node to open the chat window on the Chat inside it. Each preset's
   greeting says what to try first.

Nothing is armed and nothing is linked to your own components until you do it yourself. Presets
that need setting up say so in their intro panel and in their chat greeting.

## The set

| | Preset | What it adds | Model |
|---|---|---|---|
| 01 | Talk to a Model | The core loop, and the wireless return path that makes it possible | Claude Code |
| 02 | What the Model Knows | Grounding: six components describing your document, canvas, units and folder | Claude Code |
| 03 | Tools the Model Can Call | The Router and six tools; three return paths | Codex |
| 04 | Building on the Canvas | Eight guardrails, then real components placed on your canvas | Claude Code |
| 05 | Writing Python for You | Code pushed into a Rhino 8 Script component, fitted to its parameters (C# alongside) | Claude Code |
| 06 | Letting It Look and Walk | Take Snapshot and Move In Space; the harness's own inputs and outputs | Codex |
| 07 | Making the Pipeline Decide | Branching — Declare, Pipeline State, and a button-driven playground | Codex |
| 08 | Running Without You | Triggers, and the three things that bound the bill | Claude Code |
| 09 | Files, Downloads and Drawings | The project folder, Download File, Read File, Read PDF | Codex |
| 10 | When the Conversation Gets Long | Compaction, and what each kind costs you as well as saves | Claude Code |
| 11 | Reading Live Data | An HTTP API of your own, and an MCP server's tools | Codex |
| 12 | Handing Work to a Helper | Delegation — a harness inside a harness, called as a tool | Codex |
| 13 | Choosing and Tuning a Model | Every Model node, its Model API and its Tweaker, side by side | Claude Code |
| 14 | Looking Inside the Pipeline | The debugging preset: read a signal, read the real prompt, drive it by hand | Claude Code |

## Two things to know before you start

**Some presets use Codex rather than Claude Code, and it is not a preference.** Claude Code
ignores the tool list completely: told by the grounding that tools exist, it tries to call one and
reports that no such tool is available, with nothing on the canvas looking wrong. Of the two
keyless command-line models only Codex can drive tools. Anthropic, Gemini and OpenAI-compatible
Model nodes all support tools properly and are usually faster — they just need an API key, set up
from the chat window's home screen.

**Codex's model list is fetched live from the CLI and it changes.** If a round fails saying the
model does not exist or you do not have access to it, open the dropdown beside the Codex Model node
and pick again.

## What has actually been tested

Every preset here was built, saved, read back through the real preset loader, checked for component
errors and for overlapping annotation, and **run live in Rhino**:

- **01** — a reply came back through the loop.
- **02** — asked with no tools how many objects were in the file and on which layers, it answered
  "4 objects total, on layers Beams and Slabs" from grounding alone.
- **03** — wrote Python, ran it in Rhino through `run_rhino_script`, and reported per-layer counts.
- **04** — the whole gauntlet passed in one round, Fidelity Check included, and placed a working
  row-of-circles definition with a spacing slider.
- **05** — pushed a running-cumulative-total script into a linked Python 3 Script component.
- **06** — reported its position, stepped `{0,0,0}` → `{0,4000,0}`, and looked north.
- **07** — the branch was demonstrated in BOTH directions. Asked to build something, it declared
  `build`, Match Text returned True and the Gate opened; asked something under-specified, it
  declared `ask` with a note saying what it needed, Match returned False and the Gate read
  "shut · 0/1". Pipeline State set `phase` = `survey` and the value arrived on its wire. The
  playground's five relays behave exactly as their notes claim, verified headlessly.
- **08** — the Timer fired 20 times unattended; the Throttle paced it, the Limiter capped it, and
  the Budget Guard refused.
- **09** — listed the folder, errored correctly on a missing file, downloaded one and read it.
- **10** — crossed the threshold, compacted, and then still knew the number from turn 1 while
  honestly having lost the middle of the conversation.
- **11** — made real requests against a live open-data API, recovered from a 404 on a wrong dataset
  name, and put records on the wire one item per record.
- **12** — a full delegated round. The caller wrote its own task, Task In read "1 received", the
  helper ran a script and got 10, Task Out read "1 answered", and `10` arrived on the caller's Last
  Answer wire. The caller's conversation holds four turns — the request, the call, the answer `10`,
  and the reply. None of the helper's script or its output is in it. The helper's own conversation
  holds the four turns it took. That is the argument for delegation, demonstrated rather than
  described.
- **13** — answered on Claude Code, and Model Information reported blanks for it, which is the case
  the note is written around: a CLI provider's shorthand is not a catalogue id, and unknown is not
  no.
- **14** — both hand-minted paths. `Construct Tool Call` ran `print(1+1)` through Drive Rhino with
  no model in the loop at all, and `Construct Signal` drove a real round whose answer — "14 objects
  … Structure (10), Beams (3), Slabs (1)" — was correct.

Two presets need something set up before they do anything, and say so in their intro panel and
their chat greeting: **05** needs the Py Transmitter linked to a Python 3 Script component on your
canvas, and **11** needs at least one API endpoint or MCP server configured from the chat window's
Home screen. **06** needs points wired into the Harness node's inputs.

## Building them

`tools/presets/` holds the scripts that generate these files, driven through the Rhino MCP:

- `phybuild.py` — the shared helpers.
- `build_NN_*.py` — one per preset. Re-run one to regenerate its `.phy`.
- `verify.py` — reads a written `.phy` back the way the loader does.
- `shoot.py` — renders a harness's canvas to a PNG, so a layout can be looked at.
- `liverun.py` — places a `.phy` and drives one real round through it, no chat window needed.
- `test_07_playground.py`, `test_08_timer.py` — the two headless behaviour tests.
- `audit.py` — sweeps every written `.phy` for anything machine-specific. Runs OUTSIDE Rhino: a
  `.phy` is a zip and its `harness.gh` is raw deflate, so reading the bytes answers the question
  directly and in under a second. Exits non-zero on a finding.
- `build_all.py` — rebuilds all fourteen and runs the Rhino-side check, logging to a file.
- `check_pairs.py` — confirms every wireless Feedback pair still resolves to a Collector *after* the
  id reissue a preset load performs, and does the same for the Token Count, Set Script I/O and
  Delegate grip links. 32 pairs and 4 links across the set.

To rebuild and verify the lot:

```
# in Rhino, through the MCP:  exec(open("tools/presets/build_all.py").read())
python tools/presets/audit.py
```

Last full run on a freshly restarted Rhino — so a clean plug-in load, not a warm one: 14 built, 0
problems each, 0 broken pairs, 0 broken links, audit clean, 23 seconds.

Both of those are whole-set checks worth re-running after any change, because the two failures they
look for are silent: a preset carrying somebody else's endpoint name, and a Feedback whose collector
guid no longer resolves — which swallows the signal, hands it nowhere, and errors about nothing.

**If you script against Rhino this way, retire a document with `RemoveObjects` and *then* `Dispose`,
never `Dispose` alone.** Every component's `RemovedFromDocument` is what releases its
subscriptions — a Rhino Document grounder holds thirteen RhinoDoc handlers, a Project Folder holds a
FileSystemWatcher — and reading these presets back a few dozen times with a bare `Dispose()` left
enough stale handlers to slow the session until a trivial script could not finish. `phybuild.retire()`
does it in the right order.
