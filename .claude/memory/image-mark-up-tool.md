---
name: image-mark-up-tool
description: "The Image Mark Up human tool (2026-08-21) — why send-mode snapshots had to invert their flow through the page, and the design rules the editor holds to."
metadata: 
  node_type: memory
  type: project
  originSessionId: 00c332bc-4327-401f-8a58-85ce366d3e46
  modified: 2026-08-22T06:26:58.766Z
---

Built 2026-08-21: **Image Mark Up**, a human tool that puts an image editor in front of every image
the human sends. Core `ImageMarkUpTool` marker record, `Components/HumanTools/ImageMarkUp.cs`,
`ConversationLog.HasImageMarkUpTool`, and `Physalia.UI/src/lib/chat/ImageEditor.svelte`.
Full inventory prose in CLAUDE.md's Human Tools row.

**The load-bearing discovery: attach mode and send mode are not the same problem.**
An attach-mode capture already round-trips through the page (`attachSnapshot`), so the editor just
sits in the middle of a path that existed. A **send-mode** capture never touched the page at all —
`Chat.SendGeometrySnapshotFromWindow` captured and minted the turn host-side in one go. Marking it up
means inverting that: new verbs `marksnapshot`/`markviewsnapshot` capture and hand the image to the
page (`markUpSnapshot`) minting NOTHING, and a confirm comes back as a submit payload carrying a new
`kind` field, routed to `Chat.SendMarkedSnapshotFromWindow`. Two rules fell out of it:

- **The page gets an image to draw on, never the text that will speak for it.** The message is
  re-read off the wired tool at send time, so `text` on that payload is empty by design.
- **The grant is re-checked at confirm, not only at capture** — the same discipline the prompt path
  already applies to its images, for the same reason: the wire can change while the editor is open.

**Cancel means two different things, deliberately** (the user's call, asked and answered):
attach mode / an already-attached image → the plain image survives, only the mark-up is discarded;
send mode → there is no plain attachment to fall back to, so cancelling abandons the capture.

**The text tool has no `<input>`, and that is the fix, not an optimisation** (2026-08-21, reported
from Rhino: "the text tool does not work" — no box appeared at all). An overlaid input has to be
FOCUSED to receive a keystroke, positioned by `offsetParent` arithmetic, and kept clear of the frame's
`overflow-hidden`; any one of those failing in an embedded WebView leaves a tool that silently does
nothing. A note is now typed through the editor's own window keydown handler and drawn ON the canvas
with a caret, by the same path as a pen stroke — so it cannot fail differently from the pen. Cost:
no IME composition and no caret movement inside a note, which a few words of mark-up does not need.

Same fix, second lesson: **mark sizes are computed from the canvas's LIVE `getBoundingClientRect`**
(`imagePerCss()`), the same measurement pointer positions are mapped with — not from the `clientWidth`
binding, which can lag a frame during a resize and put a mark at the wrong size for the coordinates it
was drawn at.

**Editor design rules worth keeping:**
- Marks are OBJECTS in the image's own pixel space, flattened only on confirm. That is what lets the
  eraser lift a mark off the picture underneath (object-level: one stroke/note/arrow is one mark),
  what makes undo/redo exact, and what keeps the committed PNG at capture resolution.
- Sizes are STORED in natural pixels but CHOSEN from the on-screen scale, so "12pt" means 12pt as the
  human sees it whether the capture is 900px or 3000px wide.
- One eraser gesture is one undo step, and a gesture that hits nothing takes none — an undo that
  visibly does nothing is worse than no undo.

**Verified by driving the built bundle in headless Chrome** — but the FIRST harness gave a false pass
on the text tool, because synthetic `dispatchEvent` events run no default actions and so can say
nothing about focus. The harness now injects **trusted** input over CDP and asserts on canvas PIXELS
(a caret appears on click, typing grows the red count, backspace shrinks it, an eraser drag removes
the stroke and leaves the arrow and note). See [[headless-chat-ui-testing]]. Chrome passed the
input-based version too, pre-fix — the failure was WebView-only, so **the input-free text tool is
still unconfirmed in Rhino**, as is everything else here.

Related: [[view-snapshot-human-tool]], [[human-tools-split]], [[settings-ownership]].
