---
name: claudecode-provider-perf
description: "ClaudeCode CLI provider: visible thinking needs the UNDOCUMENTED --thinking/--thinking-display flags (MAX_THINKING_TOKENS is the wrong lever) plus a parser that doesn't drop thinking_delta; warm session works; cold start is native-binary-bound"
metadata: 
  node_type: memory
  type: reference
  originSessionId: d3fbb2b4-2915-4b6d-85a6-a6600380033d
  modified: 2026-08-14T12:18:32.760Z
---

**REVERSED 2026-08-14 — the CLI thinks again and its thinking is VISIBLE. The lever is two UNDOCUMENTED FLAGS, not an env var.**

Two independent defects, both fixed; each alone leaves thinking invisible.

1. **The provider dropped `thinking_delta` on the floor** — the parser matched only `text_delta`. That is what made the 2026-06-18 "freeze" a freeze: ~85s of real progress with nothing rendered. `SendTurnAsync` now re-emits thinking inline as `<think>…</think>` (lazy open, `content_block_stop` closes, any tag still open closed on the final chunk) — exactly what `AnthropicProtocolProvider` does, so the chat UI renders it and `ThinkingTags` strips it from resent history. Needed two new `LineKind`s, `ThinkingDelta` + `BlockStop`; the tag state spans lines so it lives in the streaming loop, not the per-line parse. `ThinkingDelta` deliberately does **not** set `emittedText`, so a thinking-only turn still falls back to the full result text.

2. **The CLI was returning EMPTY thinking.** Measured on 2.1.232: `thinking_delta` payloads arrive with `thinking` = `""` (len=0). Decompiled from the binary, the CLI computes display as `tLs({explicitDisplay, isNonInteractive, outputFormat, verbose})` — with no explicit value a `-p` / `stream-json` run returns `undefined`, and a later pass (`LBu`) then **force-sets `display:"omitted"` for any non-interactive session** unless the display was marked explicit. So thinking runs, bills, and carries no text. Fix: pass **`--thinking enabled --thinking-display summarized`**. Both are **absent from `--help`** — found by scanning strings in `claude.exe`. Gated behind `MinThinkingFlagsVersion` (2.1.232, the version verified) exactly like `--safe-mode`, since an old CLI rejects an unknown flag and exits.

**`MAX_THINKING_TOKENS` is now unset, and must stay unset.** Setting it to `0` disables thinking; setting it to a positive value is *worse than useless* — the CLI maps the env var to the **deprecated manual form** `{type:"enabled", budgetTokens:N}` (its own `--max-thinking-tokens` help reads "[DEPRECATED. Use --thinking instead for newer models]"), and that form is 400-rejected on Opus 5 / Sonnet 5 / Fable. Unset, the CLI defaults to `{type:"adaptive"}` — the same form `AnthropicModelDefaults` sends on the API path. **My first attempt at this fix set it to 8192 and did not work**; the env var was never the lever.

**Debugging method that actually found it** (repeat it rather than reasoning about the CLI): run `claude.exe` directly with `-p --output-format stream-json --include-partial-messages --verbose`, census the line types, then print the `thinking` field length. `len=0` means display is omitted — a request-shaping problem, not a parser one. Then scan the binary for the lever: `[System.Text.Encoding]::Latin1.GetString([IO.File]::ReadAllBytes($claude))` + regex (see [[inspecting-rhino-assemblies]] for the same technique on Rhino DLLs).

The second half of the original justification — "the Anthropic API provider sends NO thinking config, so it never does this" — was **already stale** when it was written down: `AnthropicModelDefaults` sends `thinking:{type:"adaptive", display:"summarized"}` for every think-by-default model (Opus 5, Sonnet 5, Fable), covered by `Build_Opus5_Unspecified_DefaultsToAdaptiveSummarizedNoSampling`. The CLI provider was the odd one out, not the match.

---

Original 2026-06-18 diagnosis, kept for the measurements:

**Root cause of the "freezes on real prompts" report: extended thinking.** A trivial prompt is fast, but a real "generate geometry to spec" prompt made the CLI emit ~85s of `thinking_delta`s (104 of them) before the first `text_delta`. The provider's parser filters thinking deltas (only `text_delta` is yielded), so the LLM Call sees nothing the whole time = apparent freeze. The Anthropic API provider sends NO thinking config, so it never does this. Fix: `BuildStartInfo` sets env `MAX_THINKING_TOKENS=0` (verified: thinking deltas 104→0, first text 87s→2.3s). `--effort low` only halved it; `MAX_THINKING_TOKENS=0` is the right lever. Remaining time on a big generation is pure output-token generation (~47s for ~4k tokens), same as the API would pay; the LLM Call shows nothing until the turn ends for BOTH providers — a possible future UX improvement, not the bug. Also fixed: pipes weren't pinned to UTF-8, so multibyte output (e.g. `→`) was corrupted by the Windows code page — `BuildStartInfo` now sets `StandardInput/Output/ErrorEncoding` to no-BOM UTF-8.

ClaudeCode provider (`src/Physalia.Core/Providers/ClaudeCode`) latency, measured 2026-06-18 against `claude.exe` 2.1.181 (224 MB native build at `~/.local/bin/claude.exe`):

- Warm session **works**: one persistent `claude -p --input-format stream-json` process handles many turns, keeps context, and exits only when stdin closes. Warm turns ~1.1s ≈ a direct Anthropic API call. The warm-session refactor (commit 808f822) is the real perf fix, not the launch flags.
- Cold start ~1.8–3s is dominated by the **native binary launch + auth**, NOT by customization loading — flag-slimming barely moves cold start in a light workspace (A/B was within noise). Parity is only achievable on warm turns; a first cold call can't equal a raw HTTP POST.
- `ClaudeCodeSession.BuildStartInfo` now adds `--safe-mode` + `--no-session-persistence`, sets an isolated empty temp cwd (`%TEMP%/physalia-claudecode`), and sets `CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC=1` (+ DISABLE_AUTOUPDATER/TELEMETRY/ERROR_REPORTING). Validated: these keep OAuth (`claude auth login`) working and stop the CLI loading whatever workspace (CLAUDE.md/MCP/skills) Rhino launched in — the main real-world win is in heavy workspaces.
- Do NOT use `--bare`: its help says auth is read only from `ANTHROPIC_API_KEY` (OAuth/keychain ignored), which breaks the provider's no-API-key design. `--safe-mode` keeps auth + model selection working while disabling CLAUDE.md/skills/plugins/hooks/MCP.
- Warm reuse is silently defeated if the system prompt changes per turn (forces reseed = fresh process). System Prompt's prompt is deterministic so it stays warm; SessionKey = LLM Call InstanceGuid. If it's ever "slow every turn," check that the delta path in `ClaudeCodeProvider.StreamAsync` is hit (conversation.Count == ConsumedMessageCount+1, last msg Role.User). See [[physalia-repo-gotchas]].
