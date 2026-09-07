---
name: harness-delegation
description: "2026-09-06 — a harness as a tool of another harness: Delegate grip-links to it, Task In / Task Out are its entry and exit, paired on the inner GH_Document with no wire between them."
metadata: 
  node_type: memory
  type: project
  originSessionId: 87baa99a-bc2a-4051-b3ac-90fef7c24c3a
  modified: 2026-09-07T04:17:03.358Z
---

A Physalia pipeline has exactly ONE Conversation Log, so every subtask ever asked stayed in that one
context forever. **Delegate** (`DelegateTool`, grip-linked via `DelegateAttrib`) hands a task to
another harness and waits; **Task In** / **Task Out** (`IO/`) are that harness's entry and exit;
`DelegationBroker` (`Components/Delegation/`) runs the session. **RUN LIVE IN RHINO AND VERIFIED
2026-09-06.**

**Why:** compaction can shrink a history but cannot SEPARATE it. Without this, a twelve-room survey
is twelve tasks in one conversation, a classification and a generation share a model because they
share a wire, and a long piece of side work poisons the thread it was done on. Chosen over a
self-contained sub-agent node deliberately: a black box would be the one part of Physalia the user
could not see, edit, validate or ship — and the harness is already the plug-in's unit of a pipeline,
so a delegated one gets compaction, guardrails, presets and even another delegate for free.

**How to apply:**
- **The two ends are paired on the INNER GH_Document, never by a wire** — no wire crosses a harness
  boundary. Same device as the PDF registry, the spend ledger and the state board. A delegate needs
  no connection to Task In/Task Out and one drag is the whole configuration.
- **The inner harness sits INSIDE the outer one** (a harness within a harness), which is what lets
  the link be a grip drag at all — a drag cannot cross two documents, which is why the script
  transmitters use picker menus instead.
- **Task In is ACTIVE where Harness In is passive**, because a task is an event and nothing else will
  start it. It has **no Armed switch**: it fires only when called, so the caller's own budget and
  triggers already bound it. An arming switch there would only be a way to make a delegate
  mysteriously time out.
- **Task Out answers with the WHOLE signal**, so a sub-pipeline whose job was to look at something
  hands the image back — as a tool attachment, riding the machinery `take_snapshot` already
  established. Reaching Task Out with nobody waiting is a **Remark, not an error**: a callable
  harness is still an ordinary pipeline someone runs by hand while building it.
- **One task at a time per inner harness**, refused with that reason. One conversation and one solve
  state: two tasks would interleave and neither answer would be trustworthy. It is also the guard
  that stops most recursion, alongside an explicit self-reference check (linked to the harness it
  lives in).
- **Refuse UP FRONT, never time out**: a harness with no Task In can never receive the task and one
  with no Task Out can never answer, so both are checked before the session starts. Waiting five
  minutes to discover a node is missing is the worst version of that message.
- **An unlinked or undescribed node advertises NOTHING** — the `ApiCall` rule for the `ApiCall`
  reason: a tool that fails every call reads to the model as broken rather than unconfigured. The
  Description is what the model reads to decide whether to delegate, so it is required.
- Tool names are namespaced `delegate__<name>` (sanitized, nickname as the default), because two
  delegates in one pipeline is the normal case and the Router dispatches on the name.

## How to test it without spending an LLM round

**Drive the Delegate with Construct Tool Call.** That node mints a signal carrying a
`ToolCallContent`, and `LlmToolComponentBase` reads its calls out of the content blocks without
caring who put them there — so a hand-made call runs the delegate exactly as the Router's would, with
no model involved. Rig: harness A holds harness B plus a Delegate grip-linked to it and a Construct
Tool Call wired into its Signal; inside B, Task In wires straight to Task Out (an echo).

Verified with `{"task":"say this back to me"}`: `Task In` caption `1 received` with the task on its
output, `Task Out` caption `1 answered` (so the broker found the waiting session), `Last Task` and
`Last Answer` both the task, the round trip completing in well under a second, and the Tool output
advertising `delegate__echo`. **`Result` was correctly `None`** — a `manual:` call answers nobody,
which is the whole reason a manual batch emits no result signal.

Related: [[harness-subdocument]], [[harness-io]], [[signal-relay-conditionals]],
[[building-harnesses-programmatically]].
