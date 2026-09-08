# Copyright (c) 2026 Physalia Contributors
# SPDX-License-Identifier: AGPL-3.0-or-later
"""
Preset 06 - "Letting It Look and Walk".

Two tools that give the model eyes and a body: Take Snapshot poses a camera and hands back the
picture, and Move In Space walks it through a lattice of points you supply. Also the first preset
with HARNESS IN and HARNESS OUT - real inputs and outputs on the harness node itself.
"""

# The ONE machine-specific line in this file. Set ROOT before exec'ing this script to build
# from a checkout somewhere else:  ROOT = r"D:\\code\\Physalia"
try:
    ROOT
except NameError:
    ROOT = r"C:\Users\rober\repos\Physalia"
exec(open(ROOT + r"\tools\presets\phybuild.py").read())

NAME = "06 - Letting It Look and Walk"
OUT = PRESETS + r"\%s.phy" % NAME
DUMP = SCRATCH + r"\dump06.txt"

host = clear_host()
H, D = new_harness(host, 200, 200, name="letting-it-look-and-walk")

SPINE = 470
TITLE_Y = 340

# --------------------------------------------------------------------------- the intro

panel(D, 40, 40,
      "LETTING IT LOOK AND WALK\r\n"
      "\r\n"
      "Everything so far has been the model reading text. This preset gives it two other senses.\r\n"
      "\r\n"
      "TAKE SNAPSHOT lets it stand somewhere, aim a camera anywhere it likes, and get the picture "
      "back. That is the only way it can catch \"technically correct, obviously wrong\".\r\n"
      "\r\n"
      "MOVE IN SPACE lets it WALK. You give it a cloud of points - the corners of rooms, the "
      "landings of a stair, a grid over a site - and it moves through them one step at a time, "
      "asking to go forward, left, up. The route it takes comes back out onto your canvas as "
      "geometry, which is the real point: its spatial reasoning becomes something your definition "
      "can build on.\r\n"
      "\r\n"
      "It is also the first preset with real INPUTS. Look at the Harness node on your own canvas "
      "and you will find parameters on its left edge, waiting for the points.",
      w=900, h=290, colour=INTRO_GREEN)

# --------------------------------------------------------------------------- 1 the chat

title(D, 60, TITLE_Y, "1 - WHERE YOU TYPE", w=270, h=44)
chat = place(D, "Chat", 130, SPINE, nick="Chat")
panel(D, 60, 570,
      "\"Walk through the building and tell me where the daylight is worst.\"\r\n"
      "\r\n"
      "Presets 01 to 03 explain the loop, the grounding and the tools. This one assumes them.",
      w=250, h=190)

# --------------------------------------------------------------------------- 2 system prompt

title(D, 380, TITLE_Y, "2 - THE SYSTEM PROMPT", w=330, h=44)
sysp = place(D, "System Prompt", 580, SPINE, nick="System Prompt")
blank_input(D, sysp, "Preamble", 380, 410, label="no preamble file")
blank_input(D, sysp, "Schema", 380, 456, label="no schema file")
extra = input_panel(D, 320, 540,
                    "You are exploring a building model. Move deliberately and look before you "
                    "judge. Say where you are standing when you describe something.",
                    w=250, h=120, nick="my own instructions")
wire(sysp, "Additional Prompt", extra, 0)
panel(D, 380, 690,
      "No preamble file and no schema: this preset is a conversation, not a JSON submission, so "
      "the instruction is typed straight in. Edit the white panel.\r\n"
      "\r\n"
      "Asking it to say where it is standing is worth doing. A model that has walked ten steps "
      "will otherwise describe what it saw without saying where from, and the description becomes "
      "useless the moment you want to check it.",
      w=330, h=300)

# --------------------------------------------------------------------------- 3 grounding

title(D, 780, TITLE_Y, "3 - WHAT IT KNOWS", w=280, h=44)
rhino = place(D, "Rhino Document", 850, 440, nick="Rhino Document")
toolsp = place(D, "Tools Present", 850, 490, nick="Tools Present")
panel(D, 780, 560,
      "RHINO DOCUMENT so it knows what is in the file, and TOOLS PRESENT so it knows it can look "
      "and move at all.\r\n"
      "\r\n"
      "Without Tools Present the two tools in stage 9 are wired, working, and never called - "
      "because nobody told the model they exist.",
      w=280, h=240)

# --------------------------------------------------------------------------- 4 human tools

title(D, 1120, TITLE_Y, "4 - BUTTONS FOR YOU", w=280, h=44)
vsnap = place(D, "View Snapshot", 1190, 440, nick="View Snapshot")
img = place(D, "Add Image", 1190, 490, nick="Add Image")
mark = place(D, "Image Mark Up", 1190, 540, nick="Image Mark Up")
panel(D, 1120, 610,
      "The other direction: pictures YOU send it.\r\n"
      "\r\n"
      "VIEW SNAPSHOT captures your active viewport as a picture and sends it. Its right-click menu "
      "decides whether it goes straight away as its own message or waits in the prompt box for you "
      "to caption it.\r\n"
      "\r\n"
      "ADD IMAGE turns on paste, drag and file-pick in the message box.\r\n"
      "\r\n"
      "IMAGE MARK UP adds no button of its own - it changes what the other two do. Every image you "
      "send now opens in an editor first: pen, arrows, text notes, an eraser. Circle the bit you "
      "mean and write \"this joint\" next to it. Far quicker than describing a location in "
      "words.\r\n"
      "\r\n"
      "There is also a GEOMETRY SNAPSHOT tool, not used here: it frames the camera on geometry "
      "this pipeline built, so it needs a transmitter to be any use. See preset 04.",
      w=280, h=480)

# --------------------------------------------------------------------------- 5 conversation log

title(D, 1460, TITLE_Y, "5 - THE RUNNING CONVERSATION", w=320, h=44)
log = place(D, "Conversation Log", 1620, SPINE, nick="Conversation Log")
wire(log, "System Prompt", sysp, "System Prompt")
wire(log, "Prompt Signal", chat, "Prompt Signal")
wire(log, "Grounding", rhino, 0)
wire(log, "Grounding", toolsp, 0)
for t in (vsnap, img, mark):
    wire(log, "Human Tools", t, "Human Tool")
panel(D, 1460, 610,
      "Pictures live IN the conversation, both ways.\r\n"
      "\r\n"
      "A snapshot the model asked for comes back as an attachment riding on the same turn as the "
      "tool result - so the model sees the picture as part of the answer to its own question, not "
      "as a separate message.\r\n"
      "\r\n"
      "That matters for cost: images are expensive in tokens and they accumulate. Preset 10 shows "
      "how to drop old ones when a conversation gets long.",
      w=320, h=300)

# --------------------------------------------------------------------------- 6 the call

title(D, 1860, TITLE_Y, "6 - ASKING THE MODEL", w=300, h=44)
model = place(D, "Codex Model", 2000, 430, nick="Codex Model")
call = place(D, "LLM Call", 2000, 545, nick="LLM Call")
wire(call, "Model", model, "Model")
wire(call, "Signal", log, "Signal")
cancel = boolean(D, 1870, 590, False, nick="stop", toggle=False)
wire(call, "Cancel", cancel, 0)
panel(D, 1860, 640,
      "CODEX, because this preset uses tools and Claude Code cannot call them - see preset 03.\r\n"
      "\r\n"
      "The model must also be one that can SEE. Codex, Anthropic and Gemini models all can; a "
      "small local model very likely cannot, and will be handed pictures it cannot read.\r\n"
      "\r\n"
      "Walking is many short rounds rather than one long one, so expect a lot of back and forth "
      "for a single question."
      "\r\n" "\r\n"
      "One practical thing about Codex: its model list is fetched LIVE from the CLI and it "
      "changes. If a round fails saying the model does not exist or you do not have access "
      "to it, open the little dropdown beside the Codex Model node and pick again - the list "
      "you are looking at is current.",
      w=300, h=420)

# --------------------------------------------------------------------------- 7 the router

title(D, 2190, TITLE_Y, "7 - THE ROUTER", w=240, h=44)
router = place(D, "Router", 2300, SPINE, nick="Router")
wire(router, "Tool Calls", call, "Tool Calls")
router_slots(router, 1)
panel(D, 2190, 570,
      "Two tools, so two outputs plus Feedback. As in preset 03, the names you see are the tool "
      "names the Router adopted once the wires were finished.",
      w=240, h=210)

# --------------------------------------------------------------------- 8 data from your canvas

title(D, 2440, TITLE_Y, "8 - DATA FROM YOUR CANVAS", w=300, h=44)
in_start = place(D, "Harness In", 2500, 440, nick="Harness In")
in_start.Params.Output[0].NickName = "start point"
in_pos = place(D, "Harness In", 2500, 500, nick="Harness In")
in_pos.Params.Output[0].NickName = "positions"
in_notes = place(D, "Harness In", 2500, 560, nick="Harness In")
in_notes.Params.Output[0].NickName = "notes"
panel(D, 2440, 630,
      "HARNESS IN is how data gets INTO a pipeline. Each one grows a parameter on the LEFT edge of "
      "the Harness node out on your canvas, sharing its name - rename either end and the other "
      "follows.\r\n"
      "\r\n"
      "So: wire a point into \"start point\", a cloud of points into \"positions\", and optionally "
      "a matching list of descriptions into \"notes\". Nothing here works until you do.\r\n"
      "\r\n"
      "It is PASSIVE, and that is deliberate. It latches whatever it was handed and re-offers it "
      "every time the pipeline solves, but it never starts a round on its own. That is what makes "
      "it safe to feed a harness from a slider you are dragging. If you DO want a change out there "
      "to start a round, that is the Data Changed trigger in preset 08 - and it comes with a "
      "warning attached.",
      w=300, h=400)

# --------------------------------------------------------------------------- 9 eyes and legs

title(D, 2800, TITLE_Y, "9 - EYES AND LEGS", w=320, h=44)
walk = place(D, "Move In Space", 2920, 460, nick="Move In Space")
look = place(D, "Take Snapshot", 2920, 640, nick="Take Snapshot")
wire(walk, "Signal", router, 0)
wire(look, "Signal", router, 1)
wire(walk, "Start Point", in_start, 0)
wire(walk, "Positions", in_pos, 0)
wire(walk, "Position Notes", in_notes, 0)
wire(look, "Current Location", walk, "Current Position")
panel(D, 2800, 740,
      "MOVE IN SPACE walks the model through your points, one step at a time.\r\n"
      "\r\n"
      "The clever part is that adjacency is WORKED OUT, never configured. From wherever it is "
      "standing, the component sorts every other point by vertical band (same level, up, down) and "
      "by one of eight compass directions, then offers only the CLOSEST in each - so a step is "
      "always a step to the next thing, and \"up\" means the next level up rather than the top of "
      "the stack. Twenty-six moves in all.\r\n"
      "\r\n"
      "Directions are FIXED and WORLD-relative: forward is +Y, right is +X, up is +Z. Not "
      "heading-relative, deliberately - a heading is undefined on the first move, has to be tracked "
      "across turns, and flips left and right every time it turns round.\r\n"
      "\r\n"
      "It can also ask WITHOUT moving, which is how it gets its bearings on the first call: "
      "\"where am I and where can I go?\"\r\n"
      "\r\n"
      "TAKE SNAPSHOT stands at whatever Current Location it is given - here, wherever the walk has "
      "reached - and aims by compass bearing and elevation. Same bearing convention as the walking, "
      "on purpose, so one mental compass covers both. A 35mm lens, about 54 degrees across, and the "
      "model is TOLD that number so that something off-camera is not mistaken for something "
      "absent.",
      w=320, h=620)

# --------------------------------------------------------------------- 10 out onto your canvas

title(D, 3220, TITLE_Y, "10 - OUT ONTO YOUR CANVAS", w=320, h=44)
out_route = place(D, "Harness Out", 3320, 460, nick="Harness Out")
wire(out_route, "Data", walk, "Traversed Points")
out_route.Params.Input[0].NickName = "route walked"
here = panel(D, 3520, 560, "where it is standing now", w=280, h=90, colour=OUTPUT_GREY)
here.AddSource(pin(walk, "out", "Current Position"))
looked = panel(D, 3520, 680, "which way it last looked", w=280, h=90, colour=OUTPUT_GREY)
looked.AddSource(pin(look, "out", "Current View"))
panel(D, 3220, 800,
      "HARNESS OUT is the general-purpose way out of a harness. Whatever arrives on it is written "
      "onto your canvas, tree structure and all.\r\n"
      "\r\n"
      "Its grip appears on the RIGHT edge of the Harness node, labelled with whatever you called "
      "the input - here, \"route walked\". Drag from that grip onto any input on your canvas and it "
      "will write there. A Panel works too. A drop on empty canvas does nothing; it never creates "
      "a target for you.\r\n"
      "\r\n"
      "This is the asymmetry worth understanding about a harness: data comes IN through real wires, "
      "because Grasshopper hands us those for free, but it goes OUT as a drag arrow, because "
      "Grasshopper has no such thing as a wire that writes.\r\n"
      "\r\n"
      "The route being real geometry is the point of the whole preset. \"Which of these rooms is "
      "worst lit\" becomes a polyline you can measure, section and build from.",
      w=320, h=420)

# --------------------------------------------------------------------------- 11 return paths

fb_res, co_res = back(D, walk, "Result", router, "Results",
                      3400, 1300, 1900, 1300, nick="tool results")
wire(fb_res, "Signal", look, "Result")
back(D, router, "Feedback", log, "LLM Tool Signal",
     2340, 1460, 1560, 1460, nick="tool round to the log")
back(D, call, "Success Signal", log, "Response Signal",
     2180, 1620, 1560, 1620, nick="reply back to the log")
panel(D, 1000, 1280,
      "THE THREE RETURN PATHS, as in preset 03: tool results to the Router, the finished round to "
      "the Conversation Log's LLM Tool Signal, and the reply to Response Signal.\r\n"
      "\r\n"
      "Both tools share one Feedback node and one Collector, because they share a destination.\r\n"
      "\r\n"
      "Worth knowing what happens to the PICTURE on the way through. A tool result is text on every "
      "provider there is, so an image cannot be one - it travels as an ATTACHMENT on the same turn, "
      "and it has to be ordered after every text result, because some providers insist a "
      "tool-answering turn begins with them.",
      w=520, h=320, colour=WARN_ORANGE)

# --------------------------------------------------------------------------- 12 what to try

panel(D, 3620, 1280,
      "THINGS TO TRY\r\n"
      "\r\n"
      "1. Wire it up first. On your own canvas: a Point for \"start point\", and a set of points "
      "for \"positions\" - a Grid, or the corners of some rooms. Wire both into the left edge of "
      "the Harness node.\r\n"
      "\r\n"
      "2. \"Where are you, and where can you go from there?\" It answers without moving. That is "
      "how it gets its bearings.\r\n"
      "\r\n"
      "3. \"Walk east until you run out of points, looking north at each stop, and tell me where "
      "the view changes.\"\r\n"
      "\r\n"
      "4. Drag the \"route walked\" grip off the Harness node onto a Curve parameter, then draw a "
      "polyline through the route. Its reasoning is now geometry.\r\n"
      "\r\n"
      "5. Wire descriptions into \"notes\" - one per point, or one for the lot - and it is told "
      "where it is in words as well as coordinates.\r\n"
      "\r\n"
      "6. Send it a screenshot with Image Mark Up: circle something and write \"what is this?\".",
      w=520, h=480)

commit_build(D, "build preset 06")
solve(D)
# The Codex model list is fetched LIVE from the CLI and changes under you - it went from
# gpt-5.5/5.4/5.4-mini to gpt-5.6-sol/terra/luna/5.5/5.4-mini inside one session here. So the
# Picker is deliberately NOT pinned: left alone it snaps to whatever the CLI offers first,
# which self-heals. A pinned name that the account cannot use answers 404 and does not.
solve(D)
solve(D)
write_dump(D, DUMP)
say("router outputs:", [p.NickName for p in router.Params.Output])
say("inlets on the proxy:", [p.NickName for p in H.Params.Input])
bad = sweep(D, NAME)
save_phy(H, OUT,
         description="Take Snapshot and Move In Space: the model poses a camera and walks through "
                     "a lattice of points you supply, and the route comes back onto your canvas as "
                     "geometry. Also the first preset with Harness In and Harness Out.",
         chat_text="Letting It Look and Walk\r\n\r\n"
                   "This pipeline can see and can move. Take Snapshot poses a camera and hands the "
                   "picture back; Move In Space walks through a cloud of points you give it.\r\n\r\n"
                   "SET THIS UP FIRST: the Harness node on your canvas has parameters on its left "
                   "edge. Wire a point into \"start point\" and a set of points into \"positions\" "
                   "- room corners, stair landings, a grid over a site.\r\n\r\n"
                   "Then try: \"where are you, and where can you go from there?\"")
say("PROBLEMS:", bad)
