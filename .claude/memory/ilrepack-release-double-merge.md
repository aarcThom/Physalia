---
name: ilrepack-release-double-merge
description: ILRepack.Lib.MSBuild.Task ships a Release-only default merge target that must be suppressed with a stub ILRepack.targets
metadata: 
  node_type: memory
  type: reference
  originSessionId: 8ba6eaa2-c1c1-4bb2-9487-ac5379251e1f
  modified: 2026-08-09T05:58:38.065Z
---

`ILRepack.Lib.MSBuild.Task` (2.0.45) ships its own merge target in
`build/ILRepack.Lib.MSBuild.Task.targets`, and it runs **in addition to** any target you write:

```xml
<PropertyGroup>
  <ILRepackTargetsFile Condition="$(ILRepackTargetsFile) == ''">$(ProjectDir)ILRepack.targets</ILRepackTargetsFile>
</PropertyGroup>
<Target Name="ILRepack" AfterTargets="Build"
        Condition="$(Configuration.Contains('Release')) and !Exists('$(ILRepackTargetsFile)')">
```

Three things make this nasty:
- **Release only.** The condition is `$(Configuration.Contains('Release'))`, so a Debug build never
  shows the problem. Physalia's Release build failed for weeks while Debug was green.
- **No `LibraryPath`.** It merges `$(OutputPath)*.dll` indiscriminately with no search directories,
  so Mono.Cecil cannot resolve host assemblies the merged deps reference:
  `error : Failed to resolve assembly: 'Grasshopper, Version=8.24.25281.15001, …'`. The error is
  reported against the PACKAGE's targets file (line 16), not your csproj — that attribution is the
  clue that it is not your target failing.
- **It runs second.** NuGet imports package build targets after the csproj body, so Physalia's own
  `RepackGha` ran first and merged correctly; the package's pass then failed on top of the finished
  output. Net effect: a **correct 14 MB .gha on disk AND a failing build** — easy to misread as
  "the artifact is broken" or to not notice at all.
- Its companion `CleanReferenceCopyLocalPaths` shares the condition and deletes every
  `ReferenceCopyLocalPaths` from the output — which would have removed the JSON stack
  (JsonSchema.Net, System.Text.Json, …) that `RepackGha` deliberately keeps LOOSE and unmerged.

**Fix (2026-08-08):** an intentionally empty `src/Physalia.GH/ILRepack.targets`. Its mere existence
flips `!Exists('$(ILRepackTargetsFile)')` false and disables both package targets; the file is also
`Import`ed, so it must be a valid (empty) `<Project>`. Do not delete it — the failure returns, in
Release only. Clean Release and Debug builds then both succeed and produce the same artifact shape:
a ~14 MB merged `.gha` plus the five loose JSON-stack DLLs.

Diagnosis technique worth reusing: inject a probe target without touching the repo via
`dotnet msbuild … -p:CustomAfterMicrosoftCommonTargets=<path>.targets`. Note that items declared
inside a target body (`RepackInput`, `RepackLibPath`) are empty to a `BeforeTargets` probe — replicate
the transform (`@(ReferencePath->'%(RootDir)%(Directory)')`) instead of reading them.

See [[physalia-repo-gotchas]].
