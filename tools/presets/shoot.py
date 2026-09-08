# Copyright (c) 2026 Physalia Contributors
# SPDX-License-Identifier: AGPL-3.0-or-later
"""
Screenshot a harness's inner canvas to a PNG, so a layout can actually be LOOKED at rather than
inferred from pivots. Set SHOOT_DOC (a GH_Document) and SHOOT_PNG before exec'ing this.
"""

# The ONE machine-specific line in this file. Set ROOT before exec'ing this script to build
# from a checkout somewhere else:  ROOT = r"D:\\code\\Physalia"
try:
    ROOT
except NameError:
    ROOT = r"C:\Users\rober\repos\Physalia"
exec(open(ROOT + r"\tools\presets\phybuild.py").read())

from System.Drawing import Rectangle, Size

canvas = Instances.ActiveCanvas
_was = canvas.Document
canvas.set_Document(SHOOT_DOC)
SHOOT_DOC.Enabled = True
canvas.Document.NewSolution(False)
canvas.Refresh()

# The union of every object's bounds, with a margin, is what to frame.
box = None
for o in SHOOT_DOC.Objects:
    b = o.Attributes.Bounds
    box = b if box is None else System.Drawing.RectangleF.Union(box, b)
pad = 30
try:
    rec = Rectangle(*SHOOT_RECT)          # (x, y, w, h) in canvas units
except Exception:
    rec = Rectangle(int(box.Left - pad), int(box.Top - pad),
                    int(box.Width + pad * 2), int(box.Height + pad * 2))

# GH_ImageSettings is nested inside GH_Canvas, so it has to be reached through the method that
# takes it rather than off the GUI namespace.
_stype = [x for x in canvas.GetType().GetMethods() if x.Name == "GenerateHiResImage"][0]     .GetParameters()[1].ParameterType
settings = System.Activator.CreateInstance(_stype)
def _set(obj, name, value):
    """Some of GH_ImageSettings' members are read-only properties over private fields."""
    t = obj.GetType()
    pr = t.GetProperty(name)
    if pr is not None and pr.CanWrite:
        pr.SetValue(obj, value)
        return
    for fn in (name, "_" + name, "m_" + name, name.lower(), "_" + name.lower()):
        f = t.GetField(fn, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
        if f is not None:
            f.SetValue(obj, value)
            return
    say("could not set", name)

# GenerateHiResImage WRITES the tiles itself and returns their paths - it does not hand back a
# Bitmap. So the destination is folder + name + extension, and a big canvas comes back as several
# files.
_set(settings, "BackColour", System.Drawing.Color.FromArgb(255, 250, 250, 246))
_set(settings, "Zoom", System.Single(float(SHOOT_ZOOM)))
settings.Folder = os.path.dirname(SHOOT_PNG)
settings.FileName = os.path.splitext(os.path.basename(SHOOT_PNG))[0]
settings.Extension = "png"

total = clr.Reference[Size]()
files = canvas.GenerateHiResImage(rec, settings, total)

# It hands back TILES, named "<col>;<row>.png" in a temp folder. Stitch them into the one image
# that was actually asked for.
from System.Drawing import Bitmap, Graphics
tiles = {}
for f in files:
    f = str(f)
    key = os.path.splitext(os.path.basename(f))[0]
    col, row = [int(v) for v in key.split(";")]
    tiles[(col, row)] = f
out = Bitmap(total.Value.Width, total.Value.Height)
g = Graphics.FromImage(out)
x = 0
for col in sorted(set(k[0] for k in tiles)):
    y = 0
    colw = 0
    for row in sorted(set(k[1] for k in tiles if k[0] == col)):
        t = Bitmap(tiles[(col, row)])
        g.DrawImage(t, x, y, t.Width, t.Height)
        colw = max(colw, t.Width)
        y += t.Height
        t.Dispose()
    x += colw
g.Dispose()
out.Save(SHOOT_PNG, System.Drawing.Imaging.ImageFormat.Png)
out.Dispose()
say("shot", SHOOT_PNG, "canvas", rec.Width, "x", rec.Height,
    "-> image", str(total.Value), "from", len(files), "tiles")

if _was is not None:
    canvas.set_Document(_was)
