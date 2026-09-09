# CLUSTERS

Drop Grasshopper cluster files (`.ghcluster`, also `.gh`/`.ghx`) into this folder to make them
available to the model through the **Cluster Grounding** component.

**Two folders are read, and yours wins.** This one ships with the plug-in and is replaced whenever
Rhino updates the package — silently, at startup — so put your own clusters in

```
%LOCALAPPDATA%\Physalia\CLUSTERS\        (Windows)
~/.local/share/Physalia/CLUSTERS/        (elsewhere)
```

where an update cannot reach them. A cluster of yours with the same name as a shipped one shadows it,
and a `clusters.json` entry there overrides the shipped description for the same file name. Nothing
is copied between the two: what we ship keeps arriving with updates, and what you write survives
them.

## `clusters.json`

An optional manifest that adds a human-written description to each cluster. It is an array of
objects, one per cluster file:

```json
[
  { "file": "MyCluster.ghcluster", "description": "Lofts a set of section curves into a hull." }
]
```

- `file` — the cluster file name in this folder.
- `description` — what the cluster does and when to use it. This text is folded into the system
  prompt. The cluster's **input/output parameters are introspected automatically** from the file,
  so you do not list them here.

A cluster file present in the folder but missing from the manifest still appears (with no
description). A manifest entry whose `file` is not present in the folder is ignored. Copy
`clusters.json.example` to `clusters.json` to get started.
