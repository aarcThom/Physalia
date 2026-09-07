# Copyright (c) 2026 Physalia Contributors
# SPDX-License-Identifier: AGPL-3.0-or-later
"""
Scenario S13 - "Run the Options Overnight".

Give it a list of options, go home, and read the comparison in the morning. For Each walks the list
one at a time; each one is built, looked at, and written up.
"""

# The ONE machine-specific line in this file. Set ROOT before exec'ing this script to build
# from a checkout somewhere else:  ROOT = r"D:\\code\\Physalia"
try:
    ROOT
except NameError:
    ROOT = r"C:\Users\rober\repos\Physalia"
exec(open(ROOT + r"\tools\presets\phybuild.py").read())

NAME = "S13 - Run the Options Overnight"
OUT = PRESETS + r"\%s.phy" % NAME
DUMP = SCRATCH + r"\dumpS13.txt"

host = clear_host()
H, D = new_harness(host, 200, 200, name="run-the-options-overnight")

panel(D, 40, 40,
      "RUN THE OPTIONS OVERNIGHT\r\n"
      "\r\n"
      "Twelve options, four minutes of thinking each, and it is already six o'clock. Write the "
      "twelve down, go home, and read the comparison in the morning.\r\n"
      "\r\n"
      "SET IT UP\r\n"
      "1. On your own canvas, make a list of the options - one line of text each. \"6m grid, flat "
      "roof\", \"7.5m grid, monopitch\", and so on. A Panel with one per line is enough.\r\n"
      "2. Wire that list into OPTIONS on the left edge of the Harness node.\r\n"
      "3. Edit the brief in stage 2 so it says what you actually want each option judged on.\r\n"
      "4. Press the button, or arm the Timer if you want it to start later.\r\n"
      "5. Go home.\r\n"
      "\r\n"
      "IT DOES THEM ONE AT A TIME AND THAT IS DELIBERATE. There is one conversation here, so twelve "
      "at once would interleave into one unreadable thread. For Each hands over option 1, waits for "
      "the whole round to finish, then hands over option 2.\r\n"
      "\r\n"
      "WHAT COMES BACK is a written comparison in the chat, one entry per option, and whatever "
      "geometry it built still sitting in Rhino on its own layers.\r\n"
      "\r\n"
      "READ THE WARNING IN STAGE 5 BEFORE YOU LEAVE THE BUILDING. This is the preset most likely to "
      "spend money while nobody is watching, and every bound on it is set on this canvas.",
      w=940, h=470, colour=INTRO_GREEN)

SPINE = 800

# --------------------------------------------------------------------------- the list

title(D, 60, 560, "1 - THE LIST OF OPTIONS", w=340, h=44)
opts = place(D, "Harness In", 140, 620, nick="Harness In")
opts.Params.Output[0].NickName = "options"

go = boolean(D, 40, 700, False, nick="press to start", toggle=False)
start = place(D, "Construct Signal", 220, 700, nick="Construct Signal")
wire(start, "Trigger", go, 0)
timer = place(D, "Timer", 220, 780, nick="Timer")
t_int = slider(D, 40, 840, 3600, 60, 86400, nick="seconds between studies")
wire(timer, "Interval", t_int, 0)

foreach = place(D, "For Each", 540, 700, nick="For Each")
wire(foreach, "Items", opts, 0)
wire(foreach, "Start", start, "Signal")

idx = panel(D, 720, 640, "which option it is on", w=260, h=70, colour=OUTPUT_GREY)
idx.AddSource(pin(foreach, "out", "Index"))
cur = panel(D, 720, 730, "the option it is doing now", w=260, h=90, colour=OUTPUT_GREY)
cur.AddSource(pin(foreach, "out", "Item"))

panel(D, 60, 900,
      "FOR EACH IS THE COMPONENT THIS PRESET IS BUILT AROUND. Wire a list into Items and a signal "
      "into Start, and it hands out ONE item, waits, hands out the next.\r\n"
      "\r\n"
      "IT WAITS BECAUSE YOU WIRE IT TO WAIT. Look at the wire coming back into NEXT from the far "
      "right of this canvas: that is the END of the per-item work. Nothing advances until the round "
      "for the current option has finished. Wire Next from anywhere earlier and you will get twelve "
      "half-finished options tangled together.\r\n"
      "\r\n"
      "STRICTLY SEQUENTIAL, and it is not a limitation to be worked around - there is ONE "
      "Conversation Log downstream, so parallel options would interleave into one thread and none "
      "of the answers would be trustworthy.\r\n"
      "\r\n"
      "INDEX IS WHAT CARRIES ANYTHING THAT IS NOT TEXT. If your options are geometry rather than "
      "words, put a List Item on your own canvas driven by Index and pick the matching one.\r\n"
      "\r\n"
      "THE LIST IS SNAPSHOTTED AT START, so a pipeline that edits your canvas cannot extend the "
      "very list it is walking. An empty list is DONE, not broken.\r\n"
      "\r\n"
      "THE TIMER is if you want it to begin at three in the morning rather than when you press the "
      "button. It never fires on arming - arming is a switch, not a run button.",
      w=340, h=580)

# --------------------------------------------------------------------------- the loop

L = core_loop(D, 1060, SPINE, model_name="Codex Model",
              instruction="You run comparative option studies.\r\n"
                          "\r\n"
                          "You are given ONE option at a time. Build it, look at it, and write it "
                          "up. Do not compare it with the others while you are on it - you will be "
                          "asked for the comparison at the end.\r\n"
                          "\r\n"
                          "For each option: build it with run_rhino_script on its OWN layer named "
                          "OPTION-<index>. Then take_snapshot and look at what you made. Then write "
                          "up, in this order: the option as given, what you built, the numbers "
                          "(footprint area, gross area, height, count of whatever matters), and "
                          "what is good and bad about it in two sentences each.\r\n"
                          "\r\n"
                          "USE THE SAME MEASUREMENTS AND THE SAME HEADINGS EVERY TIME. A comparison "
                          "is worthless if option 3 was judged on different criteria from option 1. "
                          "If something cannot be measured for this option, say NOT-MEASURED rather "
                          "than leaving it out.\r\n"
                          "\r\n"
                          "Keep each write-up under two hundred words. Somebody is going to read "
                          "twelve of these.")
wire(L["log"], "Prompt Signal", foreach, "Item Signal")
wire(L["log"], "Prompt Signal", timer, "Signal")

rhinog = place(D, "Rhino Document", 1140, 660, nick="Rhino Document")
units = place(D, "Document Units Grounding", 1300, 660, nick="Document Units")
toolsp = place(D, "Tools Present", 1460, 660, nick="Tools Present")
for g in (rhinog, units, toolsp):
    wire(L["log"], "Grounding", g, 0)
expc = place(D, "Export Conversation", 1140, 710, nick="Export Conversation")
tctl = place(D, "Trigger Control", 1300, 710, nick="Trigger Control")
strace = place(D, "Signal Trace", 1460, 710, nick="Signal Trace")
for t in (expc, tctl, strace):
    wire(L["log"], "Human Tools", t, "Human Tool")

panel(D, 1060, 1080,
      "THE BRIEF IS THE EXPERIMENT'S CONTROL. Every option must be judged the same way or the "
      "comparison means nothing - which is why the instruction insists on the same measurements and "
      "the same headings each time, and on NOT-MEASURED rather than a quiet omission.\r\n"
      "\r\n"
      "It also tells the model NOT to compare while it is on an option. Left to itself it will "
      "start referring to \"the previous scheme\", and by option 8 the write-ups are a running "
      "argument rather than twelve comparable entries.\r\n"
      "\r\n"
      "TWO HUNDRED WORDS EACH. You are going to read twelve of them at nine in the morning.\r\n"
      "\r\n"
      "SIGNAL TRACE in the chat header is how you find out what happened overnight - every signal "
      "in order, so you can see where it stopped if it did.",
      w=380, h=440)

# --------------------------------------------------------------------------- tools

title(D, 2360, 560, "3 - BUILD IT AND LOOK AT IT", w=360, h=44)
router = place(D, "Router", 2440, 800, nick="Router")
wire(router, "Tool Calls", L["call"], "Tool Calls")
router_slots(router, 2)
drive = place(D, "Drive Rhino", 2780, 740, nick="Drive Rhino")
look = place(D, "Take Snapshot", 2780, 840, nick="Take Snapshot")
state = place(D, "Pipeline State", 2780, 940, nick="Pipeline State")
st_instr = input_panel(D, 2520, 970,
                       "Store each option's key numbers under a key named after its index, so the "
                       "final comparison can be assembled from them.",
                       w=220, h=130, nick="what to keep per option")
wire(state, "Instruction", st_instr, 0)
wire(drive, "Signal", router, 0)
wire(look, "Signal", router, 1)
wire(state, "Signal", router, 2)

script = panel(D, 3060, 700, "the script for this option", w=320, h=180, colour=OUTPUT_GREY)
script.AddSource(pin(drive, "out", "Last Script"))
keys = panel(D, 3060, 900, "what it has recorded so far", w=320, h=140, colour=OUTPUT_GREY)
keys.AddSource(pin(state, "out", "Keys"))

panel(D, 2360, 1560,
      "DRIVE RHINO builds each option on its own layer, so all twelve are still in the file in the "
      "morning and you can switch between them. That is worth more than any amount of description.\r\n"
      "\r\n"
      "TAKE SNAPSHOT is what stops twelve confident write-ups of things that were never built. The "
      "model looks at its own result before judging it.\r\n"
      "\r\n"
      "PIPELINE STATE is how the final comparison gets assembled. Each option's numbers are stored "
      "under its own key as it goes, so the last round can put them side by side without re-reading "
      "the whole conversation - which by then is long and expensive.\r\n"
      "\r\n"
      "That is the difference between state and memory, incidentally: this is structured, on a "
      "wire, and gone at the end of the session. Memory is prose the model keeps for next time.",
      w=360, h=460)

# --------------------------------------------------------------------------- the return that advances

title(D, 3460, 560, "4 - AND ON TO THE NEXT ONE", w=340, h=44)
decon = place(D, "Deconstruct Signal", 3540, 740, nick="Deconstruct Signal")
wire(decon, "Signal", L["call"], "Success Signal")
out = place(D, "Harness Out", 3540, 840, nick="Harness Out")
out.Params.Input[0].NickName = "the write-ups"
wire(out, "Data", decon, "Payload")

fb_next, co_next = back(D, L["call"], "Success Signal", foreach, "Next",
                        3640, 1560, 600, 1560, nick="option finished - next one")

panel(D, 3460, 930,
      "THIS IS THE WIRE THAT MAKES IT A LOOP. The LLM Call's Success Signal goes all the way back "
      "to For Each's NEXT input, so the next option is only handed out once the current round has "
      "genuinely finished.\r\n"
      "\r\n"
      "It runs backwards across the canvas, which is why it is a Feedback and a Collector rather "
      "than a wire - Grasshopper will not let a wire form a loop, and this is a loop on purpose.\r\n"
      "\r\n"
      "MOVE THIS WIRE IF YOUR PER-OPTION WORK IS LONGER. Whatever the LAST thing in a round is, "
      "that is what Next should come from.\r\n"
      "\r\n"
      "THE WRITE-UPS come out onto your canvas as text. Drop the grip on a Panel and you have the "
      "whole study in the file, next to the geometry it describes.",
      w=340, h=420)

# --------------------------------------------------------------------------- bounds

title(D, 1000, 1700, "5 - READ THIS BEFORE YOU GO HOME", w=420, h=44)
budget = place(D, "Budget Guard", 1120, 1840, nick="Budget Guard")
b_calls = slider(D, 900, 1920, 120, 1, 600, nick="max calls for the whole night")
wire(budget, "Max Calls", b_calls, 0)
b_ext = slider(D, 900, 1970, 0, 0, 200, nick="extension - keep at zero overnight")
wire(budget, "Extension", b_ext, 0)
wire(budget, "Signal", L["log"], "Signal")
wire(L["call"], "Signal", budget, "Success Signal")
spent = panel(D, 1060, 2010, "spent so far", w=300, h=80, colour=OUTPUT_GREY)
spent.AddSource(pin(budget, "out", "Spent"))

limiter = place(D, "Signal Limiter", 1560, 1840, nick="Signal Limiter")
lim_n = slider(D, 1340, 1920, 40, 1, 400, nick="rounds allowed")
wire(limiter, "Count", lim_n, 0)
wire(limiter, "Signal", L["call"], "Fail Signal")

panel(D, 1000, 2120,
      "THREE THINGS BOUND THIS AND YOU SHOULD SET ALL THREE.\r\n"
      "\r\n"
      "BUDGET GUARD caps the whole night. Work out roughly what one option costs by running ONE by "
      "hand first, multiply, add half, and put that in. Set the EXTENSION TO ZERO overnight: the "
      "extension asks permission as a card in the chat, and at two in the morning nobody answers, "
      "so it fails closed and waits - which is a stopped study rather than a runaway one, but you "
      "would rather it simply stopped.\r\n"
      "\r\n"
      "SIGNAL LIMITER catches a failure that repeats. A round that fails and retries forever is the "
      "classic overnight bill.\r\n"
      "\r\n"
      "THE LENGTH OF YOUR LIST is the third one and the easiest to forget. Twelve options is a "
      "night; a hundred is a weekend and a bill you will have to explain.\r\n"
      "\r\n"
      "AND RUN ONE OPTION BY HAND FIRST. Every hour of this preset is an hour of whatever your "
      "brief says, right or wrong. A brief that produces a useless write-up produces twelve useless "
      "write-ups just as reliably.",
      w=420, h=500)

fb_res, co_res = back(D, drive, "Result", router, "Results",
                      2980, 1400, 2160, 1400, nick="tool results")
for t in (look, state):
    wire(fb_res, "Signal", t, "Result")
back(D, router, "Feedback", L["log"], "LLM Tool Signal",
     2140, 1700, 1000, 1240, nick="tool round to the log")

panel(D, 60, 1560,
      "THINGS TO TRY\r\n"
      "\r\n"
      "1. TWO options first, with the Timer off, and watch it work. You want to see For Each hand "
      "over the second one only after the first has finished.\r\n"
      "\r\n"
      "2. Read the two write-ups side by side. If they are not comparable, fix the brief - that is "
      "the only thing that matters here and it is cheap to fix now.\r\n"
      "\r\n"
      "3. Then twelve, and go home.\r\n"
      "\r\n"
      "4. In the morning: read the Signal Trace first. It tells you whether it finished, stopped, "
      "or is still going.\r\n"
      "\r\n"
      "5. The geometry is still there, one layer per option. That is usually the real output.\r\n"
      "\r\n"
      "6. Ask for the comparison as a last question - \"put all twelve side by side and rank them "
      "for me\" - now that it has the numbers in state.",
      w=440, h=460)

commit_build(D, "build scenario S13")
solve(D)
solve(D)
write_dump(D, DUMP)
say("router outputs:", [q.NickName for q in router.Params.Output])
say("For Each Next sources:", pin(foreach, "in", "Next").SourceCount)
bad = sweep(D, NAME)
save_phy(H, OUT,
         description="Give it a list of options, go home, read the comparison in the morning. For "
                     "Each walks the list strictly one at a time; each option is built on its own "
                     "layer, looked at, and written up to the same headings.",
         chat_text="Run the Options Overnight\r\n\r\n"
                   "SET UP FIRST: wire a list of options - one line of text each - into OPTIONS on "
                   "the left edge of the Harness node. Then edit the brief on the canvas so it "
                   "says what you want each one judged on.\r\n\r\n"
                   "RUN TWO FIRST, with the Timer off, and read them side by side. If they are not "
                   "comparable, fix the brief - a bad brief produces twelve bad write-ups just as "
                   "reliably as one.\r\n\r\n"
                   "Then set the Budget Guard, set the extension to ZERO, press start and go home.\r\n\r\n"
                   "In the morning: Signal Trace tells you whether it finished. The geometry is "
                   "still in Rhino, one layer per option.")
say("PROBLEMS:", bad)
