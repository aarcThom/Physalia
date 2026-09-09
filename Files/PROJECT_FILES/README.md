# PROJECT_FILES

**Your project files are no longer here.** They live in

```
%LOCALAPPDATA%\Physalia\PROJECT_FILES\        (Windows)
~/.local/share/Physalia/PROJECT_FILES/        (elsewhere)
```

and this folder is left empty on purpose.

Rhino's Package Manager installs each version of a plug-in into a directory of its own, and updates
installed plug-ins silently when Rhino starts. Anything Physalia wrote in here was therefore one
update away from being stranded in a folder nothing reads any more — so a harness's downloads, its
autosaved transcript and its run log moved somewhere an update cannot reach. Whatever an older build
left in here is carried across the first time the new one runs; anything it could not move is named
on the Rhino command line.

The layout there is unchanged: one folder per harness, named after the harness.

```
PROJECT_FILES/
    curious-cake-soap-fun/       <- a harness that has not been renamed
        tile_4830E.las
        downloads.json           <- what was fetched, and from where
        PDF/                     <- where Read PDF looks
        conversation.json        <- the autosaved transcript
        runs.jsonl               <- one line per inference call
```

Renaming the harness renames the folder, and the four-word name is derived from the harness's own id,
so it is stable across saves. A `.phy` package can carry the whole folder with it.
