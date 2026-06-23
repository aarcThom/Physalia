---
name: memory-sync-setup
description: How CLAUDE.md and agent memory sync across computers via git (repo .claude/memory + a junction); how to set up a new machine
metadata: 
  node_type: memory
  type: reference
  originSessionId: bbaabb4a-380c-4621-8961-67ab28dc45cd
---

Set up 2026-06-22 so `CLAUDE.md` and this agent memory travel across computers through the repo's git history (the user works on Physalia from more than one machine).

## How it works
- **`CLAUDE.md`** — un-ignored in `.gitignore` (was previously ignored), so it commits and syncs normally.
- **Agent memory** — the canonical files live IN the repo at `.claude/memory/` (real files, git-tracked). The harness still reads/writes memory at its global path `%USERPROFILE%\.claude\projects\<repo-path-hash>\memory`, which on this machine is a **directory junction** pointing into `…\repo\.claude\memory`. So every memory write lands in the repo working tree → commit → pull elsewhere → that machine's junction surfaces it to the harness. Auto-sync, no manual copy.
- `.gitignore` rule: `.claude/*` then `!.claude/memory/` — keeps machine-local `.claude/settings.local.json` ignored while tracking `.claude/memory/`.
- The stray `session-2026-03-05.txt` that was in the memory folder was moved up to `…\projects\<hash>\` (preserved, NOT synced — it's a transcript, not memory).

## New-machine setup (run once per computer, after cloning the repo)
The harness won't find memory until the global memory dir is a junction into the clone. In PowerShell, adjust `$repo` to the clone location:
```powershell
$repo   = "C:\Users\rober\repos\Physalia"          # <-- clone path on THIS machine
$hash   = ($repo -replace '[:\\]','-')              # e.g. C--Users-rober-repos-Physalia
$global = "$env:USERPROFILE\.claude\projects\$hash\memory"
$target = "$repo\.claude\memory"
New-Item -ItemType Directory -Force (Split-Path $global) | Out-Null
if (Test-Path $global) { Remove-Item -Recurse -Force $global }   # discards any local-only memory at the global path
New-Item -ItemType Junction -Path $global -Target $target
```
Junctions need no admin rights (unlike symlinks). The project-hash folder name is the absolute repo path with `:` and `\` replaced by `-`; it differs if the clone path/username differs, which is fine — the command derives it from `$repo`.

## Gotchas
- The repo `.claude/memory/` must stay REAL files; only the GLOBAL path is the junction. (Git tracks the repo files; the junction is invisible to this repo's git.)
- `cmd.exe /c "mklink /J …"` invoked from the bash tool mangled quoting and silently no-opped — use PowerShell `New-Item -ItemType Junction` (or verify with `Get-Item <path> -Force | select LinkType,Target`).
