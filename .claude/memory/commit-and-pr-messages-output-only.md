---
name: commit-and-pr-messages-output-only
description: "When asked for a commit message or PR, only print it in chat — never run git commit/push or gh"
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 6887651e-99bc-429b-b895-1b7d3c5c1d53
---

When the user asks for a commit message or a PR description, write it out in the chat as text only so they can copy it. Do NOT run `git commit`, `git push`, or `gh pr create` — the user commits/opens the PR themselves.

**Why:** The user wants to review and run the git action manually. A casual aside like "we'll just commit to main" is NOT a command to commit — still output-only. But a direct, unambiguous instruction ("commit it", "commit and don't push") IS a command — then commit (still never push unless told). Don't re-ask when the instruction is that explicit.

**How to apply:** Produce the message text (and for PRs, the body) in a copyable block and stop. Do not stage, commit, or push unless they explicitly tell you to run the command. When they do explicitly say to commit: `git add` + `git commit` only, never `git push`/`gh` unless also told. (Note: `rtk` is not installed on this machine — use plain `git`.)
