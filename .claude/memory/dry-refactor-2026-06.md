---
name: dry-refactor-2026-06
description: Codebase-wide DRY refactor of 2026-06-10 — the shared base classes it introduced in Core and GH
metadata: 
  node_type: memory
  type: project
  originSessionId: 9070a607-d646-44b4-a78e-a43dc097a34d
  modified: 2026-07-26T05:03:45.808Z
---

Codebase-wide dedup, 2026-06-10. All GUIDs, param names and serialization keys preserved. Shared infrastructure it introduced:

- **Core:** `Providers\ProtocolProviderBase` (HttpClient, `TryGetConfig<T>` guard, `SendStreamingRequestAsync`, `SendForStringAsync`, `ReadStreamLineAsync`, `ParseModelIdsFromDataArray`) — all three protocol providers rebase on it; **wire-format parsing stays per-provider**. `Common\HttpErrorMapper.MapStatusCode` is the single status→`LlmErrorKind` source (also used by AsyncTokenEstimation, LlamaCppServerQuery, ModelList). `Tokens\TokenEstimationHelpers` (overhead constants + ExtractText). `Validation\JsonExtractor` (ExtractJson/PrettyPrint moved out of Schema Validator).
- **GH:** `Components\Models\ModelComponentBase` (Anthropic/Gemini Model — NOT OpenAICompatibleModel, which is structurally different: auto-detect first model, no Picker). `Components\Models\TweakerComponentBase<TConfig>` (all three Tweakers). `Goo\PhyGoo<TGoo,T>` + `Parameters\PhyParam<TGoo>` (all 6 Goo + 6 Param classes). `Attributes\GripLinkAttrib` (drag-to-link grip state machine; FeedbackAttrib multi-link/bottom-anchor, PyTransmitterAttrib single-link/top-anchor).
- **Deleted dead code:** `PythonTest.cs` + `PythonTestAttrib.cs` (superseded by PyTransmitter).
- Tweaker/Model nicknames unified to uppercase (t/p/k → T/P/K) per convention. llama.cpp `count_tokens` non-success now maps status codes (was always Network).

Related: [[arrow-dry-refactor]] (the later grip/arrow unification), [[tier1-refactoring]].
