# Physalia memories

**The model's memories are no longer here.** They live in

```
%LOCALAPPDATA%\Physalia\MEMORIES\        (Windows)
~/.local/share/Physalia/MEMORIES/        (elsewhere)
```

and this folder is left empty on purpose: Rhino replaces a plug-in's install directory wholesale
when it updates the package, silently, at startup — so notes kept in here did not survive an update.
Anything an older build left behind is carried across the first time the new one runs.

Layout, unchanged:

- `GLOBAL/` — memories shared by every pipeline on this machine.
- `LOCAL/<name>/` — memories belonging to one pipeline. The `<name>` is whatever you type on the
  Memory tool's **Memory Folder** input; leave it blank and the tool uses its own component id,
  which is unique but not a name. It is typed rather than derived because both derivations tried
  before it failed silently by defaulting — keying on the `.gh` file meant memory followed the
  document rather than the pipeline, and keying on the harness's name meant every unrenamed pipeline
  quietly shared one folder.

The model addresses these through a virtual `/memories/global` and `/memories/local`, so what the
folders are called on disk is invisible to it.
