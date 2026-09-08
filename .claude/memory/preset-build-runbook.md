---
name: preset-build-runbook
description: "RUNBOOK: what to do when the user says 'build these presets: ...'. The toolkit, the exact procedure, the checks that must pass, and the mistakes that are silent."
metadata:
  node_type: memory
  type: reference
---

**When the user says "build these presets: X, Y, Z" — this is the procedure.** Written 2026-09-07
after building 28 of them; every rule here was paid for.

Background on WHAT the presets are and what each one teaches: [[teaching-presets]]. The GH-level
wiring rules: [[building-harnesses-programmatically]]. This file is the operational half.

## 0. Before touching anything

1. **Is Rhino running with Grasshopper open?** The whole toolkit drives Rhino through the MCP's
   `run_python`. If `_type(...)` raises "not found in any Physalia assembly", Grasshopper is not
   loaded — run the `_Grasshopper` command via `mcp__rhino__run_command` and retry.
2. **Is the `.gha` current?** If the plug-in was rebuilt, Rhino must be restarted to load it. Rhino
   holds a lock on the `.gha`, so a build fails while it is open.
3. **`ROOT`.** Every script carries one machine-specific default. If the repo is NOT at
   `C:\Users\rober\repos\Physalia`, set `ROOT` first — it is honoured by every script:
   ```python
   ROOT = r"D:\code\Physalia"
   exec(open(ROOT + r"\tools\presets\build_s01_record.py").read())
   ```
   Then fix the defaults in the scripts so it stops needing to be said.

## 1. The toolkit — `tools/presets/`

`phybuild.py` is exec'd by every other script and defines everything below. It also sets `PRESETS`
(`<ROOT>/wip_presets`) and `SCRATCH` (system temp + `/claude`), so a build script hardcodes nothing
but its own `ROOT` default.

**Placing and wiring**
- `place(doc, name, x, y, nick=None, category="Physalia", sub=None)` — `sub` is the RIBBON SECTION
  and is REQUIRED where names collide (`"Read PDF"` exists in both `LLM Tools` and `Human Tools`).
- `wire(dst, dst_in, src, src_out)` — destination first. Names are the FULL parameter names; `pin()`
  raises with the real list when you get one wrong, which is the fastest way to learn a component.
- `back(doc, src, src_out, dst, dst_in, fx, fy, cx, cy, nick)` — the wireless Feedback/Collector
  pair for any backward path. Returns `(feedback, collector)`. To add a second sender to the SAME
  destination input, `wire(fb, "Signal", other, "Result")` — one collector can serve several senders
  aimed at one input, never several destinations.
- `router_slots(router, n)` — adds n slots to the default one, so `router_slots(r, 2)` gives THREE
  tool outputs. See the trap in §5.
- `merge_inputs(merge, n)`, `core_loop(...)` (below).

**Annotation**
- `panel(doc, x, y, text, w, h, colour=None)` — the yellow explanatory note (default).
- `title(doc, x, y, text, w, h)` — the blue stage heading.
- `input_panel(doc, x, y, text, w, h, nick)` — a WHITE panel that is a real data source.
- `list_panel(doc, x, y, lines, ...)` — sets `Multiline = False` so the panel yields ONE ITEM PER
  LINE. A normal panel is one item however many lines it has.
- `blank_input(doc, obj, input_name, x, y, label)` — an empty white panel as a source, to stop a
  Picker auto-placing. **Only for TEXT inputs** — into a Point or Number param it is a conversion
  error (S12 hit this on Take Snapshot's `Current Location`; leave such an input unwired and say so
  in the annotation instead).
- Colours: `INTRO_GREEN` (the top-left overview), `NOTE_YELLOW`, `TITLE_BLUE`, `OUTPUT_GREY` (a
  read-only panel showing a wire's value), `INPUT_WHITE`.

**Values**
- `slider`, `boolean(doc, x, y, value, nick, toggle=True)` (`toggle=False` gives a Button),
  `text_param`, `setdata(param, value)`.
- `pick(doc, obj, input_name, value)` / `picker_values` / `drop_picker` / `clear_pick` for Pickers.

**Documents**
- `host_document()`, `clear_host()`, `new_harness(host, x, y, name=...)` → `(harness, innerDoc)`,
  `enter(harness)`, `find_harnesses()`.
- A NESTED harness: `h = place(D, "Harness", x, y, nick="the-helper")` then
  `HD = h.EnsureInnerDocument()` and build into `HD`.

**Finishing**
- `commit_build(doc, label)` → `solve(doc)` twice → `write_dump(doc, path)` → `sweep(doc, label)`
  → `save_phy(harness, path, description=..., chat_text=...)`.
- `retire(doc)` — **`RemoveObjects` then `Dispose`, never `Dispose` alone.** See §6.

## 2. The build loop for ONE preset

Write `tools/presets/build_sNN_name.py`, then:

```python
exec(open(r"<ROOT>\tools\presets\build_sNN_name.py").read())
```

Read the output. Iterate until `PROBLEMS: 0`. Expect two or three rounds of this — the failures are
almost always (a) a parameter name you guessed, (b) an overlap, (c) a component that does not do
what CLAUDE.md says.

Then:
```python
PHY = r"<ROOT>\wip_presets\SNN - Name.phy"
exec(open(r"<ROOT>\tools\presets\verify.py").read())     # round-trip through the real loader
```
Then from a SHELL (not Rhino): `python tools/presets/audit.py`.

Then commit. The user's standing instruction is **commit after each finished, tested preset** —
`git commit` only, never `push`, never `gh`.

At the end of a batch: `exec(open(r"<ROOT>\tools\presets\build_all.py").read())` rebuilds everything
and runs `check_pairs`, then `python tools/presets/audit.py`.

## 3. What "tested" means here

Four levels, and be explicit in the README about which a preset reached:

1. **Built** — `sweep` reports 0 problems (no component errors, no overlapping annotation).
2. **Round-tripped** — `verify.py` reads the `.phy` back through `HarnessComponent.ReadDocumentFile`
   and reports the object count, the manifest, and that a Chat is inside (the loader REFUSES a
   package with none).
3. **Whole-set checked** — `audit.py` (no machine-specific text in the bytes), `check_pairs.py`
   (Feedback pairs, grip links, Router slots), `coverage.py` (which components appear where).
4. **Run live** — `liverun.py` places it and drives a real round with no chat window.

## 4. House style for a teaching preset

- **Left to right.** Only the wireless return paths run backwards, and they are the ones that cannot
  be wires.
- **Numbered stages**, each with a blue `title` and a yellow `panel`. A green `INTRO_GREEN` panel
  top-left says what the whole thing is for.
- **Plain English, addressed to a person doing the job.** Explain a mechanism only where knowing it
  changes what the reader DOES. The numbered presets (01–14) teach the PARTS; the scenario presets
  (S01–S14) teach the WORK and point back at a numbered one for detail.
- **Say what it cannot do.** Every preset that could mislead has a paragraph saying so — "it is not
  a consultant", "not a compliance certificate", "not a quantity surveyor". Keep this.
- **`description=` and `chat_text=`** on `save_phy` are the gallery subtitle and the chat window's
  opening screen. `chat_text` REPLACES the default greeting, so it should say what to try first.
- Grey `OUTPUT_GREY` panels showing a tool's own output (the script it ran, what it downloaded) are
  worth their space — they are how a user debugs without asking the model.

## 5. Silent failures — the whole reason the checks exist

None of these error. All were hit.

- **A tool wired PAST the last Router tool slot lands on the FEEDBACK output.** It is then never
  dispatched AND never advertised, so the model is told the tool does not exist. `router_slots(r, n)`
  gives `n + 1` slots. `check_pairs.py` checks this now.
- **A Feedback whose collector guid no longer resolves** swallows the signal and hands it nowhere.
  Checked after the id reissue a preset load performs.
- **A Button into a Signal input** is a hard error (good) — the only sanctioned path is Construct
  Signal, whose input is called **`Trigger`**, not "Boolean Trigger".
- **`SetPersistentData` APPENDS** to the registered default, and a bare string becomes one item per
  CHARACTER. `setdata()` clears first and always wraps.
- **A preset carrying machine-specific text** (a username, an endpoint name someone configured).
  `audit.py` reads the package bytes — and note it must INFLATE them first, since `harness.gh` is
  raw deflate; an earlier version scanned compressed bytes, found nothing, and pronounced everything
  clean. It has a `self_test` for exactly that now. **Always make a scanner find something it
  definitely should before trusting a clean verdict.**

## 6. Environment traps

- **`retire()` = `RemoveObjects` then `Dispose`.** A bare `Dispose()` never runs
  `RemovedFromDocument`, so every component's subscriptions leak — a Rhino Document grounder holds
  thirteen RhinoDoc handlers, a Project Folder holds a FileSystemWatcher. Reading the set back a few
  dozen times with a bare Dispose slowed Rhino until a trivial script could not finish.
- **The MCP `run_python` call times out around 300s and the cancellation KILLS the script.** Anything
  long writes progress to a file (`write_log`) instead of returning it. That is why `build_all.py`
  logs rather than printing.
- **Do heavy non-Rhino work outside Rhino.** `audit.py` is a shell command for this reason.
- **The Bash tool's heredocs collapse `\\r\\n` to `\r\n`**, so a Python patch script meaning to match
  the literal text `\r\n` in a source file writes REAL newlines into it instead — which then breaks
  the file with an unterminated string. **Use the Edit tool for anything containing backslash
  escapes**, and forward-slash paths inside heredocs to dodge `\U`/`\W` escape errors.
- **`GH_DocumentIO.Open` raises a modal dialog** and hangs the MCP call. Use
  `HarnessComponent.ReadDocumentFile` by reflection (the 2-parameter overload) — that is what
  `verify.py` does.
- **Overlap checks are meaningless before `layout(doc)`**, which forces `ExpireLayout` +
  `PerformLayout` so bounds are real. `sweep` does it.

## 7. Providers — which model to put in a preset

- **Claude Code ignores the tool list entirely.** A tool-using preset on Claude Code fails in a way
  that looks like a broken canvas. Use it only for tool-free pipelines.
- **Codex is the tool-capable keyless one.** Its model list is fetched LIVE from the CLI, so never
  pin a model id — `gpt-5.5` 404'd once already.
- **Never pin a Codex model in a preset.** Leave the Picker empty.
- Anthropic / Gemini / OpenAI-compatible all do tools properly and are faster, but need a key.
- **A broken PATH inside Rhino is indistinguishable from a broken Codex install.** If Codex dies with
  `'"node"' is not recognized`, check whether `cmd` inside that Rhino can resolve ANY bare command
  (`where node`). Seen 2026-09-07: `%PATH%` expanded correctly and `C:\Windows\System32\where.exe
  node` worked, but cmd's own search resolved nothing, so the `.cmd` shim died. Not fixable
  in-process; survived a restart. Verify with `codex --version` from a normal shell.
