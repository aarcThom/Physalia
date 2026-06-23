---
name: commit-and-pr-messages-output-only
description: "When asked for a commit message or PR, only print it in chat — never run git commit/push or gh"
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 6887651e-99bc-429b-b895-1b7d3c5c1d53
---

When the user asks for a commit message or a PR description, write it out in the chat as text only so they can copy it. Do NOT run `git commit`, `git push`, or `gh pr create` — the user commits/opens the PR themselves.

**Why:** The user wants to review and run the git action manually; this holds even when they say something like "we'll just commit to main."

**How to apply:** Produce the message text (and for PRs, the body) in a copyable block and stop. Do not stage, commit, or push unless they later explicitly tell you to run the command.
