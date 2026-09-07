# Copyright (c) 2026 Physalia Contributors
# SPDX-License-Identifier: AGPL-3.0-or-later
"""
Regression test for the System Prompt Picker defect.

THE BUG: AddedToDocument re-placed a Picker on any input with no source, and that fires again every
time a file is READ. The fresh Picker had no saved choice, so on its SECOND solve it snapped to the
first entry in its list - and a plain conversational System Prompt reloaded with the 11,900-character
C# Script preamble silently folded into its prompt. Deleting the Picker was the documented
workaround and it did not survive a save.

THE FIX: PhyBase.Read marks the component as restored, and PhyBase.AutoPlacePicker skips a restored
one. Auto-placing is now what it always claimed to be in its own doc comment - something that
happens when you drop a component on the canvas.

Four assertions, run in order: a fresh placement still gets its Pickers, a deliberately-empty input
stays empty across a save and reload, the prompt stays short over repeated solves, and a document
saved WITH its Pickers still reloads with them wired and not duplicated.
"""

# The ONE machine-specific line in this file. Set ROOT before exec'ing this script to build
# from a checkout somewhere else:  ROOT = r"D:\\code\\Physalia"
try:
    ROOT
except NameError:
    ROOT = r"C:\Users\rober\repos\Physalia"
exec(open(ROOT + r"\tools\presets\phybuild.py").read())

import GH_IO

FAILED = []


def check(label, got, want):
    ok = got == want
    say("  %-58s %s   (got %r, want %r)" % (label, "PASS" if ok else "FAIL", got, want))
    if not ok:
        FAILED.append(label)


def pickers(doc):
    return len([o for o in doc.Objects if o.Name == "Picker"])


def archive_and_read(doc):
    """Round-trip a document through a real GH archive, which is what a save and reload is."""
    arch = GH_IO.Serialization.GH_Archive()
    arch.AppendObject(doc, "Definition")
    fresh = GH_Document()
    arch.ExtractObject(fresh, "Definition")
    return fresh


def prompt_of(doc):
    sp = [o for o in doc.Objects if o.Name == "System Prompt"][0]
    vals = [str(v) for v in pin(sp, "out", "System Prompt").VolatileData.AllData(True)]
    return len(vals[0]) if vals else 0


# ------------------------------------------------------------------ 1. a fresh placement

say("1. a System Prompt dropped on the canvas still gets its two Pickers")
fresh = GH_Document()
sp = place(fresh, "System Prompt", 400, 200, nick="System Prompt")
check("Pickers auto-placed on a new component", pickers(fresh), 2)
check("Preamble has a source", pin(sp, "in", "Preamble").SourceCount, 1)

# ------------------------------------------------- 2. an input left empty ON PURPOSE

say("")
say("2. the two Pickers are deleted, the way you would to leave the inputs empty")
for pk in [o for o in fresh.Objects if o.Name == "Picker"]:
    fresh.RemoveObject(pk, False)
check("no Pickers left", pickers(fresh), 0)
check("Preamble has no source", pin(sp, "in", "Preamble").SourceCount, 0)

extra = input_panel(fresh, 150, 300, "Keep answers short.", w=200, h=60, nick="my instructions")
wire(sp, "Additional Prompt", extra, 0)
fresh.Enabled = True
fresh.NewSolution(True)
baseline = prompt_of(fresh)
say("   prompt before saving: %d characters" % baseline)

# ----------------------------------------------------- 3. save and reload: THE DEFECT

say("")
say("3. saved and read back - the defect was that a Picker reappeared here")
reloaded = archive_and_read(fresh)
reloaded.Enabled = True
check("still no Pickers after the reload", pickers(reloaded), 0)
sp2 = [o for o in reloaded.Objects if o.Name == "System Prompt"][0]
check("Preamble still has no source", pin(sp2, "in", "Preamble").SourceCount, 0)

say("")
say("4. and it stayed short over repeated solves - it used to snap on the SECOND one")
lengths = []
for n in range(4):
    for o in reloaded.Objects:
        o.ExpireSolution(False)
    reloaded.NewSolution(True)
    lengths.append(prompt_of(reloaded))
    say("   solve %d: prompt is %d characters" % (n + 1, lengths[-1]))
check("prompt never grew (11902 was the old failure)", max(lengths) <= baseline, True)
check("prompt is still the typed instruction", max(lengths) < 500, True)

# ------------------------------------- 5. the ordinary case must be untouched

say("")
say("5. a document saved WITH its Pickers reloads with them wired, and not doubled")
normal = GH_Document()
sp3 = place(normal, "System Prompt", 400, 200, nick="System Prompt")
check("two Pickers to start with", pickers(normal), 2)
back_again = archive_and_read(normal)
check("still exactly two after a reload", pickers(back_again), 2)
sp4 = [o for o in back_again.Objects if o.Name == "System Prompt"][0]
check("Preamble source survived", pin(sp4, "in", "Preamble").SourceCount, 1)
check("Schema source survived", pin(sp4, "in", "Schema").SourceCount, 1)

# --------------------------------- 6. the same defect existed on six other components

say("")
say("6. every other component that auto-places a Picker, same test")
say("   (the fix is on PhyBase, so it should cover all of them at once)")
OTHERS = [
    ("Claude Code Model", None),
    ("Codex Model", None),
    ("Model API", "Models"),
    ("Anthropic Model", None),
    ("OpenAI Compatible Model", None),
    ("Token Estimator", None),
    ("Tokenization Techniques", None),
]
for name, sub in OTHERS:
    d = GH_Document()
    try:
        obj = place(d, name, 400, 200, sub=sub)
    except Exception as ex:
        say("  %-30s SKIPPED (%s)" % (name, str(ex)[:40]))
        continue
    placed = pickers(d)
    for pk in [o for o in d.Objects if o.Name == "Picker"]:
        d.RemoveObject(pk, False)
    after = pickers(archive_and_read(d))
    ok = placed >= 1 and after == 0
    say("  %-30s %s   placed %d on drop, %d after deleting and reloading"
        % (name, "PASS" if ok else "FAIL", placed, after))
    if not ok:
        FAILED.append(name)
    retire(d)

for d in (fresh, reloaded, normal, back_again):
    retire(d)

say("")
say("==== %s ====" % ("ALL PASSED" if not FAILED else "%d FAILED: %s" % (len(FAILED), FAILED)))
