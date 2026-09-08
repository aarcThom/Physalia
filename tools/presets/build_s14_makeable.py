# Copyright (c) 2026 Physalia Contributors
# SPDX-License-Identifier: AGPL-3.0-or-later
"""
Scenario S14 - "Can This Actually Be Made?".

Check the part against how it is going to be manufactured - draft, wall thickness, undercuts,
minimum radii, sheet sizes - before the quote comes back saying no.
"""

# The ONE machine-specific line in this file. Set ROOT before exec'ing this script to build
# from a checkout somewhere else:  ROOT = r"D:\\code\\Physalia"
try:
    ROOT
except NameError:
    ROOT = r"C:\Users\rober\repos\Physalia"
exec(open(ROOT + r"\tools\presets\phybuild.py").read())

NAME = "S14 - Can This Actually Be Made"
OUT = PRESETS + r"\%s.phy" % NAME
DUMP = SCRATCH + r"\dumpS14.txt"

host = clear_host()
H, D = new_harness(host, 200, 200, name="can-this-be-made")

panel(D, 40, 40,
      "CAN THIS ACTUALLY BE MADE?\r\n"
      "\r\n"
      "The part is beautiful and the toolmaker says no. Every process has a list of rules that "
      "everybody knows and nobody checks until the quote comes back: draft angle, wall thickness, "
      "undercuts, minimum internal radius, how big a sheet actually comes.\r\n"
      "\r\n"
      "THIS CHECKS THE GEOMETRY AGAINST THE RULES YOU SET. The rules are a paragraph of plain "
      "English on this canvas - edit them for your process, your supplier, your material - and it "
      "measures the model against them and tells you which face fails and by how much.\r\n"
      "\r\n"
      "IT SHIPS SET UP FOR INJECTION MOULDING because that has the strictest rules and the most "
      "expensive surprises. Rewrite the box for CNC, sheet metal, 3D printing, joinery, precast, "
      "whatever you make.\r\n"
      "\r\n"
      "  \"check this housing for draft - 1.5 degrees minimum, pulling in +Z\"\r\n"
      "  \"where is the wall thinner than 1.5mm or thicker than 3.5?\"\r\n"
      "  \"any undercuts for a two-part tool pulling along Z?\"\r\n"
      "  \"internal radii below 0.5mm - the cutter can't get in there\"\r\n"
      "  \"will each of these panels fit on a 3000 x 1500 sheet?\"\r\n"
      "\r\n"
      "IT IS NOT A DFM PACKAGE AND IT IS NOT YOUR TOOLMAKER. It is the check you were going to do "
      "by eye at eleven at night, done consistently and written down. Everything it flags is "
      "something to go and look at - and the ones it flags are usually the ones you would have "
      "missed.\r\n"
      "\r\n"
      "READ THE SCRIPT IT RAN. On this preset more than any other: \"draft angle\" has a precise "
      "meaning and you want to see that its meaning is yours.",
      w=960, h=560, colour=INTRO_GREEN)

SPINE = 800

# --------------------------------------------------------------------------- rules

title(D, 60, 640, "1 - THE MANUFACTURING RULES", w=380, h=44)

L = core_loop(D, 560, SPINE, model_name="Codex Model",
              instruction="You check geometry against manufacturing constraints. The current "
                          "process is INJECTION MOULDING in a two-part tool pulling along +Z. "
                          "Rewrite this paragraph for a different process.\r\n"
                          "\r\n"
                          "RULES TO CHECK\r\n"
                          "  Draft: every vertical-ish face at least 1.5 degrees from the pull "
                          "direction.\r\n"
                          "  Wall thickness: between 1.5 and 3.5 mm, and as even as you can "
                          "measure.\r\n"
                          "  Undercuts: nothing that would trap the tool on a straight +Z pull.\r\n"
                          "  Internal radii: nothing sharper than 0.5 mm.\r\n"
                          "  Overall: must fit within 200 x 200 x 120 mm.\r\n"
                          "\r\n"
                          "HOW TO WORK. Measure with run_rhino_script - never judge by eye, and "
                          "never assume a value you did not measure. Check the document units "
                          "FIRST and say what they are; every number above is in millimetres and a "
                          "model in metres will pass everything for the wrong reason.\r\n"
                          "\r\n"
                          "Report each rule as PASS, FAIL or NOT-MEASURED, and for a FAIL give the "
                          "worst value found, how many faces are affected, and where they are. "
                          "NOT-MEASURED is a perfectly good answer and is much better than a "
                          "confident wrong one - say what you would need in order to check it.\r\n"
                          "\r\n"
                          "When something fails, SELECT the offending objects in Rhino so the user "
                          "can see them, and say that you have.\r\n"
                          "\r\n"
                          "Then take_snapshot and look at the part before you summarise.")

rhinog = place(D, "Rhino Document", 940, 660, nick="Rhino Document")
units = place(D, "Document Units Grounding", 1100, 660, nick="Document Units")
toolsp = place(D, "Tools Present", 1260, 660, nick="Tools Present")
for g in (rhinog, units, toolsp):
    wire(L["log"], "Grounding", g, 0)
img = place(D, "Add Image", 940, 710, nick="Add Image")
mark = place(D, "Image Mark Up", 1100, 710, nick="Image Mark Up")
expc = place(D, "Export Conversation", 1260, 710, nick="Export Conversation")
for t in (img, mark, expc):
    wire(L["log"], "Human Tools", t, "Human Tool")

panel(D, 60, 1140,
      "THE WHITE BOX IS THE SPECIFICATION and it is the only thing here worth anything. Rewrite it "
      "for what you actually make. A few that are worth stealing:\r\n"
      "\r\n"
      "CNC MILLING - minimum internal radius set by the smallest cutter you will pay for; depth to "
      "diameter ratio; anything needing a fourth axis; can it be held.\r\n"
      "\r\n"
      "SHEET METAL - bend radius against material thickness; minimum flange length; hole-to-bend "
      "distance; does the flat pattern fit the sheet.\r\n"
      "\r\n"
      "3D PRINTING - unsupported overhang angle; minimum feature size; will it fit the build "
      "volume; can the powder or resin get out.\r\n"
      "\r\n"
      "JOINERY AND PANELS - sheet sizes with the grain direction, so a 2400 x 1200 board is not "
      "2400 both ways; edge distances for fixings.\r\n"
      "\r\n"
      "TWO SENTENCES IN THERE ARE LOAD-BEARING WHATEVER YOU MAKE. \"Check the document units "
      "FIRST\" - a model in metres passes a 1.5 mm wall check for entirely the wrong reason, and "
      "that is the failure mode that reaches production. And \"NOT-MEASURED is a perfectly good "
      "answer\" - a rule quietly skipped reads exactly like a rule passed.",
      w=380, h=580)

# --------------------------------------------------------------------------- tools

title(D, 1860, 640, "2 - MEASURING AND POINTING", w=380, h=44)
router = place(D, "Router", 1940, 800, nick="Router")
wire(router, "Tool Calls", L["call"], "Tool Calls")
router_slots(router, 3)
drive = place(D, "Drive Rhino", 2280, 720, nick="Drive Rhino")
look = place(D, "Take Snapshot", 2280, 820, nick="Take Snapshot")
askh = place(D, "Ask Human", 2280, 920, nick="Ask Human")
mem = place(D, "Memory", 2280, 1020, nick="Memory")
memfolder = input_panel(D, 2040, 1050, "how-we-make-things", w=200, h=40, nick="memory folder")
wire(mem, "Memory Folder", memfolder, 0)
wire(drive, "Signal", router, 0)
wire(look, "Signal", router, 1)
wire(askh, "Signal", router, 2)
wire(mem, "Signal", router, 3)

script = panel(D, 2560, 680, "the measuring script - READ IT", w=340, h=220, colour=OUTPUT_GREY)
script.AddSource(pin(drive, "out", "Last Script"))
sel = panel(D, 2560, 920, "what you selected when it asked", w=340, h=90, colour=OUTPUT_GREY)
sel.AddSource(pin(askh, "out", "Selection"))

panel(D, 1860, 1560,
      "DRIVE RHINO does the measuring, and this is the preset where you really should read the "
      "script. \"Draft angle\" has a precise meaning; so does \"wall thickness\", which is a "
      "genuinely hard thing to measure on an arbitrary solid. Seeing HOW it measured is the "
      "difference between a check and a reassuring noise.\r\n"
      "\r\n"
      "IT CAN ALSO SELECT THE FAILING OBJECTS, which is what the instruction asks it to do. A list "
      "of face indices is useless; ten faces lit up in the viewport is immediately actionable. That "
      "is worth more than any amount of prose and costs nothing extra.\r\n"
      "\r\n"
      "ASK HUMAN goes the other way - it can ask YOU to select something. \"Which face is the "
      "parting line on?\" is not a question a picture answers, and the selection is read at the "
      "moment you press the button.\r\n"
      "\r\n"
      "TAKE SNAPSHOT is how it sees the part rather than only its numbers. Some failures are "
      "obvious in a picture and invisible in a measurement.\r\n"
      "\r\n"
      "MEMORY is where your house rules go - your supplier's real sheet sizes, the radius your shop "
      "actually holds, the wall thickness that has worked before. Tell it once. That knowledge is "
      "usually in one person's head and this is a reasonable place for a copy.",
      w=380, h=540)

# --------------------------------------------------------------------------- out

title(D, 3000, 560, "3 - THE REPORT", w=340, h=44)
decon = place(D, "Deconstruct Signal", 3080, 740, nick="Deconstruct Signal")
wire(decon, "Signal", L["call"], "Success Signal")
out = place(D, "Harness Out", 3080, 840, nick="Harness Out")
out.Params.Input[0].NickName = "the DFM report"
wire(out, "Data", decon, "Payload")

panel(D, 3000, 930,
      "DRAG \"THE DFM REPORT\" ONTO A PANEL beside the part.\r\n"
      "\r\n"
      "PASS / FAIL / NOT-MEASURED per rule is the shape to insist on, because it is the shape you "
      "can send to somebody. A paragraph of prose about a part is a conversation; a list of rules "
      "with a verdict on each is a document.\r\n"
      "\r\n"
      "SEND IT TO THE TOOLMAKER WITH THE PART. Half of what comes back as a surprise is a rule "
      "somebody assumed you knew, and a report that says which rules you checked - and which you "
      "could not - gets that conversation started before the quote rather than after it.\r\n"
      "\r\n"
      "EXPORT CONVERSATION keeps the working as well as the verdict, which is what you want when "
      "somebody asks how a number was arrived at.",
      w=340, h=440)

# --------------------------------------------------------------------------- bounds + returns

budget = place(D, "Budget Guard", 1200, 1800, nick="Budget Guard")
b_calls = slider(D, 980, 1880, 60, 1, 300, nick="max calls")
wire(budget, "Max Calls", b_calls, 0)
wire(budget, "Signal", L["log"], "Signal")
wire(L["call"], "Signal", budget, "Success Signal")

fb_res, co_res = back(D, drive, "Result", router, "Results",
                      2480, 1780, 1660, 1780, nick="tool results")
for t in (look, askh, mem):
    wire(fb_res, "Signal", t, "Result")
back(D, router, "Feedback", L["log"], "LLM Tool Signal",
     1640, 2020, 700, 2020, nick="tool round to the log")

panel(D, 500, 2140,
      "THINGS TO TRY\r\n"
      "\r\n"
      "1. Give it ONE rule first. \"Check draft only, 1.5 degrees, pulling +Z.\" Read the script it "
      "wrote. If its definition of draft is not yours, fix the wording now - before it is one "
      "verdict among six.\r\n"
      "\r\n"
      "2. Check something you KNOW fails. A face you deliberately left vertical. If it passes, you "
      "have learned something important about the check rather than about the part.\r\n"
      "\r\n"
      "3. Ask it to select the failures in Rhino, then look. That is the moment this stops being a "
      "novelty.\r\n"
      "\r\n"
      "4. Feed it a model in metres on purpose and watch whether it notices. The instruction tells "
      "it to check units first; find out whether it does.\r\n"
      "\r\n"
      "5. Rewrite the rules for the thing you actually make, and put your supplier's real sheet "
      "sizes in memory.\r\n"
      "\r\n"
      "6. Then ask for the report, drop it on a panel, and send it with the part.",
      w=440, h=520)

commit_build(D, "build scenario S14")
solve(D)
solve(D)
write_dump(D, DUMP)
say("router outputs:", [q.NickName for q in router.Params.Output])
bad = sweep(D, NAME)
save_phy(H, OUT,
         description="Check the part against how it is going to be made - draft, wall thickness, "
                     "undercuts, minimum radii, sheet sizes - before the quote comes back saying "
                     "no. The rules are a paragraph you edit for your process; the verdict is "
                     "PASS / FAIL / NOT-MEASURED per rule, with the failing objects selected.",
         chat_text="Can This Actually Be Made?\r\n\r\n"
                   "It ships set up for INJECTION MOULDING. Rewrite the rules on the canvas for "
                   "CNC, sheet metal, printing, joinery - whatever you make.\r\n\r\n"
                   "START WITH ONE RULE: \"check draft only, 1.5 degrees, pulling +Z\". Then read "
                   "the script it wrote. \"Draft angle\" has a precise meaning and you want to see "
                   "that its meaning is yours.\r\n\r\n"
                   "Then check something you KNOW fails. If it passes, you have learned something "
                   "about the check rather than about the part.\r\n\r\n"
                   "Ask it to select the failures in Rhino - ten faces lit up beats any list of "
                   "indices. NOT-MEASURED is a good answer; a confident wrong one is not.")
say("PROBLEMS:", bad)
