# Copyright (c) 2026 Physalia Contributors
# SPDX-License-Identifier: AGPL-3.0-or-later
"""
Scenario S12 - "From a Sketch to a Massing".

Photograph the sketch, draw on it to say what the lines mean, and have the massing built in Rhino.
Then it looks at what it built and compares it with your drawing.
"""

# The ONE machine-specific line in this file. Set ROOT before exec'ing this script to build
# from a checkout somewhere else:  ROOT = r"D:\\code\\Physalia"
try:
    ROOT
except NameError:
    ROOT = r"C:\Users\rober\repos\Physalia"
exec(open(ROOT + r"\tools\presets\phybuild.py").read())

NAME = "S12 - From a Sketch to a Massing"
OUT = PRESETS + r"\%s.phy" % NAME
DUMP = SCRATCH + r"\dumpS12.txt"

host = clear_host()
H, D = new_harness(host, 200, 200, name="sketch-to-massing")

panel(D, 40, 40,
      "FROM A SKETCH TO A MASSING\r\n"
      "\r\n"
      "The gap everyone knows: the sketch is right, and getting it into the model takes two hours "
      "of drawing rectangles. By the time it is in, the idea has cooled.\r\n"
      "\r\n"
      "HOW IT GOES\r\n"
      "1. Photograph the sketch on your desk. Drop it in the chat, or paste it.\r\n"
      "2. DRAW ON IT. Click the pencil on the thumbnail: circle the footprint, arrow the entrance, "
      "write \"4 storeys\" on the tall bit, \"keep this open\" on the courtyard.\r\n"
      "3. Say the one thing the drawing cannot: \"the long side is about 40 metres\". Scale is the "
      "only thing it genuinely cannot get from a picture.\r\n"
      "4. It builds the massing in Rhino, then LOOKS at what it built and tells you where it "
      "differs from your sketch.\r\n"
      "5. Correct it in words, or mark up the snapshot it just sent you and send that back.\r\n"
      "\r\n"
      "MARKING UP IS WORTH MORE THAN DESCRIBING. \"The bit on the left\" is ambiguous in a way an "
      "arrow never is, and drawing it takes four seconds. This is the fastest loop in Physalia and "
      "the one people underuse.\r\n"
      "\r\n"
      "WHAT YOU GET IS A MASSING, not a building. Boxes and extrusions at roughly the right sizes "
      "in roughly the right places - the thing you were going to spend the two hours making so you "
      "could look at it. Every piece is ordinary Rhino geometry, so you carry on by hand from "
      "there.\r\n"
      "\r\n"
      "AND IT IS ONE UNDO STEP. If the answer is wrong, Ctrl+Z once and ask again differently.",
      w=940, h=520, colour=INTRO_GREEN)

SPINE = 780

# --------------------------------------------------------------------------- the loop

L = core_loop(D, 130, SPINE, model_name="Codex Model",
              instruction="You turn sketches into massing models in Rhino.\r\n"
                          "\r\n"
                          "BEFORE YOU BUILD ANYTHING, say back what you think you are looking at - "
                          "how many volumes, roughly what footprint, how many storeys each - and "
                          "state every dimension you are ASSUMING. Then build it unless the user "
                          "corrects you.\r\n"
                          "\r\n"
                          "Build with run_rhino_script. Use simple, editable geometry: closed "
                          "polylines extruded to height, on layers named MASSING-01, MASSING-02 and "
                          "so on, one layer per volume. Nothing clever - this will be edited by "
                          "hand.\r\n"
                          "\r\n"
                          "AFTER BUILDING, take_snapshot from a viewpoint that shows the massing "
                          "the way the sketch shows it, LOOK at your own result, and say plainly "
                          "where it differs from the sketch. Do not describe what you intended.\r\n"
                          "\r\n"
                          "Scale is the one thing a picture cannot give you. If nobody has given "
                          "you a dimension, use ask_human rather than inventing one - a massing at "
                          "the wrong size looks completely right until it is next to something.")

rhinog = place(D, "Rhino Document", 500, 640, nick="Rhino Document")
units = place(D, "Document Units Grounding", 660, 640, nick="Document Units")
toolsp = place(D, "Tools Present", 820, 640, nick="Tools Present")
for g in (rhinog, units, toolsp):
    wire(L["log"], "Grounding", g, 0)

img = place(D, "Add Image", 500, 690, nick="Add Image")
mark = place(D, "Image Mark Up", 660, 690, nick="Image Mark Up")
vsnap = place(D, "View Snapshot", 820, 690, nick="View Snapshot")
gsnap = place(D, "Geometry Snapshot", 980, 640, nick="Geometry Snapshot")
expc = place(D, "Export Conversation", 980, 690, nick="Export Conversation")
for t in (img, mark, vsnap, gsnap, expc):
    wire(L["log"], "Human Tools", t, "Human Tool")

panel(D, 130, 1060,
      "THE FOUR HUMAN TOOLS ARE THE POINT OF THIS PRESET, and they are not interchangeable.\r\n"
      "\r\n"
      "ADD IMAGE turns on image intake. Without it the chat will not accept a paste, a drop or a "
      "file at all - image intake is off unless a pipeline asks for it.\r\n"
      "\r\n"
      "IMAGE MARK UP adds no button of its own. It changes what the others DO: every image in the "
      "prompt box grows a pencil, and every snapshot opens in the editor before it is sent. Pen, "
      "arrows, 12pt text, an eraser, nine colours.\r\n"
      "\r\n"
      "The marks stay as OBJECTS until you confirm, so the eraser lifts a stroke off the picture "
      "underneath rather than painting over it, and the committed image is still full resolution.\r\n"
      "\r\n"
      "WATCH FOR ONE THING: cancelling means two different things. On an image you attached, cancel "
      "throws away the MARKS and keeps the picture. On a snapshot being sent as its own message, "
      "the picture was never attached anywhere, so cancel abandons the whole thing.\r\n"
      "\r\n"
      "VIEW SNAPSHOT sends what your viewport is showing right now - the fastest way to say \"like "
      "this, but not this bit\". GEOMETRY SNAPSHOT frames the camera on what the pipeline built, so "
      "it is the after picture to the sketch's before.",
      w=380, h=580)

# --------------------------------------------------------------------------- tools

title(D, 1560, 620, "BUILDING IT AND LOOKING AT IT", w=400, h=44)
router = place(D, "Router", 1640, 780, nick="Router")
wire(router, "Tool Calls", L["call"], "Tool Calls")
router_slots(router, 2)
drive = place(D, "Drive Rhino", 1980, 720, nick="Drive Rhino")
look = place(D, "Take Snapshot", 1980, 820, nick="Take Snapshot")
askh = place(D, "Ask Human", 1980, 920, nick="Ask Human")
wire(drive, "Signal", router, 0)
wire(look, "Signal", router, 1)
wire(askh, "Signal", router, 2)

script = panel(D, 2260, 680, "the script that built it", w=340, h=200, colour=OUTPUT_GREY)
script.AddSource(pin(drive, "out", "Last Script"))
aimed = panel(D, 2260, 900, "which way it last looked", w=340, h=80, colour=OUTPUT_GREY)
aimed.AddSource(pin(look, "out", "Current View"))

panel(D, 1560, 1060,
      "DRIVE RHINO builds it. The instruction tells it to use closed polylines extruded to height, "
      "one layer per volume, because you are going to edit this by hand in ten minutes and a clever "
      "construction is a nuisance rather than a gift.\r\n"
      "\r\n"
      "READ THE SCRIPT PANEL when something is the wrong size. The dimensions it assumed are "
      "written in it in plain numbers, which is faster than another round of conversation.\r\n"
      "\r\n"
      "TAKE SNAPSHOT IS THE HALF PEOPLE LEAVE OUT, and it is what makes this a loop rather than a "
      "one-shot. The model builds something, then LOOKS at it and compares it with your sketch. A "
      "model that never sees its own output will confidently tell you it did what you asked.\r\n"
      "\r\n"
      "Its CURRENT LOCATION input is left unwired here, so the camera stands at the origin. Wire a "
      "point in if you want it looking from somewhere particular - or a Move In Space walk, which "
      "is what preset S02 does.\r\n"
      "\r\n"
      "It aims by compass bearing and elevation - 0 is +Y, 90 is +X, clockwise in plan - and it is "
      "told its lens is about 54 degrees wide, so it does not read something off-camera as "
      "something missing.\r\n"
      "\r\n"
      "ASK HUMAN is for scale. A picture carries no dimensions, and the instruction tells it to ask "
      "rather than invent one - a massing at the wrong size looks completely right until you put a "
      "person next to it.",
      w=400, h=520)

# --------------------------------------------------------------------------- bounds + returns

budget = place(D, "Budget Guard", 940, 1720, nick="Budget Guard")
b_calls = slider(D, 720, 1800, 60, 1, 300, nick="max calls")
wire(budget, "Max Calls", b_calls, 0)
wire(budget, "Signal", L["log"], "Signal")
wire(L["call"], "Signal", budget, "Success Signal")

fb_res, co_res = back(D, drive, "Result", router, "Results",
                      2180, 1700, 1360, 1700, nick="tool results")
for t in (look, askh):
    wire(fb_res, "Signal", t, "Result")
back(D, router, "Feedback", L["log"], "LLM Tool Signal",
     1340, 1940, 700, 1940, nick="tool round to the log")

panel(D, 2260, 1060,
      "THINGS TO TRY\r\n"
      "\r\n"
      "1. Start with one volume. A single box at the right size teaches you more about how it "
      "reads a sketch than a whole scheme it gets half right.\r\n"
      "\r\n"
      "2. Send the SAME sketch twice - once bare, once marked up with arrows and heights. The "
      "difference is the argument for the pencil button.\r\n"
      "\r\n"
      "3. Give it a dimension in the first message. \"The long side is about 40 metres.\" Then try "
      "leaving it out and see whether it asks or guesses.\r\n"
      "\r\n"
      "4. When it sends you a snapshot, mark THAT up and send it back. \"This corner should be "
      "here\" with an arrow is a complete instruction.\r\n"
      "\r\n"
      "5. Ask it to look from a different bearing before you believe it. A massing that reads well "
      "from one angle is the oldest trap in the trade.\r\n"
      "\r\n"
      "6. When the massing is close enough, stop. Carry on by hand - this was never going to "
      "produce the building, only the thing you needed to look at.\r\n"
      "\r\n"
      "AND IF YOU WANT IT PARAMETRIC INSTEAD, use preset S04 - it builds the same massing as "
      "components with sliders, which is the right answer when the sizes are still moving.",
      w=440, h=560)

commit_build(D, "build scenario S12")
solve(D)
solve(D)
write_dump(D, DUMP)
say("router outputs:", [q.NickName for q in router.Params.Output])
bad = sweep(D, NAME)
save_phy(H, OUT,
         description="Photograph the sketch, draw on it to say what the lines mean, and have the "
                     "massing built in Rhino - then it looks at what it built and tells you where "
                     "it differs from your drawing. Ordinary geometry you carry on by hand.",
         chat_text="From a Sketch to a Massing\r\n\r\n"
                   "1. Drop a photo of your sketch in here.\r\n"
                   "2. Click the PENCIL on the thumbnail and draw on it - circle the footprint, "
                   "arrow the entrance, write \"4 storeys\" on the tall bit.\r\n"
                   "3. Tell it the one thing a picture cannot: \"the long side is about 40 "
                   "metres\".\r\n\r\n"
                   "It says back what it thinks it is looking at, builds the massing, then LOOKS at "
                   "what it built and tells you where it differs from your sketch.\r\n\r\n"
                   "Correct it in words, or mark up the snapshot it sends you and send that back - "
                   "an arrow is never ambiguous and takes four seconds.\r\n\r\n"
                   "Start with one volume. It is one undo step if you don't like it.")
say("PROBLEMS:", bad)
