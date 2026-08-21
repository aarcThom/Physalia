---
name: component-description-hooks
description: Every Physalia component owns its OWN tooltip text — shared bases expose abstract description properties instead of hard-coding one string for all subclasses.
metadata: 
  node_type: memory
  type: project
  originSessionId: 1ea4b5fb-818d-4f2f-9323-808c8b7664f9
  modified: 2026-08-21T09:14:06.621Z
---

2026-08-21: all ~79 component descriptions and every input/output tooltip were rewritten to read
as plain English written for a Grasshopper user, not as internal notes. Two rules came out of it,
and both are enforced by the compiler now:

**1. No shared tooltip text.** A base class that registers a parameter on behalf of its subclasses
must take the description from an **abstract** property, never a literal. Added:

- `RoutingComponentBase<TData>` → `SignalInputDescription`, `SignalOutputDescription`,
  `FailSignalDescription` (all abstract). `SignalOutputDescription` covers the "Success Signal"
  output, or the single "Signal" output when `HasFailOutput` is false; the five single-output
  components return `string.Empty` for the Fail one, and `CompactionComponentBase` **seals** it
  empty for the whole compactor family. Abstract rather than virtual on purpose: a new guardrail
  cannot compile until it says what its own trigger does.
- `LlmToolComponentBase` → `SignalInputDescription`, `ToolOutputDescription`, `ResultOutputDescription`
- `HumanToolComponentBase` → `ToolOutputDescription` (the output is the only thing on the component,
  so a shared description would have been the entire tooltip)
- `ModelComponentBase` → `ModelIdDescription` (joins the existing `ApiKeyDescription` / `ModelOutputDescription`)
- `TweakerComponentBase<TConfig>` → `TopPDescription` (joins `TemperatureDescription` etc.)
- `CanvasStateGrounder` → `GroundingOutputDescription`, virtual, overridden by
  `PhysaliaGroupGrounder` — "the whole canvas" vs "one group on it" IS the difference between them.

Keep **parallel structure** across siblings (all the LLM tools read "Advertises X to the model:
args in, result out…") but never the same sentence twice. Two checker scripts were used and are
worth re-running after adding components: one for cross-file duplicate description literals, one
for repeats inside a single file. 239 descriptions, zero duplicates.

**The two legitimate exceptions**, both variable-parameter sets whose members are genuinely
interchangeable: `MergeSignal`'s `InputDescription` (one text for every join input) and `Router`'s
generated output description (one text, and the output renames itself to the wired tool anyway).

**2. Voice.** Say what the component does for the person, not how it is implemented. "Fires when…"
not "Latched signal minted when…"; "the text it carries" not "its payload"; drop
consume-exactly-once / monotonic / deterministic unless the user can act on it. Keep the words that
appear on the canvas (Signal, Payload, Instructions, `.ghjson`) capitalised as references.
Descriptions are **UI only** — nothing reads them programmatically, so rewording is safe.

See [[design-fork-then-build-through]] for the working style this followed.
