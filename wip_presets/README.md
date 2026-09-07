# Teaching presets

A set of `.phy` harnesses meant to be read, not just run. Each one is a working pipeline with
every stage numbered, headed with a blue panel, and explained in a yellow one — in plain English,
on the canvas, where you are looking when you have the question.

They are designed to be worked through in order. Each assumes the ones before it and says so.

## How to use them

1. Open the Physalia chat window (bottom-right widget on the Grasshopper canvas).
2. On the **Home** screen, choose **Place predefined harness** — or drop the `.phy` file straight
   onto your canvas.
3. Right-click the Harness node and choose **Edit Harness** to go inside and read it.
4. Double-click the Harness node to open the chat window on the Chat inside it. Each preset's
   greeting says what to try first.

Nothing is armed and nothing is linked to your own components until you do it yourself. Presets
that need setting up say so in their intro panel and in their chat greeting.

## The set

| | Preset | What it adds | Model |
|---|---|---|---|
| 01 | Talk to a Model | The core loop, and the wireless return path that makes it possible | Claude Code |
| 02 | What the Model Knows | Grounding: six components describing your document, canvas, units and folder | Claude Code |
| 03 | Tools the Model Can Call | The Router and six tools; three return paths | Codex |
| 04 | Building on the Canvas | Seven guardrails, then real components placed on your canvas | Claude Code |
| 05 | Writing Python for You | Code pushed into a Rhino 8 Script component, fitted to its parameters | Claude Code |
| 06 | Letting It Look and Walk | Take Snapshot and Move In Space; the harness's own inputs and outputs | Codex |
| 07 | Making the Pipeline Decide | Branching — Declare plus a button-driven playground for five relays | Codex |
| 08 | Running Without You | Triggers, and the three things that bound the bill | Claude Code |
| 09 | Files, Downloads and Drawings | The project folder, Download File, Read File, Read PDF | Codex |
| 10 | When the Conversation Gets Long | Compaction, and what each kind costs you as well as saves | Claude Code |

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
- **04** — the whole gauntlet passed in one round and placed a working grid-of-circles definition.
- **05** — pushed a running-cumulative-total script into a linked Python 3 Script component.
- **06** — reported its position, stepped `{0,0,0}` → `{0,4000,0}`, and looked north.
- **07** — declared `build`, the Gate opened, and the playground's five relays behaved exactly as
  their notes claim.
- **08** — the Timer fired 20 times unattended; the Throttle paced it, the Limiter capped it, and
  the Budget Guard refused.
- **09** — listed the folder, errored correctly on a missing file, downloaded one and read it.
- **10** — crossed the threshold, compacted, and then still knew the number from turn 1 while
  honestly having lost the middle of the conversation.

## Building them

`tools/presets/` holds the scripts that generate these files, driven through the Rhino MCP:

- `phybuild.py` — the shared helpers.
- `build_NN_*.py` — one per preset. Re-run one to regenerate its `.phy`.
- `verify.py` — reads a written `.phy` back the way the loader does.
- `shoot.py` — renders a harness's canvas to a PNG, so a layout can be looked at.
- `liverun.py` — places a `.phy` and drives one real round through it, no chat window needed.
- `test_07_playground.py`, `test_08_timer.py` — the two headless behaviour tests.
