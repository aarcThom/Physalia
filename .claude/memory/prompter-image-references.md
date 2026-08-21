---
name: prompter-image-references
description: "The \"/<alias>\" inline image reference machinery — the pure Core parser, the alias rules, and where it lives now that Prompter is gone"
metadata: 
  node_type: memory
  type: project
  originSessionId: 9070a607-d646-44b4-a78e-a43dc097a34d
  modified: 2026-08-21T00:00:00.000Z
---

**The component this was written about is gone; the machinery is not.** Prompter was deleted in the
component reorg ([[component-reorg-2026-07]]) and the `/`-reference feature moved into the chat
window. Re-verified 2026-08-21: the parser is still `Physalia.Core/ConvoInstruct/PromptImageResolver.cs`,
the goo/param are still `Param_ImageSource`, and the producer is now the **Image Sources** component
(`Components/Grounding/ImageSources.cs` — renamed from "Image Gatherer",
[[resources-tab-image-gatherer]]).

Typing `/<alias>` in a prompt resolves each token to the referenced image **inline** — the text is
split around the token and the token text removed — producing interleaved
`TextContent`/`ImageContent` blocks for the model.

- **Pure parser in Core:** `PromptImageResolver.Resolve(prompt, IReadOnlyDictionary<string,
  ImageSource>) → ResolvedPrompt(Text, Blocks)`. `/` matches only at a word boundary (so URLs,
  `and/or` and file paths are safe); known aliases are matched longest-first, case-insensitive,
  requiring a non-word boundary after the alias; an unknown `/x` stays literal. `Text` (token
  stripped) becomes the signal payload. Sibling resolvers use the same shape for `/c/` (components)
  and `/t/` (tools) — see [[rhino-geometry-tool-and-slash-t]].
- **Flow:** the resolved blocks ride the signal as `ContentBlocks` and the Conversation Log records
  them via `RecordUserBlocks` + the `MergeIntoLastUserMessage(IReadOnlyList<MessageContent>)`
  overload, falling back to the text path when there are none. An images-only prompt (blank text,
  blocks present) records fine. This is exactly why `ContentBlocks` exists as a carrier at all —
  the only wire from the prompt source to the Conversation Log IS the signal
  ([[signal-carrier-discipline]], [[signal-lifecycle-summary]]).
- **Aliases are single-token (no whitespace)** so `/<alias>` is unambiguous: `SanitizeAlias`
  (whitespace → `-`) sanitizes defaults, and the manage-images panel rejects whitespace on edit.
- Provider adapters already serialize `InlineImage`, so none of this needed provider work.

The old note also carried a `PrompterAttrib` layout gotcha. That file no longer exists, but the trap
is live and generalized — a custom attribute that overrides `Layout()` gets no automatic grip
placement, and one that hand-composes a render channel skips `base.Render`. Both now live with the
rest of the platform traps: [[gh-custom-attribute-traps]].
