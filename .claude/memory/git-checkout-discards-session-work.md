---
name: git-checkout-discards-session-work
description: Never use `git checkout -- <file>` to undo a bad edit — in a long session the file is usually uncommitted, so it reverts to HEAD and destroys hours of work.
metadata:
  type: feedback
---

**Do not run `git checkout -- <file>` (or `git restore <file>`) to undo an edit you just made.** In a
session that has been building for a while the file is almost always UNCOMMITTED, so checkout does
not undo your last edit — it reverts the file to HEAD and silently destroys everything done to it
this session.

**Why:** I did exactly this to `Setup.svelte` to back out one bad two-line patch, and wiped the whole
setup-page rework — the API URL + key form, the Detect button, the connect/disconnect footer, the
quiet-page branch. Several turns of work, gone in one command, with no warning and no reflog entry
(nothing was ever committed).

**How to apply:** To undo your own recent edit, apply the inverse edit, or re-`Write` the file from
what you know it should contain. Reserve `git checkout -- <file>` for a file you have NOT touched
this session and genuinely want back at HEAD — restoring an accidental deletion of a tracked file
is the legitimate case. If a file is worth protecting mid-session, commit it first.

See [[commit-and-pr-messages-output-only]] for when committing is allowed at all.
