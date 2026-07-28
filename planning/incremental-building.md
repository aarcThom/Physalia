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

## What the first live session showed (2026-07-27, Vancouver House + White House)

The loop worked: three build episodes, each terminating in prose, and `now:` correctly held its
stage across every correction round. 33 inference rounds, 10 of them corrective. Three defects
surfaced, all now fixed.

**The tracker was wired between the Runtime Health Check and the Geometry Report**, so its payload
was that turn's placed GUIDs and it could never see a plan block. Not one digest was produced all
session; every report carried the single-shot closing line. The model kept to its plan from the
preamble anyway, which is exactly why nobody noticed — the safety net was inert and silent. The
tracker now classifies the payload it got (GUID list → wired past the Component Transmitter; bare
JSON → wired past the Schema Validator), says so as a **Warning** with the remedy, and captions
itself `no plan` on the canvas.

**Construction points destroyed the containment analysis.** A point has a zero-size box, so it
lies inside every solid, and each Construct Point emitted one containment line per solid: 42 of
54 reports hit `MaxContainmentLines` with nothing but `'Base A' bbox lies entirely inside 'Tower
Mass' bbox` eight times over. Buried geometry is one of the two things the report exists to catch
and it could not have reported one. `SpatialParts` now excludes point-only components from the
cluster and containment analysis while keeping them in the per-component listing, where their
coordinates are genuinely used.

**One dropped closing brace produced feedback that sent the model the wrong way.** The scan hit a
mismatched closer rather than running off the end, so `LooksTruncated` stayed silent; the
extractor then stepped *into* the broken document and recovered the `components` array, which the
validator reported as "Value is array but should be object" — for a document whose root is plainly
an object. Two identical retries before the model recovered by luck. `ScanOutcome` now separates
`RanOffEnd` from `Mismatched`, `LooksMalformed` reports the latter, `CollectBareJsonCandidates`
skips a document-shaped opener **whole** instead of walking into it, and the Schema Validator says
the brackets do not balance and where to look. Verified against the session's actual 4,989-char
payload.

### Not fixed, worth knowing

- **Fidelity Check erroring on every placement** (20×): `The Definition input did not parse as
  GhJSON (… character '#')` — its Definition input is wired to the wrong output on that canvas. It
  is inert in a staged build regardless (full graphs only), which is why the preset omits it.
- **Cap Holes failed identically in both balcony stages** — 42 single-face open breps in, 42 nulls
  out. Cap Holes needs planar openings on a joined brep; a lone extrusion surface has no hole to
  cap. Worth a `componentNotes` entry.
- **`rounding` authored directly under `extensions`** instead of inside `gh.numberslider`: one
  round lost.
- **Context growth**: system prompt 85k → 127k chars over 41 turns, on top of ~20 documents and
  ~20 reports in the history. In a staged build the model's own prior documents are redundant with
  the canvas state grounding in a way they are not in ordinary chat — the canvas already says what
  they built. That makes this rig an unusually safe candidate for the AnchoredWindow compactor.

## Open

- The loop itself is proven in Rhino (see above). Still unverified there: the `SpatialParts`
  containment filter and the tracker's new diagnostics — both live in `Physalia.GH`, which has no
  test project. The extraction fixes are covered in `Physalia.Core.Tests` and were checked against
  the real session payload.
- The spatial analysis (clustering, containment) is pure geometry reasoning sitting in a GH
  component, so it cannot be unit-tested. Worth lifting into Core if it grows again.
- `Files/PRESETS/claude_code_node.ghjson` has **stale paramIndex values** — its Conversation Log
  wires target the pre-Human-Tools input order (Grounding→1, Prompt Signal→2) and its LLM Call
  Signal targets index 3 where the parameter now sits at 2. `paramName` is correct throughout, so
  the rig evidently resolves by name; worth confirming and re-exporting. The new preset writes
  both forms correctly.
