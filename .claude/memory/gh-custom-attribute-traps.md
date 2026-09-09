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

## Closing Grasshopper reaches NEITHER `FormClosed` NOR a child's `VisibleChanged` (2026-09-07)

Both the harness panel and the chat window stayed on screen after the Grasshopper window was closed.
Two unrelated causes, each verified by decompiling the shipped assembly.

**1. Grasshopper never closes.** `GH_DocumentEditor.DocumentEditorFormClosing` sets
`e.Cancel = true` and calls `Hide()` for every `CloseReason` except `m_closeForReal` / app exit /
owner-closing / Windows shutdown — which is exactly why reopening it restores the same documents. So
`FormClosed` fires only on `Instances.CloseGrasshopper()` or Rhino shutdown (already covered by
`RhinoApp.Closing`), and a hook on it is dead for the X-click. **`FormClosing` still fires, cancelled
or not** — that is the hook, and it is the *gesture*, which is what the chat window keys on.

**2. A WinForms control is NEVER told an ancestor was hidden.** `Control.SetVisibleCore` does
`SetState(States.Visible, false)` **before** raising, and `OnVisibleChanged` forwards to children
only `if (control.Visible)` — and the `Visible` getter walks the parent chain
(`if (ParentInternal != null) return ParentInternal.Visible;`), so by then every child already reads
false. They get `internal OnParentBecameInvisible()` instead, which raises nothing. **A control hears
`VisibleChanged` when IT is hidden and never when an ancestor is**; only the form whose own state
changed raises anything. `HarnessPanelHost`'s `canvas.VisibleChanged` subscription was therefore
unreachable code for the case it was written for.

**And owning a window to a form does not cover it**: Windows hides an owned window when the owner is
MINIMISED, not when the owner is hidden.

**How to follow a host window, then:** subscribe `VisibleChanged` on the form itself — resolved via
`canvas.FindForm()`, lazily, never `Instances.DocumentEditor` (see [[harness-subdocument]] / the
panel's own note: docked, the canvas lives in Rhino's window). Hide, don't dispose, and come back
through the rebind path rather than a bare `Show()` — a hidden Grasshopper returns with its documents
intact but possibly pointed somewhere else.

**The two directions need two different hooks.** Going away keys on the GESTURE (`FormClosing`);
coming back keys on the editor's `VisibleChanged`, since a cancelled close is undone by the editor
merely being shown again. Do NOT key going away on visibility: docked into Rhino, switching panel
tabs hides the editor, and putting a conversation away over that click is not what it meant. Guard
the restore on having done the hiding, or every unrelated visibility change summons the window.

**Hiding a window instead of closing it silently breaks whatever fails closed on its ABSENCE.**
`ToolApprovalBroker`/`HumanQuestionBroker` refuse immediately when `Chat.ActiveWindow is null` — that
is what stops a tool call waiting out five or ten minutes with nowhere to ask — and a hidden window
is still an open window. So the window answers `CanAskUser` (put away with its host, not merely
"not shown yet"), the gates ask for that rather than for non-null, and anything already pending is
denied/abandoned at hide time. Same class of question for any surface with no timeout at all: a
browser-fetch offer falls back to its standalone window instead.

Eto detail worth keeping: `Visible = false` on a Form maps to WPF `Window.Hide()`
(`Eto.Wpf.Forms.WpfWindow`), so the HWND — and any Win32 ownership set on it — survives, and
`Show()` on an already-loaded Eto form is just `Visible = true` and reloads nothing.

**2026-09-08 — a panel bound on `canvas.DocumentChanged` sees the identity a step too early.**
`HarnessPanelHost` refreshes the harness panel from the canvas's `DocumentChanged`, which is the only
hook that fires for every route into a harness. But the LOAD path re-points the canvas *before* it
adopts the loaded identity: `LoadFromFile` runs `Replace(contents)` (→ `OpenInCanvas` → `canvas.Document = inner`
→ `DocumentChanged` → `Bind`) and only THEN `ApplyPackage`, which is where the manifest's name,
description and chat text land. So the panel kept showing the pipeline that had just been discarded —
and because its description/chat boxes write back on `TextChanged`, the first keystroke put that stale
text back over what was loaded. Fixed by re-pointing at the tail of `ApplyPackage`.

The general shape: **an event that fires on the SWAP cannot see anything applied after the swap.** If a
load has an identity phase separate from its content phase, the UI has to be told at the end of the
identity phase, not by listening to the content one.

Same pass closed the half-sync the `NickName` note above predicted: the panel wrote through the
setter, but an F2 on the proxy, a properties-panel edit or an undo reached nothing. The refresh rides
`OnIdleFolderSync` — already where a rename is picked up, and the only UI-safe point, since the setter
itself fires during layout, paste and archive reads. `RefreshName` guarding on `_name.Focused` is what
keeps that from fighting a user mid-type. Built and compiling; **not run in Rhino.**
