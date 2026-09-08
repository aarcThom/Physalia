# Copyright (c) 2026 Physalia Contributors
# SPDX-License-Identifier: AGPL-3.0-or-later
"""
Load a written .phy the way a user would, then drive one real round through it without the chat
window: Chat.SubmitFromWindow mints the Prompt Signal, and the LLM Call answers for real.

Set PHY, and optionally PROMPT. Leaves the harness on the canvas so a follow-up call can poll it.
"""

# The ONE machine-specific line in this file. Set ROOT before exec'ing this script to build
# from a checkout somewhere else:  ROOT = r"D:\\code\\Physalia"
try:
    ROOT
except NameError:
    ROOT = r"C:\Users\rober\repos\Physalia"
exec(open(ROOT + r"\tools\presets\phybuild.py").read())

try:
    PROMPT
except NameError:
    PROMPT = "Reply with exactly these three words and nothing else: PHYSALIA LOOP WORKS"

hc = _type("Physalia.GH.Harness.HarnessComponent")
read = [c for c in hc.GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
        if c.Name == "ReadDocumentFile" and c.GetParameters().Length == 2][0]
create = [c for c in hc.GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
          if c.Name == "CreateWith"][0]

host = clear_host()
args = System.Array[System.Object]([PHY, None])
inner = read.Invoke(None, args)
if inner is None:
    raise Exception("the loader refused %s" % PHY)
manifest = args[1]
harness = create.Invoke(None, System.Array[System.Object]([inner]))
harness.CreateAttributes()
harness.Attributes.Pivot = PointF(200.0, 200.0)
host.AddObject(harness, False)
if manifest is not None:
    # AdoptPackage is internal - reach it the same way everything else here is reached.
    hc.GetMethod("AdoptPackage", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)         .Invoke(harness, System.Array[System.Object]([PHY, manifest]))
host.NewSolution(False)

D = harness.EnsureInnerDocument()
D.Enabled = True
D.NewSolution(True)
say("placed", PHY, "->", D.ObjectCount, "objects; name", harness.NickName)

chat = [o for o in D.Objects if o.Name == "Chat"][0]
chat.SubmitFromWindow(PROMPT)
say("submitted:", PROMPT)
