---
name: claudecode-provider-perf
description: "ClaudeCode CLI provider: the 'freeze on real prompts' is extended thinking (fix MAX_THINKING_TOKENS=0); warm session works; cold start is native-binary-bound"
metadata: 
  node_type: memory
  type: reference
  originSessionId: d3fbb2b4-2915-4b6d-85a6-a6600380033d
---

**Root cause of the "freezes on real prompts" report: extended thinking.** A trivial prompt is fast, but a real "generate geometry to spec" prompt made the CLI emit ~85s of `thinking_delta`s (104 of them) before the first `text_delta`. The provider's parser filters thinking deltas (only `text_delta` is yielded), so the Reasoner sees nothing the whole time = apparent freeze. The Anthropic API provider sends NO thinking config, so it never does this. Fix: `BuildStartInfo` sets env `MAX_THINKING_TOKENS=0` (verified: thinking deltas 104→0, first text 87s→2.3s). `--effort low` only halved it; `MAX_THINKING_TOKENS=0` is the right lever. Remaining time on a big generation is pure output-token generation (~47s for ~4k tokens), same as the API would pay; the Reasoner shows nothing until the turn ends for BOTH providers — a possible future UX improvement, not the bug. Also fixed: pipes weren't pinned to UTF-8, so multibyte output (e.g. `→`) was corrupted by the Windows code page — `BuildStartInfo` now sets `StandardInput/Output/ErrorEncoding` to no-BOM UTF-8.

ClaudeCode provider (`src/Physalia.Core/Providers/ClaudeCode`) latency, measured 2026-06-18 against `claude.exe` 2.1.181 (224 MB native build at `~/.local/bin/claude.exe`):

- Warm session **works**: one persistent `claude -p --input-format stream-json` process handles many turns, keeps context, and exits only when stdin closes. Warm turns ~1.1s ≈ a direct Anthropic API call. The warm-session refactor (commit 808f822) is the real perf fix, not the launch flags.
- Cold start ~1.8–3s is dominated by the **native binary launch + auth**, NOT by customization loading — flag-slimming barely moves cold start in a light workspace (A/B was within noise). Parity is only achievable on warm turns; a first cold call can't equal a raw HTTP POST.
- `ClaudeCodeSession.BuildStartInfo` now adds `--safe-mode` + `--no-session-persistence`, sets an isolated empty temp cwd (`%TEMP%/physalia-claudecode`), and sets `CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC=1` (+ DISABLE_AUTOUPDATER/TELEMETRY/ERROR_REPORTING). Validated: these keep OAuth (`claude auth login`) working and stop the CLI loading whatever workspace (CLAUDE.md/MCP/skills) Rhino launched in — the main real-world win is in heavy workspaces.
- Do NOT use `--bare`: its help says auth is read only from `ANTHROPIC_API_KEY` (OAuth/keychain ignored), which breaks the provider's no-API-key design. `--safe-mode` keeps auth + model selection working while disabling CLAUDE.md/skills/plugins/hooks/MCP.
- Warm reuse is silently defeated if the system prompt changes per turn (forces reseed = fresh process). Composer's prompt is deterministic so it stays warm; SessionKey = Reasoner InstanceGuid. If it's ever "slow every turn," check that the delta path in `ClaudeCodeProvider.StreamAsync` is hit (conversation.Count == ConsumedMessageCount+1, last msg Role.User). See [[physalia-repo-gotchas]].
