---
name: new-machine-bringup
description: Bringing the repo up on a fresh Windows box — what a plain `dotnet build src/Physalia.slnx -c Debug` silently does NOT produce (the MCP bridge), plus the .ghlink dev loop and the SDK-10-only build.
metadata:
  node_type: memory
  type: project
---

Done 2026-09-07 on the `thomas` machine (fresh copy of the repo, nothing else installed). Everything
here is what the repo does not say and what a green build does not prove.

**A clean `dotnet build src/Physalia.slnx -c Debug` leaves `bin/.../Bridge/` EMPTY, and nothing
says so.** `src/Physalia.slnx` carries `<Build Solution="Debug|*" Project="false" />` on
`Physalia.McpBridge`, which wins over `Physalia.GH`'s build-ordering `ProjectReference` — so
`bin/Debug/net8.0` is never populated. `CopyMcpBridge` is guarded
`Condition="'@(McpBridgeFiles)' != ''"`, so it stages nothing and reports nothing, and the build
says `Build succeeded`. The symptom is one node deep: every **remote / `url:` MCP server** reports
the bridge missing while local stdio servers work perfectly — which reads as a server-specific
fault, not a build gap. Same class of confusion as the Mac `BridgeExecutable()` extension problem
(see [[mac-port-mcp-gaps]]). Fix on a fresh clone:
`dotnet build src/Physalia.McpBridge/Physalia.McpBridge.csproj -c Debug`, then rebuild
`Physalia.GH` so `CopyMcpBridge` picks it up. Release is unaffected (the exclusion is `Debug|*`).

**The dev loop is a `.ghlink`, and it was written down nowhere.** Grasshopper reads `*.ghlink`
files (one plain-text folder path per file) out of `%APPDATA%\Grasshopper\Libraries`, so
`Physalia.ghlink` containing `…\src\Physalia.GH\bin\Debug\net7.0-windows` makes every build load
directly with no copy step. Note **`%APPDATA%\Grasshopper` does not exist until Rhino has been run
once** — creating it by hand is fine and Grasshopper picks the link up on first launch.

**One SDK (10.0.400) builds all three TFMs — net7.0, net7.0-windows and the bridge's net8.0 — with
no `global.json` and no extra install.** Reference packs come from NuGet. Nothing needed pinning; do
not add a `global.json` reflexively. Expect only the known noise: `NU1701` on the net48 Grasshopper/
RhinoCommon packages, `MSB3277` on GH_IO 8.24-vs-8.34 (the NuGet Grasshopper against the installed
Rhino's copy — deliberate, see the csproj comment on `RepackLibPath`), and `CS2008` from the
assembly-less UI wrapper. 964 Core tests pass.

**Node is a hard prerequisite, not an optional extra.** No Node ⇒ the chat window is the
"UI not found" page ⇒ effectively no plug-in. `winget install OpenJS.NodeJS.LTS` (24.19.0 here) is
enough; `npm ci` + Vite then run from the build. `-p:BuildUI=false` is for a C#-only inner loop,
never for a working install. See [[physalia-repo-gotchas]] for why `npm run build` alone is not
enough.

**Also run the memory junction ([[memory-sync-setup]]).** The harness creates a REAL empty
`…\.claude\projects\<hash>\memory` on first use, so the 106 repo memories are invisible until it is
replaced by a junction — and the emptiness looks like "no memories yet" rather than a broken link.
`CLAUDE_CONFIG_DIR` is unset on this machine, so the root is `%USERPROFILE%\.claude`.

**Loaded in Rhino and verified 2026-09-07.** `Physalia.GH.gha` appears in Rhino's module list
(`tasklist /m "Physalia*"`), the **Physalia** ribbon tab renders all 13 sections with real icons
(none falling back), and the plug-in created `%LOCALAPPDATA%\Physalia` on first run. That module
check is the cheapest proof the `.ghlink` dev loop is live — no click, no screenshot.

**Still not verified:** the PDF natives. Per the CLAUDE.md compatibility note, neither a green build
NOR a loaded `.gha` says anything about `PdfNativeLibrary` resolving inside Rhino. Place a Read PDF
tool and render a page.

**Launching Rhino FROM WSL: `/runscript` is unreliable, `SendKeys` is not.** The repo sits on the
Windows drive, so the build runs through `/mnt/c/Program Files/dotnet/dotnet.exe` (there is no Linux
SDK in this WSL) and that part is fine. But
`"/mnt/c/Program Files/Rhino 8/System/Rhino.exe" /nosplash /runscript="_Grasshopper"` opened
Grasshopper on ONE launch out of three and silently did nothing on the others: bash consumes the
quotes, so Windows receives the command as separate argv entries and Rhino may run none of it. **The
failure is invisible** — Rhino starts perfectly and `Grasshopper.dll` is simply never loaded, which
reads as a plug-in fault rather than a launch fault, so check
`tasklist /m "Grasshopper*"` before blaming the build. `cmd.exe /c start` is worse: `Access is
denied`, nothing launched. What works every time is to start Rhino bare and drive its command line
with `WScript.Shell` `SendKeys('Grasshopper{ENTER}')`. Two follow-ons: `AppActivate` fails on the
Grasshopper editor window (raise it with Win32 `SetForegroundWindow` instead), and GH opens on
**"No document"** that way — `^n` to the editor gives the canvas. Grasshopper's first-run
"Getting started" dialog ignores `{ESC}` and needs a `WM_CLOSE`.

**A P/Invoke `StringBuilder` marshals as ANSI by default**, so `GetWindowTextW` without
`CharSet=CharSet.Unicode` truncates every window title at its first character — titles come back as
`G`, `G`, `U`, which looks like garbage data rather than a marshalling bug. Worth knowing for any
future window probing.

**The README cannot be followed** (checked 2026-09-07): Installation and Quick start are literal
`**TODO**`, and Configuration still tells you to `cp Files/API_KEY_CONFIG.YAML.example` and
`Files/MCP_SERVERS.YAML.example` — both files, and both `.example` templates, were deleted in
2026-09-04/05. Those two `cp` commands fail outright. Everything a fresh box actually needs is in
this note; the README has none of it.
