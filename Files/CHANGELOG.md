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

- Your own work — a harness's project folder with its downloads, autosaved transcript and run
  log, the model's memories, and any harness you saved as a preset — now lives in
  `%LOCALAPPDATA%/Physalia` rather than beside the plug-in. A package update replaces the
  install directory wholesale, so anything kept there was one silent update away from being
  stranded. Whatever an older build left behind is moved across the first time this version
  runs; nothing is overwritten, and anything it could not move is named on the Rhino command
  line.
- System prompts, clusters and the shipped presets still come from the plug-in, so they keep
  arriving with updates. A file of your own with the same name takes precedence over the
  shipped one.
- The chat window's entry screen shows which build you are running, and says so when a silent
  update has changed it.
