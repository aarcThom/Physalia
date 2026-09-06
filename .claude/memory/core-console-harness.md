---
name: core-console-harness
description: Drive Physalia.Core from a throwaway net7.0 console app to test providers against live CLIs/APIs without Rhino
metadata: 
  node_type: memory
  type: project
  originSessionId: 4b03585f-0a85-4efa-88ff-01df7c93a74e
  modified: 2026-08-17T06:24:06.031Z
---

The Boundary Rule has a payoff that is easy to miss: `Physalia.Core` has no Grasshopper dependency,
so **any provider can be driven from a plain console app and tested against the real service** —
no Rhino, no canvas, no `.gha` copy, no waiting for a component to solve.

Recipe (used 2026-08-16 to build [[codex-provider]] and [[codex-dynamic-tools]]):

```xml
<!-- scratchpad/Harness/Harness.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType><TargetFramework>net7.0</TargetFramework>
    <Nullable>enable</Nullable><ImplicitUsings>enable</ImplicitUsings>
    <NoWarn>$(NoWarn);NU1701;CA1416</NoWarn>
  </PropertyGroup>
  <ItemGroup><ProjectReference Include="…\src\Physalia.Core\Physalia.Core.csproj" /></ItemGroup>
</Project>
```

Then build a `Conversation` + `SystemPrompt` by hand and `await foreach` over
`provider.StreamAsync(...)`. `dotnet run --project <harness>` — done. Keep it in the scratchpad,
not the repo.

**Why it is worth the five minutes:** it tests things xUnit cannot (real auth, real streaming, real
subprocess lifetime) and things Rhino makes painfully slow to reach. It caught the defects that
mattered — empty reasoning deltas, an unanswered JSON-RPC request stalling a turn, a model
apologising into the conversation after a deferred tool call — before a single Rhino restart.

**Simulate the pipeline rather than guessing at it.** For a tool round, append what the Router and
Conversation Log would append (assistant turn with `ToolCallContent`, then a user turn with
`ToolResultContent`) and call again. That exercises the provider's real seed-vs-delta arithmetic.

**Always include a regression leg** for the path you did NOT change (e.g. the no-tools call), so
"I only added a feature" is a measurement rather than a hope.

Complements the unit tests in `Physalia.Core.Tests` ([[tier1-refactoring]]); it does not replace a
live Rhino run, which is still the only thing that exercises the GH components themselves.

**2026-09-05, it caught a real one.** Driving `ApiRequest.SendPagedAsync` against the live Vancouver
portal showed a 5929-record dataset stopping at 5000 with "stopped after 50 requests" — a runaway
guard that had silently become the BOUND for any API with a small page, reporting a reason unrelated
to what was asked for. Every unit test passed throughout; only real data with real page sizes showed
it. Raised to 100 pages. Cost: one `.csproj`, one `Program.cs`, no Rhino.
