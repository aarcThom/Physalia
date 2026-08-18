---
name: design-fork-then-build-through
description: Confirmed approach — investigate first, put the ONE genuine design fork to the user, then build the whole vertical slice with tests and docs.
metadata:
  type: feedback
---

For a sketch-driven feature request (the 2026-08-17 feedback-turn header,
[[feedback-turn-attribution]]) the sequence the user called out as very good work was:

1. Read the whole path before proposing anything — UI component, bridge contract, the C# that fills
   it, and the Core types behind that.
2. Surface the **one** decision the code could not settle, with its cost stated on both sides. There
   it was: badge whatever delivered the signal (cheap, sometimes wrong) versus trace back to the true
   producer (needs Core work). Everything else was a routine judgement call and stayed unasked.
3. Then build the whole vertical slice in one pass — Core, GH, Svelte, tests, CLAUDE.md, memory — and
   report what was verified versus what has never run in Rhino.

**Why:** the fork was the only place a wrong guess would have wasted the work; asking about it once
bought licence to go all the way through the stack without further check-ins. Asking about the rest
would have been noise, and guessing on it would have shipped the wrong feature.

**How to apply:** on this project, that pairing is the default for a feature ask — one blocking
question if there is a real fork (and it also settles the CLAUDE.md advice-only rule in the same
breath), then finish everything, including the memory note and the honest not-yet-run-live caveat.
