---
name: driving-rhino-from-wsl
description: "The working script channel into a running Rhino from this WSL setup — SendKeys (not RhinoCode), why it silently stops (GH focus, modal dialogs), the two Python engines, the RhinoApp.Idle trap, and the PATH a WSL-launched Windows process actually gets."
metadata:
  node_type: reference
  type: reference
---

Assembled 2026-09-07 over a long session of driving a live Rhino from WSL. Everything here is about
the CHANNEL, not about Physalia; the repo lives on `/mnt/c`, so Windows tooling has to be invoked
across the boundary.

## Launching

- Build with the **Windows** SDK: `/mnt/c/Program Files/dotnet/dotnet.exe`. There is no Linux SDK
  here, and the GH project needs `net7.0-windows` plus references under `C:\Program Files\Rhino 8`.
- **`Rhino.exe /runscript=…` is unreliable from WSL** — bash eats the quotes, Windows receives the
  command as separate argv entries, and Rhino may run none of it. It worked on one launch out of
  three. The failure is invisible: Rhino starts fine and Grasshopper is simply never loaded, which
  reads as a plug-in fault. Check `tasklist /m "Grasshopper*"` before blaming the build.
  `cmd.exe /c start` is worse — `Access is denied`, nothing launched.
- Launch Rhino bare, then send `Grasshopper{ENTER}` to its command line.

## The script channel that works

`-RunPythonScript "<path>"` typed into Rhino's command line via `WScript.Shell` `SendKeys`. Wrap it
in a helper (`runpy.ps1`) that does all of the following, because each one silently breaks the
channel:

1. **Minimize the Grasshopper window first.** It steals the keystrokes. This is why the channel
   appears to work once and then stops.
2. **Raise the Rhino main window** (`ShowWindow` + `SetForegroundWindow`). `AppActivate` works for
   the main window but **fails on the Grasshopper editor window** — use Win32 there.
3. **Clear and check for a modal dialog before AND after sending.** See below.
4. **Never sleep waiting for a result** if the tool marshals to Idle. See below.

**`RhinoCode.exe -r <id> script <file>` reports success and executes NOTHING** against a running
instance. `list` sees the instance; the script never runs; exit code is 0. Do not trust it.

## Modal dialogs silently block the channel

A dialog left up swallows every subsequent `-RunPythonScript` with no error anywhere — the symptom
is the next three scripts producing no output file, which looks exactly like the channel breaking.
Three sources seen:

- **A Python `SyntaxError` raises a modal "Rhino 8 Exception Occured"** dialog. So one bad script
  poisons the channel until dismissed. Syntax-check generator files locally first (`ast.parse`).
- **`GH_DocumentIO.Open`** on a Physalia preset pops a missing-plug-in prompt (the shipped presets
  still contain a dead `Harness Notes` object) and a **Grasshopper Font Mapper** that re-fires per
  object and **ignores `{ENTER}`** — click its button by coordinate. **Use `GH_Archive.ReadFromFile`
  + `ExtractObject` instead**: it never touches the document server and raised none of these.
- Grasshopper's first-run "Getting started" window ignores `{ESC}` and needs a `WM_CLOSE`.

Dismiss by window title with `WM_CLOSE`, and enumerate titles rather than guessing — a dialog is its
own top-level window, so click offsets must be relative to ITS rect, not Rhino's.

## Two Python engines, and they are not interchangeable

| | `-RunPythonScript` (my channel) | `run_rhino_script` (the LLM tool) |
|---|---|---|
| engine | **IronPython 2.7** | **CPython 3.9.10 + pythonnet** |
| `System.Drawing` | available via `import System` | needs `clr.AddReference("System.Drawing")` **and** `import System.Drawing` |
| strings | `unicode`, no f-strings | py3 |

A snippet proven in one says nothing about the other — that is exactly how the ComfyUI preamble first
shipped a capture snippet that could not run in the tool ([[comfy-render-preset]]). The full GH
build recipe was re-verified in CPython before being written into a preamble
([[harness-builder-preset]]).

**Log to UTF-8 bytes, always.** `str()` on a .NET string forces ASCII and an em-dash in a tool
result kills the script — which then leaves the modal dialog that blocks everything after it.

## The RhinoApp.Idle trap

`run_rhino_script` (and Take Snapshot, and anything mutating the Rhino document) marshals its work to
`RhinoApp.Idle`. **A driver script that blocks the UI thread — `Thread.Sleep`, or `.Wait()` on the
task — prevents Idle from ever firing.** The call never executes and every observable says nothing
happened: no file, no log, no error. Indistinguishable from a broken script, and it cost cycles twice.

**Fire, let the driver EXIT, read the result on a later run.** MCP calls are exempt (network I/O on a
background thread), which is why a Blender tool call worked first time with the same pattern.

Because a manual Construct Tool Call batch **discards the tool's result**, wrap the payload so it
reports itself — three separate files distinguish the three failure modes:

    open(r"…\tool-ran.txt","w").write("ran")     # the signal arrived and the tool executed
    try:    exec(open(r"…\gen.py", encoding="utf-8").read())
    except Exception:
        import traceback; open(r"…\exec-err.txt","w").write(traceback.format_exc())

A `SyntaxError` in an `exec`'d file **cannot be caught by a `try` inside that same file**, so it
leaves no log at all.

## PATH: a Windows process launched from WSL does not get the PATH you expect

- **`npx` / `npm` are NOT on it** — a bare `npx.cmd` fails with "not recognized". Write a `.cmd` that
  sets `PATH=C:\Program Files\nodejs;%PATH%` and launch that.
- **`uvx` IS on it**, and is on the PATH Rhino itself has — which is why a bare `uvx` resolved for an
  MCP entry while node tooling did not.
- **`wsl.exe --` runs no login shell**, so a Linux command launched that way gets a minimal PATH too:
  pass `env PATH=…` explicitly. That single omission made every comfy-cli-backed MCP tool fail
  ([[comfy-render-preset]]).

## Small Win32/interop notes that cost time

- **A P/Invoke `StringBuilder` marshals as ANSI by default**, so `GetWindowTextW` without
  `CharSet=CharSet.Unicode` truncates every window title at its first character — titles come back as
  `G`, `G`, `U`, which reads as garbage data rather than a marshalling bug.
- `DocumentServer.DocumentCount`, not `.Count`. `canvas.set_Document` does not exist under
  IronPython — reach the property setter by reflection.
- PowerShell's `WriteLine` appends **CRLF**, which corrupts a probe of a Linux stdio server. Write
  `"\n"` explicitly. This is what made `wsl.exe` look like a broken transport when it is not.
- Reading a boxed **protected nested record struct** from Python: `res.GetType()` itself throws
  ("Can only unbox…"). Take the type from `method.ReturnType.GetGenericArguments()[0]`, and read
  `Task.Result` off the DECLARED return type — the runtime type is an internal `AsyncStateMachineBox`
  ([[pdf-natives-verified]]).
