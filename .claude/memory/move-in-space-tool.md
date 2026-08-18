---
name: move-in-space-tool
description: "2026-08-17 Move In Space LLM tool — derived adjacency (8 cones x 3 bands, closest-per-bucket), world-frame directions, and the new LlmToolComponentBase output hooks"
metadata: 
  node_type: memory
  type: project
  originSessionId: 13e6b92d-5e4a-4e96-afb2-c30c6aab719e
  modified: 2026-08-18T06:39:53.146Z
---

**2026-08-17: new LLM tool `move_in_space`** (`Components/LlmTools/MoveInSpace.cs` + `SpaceNavigator.cs`).
Gives the model a position in a user-supplied `Positions` lattice and lets it walk one step at a time
from a `Start Point`; the route is published on a `Traversed Points` output. That output is the point
of the tool — it turns the model's spatial reasoning into geometry the definition can build on.

**Why:** the interesting design decisions are all in how adjacency and direction are defined, and both
were settled against alternatives that look reasonable and are worse.

**How to apply:**
- **Adjacency is derived, never configured — and deliberately needs no level clustering.** Bucket each
  candidate by vertical band (same/up/down, by doc tolerance) and by one of eight 45° in-plane cones,
  then offer only the CLOSEST candidate per bucket. That single rule does all the work: collinear
  points further along a bearing become reachable one move later (so a step is a step, not a leap),
  and `up_forward` resolves to the NEXT level up rather than the top of the stack — which is exactly
  what clustering Z into levels would have been for. I started to write the clustering and it was
  dead code. Verified both properties in the harness.
- **Eight cones, not four.** Four ±45° cardinals also cover the circle with no gaps, but a 45°
  diagonal neighbour then lands on a cone BOUNDARY and can steal the "forward" slot from the true
  axial neighbour, and the label the model builds its mental map from is a lie. Eight cones give
  honest labels, keep full coverage (a rotated/scattered cloud stays navigable), and cost only a
  bigger enum.
- **World-frame directions, not heading-relative.** forward=+Y, right=+X, up=+Z. A heading is
  undefined on the first move, has to be tracked by the model across turns, and flips left/right as
  it turns — all things models handle badly. Every option also carries absolute coordinates so the
  model can reason either way.
- **`direction` is optional; omitting it looks without moving.** Nothing else tells the model where it
  starts, so without a look mode the first call is a guess. A real-but-unavailable token returns
  `IsError` re-listing the legal moves, so the model never assumes a move it did not make.
- **26 tokens are generated from `SpaceNavigator.AllTokens` into the schema enum** — advertised and
  accepted cannot drift.

**New on the shared base** (`LlmToolComponentBase`, which sealed its outputs): `RegisterAdditionalOutputs`
+ `FirstAdditionalOutputIndex` (= 2, after Tool/Result) + **`OnSolveEnd`**. The trap: publish a
call-mutated output from `OnSolveEnd`, NOT `OnSolveTick` — the tick runs BEFORE the dispatched calls,
so a move made this solve would sit on the wire a solve late. `OnSolveEnd` runs once on both the sync
and async paths. Additive hooks only, so no shipped tool's param layout shifts.

**Verification:** 33 checks green in a throwaway console harness that compiles `SpaceNavigator.cs`
directly and references RhinoCommon **without** `ExcludeAssets="runtime"` (with it, the DLL is not
copied and the run dies with FileNotFoundException) — see [[core-console-harness]], which this extends
to GH-project code that only needs `Rhino.Geometry`. **Not yet run in Rhino.** No icon yet
(`MoveInSpace.png` absent → graceful `brain.png` fallback); the set is sprite-sheet generated, see
[[component-icon-generation]].

**2026-08-18 additions:** a `Current Position` output (where it stands now, on its own — this is what
feeds Take Snapshot's `Current Location`, see [[tool-image-attachments]]), and an optional
`Position Notes` text input describing what is at each position, reported to the model whenever it
stands there. The note describes the POSITION, so it is reported on arrival AND on a look-without-moving,
not only on the move. Pairing is Grasshopper's longest-list rule, extracted to the pure
`Core/Common/ListPairing.MatchLongest` so the specified behaviour is unit-tested rather than an inline
`Math.Min` — equal lengths 1:1, a shorter list reuses its LAST note, surplus notes ignored. It has to be
done in code, not by component-level data matching, because the component reads both as whole lists in
one solve and must not iterate. Both the input and the output are APPENDED last, so no saved document's
param layout shifts.

Related: [[tool-calling-gh-loop]], [[tools-in-use-component]] (picks the node up automatically — no
registration anywhere).
