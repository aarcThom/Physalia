<#
.SYNOPSIS
    Stage a built Physalia into a yak package folder: copy, prune, check, and report.

.DESCRIPTION
    Everything here exists because the build output is NOT the package.

    The prune lives in this script rather than in Physalia.GH.csproj on purpose. Narrowing
    <RuntimeIdentifiers> on a library changes restore and output layout for every developer build
    and would fight the deferred macOS port (planning/mac-port.md); deleting from a staged COPY is
    reversible, leaves dev builds byte-identical, and keeps the mac package one -KeepRuntimeIds
    away.

    The checks at the end are the point of the script. Each one corresponds to a way a package can
    look healthy on disk and fail only once installed:

      * runtimes/win-x64/native  - PdfNativeLibrary probes runtimes/<os>-<arch>/native/ at runtime.
        Prune the wrong thing and the symptom is a DllNotFoundException in Rhino ONLY, from a build
        that is green everywhere else. See planning/pdf-tools.md.
      * Bridge/                  - McpServer resolves the MCP bridge as "Bridge" beside the
        assembly, so the folder NAME is load-bearing. It is a real net8.0 executable, so its
        .deps.json and .runtimeconfig.json must SURVIVE the prune; only its .pdb goes.
      * the user-owned folders   - DataMigration scans Files/PROJECT_FILES and Files/MEMORIES
        BESIDE THE INSTALL and moves what it finds into the user's data folder on first run.
        Anything left in them here lands in every user's %LOCALAPPDATA% forever.
      * version agreement        - the package manager auto-updates installed copies silently by
        comparing versions, and the chat window shows the CHANGELOG section matching the new one.
        A manifest that disagrees with the assembly, or a version with no changelog section, is a
        release that arrives unannounced.

.PARAMETER Configuration
    Which build to stage. Release for anything that will be pushed.

.PARAMETER StagingRoot
    Where to stage. Defaults to <repo>/artifacts/yak, which is already gitignored.

.PARAMETER KeepRuntimeIds
    Native runtime identifiers to keep. Rhino 8 for Windows is x64 only: there is no 32-bit and no
    native-ARM Rhino to load us, so win-arm64 and win-x86 are 33 MB of nothing. A mac package would
    pass -KeepRuntimeIds osx-x64,osx-arm64 instead.

.PARAMETER YakBuild
    Also run 'yak build --platform win' in the staged folder.

.EXAMPLE
    ./tools/packaging/Stage-YakPackage.ps1 -YakBuild
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$StagingRoot,
    [string[]]$KeepRuntimeIds = @('win-x64'),
    [switch]$YakBuild
)

$ErrorActionPreference = 'Stop'

$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$repo = (Resolve-Path (Join-Path $here '..\..')).Path
$csproj = Join-Path $repo 'src\Physalia.GH\Physalia.GH.csproj'
$source = Join-Path $repo "src\Physalia.GH\bin\$Configuration\net7.0-windows"
if (-not $StagingRoot) { $StagingRoot = Join-Path $repo 'artifacts\yak' }
$stage = Join-Path $StagingRoot 'physalia'

$problems = [System.Collections.Generic.List[string]]::new()
$warnings = [System.Collections.Generic.List[string]]::new()

function Assert-Staged([string]$relative, [string]$why) {
    if (-not (Test-Path (Join-Path $stage $relative))) {
        $problems.Add("MISSING  $relative  - $why")
    }
}

function Get-SizeMb([string]$path) {
    $sum = (Get-ChildItem $path -Recurse -File | Measure-Object -Property Length -Sum).Sum
    return [math]::Round($sum / 1MB, 1)
}

# ---------------------------------------------------------------- versions, before anything moves

$csprojText = Get-Content $csproj -Raw
if ($csprojText -notmatch '<Version>([^<]+)</Version>') {
    throw "No <Version> in $csproj."
}
$version = $Matches[1].Trim()

$manifestSource = Join-Path $here 'manifest.yml'
$manifestText = Get-Content $manifestSource -Raw
if ($manifestText -notmatch '(?m)^version:\s*(\S+)\s*$') {
    throw "No 'version:' line in $manifestSource."
}
$manifestVersion = $Matches[1].Trim()

if ($version -ne $manifestVersion) {
    throw "Version drift: Physalia.GH.csproj says $version, manifest.yml says $manifestVersion. The package manager auto-updates on this number - fix both before staging."
}

if (-not (Test-Path $source)) {
    throw "No $Configuration build at $source. Run: dotnet build src/Physalia.slnx -c $Configuration"
}
if (-not (Test-Path (Join-Path $source 'Physalia.GH.gha'))) {
    throw "$source has no Physalia.GH.gha - that build did not finish."
}

Write-Host ''
Write-Host "Physalia $version  ->  $stage" -ForegroundColor Cyan
Write-Host ''

# ------------------------------------------------------------------------------------ copy, clean

if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Path $stage -Force | Out-Null
Copy-Item (Join-Path $source '*') $stage -Recurse -Force

$before = Get-SizeMb $stage

# --------------------------------------------------------------------------------- prune: runtimes

# Every runtimes/ folder, not just the one at the root: the MCP bridge carries its own, and a
# browser-RID System.Text.Encodings.Web rode along in it until this walked the tree.

$droppedRids = @()
foreach ($runtimes in @(Get-ChildItem $stage -Recurse -Directory -Filter 'runtimes')) {
    foreach ($rid in (Get-ChildItem $runtimes.FullName -Directory)) {
        if ($KeepRuntimeIds -notcontains $rid.Name) {
            $droppedRids += $rid.Name
            Remove-Item $rid.FullName -Recurse -Force
        }
    }
    if (-not (Get-ChildItem $runtimes.FullName -Recurse -File)) {
        Remove-Item $runtimes.FullName -Recurse -Force
    }
}
Write-Host ("runtimes  kept {0}; dropped {1} RID(s)" -f ($KeepRuntimeIds -join ', '), $droppedRids.Count)

# ---------------------------------------------------------- prune: debug and doc-validation output
#
# Physalia.GH.xml is generated so the COMPILER validates the doc comments
# (GenerateDocumentationFile in the csproj), not to ship. The .gha's own deps.json /
# runtimeconfig.json are inert for an assembly loaded by Assembly.LoadFrom, but are kept: harmless,
# and dropping them is an untested change to how the plug-in resolves.

Get-ChildItem $stage -Recurse -File -Filter '*.pdb' | Remove-Item -Force
Remove-Item (Join-Path $stage 'Physalia.GH.xml') -Force -ErrorAction SilentlyContinue

# ------------------------------------------------------------------- prune: the user's own folders
#
# These ship EMPTY, carrying only the README that says where the real one went. DataMigration scans
# them beside the install, so anything left here is copied into every user's data folder on first
# run. A README or a .gitkeep is fine; anything else is somebody's work leaking into a release.
#
# $shipped is the deliberate exception, and it is deliberately a SHORT list. A .gh preset cannot
# carry a payload the way a .phy carries its files/ folder, so a preset that needs a data file
# ships it here and lets DataMigration deliver it: the preset's Project Folder input names the
# folder with no separator, which resolves under the user's PROJECT_FILES. Preset 29 (Rhino to
# ComfyUI) is the only one that does this. Converting it to a .phy would retire the exception.

$shipped = @('PROJECT_FILES\comfy-render')

foreach ($folder in @('MEMORIES', 'PROJECT_FILES', 'PRESETS\User', 'PRESETS\Community')) {
    $dir = Join-Path $stage "Files\$folder"
    if (-not (Test-Path $dir)) { continue }

    foreach ($item in (Get-ChildItem $dir -Recurse -File)) {
        if (@('README.md', '.gitkeep') -contains $item.Name) { continue }

        $relative = $item.FullName.Substring((Join-Path $stage 'Files').Length + 1)
        if ($shipped | Where-Object { $relative.StartsWith($_ + [IO.Path]::DirectorySeparatorChar, 'OrdinalIgnoreCase') }) {
            continue
        }

        $warnings.Add($item.FullName.Substring($stage.Length + 1))
        Remove-Item $item.FullName -Force
    }

    Get-ChildItem $dir -Recurse -Directory |
        Sort-Object { $_.FullName.Length } -Descending |
        Where-Object { -not (Get-ChildItem $_.FullName -Recurse -File) } |
        ForEach-Object { Remove-Item $_.FullName -Recurse -Force }
}

# --------------------------------------------------------------------------- add the package's own

Copy-Item $manifestSource (Join-Path $stage 'manifest.yml') -Force
Copy-Item (Join-Path $here 'icon.png') (Join-Path $stage 'icon.png') -Force
Copy-Item (Join-Path $repo 'LICENSE') (Join-Path $stage 'LICENSE') -Force
Copy-Item (Join-Path $repo 'README.md') (Join-Path $stage 'README.md') -Force

# ------------------------------------------------------------------------------------------ checks

Assert-Staged 'Physalia.GH.gha'    'the plug-in itself, and it MUST sit at the package root'
Assert-Staged 'manifest.yml'       'yak build reads it from the package root'
Assert-Staged 'icon.png'           'the manifest names it; icon_url is obsolete'
Assert-Staged 'LICENSE'            'AGPL-3.0-or-later travels with the binary'
Assert-Staged 'README.md'          'what the listing url points at'
Assert-Staged 'Files\CHANGELOG.md' 'the update notice reads it from the package directory'

foreach ($rid in $KeepRuntimeIds) {
    Assert-Staged "runtimes\$rid\native" 'PdfNativeLibrary probes runtimes/<os>-<arch>/native/ at runtime'
}
Assert-Staged 'runtimes\win-x64\native\libSkiaSharp.dll' 'PDF page rasterization'
Assert-Staged 'runtimes\win-x64\native\pdfium.dll'       'PDF page rasterization'

Assert-Staged 'Bridge\Physalia.McpBridge.exe'                'remote and OAuth MCP servers are relayed through it'
Assert-Staged 'Bridge\Physalia.McpBridge.runtimeconfig.json' 'a real net8.0 exe will not start without it'
Assert-Staged 'Bridge\Physalia.McpBridge.deps.json'          'a real net8.0 exe will not start without it'

Assert-Staged 'Files\SYSTEM_PROMPTS'      'the System Prompt component resolves preambles by name'
Assert-Staged 'Files\PRESETS\Physalia'    'the shipped preset gallery'
Assert-Staged 'Files\PRESETS\Physalia\AI' 'the experimental set, behind the chat window toggle'
Assert-Staged 'Files\PROJECT_FILES\comfy-render\img2img-sdxl.json' 'preset 29 has no other way to find its SDXL graph - see $shipped above'

$changelog = Join-Path $stage 'Files\CHANGELOG.md'
if (Test-Path $changelog) {
    $pattern = '(?m)^##\s+' + [regex]::Escape($version) + '\b'
    if ((Get-Content $changelog -Raw) -notmatch $pattern) {
        $problems.Add("Files/CHANGELOG.md has no '## $version' section - the update notice would name the version and then have nothing to say about it.")
    }
}

$strayPdbs = @(Get-ChildItem $stage -Recurse -File -Filter '*.pdb')
if ($strayPdbs.Count -gt 0) {
    $problems.Add("$($strayPdbs.Count) .pdb file(s) survived the prune.")
}

$presetDir = Join-Path $stage 'Files\PRESETS\Physalia'
$presetCount = @(Get-ChildItem $presetDir -File -ErrorAction SilentlyContinue).Count
$aiCount = @(Get-ChildItem (Join-Path $presetDir 'AI') -File -ErrorAction SilentlyContinue).Count
if ($presetCount -eq 0) { $problems.Add('Files/PRESETS/Physalia holds no presets.') }

# ----------------------------------------------------------------------------------------- report

$after = Get-SizeMb $stage
$files = @(Get-ChildItem $stage -Recurse -File).Count

Write-Host ''
Write-Host ("staged    {0} files, {1} MB  (was {2} MB before the prune)" -f $files, $after, $before)
Write-Host ("presets   {0} in Physalia, {1} in Physalia/AI" -f $presetCount, $aiCount)

if ($warnings.Count -gt 0) {
    Write-Host ''
    Write-Host 'WARNING - these were in the build output and do NOT belong in a release:' -ForegroundColor Yellow
    $warnings | ForEach-Object { Write-Host "  $_" -ForegroundColor Yellow }
    Write-Host '  They were pruned from the package, but they are still in the repo, so a developer' -ForegroundColor Yellow
    Write-Host '  install still migrates them into %LOCALAPPDATA%\Physalia on first run.' -ForegroundColor Yellow
}

if ($problems.Count -gt 0) {
    Write-Host ''
    Write-Host 'FAILED:' -ForegroundColor Red
    $problems | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
    exit 1
}

Write-Host ''
Write-Host 'All checks passed.' -ForegroundColor Green

if ($YakBuild) {
    $yak = 'C:\Program Files\Rhino 8\System\Yak.exe'
    if (-not (Test-Path $yak)) { throw "Yak.exe not found at $yak." }
    Write-Host ''
    Push-Location $stage
    try { & $yak build --platform win } finally { Pop-Location }
    Get-ChildItem $stage -Filter '*.yak' | ForEach-Object {
        Write-Host ''
        Write-Host ("built     {0}  ({1} MB)" -f $_.Name, [math]::Round($_.Length / 1MB, 1)) -ForegroundColor Green
        Write-Host '          Check the rh tag in that name against the RhinoCommon you compiled against.'
    }
}
else {
    Write-Host ''
    Write-Host 'Next:'
    Write-Host "  cd `"$stage`""
    Write-Host '  & "C:\Program Files\Rhino 8\System\Yak.exe" build --platform win'
}
