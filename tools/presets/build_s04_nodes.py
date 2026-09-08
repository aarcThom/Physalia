# Copyright (c) 2026 Physalia Contributors
# SPDX-License-Identifier: AGPL-3.0-or-later
"""
Scenario S04 - "Get It to Build the Definition".

Preset 04 explains what every guardrail does. This one is the working version of the same chain,
and spends its annotation on the thing that actually decides whether you get a good definition:
how you ask, and what to do when it comes back wrong.
"""

# The ONE machine-specific line in this file. Set ROOT before exec'ing this script to build
# from a checkout somewhere else:  ROOT = r"D:\\code\\Physalia"
try:
    ROOT
except NameError:
    ROOT = r"C:\Users\rober\repos\Physalia"
exec(open(ROOT + r"\tools\presets\phybuild.py").read())

NAME = "S04 - Get It to Build the Definition"
OUT = PRESETS + r"\%s.phy" % NAME
DUMP = SCRATCH + r"\dumpS04.txt"

host = clear_host()
H, D = new_harness(host, 200, 200, name="build-the-definition")

panel(D, 40, 40,
      "GET IT TO BUILD THE DEFINITION\r\n"
      "\r\n"
      "Describe what you want and it appears on your canvas as real, editable Grasshopper "
      "components. Not a picture of a definition, not code - components with wires you can pull "
      "apart.\r\n"
      "\r\n"
      "PRESET 04 EXPLAINS EVERY GUARDRAIL IN THIS CHAIN. This one is the working version, and the "
      "annotation is about the part nobody tells you: how to ask.\r\n"
      "\r\n"
      "HOW TO ASK\r\n"
      "\r\n"
      "1. ASK FOR ONE THING. \"A grid of points on a surface, with a slider for each direction\" "
      "works. \"A parametric facade system\" gets you something plausible and useless. If you would "
      "not know how to check the answer in one look, the request is too big.\r\n"
      "\r\n"
      "2. SAY WHAT THE INPUTS ARE. Which numbers do you want to be able to change afterwards? Name "
      "them. That is the difference between a definition and a drawing.\r\n"
      "\r\n"
      "3. BUILD ON WHAT IS THERE. It can see your canvas, so \"add a fillet after the offset\" is a "
      "real instruction. You do not have to start again to change something.\r\n"
      "\r\n"
      "4. WHEN IT COMES BACK WRONG, SAY WHAT IS WRONG, NOT \"THAT'S WRONG\". \"The points are on "
      "the surface but not evenly spaced\" gets a fix. \"No\" gets a different guess.\r\n"
      "\r\n"
      "5. TAKE OVER WHENEVER YOU LIKE. The output is ordinary components. Rewire them by hand and "
      "then ask it to carry on - it will see what you changed.",
      w=980, h=560, colour=INTRO_GREEN)

SPINE = 720

# --------------------------------------------------------------------------- the loop

chat = place(D, "Chat", 130, SPINE, nick="Chat")
sysp = place(D, "System Prompt", 520, SPINE, nick="System Prompt")
log = place(D, "Conversation Log", 1000, SPINE, nick="Conversation Log")
wire(log, "System Prompt", sysp, "System Prompt")
wire(log, "Prompt Signal", chat, "Prompt Signal")

catalog = place(D, "Component Catalog", 760, 620, nick="Component Catalog", sub="Grounding")
canvas = place(D, "Canvas State", 760, 670, nick="Canvas State")
groupc = place(D, "Physalia Group Components", 760, 900, nick="Physalia Group Components")
units = place(D, "Document Units Grounding", 760, 950, nick="Document Units")
for g in (catalog, canvas, groupc, units):
    wire(log, "Grounding", g, 0)
img = place(D, "Add Image", 880, 620, nick="Add Image")
gsnap = place(D, "Geometry Snapshot", 880, 670, nick="Geometry Snapshot")
for t in (img, gsnap):
    wire(log, "Human Tools", t, "Human Tool")

model = place(D, "Claude Code Model", 1380, 620, nick="Claude Code Model")
call = place(D, "LLM Call", 1380, SPINE + 40, nick="LLM Call")
wire(call, "Model", model, "Model")
wire(call, "Signal", log, "Signal")
cancel = boolean(D, 1250, SPINE + 80, False, nick="stop", toggle=False)
wire(call, "Cancel", cancel, 0)

panel(D, 500, 1040,
      "THE TWO GROUNDINGS THAT MAKE THIS WORK\r\n"
      "\r\n"
      "COMPONENT CATALOG is the list of components it is allowed to use. Without it, it invents "
      "plausible component names that do not exist and you spend a round finding that out. Narrow "
      "it from its right-click menu if you only want a certain palette.\r\n"
      "\r\n"
      "CANVAS STATE is what makes \"add a fillet after the offset\" a sentence rather than a "
      "guess. It sends what is already on your canvas, so it EDITS rather than starting again. "
      "Physalia Group Components narrows that to the group it placed, so it is not re-reading your "
      "whole file every round.\r\n"
      "\r\n"
      "GEOMETRY SNAPSHOT lets you send it a picture of what came out. Sometimes faster than "
      "describing what is wrong.",
      w=380, h=420)

# --------------------------------------------------------------------------- the chain

title(D, 1740, 560, "THE CHAIN - EACH ONE REFUSES TO PASS BAD WORK ON", w=680, h=44)
detect = place(D, "Detect JSON", 1740, SPINE, nick="Detect JSON")
wire(detect, "Signal", call, "Success Signal")
schemav = place(D, "Schema Validator", 2040, SPINE, nick="Schema Validator")
wire(schemav, "Signal", detect, "Signal")
wire(schemav, "Schema", sysp, "Schema")
defv = place(D, "GH Definition Validator", 2340, SPINE, nick="GH Definition Validator")
wire(defv, "Signal", schemav, "Success Signal")
resolver = place(D, "Component Resolver", 2640, SPINE, nick="Component Resolver")
wire(resolver, "Signal", defv, "Success Signal")
wire(resolver, "Component Catalog", catalog, 0)
reqin = place(D, "Required Input Check", 2940, SPINE, nick="Required Input Check")
wire(reqin, "Signal", resolver, "Success Signal")
tx = place(D, "Component Transmitter", 3240, SPINE, nick="Component Transmitter")
wire(tx, "Signal", reqin, "Success Signal")
health = place(D, "Runtime Health Check", 3540, SPINE, nick="Runtime Health Check")
wire(health, "Signal", tx, "Success Signal")
georep = place(D, "Geometry Report", 3840, SPINE, nick="Geometry Report")
wire(georep, "Signal", health, "Success Signal")
blank_msg = blank_input(D, georep, "Message", 3700, SPINE + 120, "no build plan here")

panel(D, 1740, 900,
      "SEVEN CHECKS, LEFT TO RIGHT. Nothing reaches your canvas until all of them are happy, and "
      "each one hands its complaint BACK to the model rather than to you.\r\n"
      "\r\n"
      "That is the whole reason this works. The model is a good writer and a poor proofreader of "
      "its own work; these check the things a machine can check, so the conversation is about "
      "design instead of typos.\r\n"
      "\r\n"
      "IS IT JSON  ->  DOES IT MATCH THE SCHEMA  ->  IS IT A VALID GRAPH  ->  DO THOSE COMPONENTS "
      "EXIST  ->  IS EVERYTHING WIRED  ->  PLACE IT  ->  DOES IT ACTUALLY RUN  ->  IS THE GEOMETRY "
      "WHERE IT SHOULD BE.\r\n"
      "\r\n"
      "WATCH THE CAPTIONS UNDER THE NODES when a round takes a while. They tell you which check is "
      "objecting, which is usually enough to work out what to say next.\r\n"
      "\r\n"
      "Preset 04 has a paragraph on each of these if you want the detail.",
      w=680, h=380)

panel(D, 3700, 900,
      "GEOMETRY REPORT is the last one and the only one that is about DESIGN rather than "
      "correctness. It describes what got built in words - bounding boxes, what is separate from "
      "what and by how far, what contains what.\r\n"
      "\r\n"
      "\"The roof is 4m from the wall it should sit on\" is a sentence no schema check will ever "
      "produce, and it is usually the thing that is actually wrong.",
      w=380, h=300)

# --------------------------------------------------------------------------- returns

title(D, 2400, 1400, "WHEN A CHECK OBJECTS", w=320, h=44)
stall_limit = slider(D, 2180, 1470, 3, 1, 10, nick="identical failures allowed")
stall = place(D, "Stall Guard", 2620, 1520, nick="Stall Guard")
wire(stall, "Stall Limit", stall_limit, 0)
for g in (schemav, defv, resolver, reqin, tx, health):
    wire(stall, "Signal", g, "Fail Signal")

limiter = place(D, "Signal Limiter", 2620, 1620, nick="Signal Limiter")
lim_max = slider(D, 2180, 1620, 12, 1, 60, nick="rounds per request")
wire(limiter, "Count", lim_max, 0)
wire(limiter, "Signal", stall, "Success Signal")

fb_stall, co_fb = back(D, limiter, "Within Limit", log, "Feedback Signal",
                       3000, 1520, 1100, 1520, nick="complaints to the log")
fb_rep = place(D, "Feedback", 3900, 1520, nick="report to the log")
wire(fb_rep, "Signal", georep, "Signal")
fb_rep.AddCollector(co_fb.InstanceGuid)
back(D, call, "Success Signal", log, "Response Signal",
     1500, 1700, 1100, 1700, nick="reply back to the log")

panel(D, 2400, 1720,
      "TWO THINGS STAND BETWEEN THIS AND A LARGE BILL.\r\n"
      "\r\n"
      "STALL GUARD counts IDENTICAL failures. Different problems in sequence are fine - that is a "
      "pipeline making progress. The same complaint three times is a pipeline stuck, and it parks "
      "rather than paying to be stuck faster.\r\n"
      "\r\n"
      "SIGNAL LIMITER counts rounds regardless. It is the cruder one and it is the one that saves "
      "you when the model finds a NEW way to be wrong every time.\r\n"
      "\r\n"
      "When either parks, read the caption, then say something in the chat. That starts a fresh "
      "count.",
      w=380, h=380)

panel(D, 60, 1520,
      "THINGS TO TRY\r\n"
      "\r\n"
      "1. \"Divide this curve into 20 points and put a circle at each one, radius on a slider.\" "
      "Small, checkable, and it exercises the whole chain.\r\n"
      "\r\n"
      "2. Then: \"make the radius vary along the curve.\" Notice it EDITS what is there rather "
      "than building a second copy - that is Canvas State doing its job.\r\n"
      "\r\n"
      "3. Break it on purpose. Delete a wire it placed and say \"something's wrong, have a look\".\r\n"
      "\r\n"
      "4. Ask for something with no obvious component - \"pack circles into this boundary\". Watch "
      "what it does when the catalog does not have a button for the job.\r\n"
      "\r\n"
      "5. Change a slider by hand and ask it to keep going. It sees the current value.\r\n"
      "\r\n"
      "IF YOU WANT IT TO BUILD SOMETHING BIG, use preset 04's Build Plan instead - it makes the "
      "model commit to stages and finish one at a time, which is the only thing that reliably gets "
      "past the size where a single answer falls apart.",
      w=420, h=520)

pick(D, model, "Model", "sonnet")
pick(D, sysp, "Preamble", "Incremental Node Graph.txt")
pick(D, sysp, "Schema", "Incremental Node Graph.json")

commit_build(D, "build scenario S04")
solve(D)
solve(D)
write_dump(D, DUMP)
bad = sweep(D, NAME)
save_phy(H, OUT,
         description="Describe what you want and it appears on your canvas as real, editable "
                     "Grasshopper components. The working version of preset 04's guardrail chain, "
                     "annotated with how to ask for a definition and what to do when it comes back "
                     "wrong.",
         chat_text="Get It to Build the Definition\r\n\r\n"
                   "Describe what you want and it appears on your canvas as real components.\r\n\r\n"
                   "Start small and checkable:\r\n"
                   "  \"divide this curve into 20 points and put a circle at each one, radius on a "
                   "slider\"\r\n\r\n"
                   "Then edit it in place: \"make the radius vary along the curve\".\r\n\r\n"
                   "ASK FOR ONE THING AT A TIME, and say which numbers you want to be able to "
                   "change afterwards. When it comes back wrong, say WHAT is wrong - \"the points "
                   "aren't evenly spaced\" gets a fix, \"no\" gets a different guess.")
say("PROBLEMS:", bad)
