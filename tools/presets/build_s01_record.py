# Copyright (c) 2026 Physalia Contributors
# SPDX-License-Identifier: AGPL-3.0-or-later
"""
Scenario S01 - "Record and Repeat".

Do the thing once by hand. Physalia watches, describes the procedure back to you, and then repeats
it on whatever you select next.

The job, not the machinery: the annotation here says what to DO with it, and only explains a
component where knowing the mechanism changes how you use it.
"""

# The ONE machine-specific line in this file. Set ROOT before exec'ing this script to build
# from a checkout somewhere else:  ROOT = r"D:\\code\\Physalia"
try:
    ROOT
except NameError:
    ROOT = r"C:\Users\rober\repos\Physalia"
exec(open(ROOT + r"\tools\presets\phybuild.py").read())

NAME = "S01 - Record and Repeat"
OUT = PRESETS + r"\%s.phy" % NAME
DUMP = SCRATCH + r"\dumpS01.txt"

host = clear_host()
H, D = new_harness(host, 200, 200, name="record-and-repeat")

# --------------------------------------------------------------------------- what it is

panel(D, 40, 40,
      "RECORD AND REPEAT\r\n"
      "\r\n"
      "You have forty balcony slabs to trim, and every one takes the same six commands. Do it once "
      "properly and have this repeat it.\r\n"
      "\r\n"
      "HOW TO RUN IT\r\n"
      "1. Open the chat window (double-click the Harness node on your canvas).\r\n"
      "2. In the trigger list, switch RECORDING on.\r\n"
      "3. Go and do the thing in Rhino, by hand, once. Take your time - it is not timing you.\r\n"
      "4. Switch Recording off from that same list. THAT is what sends the recording.\r\n"
      "5. It describes the procedure back to you and waits. Correct it if it has misread you.\r\n"
      "6. Select the next objects and say \"do the same to these\".\r\n"
      "\r\n"
      "WHY IT WORKS AT ALL. Rhino already knows what you did - the command names, their parameters "
      "and what was selected when you ran them - so this reads your INTENT rather than diffing "
      "geometry before and after. That is the difference between \"Offset by 150\" and \"some edges "
      "moved\".\r\n"
      "\r\n"
      "IT WILL NOT GENERALISE FOR FREE. Repeating a procedure on geometry that differs is the model "
      "reasoning by analogy, and it can get that wrong. That is exactly why step 5 exists: the "
      "description is put in front of you while correcting it is still cheap.",
      w=900, h=430, colour=INTRO_GREEN)

# --------------------------------------------------------------------------- the recorder

title(D, 60, 520, "1 - WATCHING YOU WORK", w=320, h=44)
watch = place(D, "Watch Modelling", 340, 640, nick="Watch Modelling")
capture = boolean(D, 70, 600, True, nick="capture the command line", toggle=True)
wire(watch, "Capture Command Line", capture, 0)
instr = input_panel(D, 60, 660,
                    "Describe the procedure I just demonstrated as numbered steps, with the actual "
                    "numbers I used. Then STOP and wait - do not apply it to anything yet.",
                    w=200, h=140, nick="what to do with a recording")
wire(watch, "Instruction", instr, 0)
steps = panel(D, 60, 830, "the steps it recorded appear here", w=320, h=180, colour=OUTPUT_GREY)
steps.AddSource(pin(watch, "out", "Steps"))
count = panel(D, 60, 1030, "how many steps", w=320, h=60, colour=OUTPUT_GREY)
count.AddSource(pin(watch, "out", "Step Count"))
panel(D, 60, 1120,
      "SWITCH IT ON AND OFF FROM THE CHAT WINDOW, not from this node's menu - you will be in Rhino "
      "modelling, not looking at this canvas.\r\n"
      "\r\n"
      "It fires when you switch it OFF, not per command. There is no pause length that tells "
      "thinking apart from finishing, so it waits for you to say you are done.\r\n"
      "\r\n"
      "WHAT IT KEEPS: anything that actually changed the document, with its parameters, and what "
      "was selected when you ran it. View changes, selections and orbiting are dropped without "
      "being asked about. If you UNDO something, that step is removed - a recording containing a "
      "mistake and its undo teaches nothing.\r\n"
      "\r\n"
      "Gumball drags are recorded too, as the vector you moved by.\r\n"
      "\r\n"
      "CAPTURE COMMAND LINE is what gets you the numbers - the distance you typed, the radius. "
      "Worth leaving on. It is the only place those exist as text.",
      w=320, h=420)

# --------------------------------------------------------------------------- the loop

L = core_loop(D, 620, 620, model_name="Codex Model",
              instruction="You repeat modelling procedures. When you are shown a recording, "
                          "describe it back as numbered steps and WAIT. When you are then asked to "
                          "apply it, use run_rhino_script against the current selection. Say what "
                          "you are about to do before you do it, and report what changed.")
wire(L["log"], "Prompt Signal", watch, "Signal")

rhino = place(D, "Rhino Document", 700, 480, nick="Rhino Document")
toolsp = place(D, "Tools Present", 700, 530, nick="Tools Present")
tctl = place(D, "Trigger Control", 700, 1100, nick="Trigger Control")
wire(L["log"], "Grounding", rhino, 0)
wire(L["log"], "Grounding", toolsp, 0)
wire(L["log"], "Human Tools", tctl, "Human Tool")

panel(D, 620, 1180,
      "RHINO DOCUMENT is doing real work here: it reports HOW MANY OBJECTS ARE SELECTED, which is "
      "what makes \"do the same to these\" a sentence the model can act on.\r\n"
      "\r\n"
      "TRIGGER CONTROL puts the Recording switch in the chat window. Note the one asymmetry worth "
      "knowing: switching the recorder off from its OWN row hands the recording over, while SWITCH "
      "ALL OFF discards it. The page says which is which on the row.",
      w=320, h=280)

# --------------------------------------------------------------------------- doing it

title(D, 2100, 520, "3 - DOING IT AGAIN", w=320, h=44)
router = place(D, "Router", 2180, 660, nick="Router")
wire(router, "Tool Calls", L["call"], "Tool Calls")
router_slots(router, 1)
drive = place(D, "Drive Rhino", 2520, 620, nick="Drive Rhino")
askh = place(D, "Ask Human", 2520, 720, nick="Ask Human")
wire(drive, "Signal", router, 0)
wire(askh, "Signal", router, 1)
script = panel(D, 2760, 580, "the script it ran on your model", w=320, h=200, colour=OUTPUT_GREY)
script.AddSource(pin(drive, "out", "Last Script"))
sel = panel(D, 2760, 800, "what you told it to work on", w=320, h=90, colour=OUTPUT_GREY)
sel.AddSource(pin(askh, "out", "Selection"))
panel(D, 2100, 940,
      "DRIVE RHINO is how the procedure gets repeated: the model writes Python and runs it against "
      "your document. The whole run is ONE undo step, so if you dislike the result, Ctrl+Z once.\r\n"
      "\r\n"
      "READ THE GREY PANEL when something looks wrong. The script it ran is almost always a faster "
      "explanation than asking it what it did.\r\n"
      "\r\n"
      "ASK HUMAN is the safety rail. Tell it in the instruction to ask before touching anything it "
      "is unsure about, and the question arrives as a card in the chat - including \"select the "
      "ones you mean in Rhino\", whose answer is read at the moment you press the button. That "
      "selection lands on the wire above.\r\n"
      "\r\n"
      "It is CODEX, because a tool-using pipeline needs a model that can call tools and Claude Code "
      "cannot. If a round fails saying the model does not exist, re-pick from the dropdown - that "
      "list comes live from the CLI.",
      w=320, h=420)

# --------------------------------------------------------------------------- bounds

title(D, 1660, 1180, "4 - WHAT STOPS IT", w=300, h=44)
budget = place(D, "Budget Guard", 1740, 1320, nick="Budget Guard")
b_calls = slider(D, 1520, 1400, 40, 1, 300, nick="max calls")
wire(budget, "Max Calls", b_calls, 0)
wire(budget, "Signal", L["log"], "Signal")
wire(L["call"], "Signal", budget, "Success Signal")
spent = panel(D, 1660, 1440, "spent so far", w=300, h=80, colour=OUTPUT_GREY)
spent.AddSource(pin(budget, "out", "Spent"))
panel(D, 1660, 1540,
      "A recorder is a TRIGGER, so this pipeline can start a round without you typing. That is the "
      "point of it, and it is also why there is a budget guard inline.\r\n"
      "\r\n"
      "MAX CALLS is the cap that works here. A command-line model reports no useful token figure, "
      "so a token budget on one is meaningless.\r\n"
      "\r\n"
      "Nothing is armed when this file opens - arming is never saved - so it cannot start spending "
      "on somebody else's machine when you send it to them.",
      w=300, h=300)

# --------------------------------------------------------------------------- return paths

fb_res, co_res = back(D, drive, "Result", router, "Results",
                      2900, 1320, 1900, 1320, nick="tool results")
wire(fb_res, "Signal", askh, "Result")
back(D, router, "Feedback", L["log"], "LLM Tool Signal",
     2180, 1480, 1240, 1480, nick="tool round to the log")
panel(D, 60, 1580,
      "THINGS TO TRY\r\n"
      "\r\n"
      "1. Record something small first: draw a line, offset it, join. Switch off and read what it "
      "describes back. You will learn quickly how much detail it keeps.\r\n"
      "\r\n"
      "2. Record a procedure, then select DIFFERENT objects and say \"do the same to these\".\r\n"
      "\r\n"
      "3. Deliberately make a mistake mid-recording and undo it. The undone step should not appear "
      "in the description.\r\n"
      "\r\n"
      "4. Ask it to write the procedure into its memory so you can ask for it again next week - "
      "add a Memory tool to the Router for that.",
      w=520, h=380)

commit_build(D, "build scenario S01")
solve(D)
solve(D)
write_dump(D, DUMP)
say("router outputs:", [q.NickName for q in router.Params.Output])
say("prompt signal sources:", pin(L["log"], "in", "Prompt Signal").SourceCount)
bad = sweep(D, NAME)
save_phy(H, OUT,
         description="Do it once by hand and have Physalia repeat it. Watch Modelling records the "
                     "commands you ran and what they were applied to; the model describes the "
                     "procedure back, waits for you to confirm, then repeats it on your next "
                     "selection.",
         chat_text="Record and Repeat\r\n\r\n"
                   "Do the thing once by hand. This watches, describes the procedure back, and "
                   "then repeats it on whatever you select next.\r\n\r\n"
                   "1. Switch RECORDING on in the trigger list below.\r\n"
                   "2. Do it once in Rhino, by hand.\r\n"
                   "3. Switch Recording off - that is what sends it.\r\n"
                   "4. Read what it describes back, and correct it if needed.\r\n"
                   "5. Select the next objects and say \"do the same to these\".\r\n\r\n"
                   "Switching the recorder off from its own row HANDS OVER the recording. Switch "
                   "all off discards it.")
say("PROBLEMS:", bad)
