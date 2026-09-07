# Copyright (c) 2026 Physalia Contributors
# SPDX-License-Identifier: AGPL-3.0-or-later
"""
Round-trip test for a written .phy: read it back the way the preset loader does, and report what
the other end would get. Set PHY before exec'ing this.
"""

exec(open(r"C:\Users\rober\repos\Physalia\tools\presets\phybuild.py").read())

hc = _type("Physalia.GH.Harness.HarnessComponent")
pkg = _type("Physalia.Core.Packaging.PhyPackage")

say("is a package (PK header):", pkg.GetMethod("IsPackage").Invoke(None, System.Array[System.Object]([PHY])))

# Two overloads (with and without the out-manifest); pick the two-parameter one.
m = None
for cand in hc.GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public):
    if cand.Name == "ReadDocumentFile" and cand.GetParameters().Length == 2:
        m = cand
assert m is not None, "ReadDocumentFile(string, out PhyManifest) not found"
args = System.Array[System.Object]([PHY, None])
doc = m.Invoke(None, args)
mani = args[1]
if doc is None:
    say("READ FAILED - the loader would refuse this file")
else:
    say("read back:", doc.ObjectCount, "objects")
    say("manifest name:", mani.Name if mani else "(none)")
    say("manifest description:", (mani.Description if mani else None) or "(none)")
    say("manifest chatText:", ((mani.ChatText if mani else None) or "(none)").replace("\r\n", " / ")[:200])
    chats = [o for o in doc.Objects if o.Name == "Chat"]
    say("chats inside:", len(chats), "- the loader refuses a package with none")

    kinds = {}
    for o in doc.Objects:
        kinds[o.Name] = kinds.get(o.Name, 0) + 1
    say("contents:", ", ".join("%s x%d" % (k, v) for k, v in sorted(kinds.items())))

    # overlap check: two annotation panels sitting on top of each other is a layout bug the
    # component-error sweep cannot see.
    boxes = []
    for o in doc.Objects:
        b = o.Attributes.Bounds
        boxes.append((o.Name, o.NickName, b.Left, b.Top, b.Right, b.Bottom))
    clashes = 0
    for i in range(len(boxes)):
        for j in range(i + 1, len(boxes)):
            a, b = boxes[i], boxes[j]
            ox = min(a[4], b[4]) - max(a[2], b[2])
            oy = min(a[5], b[5]) - max(a[3], b[3])
            if ox > 4 and oy > 4:
                say("  OVERLAP %s/%s vs %s/%s by %.0fx%.0f"
                    % (a[0], a[1], b[0], b[1], ox, oy))
                clashes += 1
    say("overlaps:", clashes)
    xs = [b[2] for b in boxes] + [b[4] for b in boxes]
    ys = [b[3] for b in boxes] + [b[5] for b in boxes]
    say("extent: x %.0f..%.0f  y %.0f..%.0f" % (min(xs), max(xs), min(ys), max(ys)))
    doc.Dispose()
