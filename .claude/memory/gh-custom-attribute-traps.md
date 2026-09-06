---
name: gh-custom-attribute-traps
description: Grasshopper platform traps for custom attributes and rename watching — verified against the shipped assembly. Read before writing any custom Layout/Render or anything that reacts to a rename or a move.
metadata: 
  node_type: memory
  type: project
  originSessionId: d239a690-60af-41ac-9249-d23231b49367
  modified: 2026-08-21T08:43:41.509Z
---

Six things about Grasshopper that are not written down anywhere and each cost a wrong fix. All
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
live.** By this evidence it only fires for the name box, so an F2 rename of an UNWIRED output goes
unnoticed and the locked interface reports the old variable name. Documented as a KNOWN GAP in
`ScriptIO.WatchTarget` 2026-08-21; still unfixed, and the override-the-setter hook is NOT available
there — those params belong to the user's script component, so there is no type of ours to override.
Closing it needs a different mechanism than a subscription.

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

**5. Overriding `Layout()` costs you every param grip.** An attribute that fully overrides `Layout()`
without calling the base gets NO automatic grip placement or drawing: each parameter needs its
`Attributes.Pivot` AND `Bounds` set by hand, plus its own `DrawWireGrip` call, or it is invisible and
unwireable. Note the pairing — `GH_Capsule.AddOutputGrip(y)` is visual only, so a grip that looks
right can still be dead to the mouse until the param's bounds agree with it. Also widen the pick
region on whichever edges carry grips, or a grip drawn past the capsule edge cannot be clicked. Found
on the old `PrompterAttrib` (since deleted) and still true of every hand-laid-out attribute in the
repo — `HarnessAttrib` does exactly this for its inlet rows. Same family as trap 4:
[[prompter-image-references]] carried the original note.

**6. Do not floor your capsule width on GH's `bounds.Width` if you add content of your own.** GH's
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

## Text input on the Grasshopper canvas needs its OWN WINDOW (2026-09-06)

**A control parented to `GH_Canvas` cannot reliably HOLD keyboard focus, so typing into it goes to the
Rhino command line.** Rhino routes keystrokes to its prompt unless the focused window is a text
control.

**Why.** `GH_Canvas` derives from `Control`, not `ContainerControl` (verified against the shipped
assembly). That breaks the chain WinForms uses to restore focus into a child: the containing `Form`
walks `ContainerControl`s, finds a plain `Control`, and puts focus back on the canvas at every
re-activation. Focus lands on the field when clicked and does not survive.

**Calling `Focus()` explicitly does NOT fix it** — tried, shipped, still broken. Getting focus was
never the problem; keeping it is.

**Grasshopper's own in-canvas editor is not a counter-example — it concedes the point.**
`GH_TextBoxInputBase.ShowTextInputBox` adds a `TextBox` to the canvas, calls `Focus()` on it, and
then **hides itself on `LostFocus`**. It is transient by design and never has to hold focus through
anything. A panel that stays on screen does.

**The fix is an owned borderless top-level `Form`** (`HarnessPanel`): `Owner =
Instances.DocumentEditor` for z-order and minimise behaviour, `ShowWithoutActivation`,
`ShowInTaskbar = false`, `AutoScaleMode.None` set before children. The cost is position — a child
gets it from its parent for free — so the host repositions on the canvas's
`LocationChanged`/`SizeChanged`/`ParentChanged` and the editor's `Move`/`Resize`. The chat window has
always accepted typing for exactly this reason: it has always been its own window.

Also verified while chasing this, and useful in itself: the canvas steals focus **nowhere** (no
`Focus()` call in `GH_Canvas`), it forwards **nothing** to Rhino, and `GH_Canvas.HasControlWithFocus`
walks `Controls` checking `Focused`/`ContainsFocus`. Decompile with `ilspycmd -r "C:\Program
Files\Rhino 8\System"`.
