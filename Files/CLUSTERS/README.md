# CLUSTERS

Drop Grasshopper cluster files (`.ghcluster`, also `.gh`/`.ghx`) into this folder to make them
available to the model through the **Cluster Grounding** component.

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
