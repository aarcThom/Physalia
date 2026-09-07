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
| 03 | Tools the Model Can Call | The Router and eight tools; three return paths | Codex |
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

## The scenario set

The numbered presets teach the parts. These teach the **work** — each one is a job somebody
actually has, wired up and annotated with how to do it rather than with what each component is.
They assume you have read 01 to 03; where one of them needs the detail, it points at the numbered
preset that has it.

| | Preset | The job | Model |
|---|---|---|---|
| S01 | Record and Repeat | Do it once by hand; it watches, describes the procedure back, and repeats it on your next selection | Codex |
| S02 | Walk the Building and Review It | Give it points to stand on and a rubric; it walks, looks, and writes a review — and the route comes back as geometry | Codex |
| S03 | Interrogate and Tidy Your Rhino Model | Ask your model a question in English; it writes Python and answers, or does the boring fix | Codex |
| S04 | Get It to Build the Definition | The working guardrail chain, annotated with **how to ask** rather than what each check does | Claude Code |
| S05 | Write Me a C# Component | You draw the wires, it writes the code inside them | Claude Code |
| S06 | Get a Second Opinion | Two vendors, two conversations: one writes, one objects, and only APPROVED reaches your canvas | Claude Code + Codex |
| S07 | Send the Legwork to a Specialist | Two helper harnesses — a surveyor and a researcher — so the noise stays out of your conversation | Codex |
| S08 | Check the Model Against the Document | Read the code, spec or drawing set and the model together, and report the discrepancy | Codex |
| S09 | Build the Site Context from Open Data | Find it, fetch it, work out its coordinate system, get it into Rhino | Codex |
| S10 | A Take-off That Keeps Itself Up To Date | Quantities that re-count when the model changes, onto a wire rather than into a chat | Codex |
| S11 | Explain the Definition Nobody Documented | Inherit a file and get back what it does, which sliders matter and where it breaks — read-only | Codex |
| S12 | From a Sketch to a Massing | Photograph the sketch, draw on it, get the massing built — then it looks at its own result | Codex |

S08 to S12 are the ones nobody asked for. They are here because the question this set has to
answer for a working architect or designer is not "what can it do to my Grasshopper canvas" but
"what does it save me on Thursday", and the best answers to that have nothing to do with
generating node graphs: read a document against the model, get the site context in, keep the
quantities honest, understand the file you inherited, and get the sketch off the desk.

Three of them are structurally unlike anything in the numbered set and are worth reading for that
alone: **S06 puts two Conversation Logs in one harness** (the join is one wire — the writer's
Success Signal into the critic's Prompt Signal); **S07 has two nested harnesses**, each a complete
pipeline with its own Chat; **S10 puts a value rather than prose on a Harness Out**, which is what
separates a tool from a chat about the same subject.

## What the set covers

**103 of the 107 placeable Physalia components appear in at least one preset.** That is measured,
not asserted — `tools/presets/coverage.py` reads every `.phy` and checks it against what the
plug-in offers, and prints which preset each component appears in. Run it after adding a preset to
see what is still uncovered.

Four are deliberately absent:

| | Why |
|---|---|
| Cluster Grounding | a scaffold; not finished in the plug-in |
| Python Grounding | a scaffold; not finished in the plug-in |
| Image Sources | its `/<alias>` prompt reference has no consumer left in the chat window, so a preset would teach something that does not work |
| LlamaCpp Model Info | it would sit on preset 13's canvas warning about an unwired input forever; the note there says what it is for instead |

Params are excluded from the count — they are wire types, not things a preset teaches.

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

### The scenario set

All ten were built, saved, read back through the real preset loader, swept for component errors and
overlapping annotation, and checked by all three standing checks (`audit`, `check_pairs`,
`coverage`). Structural things worth naming because they could have failed silently and did not:

- **S05** — the Set Script I/O to C# Transmitter grip link survives the loader's id reissue.
- **S06** — both Conversation Logs and both LLM Calls come back as a pair, and the writer-to-critic
  join is intact.
- **S07** — both Delegate grip links resolve to their nested harnesses after reload, and both inner
  documents come back whole (27 and 24 objects).
- **S10** — the Pipeline State tool dispatches. It did not at first: see the note below.

Live in Rhino:

- **S04** — a full round. The whole chain reported Success in one pass — Detect JSON, Schema
  Validator, GH Definition Validator, Component Resolver, Required Input Check, Component
  Transmitter, Runtime Health Check, Geometry Report — and a working definition landed on the host
  canvas: a Number Slider nicknamed `Radius`, an XY Plane, a Circle and a Panel, grouped as
  "Circle at Origin" inside the Physalia master group.
- **S05** — a full round, and this is the **first time the C# Transmitter has been run live in
  Rhino at all**. A real Rhino 8 C# Script component was placed on the host canvas, the transmitter
  linked to it, and the model asked for a sum. Schema Validator, C# Transmitter and Runtime Health
  Check all reported Success and this is what landed in the component:

  ```csharp
  private void RunScript(double x, double y, ref object a)
  {
      a = x + y;
      Print(a.ToString());
  }
  ```

  The signature matches the target's actual parameters, which is the check that gates the push. Two
  things confirmed on the way: `IsLinkTarget` accepts the Rhino 8 `CSharpComponent` and **refuses
  the obsolete `Component_CSNET_Script`**, which is the language guard working; and Set Script I/O
  reads its target through the transmitter's link rather than needing one of its own.
- **S06** — the writer half ran and answered. The critic half could not be reached; see below.

- **S04 and S05 between them exercise both transmitter paths**, so the two ways Physalia writes to
  your canvas — a whole node graph, and code inside a component you own — are both proven here.

**The other eight could not be live-run in this session, and the reason is not the presets.** In
the Rhino process reached by the scripting bridge used to build these, `cmd.exe` cannot resolve
**any** bare command name — not `node`, not even `where` — although `%PATH%` expands correctly and
`C:\Windows\System32\where.exe node` finds node perfectly. Codex ships as a `.cmd` shim that runs
`node`, so every Codex round dies with `'"node"' is not recognized`. Claude Code is unaffected
because it is a real `.exe` resolved by absolute path. Codex ran fine earlier in this project on
the numbered presets, so this is a condition of the session rather than a Physalia regression —
but it is worth knowing that **a broken PATH inside Rhino looks exactly like a broken Codex
install**, and the way to tell them apart is to run `codex --version` from a normal shell.

### The bug S10 found

Building S10 I wired Pipeline State to Router output index 2 when `router_slots(router, 1)` had
made only two tool slots. The **last** Router output is Feedback, so the tool's Signal was wired to
the feedback path: it was never dispatched and never advertised, and the model would simply have
been told the tool does not exist. Nothing errors, no sweep can see it, and the canvas looks right.

`check_pairs.py` now walks every Router's last output in every preset and reports anything but a
Feedback sender on it. All 50 tool slots across the 26 presets are clean.

## Building them

`tools/presets/` holds the scripts that generate these files, driven through the Rhino MCP:

- `phybuild.py` — the shared helpers.
- `build_NN_*.py` — one per numbered preset. `build_sNN_*.py` — one per scenario preset.
  Re-run one to regenerate its `.phy`.
- `verify.py` — reads a written `.phy` back the way the loader does.
- `shoot.py` — renders a harness's canvas to a PNG, so a layout can be looked at.
- `liverun.py` — places a `.phy` and drives one real round through it, no chat window needed.
- `test_07_playground.py`, `test_08_timer.py` — the two headless behaviour tests.
- `audit.py` — sweeps every written `.phy` for anything machine-specific. Runs OUTSIDE Rhino: a
  `.phy` is a zip and its `harness.gh` is raw deflate, so reading the bytes answers the question
  directly and in under a second. Exits non-zero on a finding.
- `check_pairs.py` — every wireless Feedback pair, every grip link and every Router tool slot,
  in every preset, AFTER the id reissue a load performs. These are the three things that break
  silently and completely.
- `coverage.py` — which components the set demonstrates and which it does not.
- `build_all.py` — rebuilds the set and runs the Rhino-side check, logging to a file.

`phybuild.core_loop()` builds the six components every pipeline repeats — Chat, System Prompt,
Conversation Log, a Model, LLM Call and the reply path. Hand-wiring that for each new preset is
how a Feedback ends up pointing at nothing.
- `coverage.py` — every component the plug-in offers against every component the presets use.
- `check_pairs.py` — confirms every wireless Feedback pair still resolves to a Collector *after* the
  id reissue a preset load performs, and does the same for the Token Count, Set Script I/O and
  Delegate grip links. 32 pairs and 4 links across the set.

To rebuild and verify the lot:

```
# in Rhino, through the MCP:  exec(open("tools/presets/build_all.py").read())
python tools/presets/audit.py
```

Last whole-set check across all 26: audit clean, 73 Feedback pairs resolved, 7 grip links resolved,
50 Router tool slots correctly routed, 0 problems.

All three are worth re-running after any change, because every failure they look for is silent: a
preset carrying somebody else's endpoint name; a Feedback whose collector guid no longer resolves,
which swallows the signal, hands it nowhere and errors about nothing; and a tool wired to the
Router's feedback output, which is never dispatched and never advertised.

**If you script against Rhino this way, retire a document with `RemoveObjects` and *then* `Dispose`,
never `Dispose` alone.** Every component's `RemovedFromDocument` is what releases its
subscriptions — a Rhino Document grounder holds thirteen RhinoDoc handlers, a Project Folder holds a
FileSystemWatcher — and reading these presets back a few dozen times with a bare `Dispose()` left
enough stale handlers to slow the session until a trivial script could not finish. `phybuild.retire()`
does it in the right order.
