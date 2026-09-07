# Copyright (c) 2026 Physalia Contributors
# SPDX-License-Identifier: AGPL-3.0-or-later
"""
Scenario S10 - "A Take-off That Keeps Itself Up To Date".

A quantity schedule that re-counts itself when you change the model, and puts the numbers on your
canvas rather than in a conversation you have to scroll back through.
"""

# The ONE machine-specific line in this file. Set ROOT before exec'ing this script to build
# from a checkout somewhere else:  ROOT = r"D:\\code\\Physalia"
try:
    ROOT
except NameError:
    ROOT = r"C:\Users\rober\repos\Physalia"
exec(open(ROOT + r"\tools\presets\phybuild.py").read())

NAME = "S10 - A Take-off That Keeps Itself Up To Date"
OUT = PRESETS + r"\%s.phy" % NAME
DUMP = SCRATCH + r"\dumpS10.txt"

host = clear_host()
H, D = new_harness(host, 200, 200, name="take-off-that-keeps-up")

panel(D, 40, 40,
      "A TAKE-OFF THAT KEEPS ITSELF UP TO DATE\r\n"
      "\r\n"
      "Somebody does the quantities on Tuesday. The scheme changes on Wednesday. The spreadsheet "
      "is wrong from Wednesday afternoon and nobody notices until the cost plan comes back.\r\n"
      "\r\n"
      "THIS COUNTS THE MODEL AND RE-COUNTS IT WHEN THE MODEL CHANGES. The numbers land on your "
      "Grasshopper canvas as data, so they can feed a panel, a spreadsheet export, a cost formula "
      "or a drawing annotation - and they are never more than a few seconds out of date.\r\n"
      "\r\n"
      "IT IS NOT A QUANTITY SURVEYOR. It counts what you tell it to count, the way you tell it to. "
      "The value is that the counting rule lives in one place, is written in plain English, and is "
      "applied identically every single time - which is more than can be said for a spreadsheet "
      "three people have edited.\r\n"
      "\r\n"
      "WHY NOT JUST BUILD IT WITH COMPONENTS? For a fixed rule you should - it is faster and it is "
      "free. This earns its keep when the rule is FUZZY: \"count the glazing by pane, but treat a "
      "sliding door as one unit\", \"anything under 300mm is an offcut\", \"exclude the temporary "
      "works layers\". Those are sentences, not components, and you will change them weekly.\r\n"
      "\r\n"
      "READ THE COUNTING RULE BELOW BEFORE YOU TRUST A NUMBER. It is the whole thing.",
      w=960, h=470, colour=INTRO_GREEN)

SPINE = 760

# --------------------------------------------------------------------------- the rule

title(D, 60, 560, "1 - THE COUNTING RULE", w=340, h=44)

L = core_loop(D, 560, SPINE, model_name="Codex Model",
              instruction="You produce quantity take-offs from a Rhino model.\r\n"
                          "\r\n"
                          "MEASURE, DO NOT ESTIMATE. Use run_rhino_script for every number and "
                          "print what you counted. Never carry a figure over from an earlier "
                          "round - the model may have changed since, which is the whole reason "
                          "this pipeline exists.\r\n"
                          "\r\n"
                          "COUNTING RULES: measure per LAYER. Report areas in square metres and "
                          "lengths in metres, to two decimal places. State the object count "
                          "alongside every quantity so a wrong count is visible. Exclude any layer "
                          "whose name starts with TEMP-, WIP- or REF-.\r\n"
                          "\r\n"
                          "ANSWER AS A TABLE, one line per item, fields separated by a comma:\r\n"
                          "item, layer, count, quantity, unit\r\n"
                          "\r\n"
                          "Nothing before the table and nothing after it except a single line "
                          "starting TOTAL. If something could not be measured, give it a line with "
                          "the quantity NOT-MEASURED and say why on that line - a gap you can see "
                          "beats a number you cannot trust.")

rhinog = place(D, "Rhino Document", 900, 620, nick="Rhino Document")
units = place(D, "Document Units Grounding", 1060, 620, nick="Document Units")
toolsp = place(D, "Tools Present", 1220, 620, nick="Tools Present")
for g in (rhinog, units, toolsp):
    wire(L["log"], "Grounding", g, 0)
expc = place(D, "Export Conversation", 1220, 670, nick="Export Conversation")
tctl = place(D, "Trigger Control", 1220, 720, nick="Trigger Control")
for t in (expc, tctl):
    wire(L["log"], "Human Tools", t, "Human Tool")

panel(D, 60, 640,
      "THE WHITE BOX IN THE MIDDLE IS THE SPECIFICATION. Edit it. Everything this pipeline is "
      "worth is in that text.\r\n"
      "\r\n"
      "Three things in it are load-bearing and worth keeping when you rewrite it:\r\n"
      "\r\n"
      "MEASURE, DO NOT ESTIMATE, and never reuse an earlier figure. Without that sentence a model "
      "asked to \"update the take-off\" will cheerfully adjust last round's numbers by what it "
      "thinks changed. That defeats the entire point.\r\n"
      "\r\n"
      "THE COUNT BESIDE EVERY QUANTITY. An area that is wrong because 200 objects were caught "
      "instead of 20 looks perfectly plausible on its own; the count makes it obvious.\r\n"
      "\r\n"
      "NOT-MEASURED RATHER THAN A GUESS. A visible gap is something you can go and fix. A confident "
      "wrong number is something that gets built.\r\n"
      "\r\n"
      "THE TABLE FORMAT is what makes the answer usable downstream - comma-separated, one line per "
      "item, nothing around it. That is a hair away from a CSV, which is deliberate.",
      w=420, h=560)

# --------------------------------------------------------------------------- trigger

title(D, 60, 1260, "2 - WHAT MAKES IT RE-COUNT", w=340, h=44)
trig = place(D, "Rhino Changed", 220, 1400, nick="Rhino Changed")
t_geo = boolean(D, 40, 1360, True, nick="watch geometry", toggle=True)
t_lay = boolean(D, 40, 1410, True, nick="watch layers", toggle=True)
t_sel = boolean(D, 40, 1460, False, nick="watch selection", toggle=True)
wire(trig, "Geometry", t_geo, 0)
wire(trig, "Layers", t_lay, 0)
wire(trig, "Selection", t_sel, 0)

throttle = place(D, "Signal Throttle", 480, 1400, nick="Signal Throttle")
th_int = slider(D, 260, 1500, 60, 5, 600, nick="seconds between re-counts")
wire(throttle, "Interval", th_int, 0)
wire(throttle, "Signal", trig, "Signal")
wire(L["log"], "Prompt Signal", throttle, "Signal")

panel(D, 60, 1560,
      "RHINO CHANGED wakes the pipeline when you edit the MODEL. Grasshopper would never notice "
      "that on its own - nothing on the canvas moved - so this subscribes to Rhino's own events.\r\n"
      "\r\n"
      "WATCH SELECTION IS OFF, deliberately. Clicking on things is not a change to the quantities, "
      "and with it on you would start a paid round every time you selected something.\r\n"
      "\r\n"
      "SIGNAL THROTTLE IS THE COMPONENT THAT MAKES THIS AFFORDABLE. Dragging a wall generates a "
      "stream of events; deleting fifty objects generates fifty. The throttle lets ONE through per "
      "interval and keeps the NEWEST, because an overtaken event is stale by definition. Sixty "
      "seconds is a sane start - drop it to five while you are testing and put it back afterwards.\r\n"
      "\r\n"
      "ARMING IS NEVER SAVED, so this opens switched off however you last left it. Arm it from the "
      "trigger list in the chat window, which is also where you switch it off in a hurry.\r\n"
      "\r\n"
      "THE HONEST WARNING: an armed trigger plus a model somebody else is editing is a pipeline "
      "spending money while you are at lunch. That is what the budget guard in stage 4 is for, and "
      "it is not optional here.",
      w=420, h=560)

# --------------------------------------------------------------------------- tools

title(D, 1980, 560, "3 - COUNTING IT", w=340, h=44)
router = place(D, "Router", 2060, 760, nick="Router")
wire(router, "Tool Calls", L["call"], "Tool Calls")
router_slots(router, 2)
drive = place(D, "Drive Rhino", 2400, 700, nick="Drive Rhino")
mem = place(D, "Memory", 2400, 800, nick="Memory")
memfolder = input_panel(D, 2180, 830, "take-off-rules", w=190, h=40, nick="memory folder")
wire(mem, "Memory Folder", memfolder, 0)
state = place(D, "Pipeline State", 2400, 920, nick="Pipeline State")
st_instr = input_panel(D, 2140, 950,
                       "After each take-off, store the TOTAL under the key 'total' and the object "
                       "count under 'objects', so the canvas can compare this run with the last "
                       "one.",
                       w=220, h=140, nick="what to remember between runs")
wire(state, "Instruction", st_instr, 0)
st_key = input_panel(D, 2140, 1110, "total", w=220, h=40, nick="the key to read out")
wire(state, "Key", st_key, 0)
wire(drive, "Signal", router, 0)
wire(mem, "Signal", router, 1)
wire(state, "Signal", router, 2)

script = panel(D, 2680, 660, "the counting script it ran", w=340, h=200, colour=OUTPUT_GREY)
script.AddSource(pin(drive, "out", "Last Script"))
st_val = panel(D, 2680, 880, "the running total it stored", w=340, h=90, colour=OUTPUT_GREY)
st_val.AddSource(pin(state, "out", "Value"))
st_keys = panel(D, 2680, 990, "everything it is remembering", w=340, h=110, colour=OUTPUT_GREY)
st_keys.AddSource(pin(state, "out", "Keys"))

panel(D, 1980, 1180,
      "DRIVE RHINO does the counting. It writes Python, runs it against your live document and "
      "prints what it found - so the same tool that could move geometry is being used here only to "
      "read it.\r\n"
      "\r\n"
      "READ THE SCRIPT PANEL AT LEAST ONCE. It is the only way to see whether \"glazing area\" "
      "meant the surface area of the panes or the area of their bounding boxes, and those differ "
      "by a lot.\r\n"
      "\r\n"
      "PIPELINE STATE IS THE ONE PEOPLE MISS. It puts a VALUE ON A WIRE - not prose in a "
      "conversation. The model writes 'total' after each run and the canvas can read it back, so "
      "you can compare this count with the last one and put a Signal Gate on the difference: only "
      "tell me when it moves by more than 2 percent.\r\n"
      "\r\n"
      "It is NOT the Memory tool. Memory is notes the model writes for its future self and the "
      "pipeline never looks inside them. This is structured state your graph can branch on. State "
      "is per session; memory is files that outlive it.\r\n"
      "\r\n"
      "MEMORY here holds the counting conventions your office argues about once a year - which "
      "layer names mean what, whether you count openings or panes. Written down once, applied every "
      "time.",
      w=340, h=580)

# --------------------------------------------------------------------------- out

title(D, 3160, 560, "4 - ONTO THE CANVAS", w=340, h=44)
decon = place(D, "Deconstruct Signal", 3240, 720, nick="Deconstruct Signal")
wire(decon, "Signal", L["call"], "Success Signal")
out_tbl = place(D, "Harness Out", 3240, 820, nick="Harness Out")
out_tbl.Params.Input[0].NickName = "the schedule"
wire(out_tbl, "Data", decon, "Payload")
out_tot = place(D, "Harness Out", 3240, 920, nick="Harness Out")
out_tot.Params.Input[0].NickName = "running total"
wire(out_tot, "Data", state, "Value")

panel(D, 3160, 1010,
      "TWO GRIPS on the right edge of the Harness node out on your canvas.\r\n"
      "\r\n"
      "THE SCHEDULE is the table as text. Drop it on a PANEL and it lands one line per row, which "
      "is exactly what a Text Split or a CSV writer wants next. That is the shortest route from "
      "this pipeline to a spreadsheet somebody else can open.\r\n"
      "\r\n"
      "RUNNING TOTAL is the single number, so it can go straight into a cost formula, a comparison "
      "with last week's figure, or a text tag in a drawing.\r\n"
      "\r\n"
      "THIS IS THE PART THAT MAKES IT A TOOL RATHER THAN A CHAT. A number on a wire can be "
      "compared, plotted, exported and annotated. A number in a conversation has to be read by a "
      "person and typed somewhere else, which is where it goes wrong.\r\n"
      "\r\n"
      "A grip is dragged onto a PANEL or a parameter INPUT. Dropping it on empty canvas does "
      "nothing - it never creates a target for you.",
      w=340, h=520)

# --------------------------------------------------------------------------- bounds + returns

title(D, 1000, 1400, "5 - WHAT STOPS IT", w=300, h=44)
budget = place(D, "Budget Guard", 1080, 1540, nick="Budget Guard")
b_calls = slider(D, 860, 1620, 40, 1, 400, nick="max calls this session")
wire(budget, "Max Calls", b_calls, 0)
b_ext = slider(D, 860, 1670, 20, 0, 200, nick="extra calls if you approve")
wire(budget, "Extension", b_ext, 0)
wire(budget, "Signal", L["log"], "Signal")
wire(L["call"], "Signal", budget, "Success Signal")
spent = panel(D, 1000, 1720, "spent so far this session", w=300, h=80, colour=OUTPUT_GREY)
spent.AddSource(pin(budget, "out", "Spent"))

panel(D, 1000, 1820,
      "A PIPELINE WITH AN ARMED TRIGGER AND NO BUDGET GUARD HAS NO UPPER BOUND ON ITS BILL. This "
      "one has a trigger. Do not remove this node.\r\n"
      "\r\n"
      "It is checked BEFORE each call against what has already been spent, so the worst case is "
      "one call over. The cost of a call is not knowable until it has been made, and a runaway loop "
      "is stopped just as dead one call late.\r\n"
      "\r\n"
      "MAX CALLS is the cap that works on a command-line model, which reports no useful token "
      "figure at all.\r\n"
      "\r\n"
      "WHEN IT RUNS OUT it asks, as a card in the chat, whether it may have the extension on the "
      "second slider. Set that to zero and the budget is final with no card at all.",
      w=300, h=380)

fb_res, co_res = back(D, drive, "Result", router, "Results",
                      2800, 1500, 1820, 1500, nick="tool results")
for t in (mem, state):
    wire(fb_res, "Signal", t, "Result")
back(D, router, "Feedback", L["log"], "LLM Tool Signal",
     1500, 1900, 700, 1900, nick="tool round to the log")

panel(D, 520, 2280,
      "THINGS TO TRY\r\n"
      "\r\n"
      "1. Run it by hand first, with the trigger switched OFF. Type \"take off the model\" in the "
      "chat and read the script panel. Get the rule right before anything is automatic.\r\n"
      "\r\n"
      "2. Check one number by hand. Just one. It is worth the five minutes and it is how you find "
      "out whether \"area\" meant what you meant.\r\n"
      "\r\n"
      "3. Then arm Rhino Changed from the trigger list, move a wall, and watch it re-count itself. "
      "Drop the throttle to five seconds while you are testing.\r\n"
      "\r\n"
      "4. Wire THE SCHEDULE out to a Panel, then split it on commas into real columns.\r\n"
      "\r\n"
      "5. Wire RUNNING TOTAL into a comparison with a number you type, and a Signal Gate after it, "
      "so you only hear about it when the quantity moves more than you can absorb.\r\n"
      "\r\n"
      "6. Teach it a house convention - \"we count sliding doors as one unit\" - and check it "
      "remembers next session.\r\n"
      "\r\n"
      "7. Before you go home, switch the trigger off from the chat window. Nothing is armed when a "
      "file is opened, but it stays armed while the file is open.",
      w=600, h=560)

commit_build(D, "build scenario S10")
solve(D)
solve(D)
write_dump(D, DUMP)
say("router outputs:", [q.NickName for q in router.Params.Output])
bad = sweep(D, NAME)
save_phy(H, OUT,
         description="A quantity schedule that re-counts itself when you change the model. The "
                     "counting rule is a paragraph of plain English you can edit; the numbers land "
                     "on your Grasshopper canvas as data, so they can feed a spreadsheet, a cost "
                     "formula or a drawing annotation.",
         chat_text="A Take-off That Keeps Itself Up To Date\r\n\r\n"
                   "RUN IT BY HAND FIRST, with the trigger off. Type \"take off the model\" and "
                   "read the script it ran - that is where you find out whether \"area\" meant "
                   "what you meant. Check one number by hand.\r\n\r\n"
                   "Then edit the counting rule on the canvas until it says what your office "
                   "actually does.\r\n\r\n"
                   "THEN arm Rhino Changed in the trigger list below, move something, and watch it "
                   "re-count. The throttle stops a drag becoming fifty rounds; the budget guard "
                   "stops the afternoon becoming a bill.\r\n\r\n"
                   "Switch the trigger off before you go home.")
say("PROBLEMS:", bad)
