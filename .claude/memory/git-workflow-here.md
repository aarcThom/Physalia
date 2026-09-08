---
name: git-workflow-here
description: "Committing safely in this checkout: the whole worktree is CRLF while HEAD is LF, so `git add -A` writes a ~770-file line-ending commit. Also the missing git identity, why push needs the Windows git, and the branch-not-main decision."
metadata:
  node_type: memory
  type: project
---

Learned 2026-09-07 across four commit-and-push rounds. Complements
[[commit-and-pr-messages-output-only]], which is the standing preference about WHEN to commit; this
is about HOW, in this checkout.

## THE BIG ONE: the worktree is CRLF, HEAD is LF

`core.autocrlf` is unset, there is no `.gitattributes`, and every text file on disk has been
CRLF-flipped at some point before these sessions. So **~770 files show as modified with zero real
changes**, and `git add -A` would write a commit of pure line-ending churn with the actual work
buried inside it. `git diff --ignore-all-space` does NOT reliably filter this out — it reported 1
file once and 772 later.

**Find the real change set by comparing content with `\r` stripped:**

```bash
git diff --name-only -z | while IFS= read -r -d '' f; do
  if ! diff -q <(git show "HEAD:$f" 2>/dev/null | tr -d '\r') <(tr -d '\r' < "$f") >/dev/null 2>&1
  then printf '  REAL: %s\n' "$f"; fi
done
```

Use `-z` and a quoted read — several paths contain spaces (`Files/SYSTEM_PROMPTS/PREAMBLE/C# Script.txt`),
and an unquoted loop breaks on them silently, skipping exactly the files most likely to matter.

**Then stage explicit paths, never `-A`.** And for a tracked file you edited, **normalise it to LF
before staging** so its diff shows your addition rather than a whole-file flip:

```python
b = open(p,"rb").read(); open(p,"wb").write(b.replace(b"\r\n", b"\n"))
```

Leave the other ~769 alone. Editing scripts should DETECT the file's existing line endings and
preserve them; once a file has been committed as LF it stays LF and needs no further care.

**Pending decision, not mine to take:** a `.gitattributes` with `* text=auto eol=lf` plus a one-off
`git add --renormalize .` would settle this permanently — but that IS the ~770-file commit, so it
belongs on a quiet tree as its own deliberate change, never smuggled in with feature work.

## Git identity is unset in WSL

The first commit failed outright: `fatal: empty ident name`. The repo's own recent commits use
**`aarcThom <robertthomasgaudin@gmail.com>`** (older ones
`Thomas Gaudin <113943125+aarcThom@users.noreply.github.com>`), so that is what I set — **repo-local,
not `--global`** — to match history. Deliberately NOT the work address in the session context, since
this repo's history uses the personal one.

## Push needs the Windows git

WSL's git cannot run the credential helper: the configured `git-credential-manager.exe` path is
unquoted, so it fails with `/mnt/c/Program: not found` and then
`could not read Username for 'https://github.com'`. **Push with the Windows git instead** —
`"/mnt/c/Program Files/Git/cmd/git.exe" push …` — which has GCM wired up properly. Safe because a
push touches no working-tree files, so its (possibly different) `autocrlf` config cannot alter
anything. Fetch/commit/diff all work fine with WSL git.

## Standing decisions

- **Branch, never `main`.** `main` is the default branch and the repo's history is PR merges
  (`Merge pull request #18 from aarcThom/final-pass`), so pushing straight to it would bypass the
  user's own workflow. Push a branch and hand over the `pull/new/<branch>` link.
- **Commit and push only on explicit instruction.** [[commit-and-pr-messages-output-only]] says
  messages are output-only and push is off by default; on 2026-09-07 the user explicitly said
  "commit and push" four times, which overrides it for those batches. The default has not changed —
  keep printing messages and waiting to be asked.
- **Two commits per batch** has worked well: one `feat(presets):` for the artifacts, one
  `docs(memory):` for the notes. Repo message style is conventional-commit prefixes with a
  descriptive, slightly literary subject line, and bodies that explain WHY rather than list files.
- End commit messages with the `Co-Authored-By: Claude Opus 5 (1M context)` trailer.
- **Verify before pushing**: `git diff --stat <base>..HEAD` should list exactly the intended files,
  and the real-change sweep above should come back empty.
