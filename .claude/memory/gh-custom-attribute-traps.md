---
name: gh-custom-attribute-traps
description: Grasshopper platform traps for custom attributes and rename watching — verified against the shipped assembly. Read before writing any custom Layout/Render or anything that reacts to a rename or a move.
metadata: 
  node_type: memory
  type: project
  originSessionId: d239a690-60af-41ac-9249-d23231b49367
  modified: 2026-08-21T08:43:41.509Z
---

Four things about Grasshopper that are not written down anywhere and each cost a wrong fix. All
verified by reading the IL of the shipped `Grasshopper.dll` (method see [[inspecting-rhino-assemblies]]),
not inferred. Learned building [[harness-io]]; they apply to any custom attribute in this repo.

**1. `NickName`'s setter raises NOTHING.** `GH_InstanceDescription.NickName` — which every component
and every param inherits — has a setter whose body is a bare field assignment, no calls at all. The
only members that raise `ObjectChanged(NickName)` are the right-click name-box handlers
(`Menu_NameItemTextChanged`, `Menu_NickNameChanged`, `Menu_NameItemKeyDown`). So an F2 or
properties-panel rename reaches **no handler anywhere**.

**How to apply:** never build a rename watch on `ObjectChanged`. The setter IS virtual, so OVERRIDE it
— that is the one hook that cannot be missed. If the name is editable at both ends, make the sync
two-way and cut the recursion with an equality guard; a derived-only name (one end reading the other
live) silently reverts what the user typed at the other end.

**⚠ [[script-io-grounder]]'s rename watch is built on `ObjectChanged` alone and has never been run
live.** By this evidence it only fires for the name box. Likely latent bug, untouched so far.

**2. `ExpireLayout()` is not a promise that `Layout()` will run.** `PerformLayout` is called from
about a dozen places in the whole assembly and **the paint loop is not one of them** — layout is
performed on SOLUTION, not on paint. An attribute that reconciles state in `Layout()` can therefore
sit unfired indefinitely.

**How to apply:** anything that must be current on screen goes in `Render` (which does run every
frame) or through an explicit push. Layout is for sizing, and only sizing.

**3. A MOVE raises nothing at all.** No event for a changed pivot. [[group-scoped-grounding]]'s
`MasterGroupFollower` ended up polling at idle for exactly this. Where polling is too heavy, check
for the drift in `SolveInstance` — the one thing that runs often and runs for certain.

**4. Hand-composing one render channel skips ALL of `base.Render`.** If a custom attribute builds the
`Objects` channel itself and never calls the base, Grasshopper's own render never runs — and that is
what draws the **wires arriving at the component's inputs**. Symptom: data crosses perfectly well and
no wire is painted. Every non-Objects channel must fall through to `base.Render`.

The symptom is diagnostic in general: painting and delivery are unrelated concerns, so "the data
arrives but nothing is drawn" is always a render path, never a solver one.

**5. Do not floor your capsule width on GH's `bounds.Width` if you add content of your own.** GH's
layout already reserves an icon region between the input and output columns. Reserve another and take
the larger, and the node ends up wider than anything in it, with all the slack falling on whichever
side your content is not centred against. Size the capsule from its parts instead —
`inputColumn + gap + centre + gap + labelColumn` — spending the gap on both sides and counting it
twice, so centring in what is left leaves the two equal. Keep GH's width as a floor only where it has
nothing of its own to size from.

**Measuring text at layout: use the UNADJUSTED font.** `GH_FontServer.Standard` / `.Large`, never
`StandardAdjusted` / `LargeAdjusted`. Layout runs in canvas units while the adjusted fonts follow the
canvas zoom, and layout does not re-run when you zoom — measuring with an adjusted font bakes one
zoom level into the geometry (it once reserved a third of a node at high zoom and left a hole at 1:1).
`TextRenderer.MeasureText` measures without a `Graphics`, which layout does not have. Text DRAWN at
paint time still uses the adjusted font, and should be drawn from a measured point rather than clipped
into a rect, so a name that outgrew the last measurement overhangs instead of losing its tail.
