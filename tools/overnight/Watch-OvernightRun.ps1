<#
.SYNOPSIS
Watches an unattended Physalia run (pre-ship rig F3) and reports whether it stayed inside its
budget. Read-only: it touches nothing Rhino owns.

.DESCRIPTION
F3 is the pass that decides whether the trigger tier is safe to ship, and its failure condition is
"anything that spent more than the cap allowed" — with a single unexplained call treated as a
blocking defect. That is a question about a night you were asleep for, so it has to be answered from
what the run left on disk.

This script tails the project folder's own records and keeps the running totals nobody can
reconstruct in the morning:

  runs.jsonl              one line per inference call — the only per-call cost record there is
  conversation.json       the autosaved transcript; its turn count and freshness
  downloads.json + files  project-folder growth, which is how the download->watcher->download
                          loop would show itself

WHY IT ONLY READS FILES. Observing must not perturb: asking Grasshopper anything can expire a
component or run a solution, and a monitor that nudges the pipeline it is measuring is worthless
for this rig. Everything here is file I/O, so it is safe to start, stop and restart mid-run.

WHY IT TRACKS A BYTE OFFSET. runs.jsonl is append-only by design, so new calls are new bytes: the
tail costs nothing however long the night was, and a last line caught half-written is skipped and
re-read whole on the next poll — which is the behaviour the format was chosen for.

WHAT IT CANNOT KNOW. The Budget Guard's caps live on the node, not on disk, so pass them in with
-MaxCalls / -MaxTokens and they become assertions. Without them the script still reports, but it
cannot fail anything.

.PARAMETER ProjectFolder
The harness's project folder. Omit to pick the most recently written folder under
Files/PROJECT_FILES, which is the one an active run is writing to.

.PARAMETER MaxCalls
The Budget Guard's Max Calls. Calls beyond it are flagged as an overrun. Remember the guard is
checked BEFORE a call against what is already spent, so overrunning by exactly one call is correct
behaviour, not a defect — the script says so rather than crying wolf.

.PARAMETER MaxTokens
The Budget Guard's Max Tokens, same treatment. NOTE it is meaningless on a CLI provider: those
report the delta they were sent, not the prompt, so runs.jsonl has shown inputTokens 6 against a
5,650-character prompt. On Claude Code or Codex, trust -MaxCalls only.

.PARAMETER MinIntervalSeconds
The Signal Throttle's interval. Two calls closer together than this are flagged: it is the cheapest
signature of a runaway that a cap alone would not catch until the money was gone.

.PARAMETER PollSeconds
How often to look. Default 30. There is no reason to poll fast — nothing here is a race.

.PARAMETER StopAfterMinutes
Stop on its own after this many minutes and print the verdict. 0 (the default) watches until you
press Ctrl+C. Prefer setting it for an actual overnight run: the verdict is the deliverable, and it
should be sitting in the log when you come back rather than waiting for someone to interrupt the
script before it will say anything.

.EXAMPLE
.\Watch-OvernightRun.ps1 -MaxCalls 200 -MinIntervalSeconds 300

.EXAMPLE
.\Watch-OvernightRun.ps1 -ProjectFolder 'C:\...\PROJECT_FILES\curious-cake-soap-fun' -MaxCalls 50 -MaxTokens 400000
#>
[CmdletBinding()]
param(
  [string]$ProjectFolder,
  [int]$MaxCalls = 0,
  [long]$MaxTokens = 0,
  [int]$MinIntervalSeconds = 0,
  [int]$PollSeconds = 30,
  [int]$StopAfterMinutes = 0,
  [string]$LogPath
)

$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------- locate the project folder
if (-not $ProjectFolder) {
  $roots = @(
    (Join-Path $PSScriptRoot '..\..\Files\PROJECT_FILES'),
    'C:\Users\rober\repos\Physalia\Files\PROJECT_FILES'
  ) | Where-Object { Test-Path $_ } | Select-Object -First 1
  if (-not $roots) { throw "No Files\PROJECT_FILES found. Pass -ProjectFolder explicitly." }
  $cand = Get-ChildItem $roots -Directory |
          Sort-Object { (Get-ChildItem $_.FullName -Recurse -File -ErrorAction SilentlyContinue |
                         Measure-Object -Maximum LastWriteTime).Maximum } -Descending |
          Select-Object -First 1
  if (-not $cand) { throw "No harness project folders under $roots. Pass -ProjectFolder explicitly." }
  $ProjectFolder = $cand.FullName
}
if (-not (Test-Path $ProjectFolder)) { throw "Project folder not found: $ProjectFolder" }
$ProjectFolder = (Resolve-Path $ProjectFolder).Path

$runsPath = Join-Path $ProjectFolder 'runs.jsonl'
$convPath = Join-Path $ProjectFolder 'conversation.json'
if (-not $LogPath) {
  $LogPath = Join-Path $ProjectFolder ("overnight-watch-{0:yyyyMMdd-HHmmss}.log" -f (Get-Date))
}

function Write-Line([string]$text, [string]$colour = 'Gray') {
  $stamp = (Get-Date).ToString('HH:mm:ss')
  Write-Host "$stamp  $text" -ForegroundColor $colour
  # A monitor whose own record dies with the console is no use the morning after.
  try { Add-Content -Path $LogPath -Value "$((Get-Date).ToString('o'))  $text" -Encoding utf8 } catch { }
}

# ---------------------------------------------------------------- read only the NEW bytes
$script:offset = 0
function Read-NewRecords {
  if (-not (Test-Path $runsPath)) { return @() }
  $len = (Get-Item $runsPath).Length
  if ($len -lt $script:offset) {
    # The file shrank: replaced, or the project folder moved with a rename. Start over rather than
    # reading from a stale offset into the middle of a line.
    Write-Line "runs.jsonl shrank ($($script:offset) -> $len bytes) — re-reading from the start" 'Yellow'
    $script:offset = 0
  }
  if ($len -eq $script:offset) { return @() }

  $fs = [System.IO.File]::Open($runsPath, 'Open', 'Read', 'ReadWrite')
  try {
    $fs.Seek($script:offset, 'Begin') | Out-Null
    $buf = New-Object byte[] ($len - $script:offset)
    $read = $fs.Read($buf, 0, $buf.Length)
  } finally { $fs.Dispose() }

  $text = [System.Text.Encoding]::UTF8.GetString($buf, 0, $read)
  # A trailing partial line is left for the next poll -- that is the JSONL contract, not a defect.
  $complete = $text.LastIndexOf("`n")
  if ($complete -lt 0) { return @() }
  $script:offset += [System.Text.Encoding]::UTF8.GetByteCount($text.Substring(0, $complete + 1))

  $out = @()
  foreach ($line in $text.Substring(0, $complete).Split("`n")) {
    $t = $line.Trim()
    if (-not $t) { continue }
    try { $out += ($t | ConvertFrom-Json) } catch { Write-Line "unparseable line skipped: $($t.Substring(0,[Math]::Min(80,$t.Length)))" 'DarkYellow' }
  }
  return $out
}

function Get-FolderSize {
  $f = Get-ChildItem $ProjectFolder -Recurse -File -ErrorAction SilentlyContinue
  [pscustomobject]@{ Files = @($f).Count; Bytes = (($f | Measure-Object -Sum Length).Sum) }
}

function Get-TurnCount {
  if (-not (Test-Path $convPath)) { return $null }
  try { return @(((Get-Content $convPath -Raw -ErrorAction Stop) | ConvertFrom-Json).turns).Count }
  catch { return -1 }   # mid-write; the autosave rewrites it whole every turn
}

# ---------------------------------------------------------------- baseline
$calls = 0; $inTok = 0L; $outTok = 0L; $fails = 0
$violations = New-Object System.Collections.Generic.List[string]
$lastWhen = $null
$startedAt = Get-Date
$base = Get-FolderSize
$baseTurns = Get-TurnCount

# Everything already in runs.jsonl belongs to earlier sittings, so it is consumed as baseline and
# the totals below describe THIS watch only. That is what makes the cap assertions meaningful.
$pre = @(Read-NewRecords)

Write-Line "=================== overnight watch ===================" 'Cyan'
Write-Line "project folder : $ProjectFolder" 'Cyan'
Write-Line "log            : $LogPath" 'Cyan'
Write-Line "pre-existing   : $($pre.Count) call(s) already in runs.jsonl (baseline, not counted)" 'Cyan'
Write-Line "conversation   : $(if ($null -eq $baseTurns) { 'no conversation.json yet' } else { "$baseTurns turn(s)" })" 'Cyan'
Write-Line "folder         : $($base.Files) file(s), $([Math]::Round($base.Bytes/1MB,1)) MB" 'Cyan'
$capText = @()
if ($MaxCalls -gt 0)  { $capText += "MaxCalls=$MaxCalls" }
if ($MaxTokens -gt 0) { $capText += "MaxTokens=$MaxTokens" }
if ($MinIntervalSeconds -gt 0) { $capText += "MinInterval=${MinIntervalSeconds}s" }
Write-Line "asserting      : $(if ($capText.Count) { $capText -join ', ' } else { 'nothing — pass -MaxCalls/-MaxTokens to make this a test' })" 'Cyan'
Write-Line "stops         : $(if ($StopAfterMinutes -gt 0) { "after $StopAfterMinutes min, verdict written automatically" } else { 'on Ctrl+C' })" 'Cyan'

try {
  while ($true) {
    foreach ($r in @(Read-NewRecords)) {
      $calls++
      $inTok  += [long]$r.inputTokens
      $outTok += [long]$r.outputTokens
      $ok = [bool]$r.ok
      if (-not $ok) { $fails++ }

      $gap = ''
      if ($r.when) {
        try {
          $when = [datetime]::Parse($r.when, $null, 'RoundtripKind')
          if ($lastWhen) {
            $secs = [Math]::Round(($when - $lastWhen).TotalSeconds)
            $gap = " (+${secs}s)"
            if ($MinIntervalSeconds -gt 0 -and $secs -lt $MinIntervalSeconds) {
              $m = "TOO FAST: call $calls came ${secs}s after the previous, under the ${MinIntervalSeconds}s throttle"
              $violations.Add($m); Write-Line $m 'Red'
            }
          }
          $lastWhen = $when
        } catch { }
      }

      $colour = if ($ok) { 'Green' } else { 'Red' }
      Write-Line ("call {0,-4} {1,-22} in={2,-7} out={3,-7} {4,6}ms ok={5}{6}{7}" -f `
        $calls, $r.model, $r.inputTokens, $r.outputTokens, $r.ms, $ok, $gap,
        $(if ($r.error) { "  ERROR: $($r.error)" } else { '' })) $colour

      if ($MaxCalls -gt 0 -and $calls -gt ($MaxCalls + 1)) {
        # +1 is not slack: the guard checks BEFORE a call against what is already spent, so a
        # pipeline can legitimately overrun by exactly one. Two is a defect.
        $m = "BUDGET OVERRUN: $calls calls against MaxCalls=$MaxCalls (one over is by design; this is $($calls - $MaxCalls) over)"
        if (-not $violations.Contains($m)) { $violations.Add($m); Write-Line $m 'Red' }
      }
      if ($MaxTokens -gt 0 -and ($inTok + $outTok) -gt $MaxTokens) {
        $m = "TOKEN OVERRUN: $($inTok + $outTok) tokens against MaxTokens=$MaxTokens"
        if (-not $violations.Contains($m)) { $violations.Add($m); Write-Line $m 'Red' }
      }
    }

    $now = Get-FolderSize
    $grewMB = [Math]::Round(($now.Bytes - $base.Bytes) / 1MB, 1)
    $turns = Get-TurnCount
    Write-Line ("heartbeat  calls={0} tokens={1} fails={2} turns={3} folder=+{4} file(s) +{5} MB  elapsed={6:hh\:mm}" -f `
      $calls, ($inTok + $outTok), $fails, $(if ($turns -lt 0) { 'mid-write' } else { $turns }),
      ($now.Files - $base.Files), $grewMB, ((Get-Date) - $startedAt)) 'DarkGray'

    # A transcript that stops advancing while calls keep arriving means the autosave is failing --
    # invisible otherwise, and it is the record you would want in the morning.
    if ($calls -gt 0 -and $turns -ge 0 -and $null -ne $baseTurns -and $turns -eq $baseTurns) {
      $m = "conversation.json has not gained a turn after $calls call(s) — autosave may be failing"
      if (-not $violations.Contains($m)) { $violations.Add($m); Write-Line $m 'Yellow' }
    }

    if ($StopAfterMinutes -gt 0 -and ((Get-Date) - $startedAt).TotalMinutes -ge $StopAfterMinutes) {
      Write-Line "reached -StopAfterMinutes $StopAfterMinutes — stopping" 'Cyan'
      break
    }

    Start-Sleep -Seconds $PollSeconds
  }
}
finally {
  $elapsed = (Get-Date) - $startedAt
  Write-Line "" 'Gray'
  Write-Line "=================== verdict ===================" 'Cyan'
  Write-Line ("watched        : {0:hh\:mm\:ss}" -f $elapsed) 'Cyan'
  Write-Line "calls          : $calls" 'Cyan'
  Write-Line "tokens         : $($inTok + $outTok)  (in $inTok / out $outTok)" 'Cyan'
  Write-Line "failed calls   : $fails" 'Cyan'
  $endTurns = Get-TurnCount
  Write-Line "turns          : $baseTurns -> $(if ($endTurns -lt 0) { 'mid-write' } else { $endTurns })" 'Cyan'
  $end = Get-FolderSize
  Write-Line "folder growth  : +$($end.Files - $base.Files) file(s), +$([Math]::Round(($end.Bytes - $base.Bytes)/1MB,1)) MB" 'Cyan'
  if ($MaxCalls -gt 0)  { Write-Line "vs MaxCalls    : $calls / $MaxCalls" 'Cyan' }
  if ($MaxTokens -gt 0) { Write-Line "vs MaxTokens   : $($inTok + $outTok) / $MaxTokens" 'Cyan' }

  if ($violations.Count -eq 0) {
    if ($calls -eq 0) {
      Write-Line "RESULT: NOTHING HAPPENED — no calls were recorded. That is not a pass." 'Yellow'
      Write-Line "        Check a trigger was actually armed (arming never persists, so a reopened file is off)." 'Yellow'
    } else {
      Write-Line "RESULT: PASS — $calls call(s), every one accounted for in runs.jsonl, no cap exceeded." 'Green'
    }
  } else {
    Write-Line "RESULT: FAIL — $($violations.Count) violation(s). F3 treats a single unexplained call as blocking." 'Red'
    foreach ($v in $violations) { Write-Line "   - $v" 'Red' }
  }
  Write-Line "full log: $LogPath" 'Cyan'
}
