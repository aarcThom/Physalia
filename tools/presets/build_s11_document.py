# Copyright (c) 2026 Physalia Contributors
# SPDX-License-Identifier: AGPL-3.0-or-later
"""
Scenario S11 - "Explain the Definition Nobody Documented".

Point it at a Grasshopper file - yours from eighteen months ago, or somebody else's - and get back
what it does, which sliders matter, and where it will break.
"""

# The ONE machine-specific line in this file. Set ROOT before exec'ing this script to build
# from a checkout somewhere else:  ROOT = r"D:\\code\\Physalia"
try:
    ROOT
except NameError:
    ROOT = r"C:\Users\rober\repos\Physalia"
exec(open(ROOT + r"\tools\presets\phybuild.py").read())

NAME = "S11 - Explain the Definition Nobody Documented"
OUT = PRESETS + r"\%s.phy" % NAME
DUMP = SCRATCH + r"\dumpS11.txt"

host = clear_host()
H, D = new_harness(host, 200, 200, name="explain-this-definition")

panel(D, 40, 40,
      "EXPLAIN THE DEFINITION NOBODY DOCUMENTED\r\n"
      "\r\n"
      "Somebody has left, and their definition is now yours. Or it is your own from eighteen months "
      "ago, which is the same problem with more embarrassment. Two hundred components, four of the "
      "sliders matter, and nobody wrote any of it down.\r\n"
      "\r\n"
      "OPEN THE FILE, PLACE THIS BESIDE IT, AND ASK\r\n"
      "  \"what does this definition actually do? explain it to me in order.\"\r\n"
      "  \"which sliders change the result and which ones are leftovers?\"\r\n"
      "  \"if I change the input curve to something with more segments, where does it break?\"\r\n"
      "  \"what would you delete? what is not connected to the output?\"\r\n"
      "  \"write me the README this file should have shipped with.\"\r\n"
      "\r\n"
      "IT READS THE GRAPH, NOT A PICTURE OF IT. Canvas State hands over the actual components, "
      "their nicknames, their wires and the values on them. So \"the third slider from the left\" "
      "is a thing it can answer about, and so is \"nothing downstream of this component is used\".\r\n"
      "\r\n"
      "THE JOB THIS REPLACES is an hour of clicking through somebody else's file with the panel "
      "tool, and it is an hour you will not enjoy or remember. Get the map first, then go and read "
      "the four components that turn out to matter.\r\n"
      "\r\n"
      "WHAT IT CANNOT TELL YOU is WHY. A definition records what somebody did, never what they were "
      "trying to achieve or which client meeting caused it. Where it starts explaining intent, it "
      "is guessing - reasonably, but guessing.",
      w=960, h=520, colour=INTRO_GREEN)

SPINE = 780

# --------------------------------------------------------------------------- the loop

L = core_loop(D, 130, SPINE, model_name="Codex Model",
              instruction="You explain Grasshopper definitions to whoever has inherited them.\r\n"
                          "\r\n"
                          "Work from the graph you are given, not from what the file is probably "
                          "for. Explain it in DATA ORDER - start at the inputs, follow the wires, "
                          "end at what is produced.\r\n"
                          "\r\n"
                          "Always name components by their NICKNAME as well as their type, so the "
                          "reader can find them on the canvas. Where you can, say which number a "
                          "slider is feeding and what changing it would do.\r\n"
                          "\r\n"
                          "Call out: anything not connected to an output, anything that would "
                          "break on a different input, and any place the same thing is computed "
                          "twice.\r\n"
                          "\r\n"
                          "Separate WHAT IT DOES from WHAT YOU THINK IT IS FOR, and label the "
                          "second one as a guess. A definition records what somebody did, never "
                          "why.\r\n"
                          "\r\n"
                          "Use search_components when you meet a component you do not recognise "
                          "rather than inferring its behaviour from its name.")

canvas = place(D, "Canvas State", 480, 640, nick="Canvas State")
catalog = place(D, "Component Catalog", 640, 640, nick="Component Catalog", sub="Grounding")
units = place(D, "Document Units Grounding", 800, 640, nick="Document Units")
rhinog = place(D, "Rhino Document", 960, 640, nick="Rhino Document")
for g in (canvas, catalog, units, rhinog):
    wire(L["log"], "Grounding", g, 0)
img = place(D, "Add Image", 480, 690, nick="Add Image")
vsnap = place(D, "View Snapshot", 640, 690, nick="View Snapshot")
expc = place(D, "Export Conversation", 800, 690, nick="Export Conversation")
for t in (img, vsnap, expc):
    wire(L["log"], "Human Tools", t, "Human Tool")

panel(D, 130, 1060,
      "CANVAS STATE IS THE WHOLE TRICK. It serialises what is actually on your canvas - every "
      "component, its nickname, its position, every wire and the values sitting on the inputs - and "
      "hands that to the model as part of the prompt.\r\n"
      "\r\n"
      "So it is reading the definition, not a screenshot of it. That is why it can say \"the slider "
      "nicknamed 'bay width' feeds Divide Curve's Count, and nothing else uses it\".\r\n"
      "\r\n"
      "IT READS THE CANVAS THIS HARNESS IS STANDING ON, which is your document - not the inside of "
      "this harness. Open the file you inherited, drop this harness into it, and ask.\r\n"
      "\r\n"
      "COMPONENT CATALOG is what stops it inventing a plausible description for a component it has "
      "not met. Paired with the search tool on the right, an unfamiliar component becomes a lookup "
      "rather than a guess.\r\n"
      "\r\n"
      "A VERY LARGE DEFINITION WILL BE EXPENSIVE. Canvas State sends the graph on every round, so a "
      "2000-component file is a large prompt each time. Ask fewer, bigger questions - and see "
      "preset 10 if a session gets long.",
      w=380, h=520)

# --------------------------------------------------------------------------- tools

title(D, 1520, 620, "WHAT IT CAN LOOK UP", w=360, h=44)
router = place(D, "Router", 1600, 780, nick="Router")
wire(router, "Tool Calls", L["call"], "Tool Calls")
router_slots(router, 2)
csearch = place(D, "Component Search", 1940, 720, nick="Component Search")
wire(csearch, "Component Catalog", catalog, 0)
rcs = place(D, "RhinoCommon Search", 1940, 820, nick="RhinoCommon Search")
mem = place(D, "Memory", 1940, 920, nick="Memory")
memfolder = input_panel(D, 1720, 950, "definitions-i-inherited", w=200, h=40, nick="memory folder")
wire(mem, "Memory Folder", memfolder, 0)
wire(csearch, "Signal", router, 0)
wire(rcs, "Signal", router, 1)
wire(mem, "Signal", router, 2)

panel(D, 1520, 1060,
      "THREE TOOLS, AND NONE OF THEM TOUCHES YOUR FILE. This pipeline only READS. There is no Drive "
      "Rhino and no transmitter here on purpose: the first thing you do with an inherited definition "
      "is understand it, and a tool that could rearrange it while you are still working out what it "
      "does is not what you want in the room.\r\n"
      "\r\n"
      "COMPONENT SEARCH looks a Grasshopper component up by name or by what it does. Cheap, and it "
      "is the difference between a description and a guess when a plug-in component turns up.\r\n"
      "\r\n"
      "RHINOCOMMON SEARCH is the same for the Rhino API, which matters when the definition contains "
      "script components.\r\n"
      "\r\n"
      "MEMORY is where the explanation goes so you do not pay for it twice. \"Write down what this "
      "definition does\" at the end of a session, and next month it starts from what it already "
      "worked out. Give each inherited file its own memory folder name if you have several.\r\n"
      "\r\n"
      "WHEN YOU ARE READY TO CHANGE IT, use preset S04 - it has Canvas State too, so it edits what "
      "is there rather than starting again.",
      w=360, h=520);

# --------------------------------------------------------------------------- out

title(D, 2420, 560, "THE README IT SHOULD HAVE HAD", w=380, h=44)
decon = place(D, "Deconstruct Signal", 2500, 720, nick="Deconstruct Signal")
wire(decon, "Signal", L["call"], "Success Signal")
out = place(D, "Harness Out", 2500, 820, nick="Harness Out")
out.Params.Input[0].NickName = "the explanation"
wire(out, "Data", decon, "Payload")

panel(D, 2420, 910,
      "DRAG \"THE EXPLANATION\" ONTO A PANEL on the canvas you are documenting, and leave it there.\r\n"
      "\r\n"
      "That is the actual deliverable. A panel of plain English sitting beside the definition is "
      "worth more to the next person than any amount of conversation they will never see, and it "
      "gets saved in the file.\r\n"
      "\r\n"
      "Ask for the README explicitly - \"write me the README this file should have shipped with\" - "
      "and you get something shaped to be pasted rather than a chat transcript.\r\n"
      "\r\n"
      "EXPORT CONVERSATION keeps the whole exchange including everything you asked along the way. "
      "Worth doing once, for the file you will hand on next.",
      w=380, h=400);

# --------------------------------------------------------------------------- returns

budget = place(D, "Budget Guard", 900, 1680, nick="Budget Guard")
b_calls = slider(D, 680, 1760, 40, 1, 200, nick="max calls")
wire(budget, "Max Calls", b_calls, 0)
wire(budget, "Signal", L["log"], "Signal")
wire(L["call"], "Signal", budget, "Success Signal")
spent = panel(D, 840, 1800, "spent so far", w=300, h=80, colour=OUTPUT_GREY)
spent.AddSource(pin(budget, "out", "Spent"))

fb_res, co_res = back(D, csearch, "Result", router, "Results",
                      2140, 1660, 1320, 1660, nick="tool results")
for t in (rcs, mem):
    wire(fb_res, "Signal", t, "Result")
back(D, router, "Feedback", L["log"], "LLM Tool Signal",
     1300, 1900, 700, 1900, nick="tool round to the log")

panel(D, 2420, 1400,
      "THINGS TO TRY\r\n"
      "\r\n"
      "1. Start wide: \"what does this definition do? explain it in order, inputs first.\" Read it "
      "before asking anything else - it usually tells you what to ask next.\r\n"
      "\r\n"
      "2. \"Which sliders actually change the output?\" On an inherited file this is the single "
      "most valuable question, and the answer is usually four out of thirty.\r\n"
      "\r\n"
      "3. \"What isn't connected to anything?\" Dead branches are where an afternoon goes.\r\n"
      "\r\n"
      "4. \"If I feed it a closed curve instead of an open one, what breaks?\" It can reason about "
      "that from the graph, and it is the question you were going to find out the hard way.\r\n"
      "\r\n"
      "5. Ask it to write the explanation into memory, then come back next week and ask it to "
      "carry on. It should not have to read the whole thing again.\r\n"
      "\r\n"
      "6. Check one claim by hand before you trust the rest. Pick a slider it says does nothing and "
      "move it.",
      w=440, h=520);

commit_build(D, "build scenario S11")
solve(D)
solve(D)
write_dump(D, DUMP)
say("router outputs:", [q.NickName for q in router.Params.Output])
bad = sweep(D, NAME)
save_phy(H, OUT,
         description="Point it at a Grasshopper file - yours from eighteen months ago, or somebody "
                     "else's - and get back what it does, which sliders matter and where it will "
                     "break. It reads the actual graph, not a picture of it, and it only reads.",
         chat_text="Explain the Definition Nobody Documented\r\n\r\n"
                   "Open the file you inherited, drop this harness into it, and ask:\r\n\r\n"
                   "  \"what does this definition do? explain it in order, inputs first\"\r\n"
                   "  \"which sliders actually change the output?\"\r\n"
                   "  \"what isn't connected to anything?\"\r\n"
                   "  \"write me the README this file should have shipped with\"\r\n\r\n"
                   "It reads the real graph - components, nicknames, wires and the values on them - "
                   "so it can answer about a particular slider. It only READS; there is nothing "
                   "here that can change your file.\r\n\r\n"
                   "Check one claim by hand before trusting the rest.")
say("PROBLEMS:", bad)
