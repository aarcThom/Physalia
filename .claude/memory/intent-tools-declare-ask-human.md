---
name: intent-tools-declare-ask-human
description: "2026-09-06 — Declare (model picks a route the pipeline offers) and Ask Human (model asks a question, including a Rhino selection); every question edge returns UNANSWERED, never a guessed default."
metadata: 
  node_type: memory
  type: project
  originSessionId: 87baa99a-bc2a-4051-b3ac-90fef7c24c3a
  modified: 2026-09-07T04:16:44.451Z
---

Two tools built together because they are the two directions of intent across the human boundary.
**Declare** (`declare`, `DeclareTool`) lets the model say where the work stands; **Ask Human**
(`ask_human`, `AskHuman`) lets it ask for something only a person has. BUILT, **not run in Rhino**.

**Why:** before these, the pipeline had to INFER the model's intent from prose (Detect JSON's
heuristic, the Geometry Report asking for a prose reply, a text match for "done" that also matches
"not done yet") — and an inference about intent is wrong exactly when it matters, because a model
hedges most when it is least sure. In the other direction the only question available was yes-or-no
through the approval card, so a model missing one fact could only guess or stall.

**How to apply:**
- **Declare's routes are typed on the node** (so they ship in a preset) and generated into the schema
  as an enum, so advertised and accepted cannot drift — the `SpaceNavigator` token precedent. An
  off-list route comes back as an error that RE-LISTS the legal ones.
- Branch on it exactly: `Route` → an equality test → a Signal Gate's `Open`. A Signal Switch on the
  payload is the looser form, since the payload carries the model's own note.
- **Declare is only the SECOND tool ever to override `GroundingDirective`** (Memory is the first),
  and for the documented reason: a model not told it must declare simply answers in prose, which is
  the thing the node exists to stop the pipeline interpreting. A tool description is read once the
  model is already weighing the call; a prompt directive is read before it decides.
- **Declare's signal fires in the SAME solve as the tool result** — that is the only moment the node
  is awake, and "the round finished" is not observable from inside a tool. So a declaration wired
  into a Prompt Signal joins the tool-result turn, which `MergeIntoLastUserMessage` already handles.
  Do not try to defer it to round completion.
- **`HumanQuestionBroker` is the THIRD sibling of `ToolApprovalBroker` and `BrowserFetchOffers`, and
  must not be folded into either.** An approval blocks a call and must fail CLOSED; a fetch offer
  blocks nothing so has no timeout and no default; a question blocks a call but **has no safe answer
  to invent**. So every edge — no window, window closed, round cancelled, timeout — returns
  *unanswered*, and the tool tells the model so in as many words. Inventing an answer is the one
  truly unrecoverable failure here: the model would proceed as though a person had agreed.
- **Ten minutes, not the approval card's five**: a consent decision is a yes/no about something
  already described; answering a question means going and looking at a drawing or the model.
- **`expect: "rhino_selection"` is what justifies the seam.** "Which ones" cannot be typed. The
  selection is read HOST-SIDE at the moment the button is pressed — not when the question was asked —
  and the ids land on the node's `Selection` output as well as in the answer, so `run_rhino_script`
  can act on exactly those objects.
- Answers ride the **SUBMIT channel** under `kind: "human-answer"` (postMessage, `HumanAnswerPayload`
  parsed in `SubmitJsonPayload`), not a `phbridge://` query, because a typed answer can be a pasted
  paragraph. Skip is its own field, not an empty answer — refusing to answer and answering with
  nothing are different facts.

Related: [[signal-relay-conditionals]], [[project-file-tools]], [[memory-tool]].
