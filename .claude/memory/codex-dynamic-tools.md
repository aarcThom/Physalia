---
name: codex-dynamic-tools
description: "How Codex calls Physalia's LLM Tools — dynamicTools declared, the call DEFERRED back to the Router, session stays warm (2026-08-16)"
metadata: 
  node_type: memory
  type: project
  originSessionId: 4b03585f-0a85-4efa-88ff-01df7c93a74e
  modified: 2026-08-17T05:51:37.151Z
---

Added 2026-08-16, on top of [[codex-provider]]. Codex is the only local-CLI provider that can call
Physalia's LLM Tools. **Nothing about wiring a canvas changed** — Router, tool nodes, Feedback,
Collector, Conversation Log are all untouched, and so is Claude Code.

**The shape: defer, don't service.** Codex's `dynamicTools` wants the CLIENT to execute the tool and
answer inside the turn. Physalia can't — tools are GH components that run across solves — so the
call is answered with a refusal and handed to the Router instead:

1. `thread/start` declares `dynamicTools: [{type:"function", name, description, inputSchema}]`,
   a 1:1 match for `LlmToolDefinition`. EXPERIMENTAL field → `capabilities.experimentalApi: true`,
   opted into **only when tools exist** so the plain path keeps the handshake it was verified with.
2. The call arrives as an `item/tool/call` **server request** and blocks the turn until answered.
   Answer `success:false` + "deferred, do not retry, the result comes in the next user message",
   then `turn/interrupt`.
3. Emit the call on the final chunk as `ToolCalls` → LLM Call mints the Aux signal → Router.
4. **Session stays warm**: the tool-call turn counts as consumed, so the result returns as a normal
   one-message delta (1.2-1.8s measured, no reseed, no cold start), carried as TEXT
   `[Tool result: id:…]` worded to match `ConversationHelpers`.

**The defect that made version 1 unusable, and the fix.** `turn/interrupt` does NOT reliably stop
generation before the model reacts to the deferral. Measured: "I couldn't read the document units
from this session. If you try again, I can check it." — which would have become the assistant turn's
text in the chat and the log. Fix: **drop every text/reasoning/completed-message line that arrives
after the first tool call.** Don't race the interrupt, discard the tail. The surviving run-up makes
the assistant turn preamble + tool_use, exactly the HTTP providers' shape. Keep the interrupt anyway
— it cuts generated tokens.

**Measured facts:** calls arrive SEQUENTIALLY (each answered before the next is made), never batched
like Anthropic's parallel tool_use — so multi-tool work costs more rounds, not more wiring. The
whole 4-round exchange (call → result → call → result) plus a no-tools regression runs green through
`CodexProvider` against the live CLI.

`ToolSchema.Parse` (new, `Core/Common`) is now the ONE reading of `InputSchemaJson`;
`ProtocolProviderBase.ParseToolSchema` delegates to it so the HTTP providers and Codex cannot drift.

**Not verified:** whether an older CLI silently DROPS `dynamicTools` rather than erroring. If it
does, the model would simply never call a tool and there is no way to detect it from the response.

**Still true and now honest:** `ToolsGrounding` writes "the only tools available to you are…" into
the system prompt. On Claude Code that is still a lie (no definitions are sent); a warning on
LLM Call when `Instructions.Tools` is non-empty and the provider cannot carry them is the open
follow-up.
