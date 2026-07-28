# Incremental building — one stage at a time

Status: implemented 2026-07-27 on `feat/incremental_nodes`. **Not yet run in Rhino.**

## The problem

Physalia's generation loop was single-shot with repair: the model writes the whole definition,
Physalia places it, and the guardrails feed defects back until the graph solves. Everything
downstream of the LLM Call reports on the graph *that exists*, which is exactly why the loop
cannot tell a correct first slice from a finished definition — both measure clean.

So the model was writing 40-component houses against nothing but arithmetic it did in its head,
and discovering at the end that the roof did not meet the walls. Feedback arrived after every
assumption had already been committed.

The fix is to make the unit of submission a **stage** rather than a definition: place a slice,
solve it, measure it, and author the next slice against the measurements. Every assumption gets
tested while it is still one patch away from being wrong.

## The loop

```
Chat ─→ Conversation Log ─→ LLM Call ─→ Detect JSON ─→ Build Plan ─→ Schema Validator
                  ↑                                         │              ↓
                  │                                    (Progress)   GH Definition Validator
                  │                                         │              ↓
                  │                                         │       Component Resolver
                  │                                         │              ↓
                  │                                         │      Required Input Check
                  │                                         │              ↓
                  │                                         │      Component Transmitter
                  │                                         │              ↓
                  │                                         │      Runtime Health Check
                  │                                         ↓              ↓
                  └── Feedback Collector ←── Stall Guard ←── Geometry Report
```

The only structural change from the single-shot rig is that **the success path now loops too**.
Previously a clean placement ended in silence; now the Geometry Report's measurements go back to
the Conversation Log as a user turn, and the model answers with the next stage. The loop still
terminates the same way it always did: the model replies in prose, Detect JSON swallows it, and
nothing fires downstream.

## The plan block

The model declares its stages in plain text ahead of its JSON:

```
<plan>
goal: A gabled house on the XY plane, 8m x 12m, sitting on the ground.
1. Ground floor mass
2. Gabled roof
3. Window openings
now: 2
</plan>
{ …the ghpatch that adds stage 2… }
```

It is prose, not a document field. `JsonExtractor` already takes the last JSON block in a
response and discards everything around it, so the block reaches the conversation history intact
and never reaches the placement path.

**This was deliberate.** The obvious alternative — a `buildPlan` property inside the GhJSON —
would have to survive Physalia's schema, the GhJSON library's own schema, and its deserializer,
and a rejection anywhere in that chain fails a submission for a reason that has nothing to do
with the graph. Prose ahead of the JSON is a shape the pipeline already supports.

`now:` is the stage the response *builds*. On a correction round it stays put; it advances only
when the previous stage measured correctly. A response that omits it leaves the tracker's stage
unchanged rather than advancing on a guess — the round where that matters most is a correction
round, and guessing forward would mark an unbuilt stage as built.

## Build Plan (`Components/ControlFlow/BuildPlanTracker.cs`)

A tap, not a gate: the response always passes through unchanged on the single Signal output, so
wiring it in can never cost a submission. It parses the block (`Physalia.Core.Planning`), holds
the plan for the session, and renders a **progress digest** on its `Progress` text output —
stages built, stage just placed, stages outstanding, and the instruction that decides whether the
loop continues.

Session-only, like every other lifecycle state. A reopened document has no plan until the next
response carries one.

## Why the digest owns the "what to do next" line

The Geometry Report used to close with *"if the geometry matches your intent, reply in plain
prose"*. That is right for a single-shot generation and fatal for an incremental one: a correct
stage 1 measures exactly as clean as a finished definition, so the model is handed an exit on the
first round and takes it.

So the digest carries the instruction instead, and the Geometry Report defers to it — detected by
the digest's own marker (`BuildPlanParser.DigestMarker`), wired through the report's existing
`Message` input. No mode flag anywhere: wire the tracker in and the report adapts; leave it out
and the single-shot wording stands. The digest leads the report, because everything after it is
the evidence it is asking the model to weigh.

## What makes a stage, and why the lint agrees

The preamble requires every stage to produce geometry and to stand on its own — every source it
adds drives something inside the same document, every required input wired or internalized. That
is not just good practice; it is what the existing lint already enforces:

- `LintOrphan` rejects a component whose outputs are all data-only hints and that nothing
  consumes. A slider added now "for the next stage" is rejected. Geometry terminals are exempt via
  the output type hint, so a stage ending in an unconsumed Domain Box passes — which is exactly
  the shape a good stage has.
- The Geometry Report measures geometry. A stage whose result is numbers and domains comes back
  as "NO GEOMETRY WAS PRODUCED", reported as a defect.

Both constraints push toward the same stage shape, so the rule the preamble states and the rule
the code enforces are the same rule.

## Assets

| File | What it is |
|---|---|
| `Files/SYSTEM_PROMPTS/PREAMBLE/Incremental Node Graph.txt` | The staged-construction preamble |
| `Files/SYSTEM_PROMPTS/SCHEMA/Incremental Node Graph.json` | `Node Graph.json` with the mode rules rewritten for staging: one stage per response, build on what is there, a stage must produce geometry and stand alone, one group per stage |
| `Files/PRESETS/claude_code_incremental.ghjson` | The rig above, 28 components |

The preset's Signal Limiter is set to 40 rounds (a staged build spends several rounds per stage)
and the Stall Guard to 3 identical failures.

**Canvas State Grounding is not optional in this rig.** Stage N+1 addresses stage N's components
by the ids the canvas state publishes; without it the model cannot wire to what it already built.

## Deliberately left out

- **Fidelity Check.** It diffs authored intent against realization for full graphs only and passes
  patches through, so in a staged build it would check stage 1 and nothing else.
- **A stage counter that enforces progress.** The model decides when it is done; Physalia reports
  facts. Making the tracker refuse to let the loop end would mean Physalia adjudicating design
  completeness, which it cannot do.

## Open

- Not yet run in Rhino. The parser and the shipped schema's examples are covered by
  `Physalia.Core.Tests`; the component wiring and the preset are not verifiable outside GH.
- `Files/PRESETS/claude_code_node.ghjson` has **stale paramIndex values** — its Conversation Log
  wires target the pre-Human-Tools input order (Grounding→1, Prompt Signal→2) and its LLM Call
  Signal targets index 3 where the parameter now sits at 2. `paramName` is correct throughout, so
  the rig evidently resolves by name; worth confirming and re-exporting. The new preset writes
  both forms correctly.
