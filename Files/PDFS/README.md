# PDFS

Standing reference PDFs the **Read PDF** tool (LLM Tools) can always read, on top of anything a
human attaches in the chat.

Each subfolder here is addressed by NAME on a Read PDF node's `PDF Folder` input:

```
Files/PDFS/
    structural-standards/    <- PDF Folder = "structural-standards"
    site-survey/             <- PDF Folder = "site-survey"
```

The name is typed on the node rather than derived from anything, so it is ordinary internalized
parameter data: it is saved inside the `.gh` and it **travels inside a preset**. That is the point —
a pipeline can ship already pointed at its own reference set.

A `PDF Folder` value that looks like a real path (`C:\...`, `\server\share\...`, `/mnt/...`) is used
as it stands instead, so a practice can point at a network share without copying anything here. A
bare name is sanitized to a single folder name, which is also what stops it walking out of this
directory.

Only `.pdf` files in the top level of a folder are listed, up to 200 of them. Nothing is written
here by Physalia — the tool only reads.

PDFs the human drops into the chat do **not** land here. Those are referenced where they already sit
and are never copied, which is what makes attaching a several-hundred-megabyte drawing set free.
