# PROJECT_FILES

Each harness's own working files: what it downloads, what you drop in for it, and the reference
material it reads. One folder per harness, named after the harness.

```
Files/PROJECT_FILES/
    curious-cake-soap-fun/       <- a harness that has not been renamed
        tile_4830E.las
        downloads.json           <- what was fetched, and from where
        PDF/                     <- where Read PDF looks
    vancouver-lidar/             <- the same harness, renamed
```

A new harness is named with four words derived from its instance id — `curious-cake-soap-fun` —
so it has a memorable folder from the moment it is placed, and two harnesses can never share one.
Rename the harness (in the panel at the top-left of its canvas, or on the node) and **this folder is
renamed with it**, files and all.

## Pointing somewhere else

Every node that reads project files — Project Folder, Download File, Read File, Read PDF — takes a
`Project Folder` value, and the spelling decides what it means:

| Typed | Means |
|---|---|
| *(blank)* | the harness's own folder, here |
| `site-survey` | `Files/PROJECT_FILES/site-survey` — no separator, so it is a NAME |
| `./data`, `../shared/las` | relative to the folder the Grasshopper file is saved in |
| `D:\Projects\x`, `\share\y` | used exactly as typed |

A name is reduced to one safe folder name, which is also what stops it climbing out of this
directory. A path is not: it is a location you typed on purpose.

## What travels

Saving a harness as a preset writes a `.phy`, which carries this folder — except for files
`downloads.json` accounts for. Those are recorded as URLs and fetched again at the other end, so a
400MB point cloud costs the package a couple of hundred bytes instead of 400MB. Anything you added
by hand is carried in full, because nothing can fetch it again.
