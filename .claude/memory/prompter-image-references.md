---
name: prompter-image-references
description: "Prompter's \"/<alias>\" inline image references — Core parser, signal flow, alias rules, and the PrompterAttrib grip gotcha (2026-06-13)"
metadata: 
  node_type: memory
  type: project
  originSessionId: 9070a607-d646-44b4-a78e-a43dc097a34d
  modified: 2026-07-26T05:03:33.437Z
---

Prompter gained input 0 = **Image Sources** (`Param_ImageSource`, list, optional) fed by Image Gatherer ([[resources-tab-image-gatherer]]). Typing `/<alias>` in a prompt and submitting (Shift+Enter) resolves each token to the referenced image **inline** (text split around the token; token text removed) → interleaved `TextContent`/`ImageContent` blocks delivered to the model.

- Pure parser in Core: `Physalia.Core/ConvoInstruct/PromptImageResolver.Resolve(prompt, IReadOnlyDictionary<string,ImageSource>) → ResolvedPrompt(Text, Blocks)`. `/` matches only at a word boundary (so URLs/`and/or`/paths are safe); known aliases matched longest-first, case-insensitive, requiring a non-word boundary after the alias; unknown `/x` stays literal. `Text` (token-stripped) = signal payload.
- Flow: Prompter caches alias→source map each `SolveInstance` (submit fires from UI, outside solve) → `LatchSuccess(text, contentBlocks: blocks)` → Conversation Log's `ApplySignal` prompt case records `ContentBlocks` (via `RecordUserBlocks` + `Conversation.MergeIntoLastUserMessage(IReadOnlyList<MessageContent>)` overload) when present, else the old text path. Images-only prompt (blank text, has blocks) records fine.
- **Aliases are single-token (no whitespace)** so `/<alias>` is unambiguous: `ImageGatherer.SanitizeAlias` (whitespace→`-`) sanitizes defaults; the Manage Images panel rejects whitespace on alias edit. Provider adapters already serialize `InlineImage`, so no provider work.
- **PrompterAttrib gotcha:** `PrompterAttrib` fully overrides `Layout()`/`Render()` (custom panel) and renders the Objects channel itself, so GH does NOT auto-draw or auto-position param grips. Each param needs a manual `LayoutXParam()` (set `param.Attributes.Pivot` + `Bounds`) AND a custom `DrawWireGrip` call, or it's invisible/unwireable. The Image Sources input grip mirrors the output grip on the LEFT edge of the convo panel, vertically aligned (same midY); `_layoutBounds` is expanded on both sides (`x-4, _width+8`) so both grips stay clickable.

Note: Prompter itself was later deleted in the component reorg ([[component-reorg-2026-07]]) — the `/`-reference machinery lives on in the chat window. Related: [[signal-carrier-discipline]], [[rhino-geometry-tool-and-slash-t]].
