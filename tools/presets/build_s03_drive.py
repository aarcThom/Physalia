# Copyright (c) 2026 Physalia Contributors
# SPDX-License-Identifier: AGPL-3.0-or-later
"""
Scenario S03 - "Interrogate and Tidy Your Rhino Model".

The model writes Python and runs it against your live document, so you can ask it anything about
your model in plain English and have it do the boring fixes.
"""

# The ONE machine-specific line in this file. Set ROOT before exec'ing this script to build
# from a checkout somewhere else:  ROOT = r"D:\\code\\Physalia"
try:
    ROOT
except NameError:
    ROOT = r"C:\Users\rober\repos\Physalia"
exec(open(ROOT + r"\tools\presets\phybuild.py").read())

NAME = "S03 - Interrogate and Tidy Your Rhino Model"
OUT = PRESETS + r"\%s.phy" % NAME
DUMP = SCRATCH + r"\dumpS03.txt"

host = clear_host()
H, D = new_harness(host, 200, 200, name="interrogate-and-tidy")

panel(D, 40, 40,
      "INTERROGATE AND TIDY YOUR RHINO MODEL\r\n"
      "\r\n"
      "Ask your model a question in English and get a real answer, because the model writes Python "
      "and runs it against your live document.\r\n"
      "\r\n"
      "QUESTIONS IT ANSWERS PROPERLY\r\n"
      "  \"how many objects are on a layer that isn't in my layer standard?\"\r\n"
      "  \"what's the total surface area of everything on LAYER-GLAZING?\"\r\n"
      "  \"find every block instance that has been scaled non-uniformly\"\r\n"
      "  \"which curves aren't planar?\"\r\n"
      "  \"is anything more than 500m from the origin?\"\r\n"
      "\r\n"
      "AND JOBS IT DOES\r\n"
      "  \"move everything on Default onto the right layer by object type\"\r\n"
      "  \"rename my layers to match the standard in your memory\"\r\n"
      "  \"purge unused blocks and materials, then tell me what you removed\"\r\n"
      "  \"cap every open polysurface you can, and list the ones you couldn't\"\r\n"
      "\r\n"
      "WHY THIS AND NOT A PLUG-IN. A plug-in does the job somebody anticipated. This does the job "
      "you have in front of you, including the one-off nobody would ever write a button for.\r\n"
      "\r\n"
      "THE UNDO RULE. Everything one round does is ONE undo step. If you dislike the result, "
      "Ctrl+Z once and it is gone. Read that sentence again before you run this on a live model at "
      "five o'clock on a Friday.",
      w=920, h=470, colour=INTRO_GREEN)

# --------------------------------------------------------------------------- the loop

L = core_loop(D, 420, 720, model_name="Codex Model",
              instruction="You work on the user's live Rhino document with run_rhino_script.\r\n"
                          "\r\n"
                          "ALWAYS: read your memory first - it holds this office's layer and "
                          "naming standards. Look before you touch: run a script that MEASURES and "
                          "print what you found, then say what you propose to change and roughly "
                          "how many objects it will affect.\r\n"
                          "\r\n"
                          "If a job is destructive, irreversible in practice, or touches more than "
                          "about fifty objects, use ask_human before running it.\r\n"
                          "\r\n"
                          "Report what actually changed, using the object counts, not what you "
                          "intended to change.")

rhino = place(D, "Rhino Document", 500, 560, nick="Rhino Document")
units = place(D, "Document Units Grounding", 500, 610, nick="Document Units")
toolsp = place(D, "Tools Present", 660, 560, nick="Tools Present")
for g in (rhino, units, toolsp):
    wire(L["log"], "Grounding", g, 0)
vsnap = place(D, "View Snapshot", 660, 610, nick="View Snapshot")
img = place(D, "Add Image", 820, 560, nick="Add Image")
export = place(D, "Export Conversation", 820, 610, nick="Export Conversation")
for t in (vsnap, img, export):
    wire(L["log"], "Human Tools", t, "Human Tool")

panel(D, 420, 1080,
      "RHINO DOCUMENT is what stops the first round being wasted. Without it the model opens every "
      "session by running a script just to learn your layer table and object count. With it, that "
      "is already in front of it - including HOW MANY OBJECTS ARE SELECTED, which is what makes "
      "\"fix these\" a sentence it can act on.\r\n"
      "\r\n"
      "It refreshes when you edit the Rhino document, which is not something Grasshopper would "
      "normally notice at all.\r\n"
      "\r\n"
      "VIEW SNAPSHOT lets you show it what you are looking at without leaving the chat. Often "
      "faster than describing the problem.",
      w=340, h=340)

# --------------------------------------------------------------------------- tools

title(D, 1900, 480, "2 - WHAT IT CAN DO TO YOUR MODEL", w=420, h=44)
router = place(D, "Router", 1980, 640, nick="Router")
wire(router, "Tool Calls", L["call"], "Tool Calls")
router_slots(router, 2)
drive = place(D, "Drive Rhino", 2320, 600, nick="Drive Rhino")
askh = place(D, "Ask Human", 2320, 700, nick="Ask Human")
mem = place(D, "Memory", 2320, 800, nick="Memory")
memfolder = input_panel(D, 2100, 830, "office-standards", w=190, h=40, nick="memory folder")
wire(mem, "Memory Folder", memfolder, 0)
wire(drive, "Signal", router, 0)
wire(askh, "Signal", router, 1)
wire(mem, "Signal", router, 2)

script = panel(D, 2560, 560, "the script it ran - READ THIS", w=340, h=220, colour=OUTPUT_GREY)
script.AddSource(pin(drive, "out", "Last Script"))
sel = panel(D, 2560, 800, "what you selected when it asked", w=340, h=90, colour=OUTPUT_GREY)
sel.AddSource(pin(askh, "out", "Selection"))

panel(D, 1900, 920,
      "DRIVE RHINO runs Python 3 in the same process Rhino is in - the Script Editor's own engine. "
      "That means a failure comes back as a real error with a line number, not a wall of text it "
      "has to guess at, and PRINT IS THE ANSWER: it asks a question by writing three lines and "
      "reading what they print, in the same round.\r\n"
      "\r\n"
      "That is why there is no separate \"inspect the model\" tool. There does not need to be one.\r\n"
      "\r\n"
      "IT REPORTS THE OBJECT COUNT BEFORE AND AFTER, EVEN WHEN THE SCRIPT FAILS. A script that "
      "raised half way through has still done what it did up to that point - assuming otherwise is "
      "how a retry doubles your geometry.\r\n"
      "\r\n"
      "ASK HUMAN is the brake. The instruction tells it to ask before anything big or destructive; "
      "the question arrives as a card in the chat, and one of the answers it can ask for is \"go "
      "and select the ones you mean\", read at the moment you press the button.\r\n"
      "\r\n"
      "MEMORY is where your office standards live. Tell it once - \"our layers are "
      "DISCIPLINE-ELEMENT-STATUS\" - and it writes that down for next week. Name the folder in the "
      "white box; every pipeline given the same name shares one set of notes.",
      w=420, h=560)

# --------------------------------------------------------------------------- report out

title(D, 3000, 480, "3 - GETTING THE ANSWER OUT", w=320, h=44)
decon = place(D, "Deconstruct Signal", 3080, 600, nick="Deconstruct Signal")
wire(decon, "Signal", L["call"], "Success Signal")
out = place(D, "Harness Out", 3080, 700, nick="Harness Out")
out.Params.Input[0].NickName = "what it said"
wire(out, "Data", decon, "Payload")
panel(D, 3000, 790,
      "HARNESS OUT puts the model's written answer onto your canvas as text. Drag the grip labelled "
      "\"what it said\" from the right edge of the Harness node onto a Panel.\r\n"
      "\r\n"
      "Useful when the answer is a schedule or a list of problem objects and you want it beside the "
      "model rather than scrolled away in a conversation.\r\n"
      "\r\n"
      "For the whole conversation, use EXPORT CONVERSATION in the chat header instead.\r\n"
      "\r\n"
      "DECONSTRUCT SIGNAL is how you read what is travelling on a wire without consuming it - it "
      "only looks. Every signal carries its text on Payload.",
      w=320, h=320)

# --------------------------------------------------------------------------- returns + bounds

budget = place(D, "Budget Guard", 1400, 1220, nick="Budget Guard")
b_calls = slider(D, 1180, 1300, 60, 1, 300, nick="max calls")
wire(budget, "Max Calls", b_calls, 0)
wire(budget, "Signal", L["log"], "Signal")
wire(L["call"], "Signal", budget, "Success Signal")
spent = panel(D, 1320, 1340, "spent so far", w=300, h=80, colour=OUTPUT_GREY)
spent.AddSource(pin(budget, "out", "Spent"))

fb_res, co_res = back(D, drive, "Result", router, "Results",
                      2700, 1220, 1700, 1220, nick="tool results")
wire(fb_res, "Signal", askh, "Result")
wire(fb_res, "Signal", mem, "Result")
back(D, router, "Feedback", L["log"], "LLM Tool Signal",
     1700, 1560, 1060, 1560, nick="tool round to the log")

panel(D, 60, 1180,
      "THINGS TO TRY\r\n"
      "\r\n"
      "1. Start by ASKING, not doing. \"How many objects are on the Default layer, broken down by "
      "type?\" You will see immediately whether it is reading your model properly.\r\n"
      "\r\n"
      "2. Then a reversible job. \"Put everything on Default onto a layer called TRIAGE.\" Check it. "
      "Ctrl+Z. Notice the whole thing goes in one press.\r\n"
      "\r\n"
      "3. Teach it your standard: \"remember that our layers are DISCIPLINE-ELEMENT-STATUS, all "
      "caps, hyphen separated.\" Then in a later session ask it to check the file against that.\r\n"
      "\r\n"
      "4. When something surprises you, read the grey panel with the script in it. It is almost "
      "always faster than asking the model what it did.\r\n"
      "\r\n"
      "5. Add a TIMER trigger and a folder of incoming files if you want this to run overnight - "
      "but raise the budget guard first, and read what it wrote in the morning before trusting it.\r\n"
      "\r\n"
      "SAFETY, PLAINLY: this runs unrestricted Python inside Rhino. It can read and write anywhere "
      "you can. The value is exactly that, and so is the risk. Do not point it at a model you have "
      "not saved.",
      w=340, h=560)

commit_build(D, "build scenario S03")
solve(D)
solve(D)
write_dump(D, DUMP)
say("router outputs:", [q.NickName for q in router.Params.Output])
bad = sweep(D, NAME)
save_phy(H, OUT,
         description="Ask your Rhino model questions in plain English and have the boring fixes "
                     "done for you. The model writes Python and runs it against your live "
                     "document, so it can measure, report and tidy - one undo step per round.",
         chat_text="Interrogate and Tidy Your Rhino Model\r\n\r\n"
                   "Ask a question about your model in plain English. It writes Python, runs it "
                   "against your live document, and tells you what it found.\r\n\r\n"
                   "Start by asking, not doing:\r\n"
                   "  \"how many objects are on the Default layer, by type?\"\r\n"
                   "  \"which curves aren't planar?\"\r\n"
                   "  \"total surface area on LAYER-GLAZING?\"\r\n\r\n"
                   "Then a job: \"move everything on Default onto a layer called TRIAGE.\"\r\n\r\n"
                   "Everything one round does is ONE undo step. Save your file first - this runs "
                   "real Python against your real model.")
say("PROBLEMS:", bad)
