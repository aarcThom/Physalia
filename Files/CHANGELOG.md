# Changelog

What changed in each release of Physalia.

The chat window reads this file. When Rhino's Package Manager updates the plug-in — which it
does silently, at startup, with no prompt — the window shows the section matching the new
version, so the update is not the first thing you learn about from your pipeline behaving
differently. So the format is load-bearing: **one `## <version>` heading per release**, newest
first, and nothing but the release's own notes underneath it. A date or a name after the number
is fine (`## 1.1.0 — 2026-10-01`); a missing section costs the notice its detail and nothing
else.

## 1.0.0

- Your own work — project folders and their downloads, autosaved transcripts, run logs, the model's
  memories, and any harness you saved as a preset — now lives in `%LOCALAPPDATA%\Physalia`, where a
  plug-in update cannot reach it. Anything an older build left behind is moved across on first run.
- System prompts, clusters and the shipped presets still come from the plug-in, so they keep
  arriving with updates. A file of your own with the same name wins.
- The entry screen shows which build you are running, and says so when an update changes it.
