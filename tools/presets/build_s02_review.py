# Copyright (c) 2026 Physalia Contributors
# SPDX-License-Identifier: AGPL-3.0-or-later
"""
Scenario S02 - "Walk the Building and Review It".

Give it a set of points through your scheme and a rubric, and it walks through, looks around at each
stop, and writes you a review. The route it took comes back onto your canvas as geometry.
"""

# The ONE machine-specific line in this file. Set ROOT before exec'ing this script to build
# from a checkout somewhere else:  ROOT = r"D:\\code\\Physalia"
try:
    ROOT
except NameError:
    ROOT = r"C:\Users\rober\repos\Physalia"
exec(open(ROOT + r"\tools\presets\phybuild.py").read())

NAME = "S02 - Walk the Building and Review It"
OUT = PRESETS + r"\%s.phy" % NAME
DUMP = SCRATCH + r"\dumpS02.txt"

host = clear_host()
H, D = new_harness(host, 200, 200, name="walk-the-building")

panel(D, 40, 40,
      "WALK THE BUILDING AND REVIEW IT\r\n"
      "\r\n"
      "A design review where the reviewer actually goes and looks, from the positions a person would "
      "stand in, rather than from one hero view.\r\n"
      "\r\n"
      "SET IT UP\r\n"
      "1. On your own canvas, make a set of points where a person would stand - room centres, the "
      "top and bottom of each stair, the entrance, a few points along the approach. Twenty is "
      "plenty; they do not need to be tidy.\r\n"
      "2. Wire them into POSITIONS on the left edge of the Harness node, and one point into START.\r\n"
      "3. Optionally wire a matching list of names into NOTES - \"main stair, upper landing\" - so it "
      "knows where it is in words as well as coordinates.\r\n"
      "4. Edit the rubric in stage 2 to whatever you are actually reviewing for.\r\n"
      "\r\n"
      "THEN ASK: \"walk the ground floor and tell me where the daylight is worst\", or \"go to each "
      "stair landing, look back down, and tell me if the handrail reads as continuous\".\r\n"
      "\r\n"
      "WHAT YOU GET BACK: prose in the chat, and the ROUTE as real geometry on your canvas. The "
      "second one is the point - its reasoning becomes something you can section, measure and put in "
      "a drawing.\r\n"
      "\r\n"
      "BE HONEST ABOUT WHAT THIS IS. It is a fresh pair of eyes that never gets bored and has no "
      "stake in the scheme. It is not a consultant. It cannot see anything you have not modelled, "
      "and it will happily describe a daylight condition in a model with no glazing in it.",
      w=920, h=470, colour=INTRO_GREEN)

# --------------------------------------------------------------------------- inputs

title(D, 60, 560, "1 - WHERE IT CAN STAND", w=300, h=44)
in_start = place(D, "Harness In", 140, 680, nick="Harness In")
in_start.Params.Output[0].NickName = "start"
in_pos = place(D, "Harness In", 140, 740, nick="Harness In")
in_pos.Params.Output[0].NickName = "positions"
in_notes = place(D, "Harness In", 140, 800, nick="Harness In")
in_notes.Params.Output[0].NickName = "notes"
panel(D, 60, 860,
      "These three grow parameters on the LEFT edge of the Harness node out on your canvas. Wire "
      "your points in there.\r\n"
      "\r\n"
      "Nothing here works until you do - and it will tell you so rather than pretending.\r\n"
      "\r\n"
      "They are PASSIVE: feeding them from a slider you are dragging cannot start a round. Only you "
      "or a trigger does that.",
      w=300, h=260)

# --------------------------------------------------------------------------- the loop

L = core_loop(D, 560, 700, model_name="Codex Model",
              instruction="You are reviewing a building by walking through it.\r\n"
                          "\r\n"
                          "Work like this: call move_in_space with no direction first to find out "
                          "where you are and what your options are. Then move deliberately, and "
                          "LOOK before you judge - take_snapshot at each stop, more than once if "
                          "the room matters.\r\n"
                          "\r\n"
                          "Always say where you were standing when you describe something. Review "
                          "for: daylight and glare, whether the way out is obvious, headroom and "
                          "pinch points, what the first thing you see on entering is, and anything "
                          "that looks unresolved.\r\n"
                          "\r\n"
                          "Say plainly when something is not modelled rather than inferring it.")
vsnap = place(D, "View Snapshot", 640, 560, nick="View Snapshot")
img = place(D, "Add Image", 640, 610, nick="Add Image")
mark = place(D, "Image Mark Up", 800, 560, nick="Image Mark Up")
rhino = place(D, "Rhino Document", 800, 610, nick="Rhino Document")
toolsp = place(D, "Tools Present", 960, 560, nick="Tools Present")
units = place(D, "Document Units Grounding", 960, 610, nick="Document Units")
for g in (rhino, toolsp, units):
    wire(L["log"], "Grounding", g, 0)
for t in (vsnap, img, mark):
    wire(L["log"], "Human Tools", t, "Human Tool")

panel(D, 560, 1000,
      "THE RUBRIC IS THE WHOLE JOB. Edit the white panel - it is what turns \"describe this\" into a "
      "review. Be specific about what you are reviewing FOR; a vague rubric gets vague prose.\r\n"
      "\r\n"
      "The instruction also tells it to say when something is not modelled. Worth keeping. Without "
      "it you get confident opinions about glazing that does not exist.\r\n"
      "\r\n"
      "IMAGE MARK UP means you can answer it in kind: circle the thing you mean on one of its own "
      "snapshots and send it back.",
      w=360, h=320)

# --------------------------------------------------------------------------- eyes and legs

title(D, 2100, 560, "3 - EYES AND LEGS", w=320, h=44)
router = place(D, "Router", 2180, 700, nick="Router")
wire(router, "Tool Calls", L["call"], "Tool Calls")
router_slots(router, 2)
walk = place(D, "Move In Space", 2520, 640, nick="Move In Space")
look = place(D, "Take Snapshot", 2520, 790, nick="Take Snapshot")
askh = place(D, "Ask Human", 2520, 900, nick="Ask Human")
wire(walk, "Signal", router, 0)
wire(look, "Signal", router, 1)
wire(askh, "Signal", router, 2)
wire(walk, "Start Point", in_start, 0)
wire(walk, "Positions", in_pos, 0)
wire(walk, "Position Notes", in_notes, 0)
wire(look, "Current Location", walk, "Current Position")
panel(D, 2100, 980,
      "MOVE IN SPACE works out for itself which points are reachable from where it is standing - "
      "nearest in each of eight compass directions, plus one level up and one down. So a scattered "
      "cloud is navigable without you organising it.\r\n"
      "\r\n"
      "Directions are FIXED: forward is +Y, right is +X. Not relative to which way it is facing, "
      "which would flip left and right every time it turned round.\r\n"
      "\r\n"
      "TAKE SNAPSHOT stands wherever the walk has reached and aims by compass bearing and "
      "elevation - the same compass, so one mental model covers both. About a 35mm lens, and it is "
      "TOLD that, so it does not mistake something off-camera for something absent.",
      w=320, h=360)

# --------------------------------------------------------------------------- outputs

title(D, 2960, 560, "4 - WHAT COMES BACK", w=320, h=44)
out_route = place(D, "Harness Out", 3040, 680, nick="Harness Out")
wire(out_route, "Data", walk, "Traversed Points")
out_route.Params.Input[0].NickName = "route walked"
here = panel(D, 3240, 760, "where it is standing now", w=300, h=80, colour=OUTPUT_GREY)
here.AddSource(pin(walk, "out", "Current Position"))
looked = panel(D, 3240, 860, "which way it last looked", w=300, h=80, colour=OUTPUT_GREY)
looked.AddSource(pin(look, "out", "Current View"))
panel(D, 2960, 970,
      "HARNESS OUT puts the route on your canvas. Drag the grip labelled \"route walked\" from the "
      "RIGHT edge of the Harness node onto a Curve parameter, then draw a polyline through it.\r\n"
      "\r\n"
      "That is what makes this more than a chat: the path it took, the stops it made and the "
      "directions it looked are geometry you can put in a drawing, section, or hand to somebody "
      "else.\r\n"
      "\r\n"
      "Use EXPORT CONVERSATION in the chat header to keep the written review.",
      w=320, h=320)

# --------------------------------------------------------------------------- bounds + returns

budget = place(D, "Budget Guard", 1700, 1300, nick="Budget Guard")
b_calls = slider(D, 1480, 1380, 80, 1, 400, nick="max calls")
wire(budget, "Max Calls", b_calls, 0)
wire(budget, "Signal", L["log"], "Signal")
wire(L["call"], "Signal", budget, "Success Signal")
spent = panel(D, 1620, 1420, "spent so far", w=300, h=80, colour=OUTPUT_GREY)
spent.AddSource(pin(budget, "out", "Spent"))
panel(D, 1620, 1520,
      "A WALKTHROUGH IS MANY SHORT ROUNDS - a move, a look, a judgement, repeat - so it costs more "
      "than it looks like it should. Twenty stops with two looks each is sixty-odd calls.\r\n"
      "\r\n"
      "Hence the budget guard, and hence starting with five points rather than fifty.",
      w=300, h=240)

fb_res, co_res = back(D, walk, "Result", router, "Results",
                      2900, 1300, 1900, 1300, nick="tool results")
wire(fb_res, "Signal", look, "Result")
wire(fb_res, "Signal", askh, "Result")
back(D, router, "Feedback", L["log"], "LLM Tool Signal",
     2180, 1460, 1220, 1460, nick="tool round to the log")

panel(D, 60, 1300,
      "THINGS TO TRY\r\n"
      "\r\n"
      "1. Start with five points and one question. Get a feel for how it moves before spending on "
      "a full floor.\r\n"
      "\r\n"
      "2. \"Where are you and where can you go?\" - it answers without moving. That is how it gets "
      "its bearings, and how you check your lattice is sensible.\r\n"
      "\r\n"
      "3. Put the points at EYE HEIGHT, not on the floor. It looks from where it stands.\r\n"
      "\r\n"
      "4. Wire the route into a polyline and compare two schemes by the path each one makes you "
      "walk.\r\n"
      "\r\n"
      "5. Ask it to walk the same route twice - once as a visitor looking for reception, once as "
      "somebody leaving in a hurry. Same geometry, different review.",
      w=480, h=420)

commit_build(D, "build scenario S02")
solve(D)
solve(D)
write_dump(D, DUMP)
say("router outputs:", [q.NickName for q in router.Params.Output])
bad = sweep(D, NAME)
save_phy(H, OUT,
         description="A design review where the reviewer walks through. Give it points to stand on "
                     "and a rubric; it moves, looks around, and writes a review - and the route it "
                     "took comes back onto your canvas as geometry.",
         chat_text="Walk the Building and Review It\r\n\r\n"
                   "SET UP FIRST: wire a set of points where a person would stand into POSITIONS on "
                   "the left edge of the Harness node, and one point into START. Room centres, stair "
                   "landings, the entrance. Twenty is plenty.\r\n\r\n"
                   "Then try: \"where are you and where can you go?\" - it answers without moving, "
                   "which is how you check the lattice makes sense.\r\n\r\n"
                   "Then: \"walk the ground floor and tell me where the daylight is worst.\"\r\n\r\n"
                   "It can only see what you have modelled. Edit the rubric on the canvas to review "
                   "for what you actually care about.")
say("PROBLEMS:", bad)
