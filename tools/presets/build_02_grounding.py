# Copyright (c) 2026 Physalia Contributors
# SPDX-License-Identifier: AGPL-3.0-or-later
"""
Preset 02 - "What the Model Knows".

Preset 01 could talk but was blind: ask it about your Rhino file and it has to guess. GROUNDING is
what fixes that - a set of components that describe your document, your canvas and your project
folder, and fold that description into the system prompt on every turn.
"""

exec(open(r"C:\Users\rober\repos\Physalia\tools\presets\phybuild.py").read())

NAME = "02 - What the Model Knows"
OUT = r"C:\Users\rober\repos\Physalia\wip_presets\%s.phy" % NAME
DUMP = r"C:\Users\rober\AppData\Local\Temp\claude\dump02.txt"

host = clear_host()
H, D = new_harness(host, 200, 200, name="what-the-model-knows")

# --------------------------------------------------------------------------- the intro

panel(D, 40, 40,
      "WHAT THE MODEL KNOWS\r\n"
      "\r\n"
      "The pipeline in preset 01 could talk, but it was blind. Ask it \"how many objects are on my "
      "Beams layer?\" and it has nothing to go on.\r\n"
      "\r\n"
      "GROUNDING is the fix. Each grounding component writes a short description of one thing - "
      "your Rhino document, your Grasshopper canvas, the units you are working in, this "
      "pipeline's own folder - and they all plug into the Conversation Log's GROUNDING input. The "
      "Conversation Log folds them into the system prompt, fresh, on every single turn.\r\n"
      "\r\n"
      "Grounding is not a message and not a tool. It is background knowledge: the model does not "
      "have to ask for it, and it does not cost a round trip. Everything is capped, so a "
      "10,000-object file contributes about as much text as a 10-object one.",
      w=900, h=250, colour=INTRO_GREEN)

# --------------------------------------------------------------------------- 1 the chat

title(D, 60, 330, "1 - WHERE YOU TYPE", w=280, h=44)
chat = place(D, "Chat", 120, 470, nick="Chat")
panel(D, 60, 560,
      "Same as preset 01: the Chat component is the door onto the chat window, and it mints a "
      "signal carrying whatever you typed.\r\n"
      "\r\n"
      "If any of this is unfamiliar, read \"01 - Talk to a Model\" first. This preset only adds "
      "the grounding column in stage 3.",
      w=255, h=170)

# --------------------------------------------------------------------------- 2 system prompt

title(D, 400, 330, "2 - THE SYSTEM PROMPT", w=340, h=44)
sysp = place(D, "System Prompt", 600, 470, nick="System Prompt")
blank_input(D, sysp, "Preamble", 400, 410, label="no preamble file")
blank_input(D, sysp, "Schema", 400, 456, label="no schema file")
extra = input_panel(D, 330, 540,
                    "You are helping someone inside Rhino and Grasshopper. Use the document facts "
                    "you were given rather than guessing, and say so when something is not in "
                    "them.",
                    w=250, h=105, nick="my own instructions")
wire(sysp, "Additional Prompt", extra, 0)
panel(D, 400, 690,
      "The System Prompt is only the FIXED part of what the model is told. The grounding in stage "
      "3 is the part that changes as you work.\r\n"
      "\r\n"
      "Worth saying explicitly in your instruction that the facts it has been given are real and "
      "should be used rather than guessed at - a model given a document summary will still "
      "sometimes hedge about it.",
      w=340, h=210)

# ------------------------------------------------------------------- 3 grounding (the point)

title(D, 820, 330, "3 - GROUNDING: WHAT IT KNOWS ABOUT YOUR FILE", w=420, h=44)
rhino = place(D, "Rhino Document", 900, 430, nick="Rhino Document")
canvas = place(D, "Canvas State", 900, 480, nick="Canvas State")
catalog = place(D, "Component Catalog", 900, 530, nick="Component Catalog", sub="Grounding")
units = place(D, "Document Units Grounding", 900, 580, nick="Document Units")
groupc = place(D, "Physalia Group Components", 900, 630, nick="Physalia Group Components")
proj = place(D, "Project Folder", 900, 710, nick="Project Folder")
folder = panel(D, 860, 800, "this pipeline's folder on disk appears here", w=330, h=70,
               colour=OUTPUT_GREY)
folder.AddSource(pin(proj, "out", "Folder"))
panel(D, 820, 900,
      "SIX GROUNDERS, each describing one thing. All six go into ONE input - Grounding takes a "
      "list.\r\n"
      "\r\n"
      "RHINO DOCUMENT - how many objects there are and of what kinds, the layer table, the overall "
      "extents, and how many objects are SELECTED. That last one is what makes \"move these\" a "
      "sentence the model can act on.\r\n"
      "\r\n"
      "CANVAS STATE - what is already on your Grasshopper canvas, so it can extend your definition "
      "rather than start again.\r\n"
      "\r\n"
      "COMPONENT CATALOG - which Grasshopper components exist and what their inputs are called. "
      "Right-click it to choose which ribbon tabs to include; the whole of Grasshopper is far more "
      "than any model needs.\r\n"
      "\r\n"
      "DOCUMENT UNITS - millimetres, feet, whatever you are in. It only TELLS the model; it never "
      "changes your document.\r\n"
      "\r\n"
      "PHYSALIA GROUP COMPONENTS - the components this pipeline itself has placed, so it can edit "
      "its own work instead of piling more on top.\r\n"
      "\r\n"
      "PROJECT FOLDER - names this pipeline's own folder and lists what is in it. Its second "
      "output is the absolute path as text: wire that into Download File, Read File or Read PDF "
      "and the folder is configured once for the whole pipeline. Leave its input blank for the "
      "folder named after this harness.",
      w=420, h=620)

# --------------------------------------------------------------------------- 4 conversation log

title(D, 1320, 330, "4 - THE RUNNING CONVERSATION", w=350, h=44)
log = place(D, "Conversation Log", 1480, 470, nick="Conversation Log")
wire(log, "System Prompt", sysp, "System Prompt")
wire(log, "Prompt Signal", chat, "Prompt Signal")
for g in (rhino, canvas, catalog, units, groupc, proj):
    wire(log, "Grounding", g, 0)
panel(D, 1320, 690,
      "The Conversation Log REBUILDS the grounding section of the system prompt every turn, from "
      "whatever the grounders are reporting right now. It is not captured once at the start.\r\n"
      "\r\n"
      "That matters because your document keeps moving. Draw a wall in Rhino and the very next "
      "message already knows about it.\r\n"
      "\r\n"
      "Note how the grounders manage this: editing Rhino geometry runs no Grasshopper solution "
      "anywhere, so Rhino Document and Project Folder watch for changes themselves and just mark "
      "themselves stale. The solve your next message causes then recomputes them before the prompt "
      "is assembled.",
      w=350, h=290)

# --------------------------------------------------------------------------- 5 the call

title(D, 1820, 330, "5 - ASKING THE MODEL", w=350, h=44)
model = place(D, "Claude Code Model", 1980, 400, nick="Claude Code Model")
call = place(D, "LLM Call", 1980, 540, nick="LLM Call")
wire(call, "Model", model, "Model")
wire(call, "Signal", log, "Signal")
cancel = boolean(D, 1850, 585, False, nick="stop", toggle=False)
wire(call, "Cancel", cancel, 0)
panel(D, 1820, 690,
      "Unchanged from preset 01. The LLM Call has no idea any of this grounding exists - it just "
      "sends the Instructions it was handed.\r\n"
      "\r\n"
      "That separation is the whole point. You can add, remove or reconfigure grounding without "
      "touching the part of the pipeline that talks to the model.",
      w=350, h=210)

# --------------------------------------------------------------------------- 6 what came back

title(D, 2320, 330, "6 - WHAT CAME BACK", w=350, h=44)
dec = place(D, "Deconstruct Signal", 2480, 470, nick="Deconstruct Signal")
wire(dec, "Signal", call, "Success Signal")
reply = panel(D, 2760, 400, "the model's reply lands here", w=300, h=170, colour=OUTPUT_GREY)
reply.AddSource(pin(dec, "out", "Payload"))
errs = panel(D, 2760, 600, "errors from the model land here", w=300, h=120, colour=ERROR_PINK)
errs.AddSource(pin(call, "out", "Fail Signal"))
panel(D, 2320, 690,
      "To see the grounding itself, wire this Deconstruct Signal to the CONVERSATION LOG's output "
      "instead, then run an Instructions Decompositor off its Instructions - the system prompt "
      "that comes out has the grounding sections in it.\r\n"
      "\r\n"
      "Worth doing once. Reading exactly what the model was told explains more about a "
      "disappointing answer than anything else in Physalia.",
      w=350, h=240)

# --------------------------------------------------------------------------- 7 the way back

back(D, call, "Success Signal", log, "Response Signal",
     2560, 1250, 1290, 1250, nick="reply back to the log")
panel(D, 1460, 1200,
      "THE WAY BACK, exactly as in preset 01: Feedback on the right, Feedback Collector on the "
      "left, no wire between them, because an ordinary wire back to the Conversation Log would be "
      "a loop and Grasshopper refuses loops.",
      w=520, h=180, colour=WARN_ORANGE)

# --------------------------------------------------------------------------- 8 what to try

panel(D, 2320, 1420,
      "THINGS TO TRY\r\n"
      "\r\n"
      "1. Ask \"what is in my Rhino file?\" before drawing anything, then draw a box and ask "
      "again. Nothing was re-wired between the two answers.\r\n"
      "\r\n"
      "2. Select two objects in Rhino and ask \"what have I got selected?\".\r\n"
      "\r\n"
      "3. Right-click Component Catalog and turn its tabs down to just Curve and Surface. Ask what "
      "components it can use.\r\n"
      "\r\n"
      "4. Read the grey panel in stage 3 - that is where this pipeline's files live. Drop a file "
      "in it and ask what is there.",
      w=520, h=300)

commit_build(D, "build preset 02")
solve(D)
pick(D, model, "Model", "sonnet")
solve(D)
write_dump(D, DUMP)
bad = sweep(D, NAME)
save_phy(H, OUT,
         description="Adds GROUNDING to the core loop: six components that describe your Rhino "
                     "document, your canvas, your units and this pipeline's folder, folded into "
                     "the system prompt fresh on every turn.",
         chat_text="What the Model Knows\r\n\r\n"
                   "Same loop as preset 01, but now the model is told about your file before you "
                   "say anything - object counts, layers, what is selected, what is on your "
                   "canvas, your units, and this pipeline's own folder.\r\n\r\n"
                   "Try: \"what is in my Rhino file?\" Then draw something and ask again.")
say("PROBLEMS:", bad)
