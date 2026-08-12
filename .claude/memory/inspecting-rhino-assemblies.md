---
name: inspecting-rhino-assemblies
description: "How to interrogate Rhino 8's shipped DLLs from PowerShell — reflect over types/members, and pull literal strings (templates, regexes) out of the metadata"
metadata: 
  node_type: memory
  type: reference
  originSessionId: 5eabf66c-d38d-414b-8bbf-e50f65864dd1
  modified: 2026-08-12T05:45:43.071Z
---

When a question is "what does the Rhino/RhinoCode API actually do here", the shipped assemblies in
`C:\Program Files\Rhino 8\System\` answer it directly, and faster than the docs. Two techniques,
both used to build [[csharp-transmitter]]:

**1. Reflect over the types.** `Assembly::LoadFrom` works for `Rhino.Runtime.Code.dll` and
`RhinoCodePlatform.GH.dll` on their own, but anything touching Grasshopper (e.g.
`RhinoCodePlatform.GH1.dll`) throws `ReflectionTypeLoadException` — `GH_IO` lives in the
Grasshopper plug-in folder, not `System\`. Register a resolver first:

```powershell
$handler = [System.ResolveEventHandler]{
  param($s,$e)
  $n=(New-Object System.Reflection.AssemblyName $e.Name).Name
  foreach($d in @('C:\Program Files\Rhino 8\System',
                  'C:\Program Files\Rhino 8\Plug-ins\Grasshopper')){
    $f=Join-Path $d "$n.dll"; if(Test-Path $f){ return [Reflection.Assembly]::LoadFrom($f) } }
  return $null }
[AppDomain]::CurrentDomain.add_AssemblyResolve($handler)
```
Then `$a.GetType('Ns.Type').GetMembers() | % { $_.ToString() }`. Wrap `GetTypes()` in try/catch and
fall back to `$_.Exception.Types | ? {$_ -ne $null}` for partially-loadable assemblies. Static
members are reachable too — this is how `LanguageSpec.CSharp` / `.Python3` and their
`*.*.csharp@*.*` wildcard forms were confirmed, along with `Matches` reading **scope-first** (the
wildcard-holding spec is the receiver).

**2. Grep the metadata for string literals.** The engine's code templates and its parsing regexes
are plain string constants, so they can be read straight out of the file — no decompiler needed:

```powershell
$b=[IO.File]::ReadAllBytes($dll)
$u16=[Text.Encoding]::Unicode.GetString($b)   # .NET metadata strings are UTF-16
$u8 =[Text.Encoding]::UTF8.GetString($b)      # older/native-ish blobs
```
Search, then print a ±1500-char window around each hit. Output is interleaved with mojibake (every
OTHER string decodes as CJK garbage when you guess the wrong width) — read past it, the run you
want is legible. This is how the `Script_Instance : GH_ScriptInstance` template and the
`private\s+(async\s+)?void\s+RunScript\(...` / `(ref|out)\s` /
`(IEnumerable|IList|List|DataTree)\<...\>` patterns were recovered from
`RhinoCodePlatform.Rhino3D.dll`. **Copy such a regex verbatim into any guard that front-runs the
engine** — a looser guard passes source that then dies on the canvas.

Note `ScriptComponents.gha` (legacy GH C# component) and `RhinoCodePluginGH.gha` (the Rhino 8 one)
are different components; only the latter implements `IScriptComponent`. See
[[physalia-repo-gotchas]] for the build-side traps.
