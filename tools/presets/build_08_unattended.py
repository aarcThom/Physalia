# Copyright (c) 2026 Physalia Contributors
# SPDX-License-Identifier: AGPL-3.0-or-later
"""
Preset 08 - "Running Without You".

Every preset so far waits for a person to type. This one starts rounds on its own - a clock, a
folder, the Rhino document, data on your canvas, or a recording of you modelling by hand - and it
carries the three things that bound what an unattended pipeline can do to your bill.
"""

exec(open(r"C:\Users\rober\repos\Physalia\tools\presets\phybuild.py").read())

NAME = "08 - Running Without You"
OUT = r"C:\Users\rober\repos\Physalia\wip_presets\%s.phy" % NAME
DUMP = r"C:\Users\rober\AppData\Local\Temp\claude\dump08.txt"

host = clear_host()
H, D = new_harness(host, 200, 200, name="running-without-you")

# --------------------------------------------------------------------------- the intro

panel(D, 40, 40,
      "RUNNING WITHOUT YOU\r\n"
      "\r\n"
      "Every preset so far has been downstream of somebody sitting there typing. TRIGGERS change "
      "that: they are signal SOURCES, so a round can start because a clock went off, a file "
      "appeared, the Rhino document changed, or data on your canvas moved.\r\n"
      "\r\n"
      "That is genuinely useful and it is also the point at which Physalia can spend money while "
      "you are asleep. So read stages 2, 3 and 6 as carefully as stage 1: a THROTTLE to bound the "
      "rate, a LIMITER to bound one loop, and a BUDGET GUARD to bound the whole session. A "
      "pipeline with a trigger armed and no Budget Guard has no upper bound on its bill.\r\n"
      "\r\n"
      "Two things that are true of every trigger. ARMING IS NEVER SAVED - a file always opens with "
      "everything switched off, so a pipeline you send a colleague cannot start spending their "
      "money the moment they open it. And BURSTS ARE COALESCED: one copied folder is one "
      "file-system event per file, so events accumulate and a settle timer restarts on each, "
      "making a burst into one signal.",
      w=960, h=320, colour=INTRO_GREEN)

# --------------------------------------------------------------------------- 1 the sources

title(D, 60, 400, "1 - WHAT CAN START A ROUND", w=400, h=44)

chat = place(D, "Chat", 620, 480, nick="Chat")

# --- timer
timer = place(D, "Timer", 620, 590, nick="Timer")
t_int = slider(D, 200, 570, 300, 5, 3600, nick="every N seconds")
wire(timer, "Interval", t_int, 0)
t_pay = input_panel(D, 200, 615, "Check whether anything needs doing.", w=250, h=50,
                    nick="what the timer says")
wire(timer, "Payload", t_pay, 0)

# --- folder watcher
watcher = place(D, "Folder Watcher", 620, 740, nick="Folder Watcher")
w_filter = input_panel(D, 200, 700, "*.las;*.csv", w=250, h=40, nick="which files to watch")
wire(watcher, "Filter", w_filter, 0)
w_sub = boolean(D, 230, 755, True, nick="look in subfolders", toggle=True)
wire(watcher, "Subfolders", w_sub, 0)
w_settle = slider(D, 200, 790, 3, 1, 60, nick="settle for N seconds")
wire(watcher, "Settle", w_settle, 0)
w_files = panel(D, 860, 720, "the files that changed appear here", w=280, h=110,
                colour=OUTPUT_GREY)
w_files.AddSource(pin(watcher, "out", "Changed Files"))

# --- rhino changed
rhtrig = place(D, "Rhino Changed", 620, 920, nick="Rhino Changed")
r_geo = boolean(D, 240, 875, False, nick="watch geometry", toggle=True)
r_sel = boolean(D, 240, 910, True, nick="watch selection", toggle=True)
r_lay = boolean(D, 240, 945, False, nick="watch layers", toggle=True)
r_new = boolean(D, 240, 980, False, nick="watch new files", toggle=True)
wire(rhtrig, "Geometry", r_geo, 0)
wire(rhtrig, "Selection", r_sel, 0)
wire(rhtrig, "Layers", r_lay, 0)
wire(rhtrig, "New File", r_new, 0)

# --- data changed
datatrig = place(D, "Data Changed", 620, 1090, nick="Data Changed")
d_in = place(D, "Harness In", 240, 1060, nick="Harness In")
d_in.Params.Output[0].NickName = "watch this"
wire(datatrig, "Data", d_in, 0)
d_pay = input_panel(D, 200, 1105, "The numbers on my canvas changed.", w=250, h=50,
                    nick="what it says")
wire(datatrig, "Payload", d_pay, 0)

# --- watch modelling
watchmod = place(D, "Watch Modelling", 620, 1250, nick="Watch Modelling")
m_cap = boolean(D, 230, 1215, True, nick="capture the command line", toggle=True)
wire(watchmod, "Capture Command Line", m_cap, 0)
m_instr = input_panel(D, 200, 1255, "Describe the procedure I just demonstrated, then wait.",
                      w=250, h=60, nick="what to do with it")
wire(watchmod, "Instruction", m_instr, 0)
m_steps = panel(D, 860, 1230, "the steps it recorded appear here", w=280, h=120,
                colour=OUTPUT_GREY)
m_steps.AddSource(pin(watchmod, "out", "Steps"))

panel(D, 60, 1420,
      "SIX SOURCES. The Chat is one of them - you can still type - and the other five need arming "
      "from their own right-click menus before they do anything.\r\n"
      "\r\n"
      "TIMER is a clock, and it is the one that makes a truly unattended pipeline possible: it "
      "needs nothing to happen anywhere. It never fires on being armed - arming is a switch, not a "
      "run button - and it will not go below one second.\r\n"
      "\r\n"
      "FOLDER WATCHER fires when files appear, change or vanish in this pipeline's project folder. "
      "Its CHANGED FILES output is the point of it: a dropped survey file is to be imported, not "
      "read about, and that output puts the absolute paths on a wire. Removed files are kept off "
      "it. It also ignores files THIS pipeline downloaded, or the download tool and the watcher "
      "would chase each other round a loop no round limit ever catches.\r\n"
      "\r\n"
      "RHINO CHANGED wakes on the Rhino document moving. WATCH SELECTION is the one to reach for: "
      "\"move these\" only ever resolves if a round starts when the selection changes. Geometry is "
      "left off here because it is much noisier than it sounds.\r\n"
      "\r\n"
      "DATA CHANGED fires when data on your canvas changes - it is the active counterpart of "
      "Harness In. READ THE WARNING IN STAGE 2 BEFORE ARMING IT.\r\n"
      "\r\n"
      "WATCH MODELLING is the odd one out and the most interesting. Tick Recording, model "
      "something by hand in Rhino, untick it - and the whole procedure arrives as one signal: the "
      "commands you ran, their parameters, what was selected when. It fires on being switched OFF, "
      "not per command, because no pause length tells thinking apart from finishing. An Undo pops "
      "the last step, because a recording containing a mistake and its undo teaches nothing.",
      w=760, h=700)

# --------------------------------------------------------------------------- 2 the throttle

title(D, 1200, 400, "2 - BOUND THE RATE", w=320, h=44)
throttle = place(D, "Signal Throttle", 1380, 560, nick="Signal Throttle")
for t in (timer, watcher, rhtrig, datatrig, watchmod):
    wire(throttle, "Signal", t, "Signal")
th_int = slider(D, 1180, 620, 30, 1, 600, nick="one round every N seconds")
wire(throttle, "Interval", th_int, 0)
panel(D, 1200, 700,
      "All five triggers arrive at ONE Signal Throttle, which lets one signal through per interval "
      "and keeps the newest when it is holding something.\r\n"
      "\r\n"
      "This is where a throttle belongs: at the point where several sources meet, so the rate is "
      "stated once in one place instead of being argued about at each source.\r\n"
      "\r\n"
      "THE WARNING ABOUT DATA CHANGED. If what it is watching is downstream of anything this "
      "pipeline writes, every round starts the next one, forever. Nothing can detect that, because "
      "the loop runs out through your canvas and back - it is not visible as a cycle from in here. "
      "The Signal Limiter in stage 3 bounds it and the Budget Guard in stage 6 is the backstop, "
      "and with a trigger armed you want both.",
      w=320, h=440)

# --------------------------------------------------------------------------- 3 the limiter

title(D, 1620, 400, "3 - BOUND ONE LOOP", w=320, h=44)
limiter = place(D, "Signal Limiter", 1800, 560, nick="Signal Limiter")
wire(limiter, "Signal", throttle, "Signal")
lim_count = slider(D, 1600, 640, 20, 1, 200, nick="at most N rounds")
wire(limiter, "Count", lim_count, 0)
lim_reset = boolean(D, 1640, 680, False, nick="reset the count", toggle=True)
wire(limiter, "Reset", lim_reset, 0)
over = panel(D, 2060, 640, "rounds that were refused land here", w=280, h=110,
             colour=ERROR_PINK)
over_dec = place(D, "Deconstruct Signal", 1860, 700, nick="Deconstruct Signal")
wire(over_dec, "Signal", limiter, "Over Limit")
over.AddSource(pin(over_dec, "out", "Payload"))
panel(D, 1620, 800,
      "SIGNAL LIMITER counts total rounds and stops. WITHIN LIMIT carries them on; OVER LIMIT is "
      "where they go once the count is used up, so a loop can notice and say something rather than "
      "just stopping.\r\n"
      "\r\n"
      "Flip the toggle to start counting again.\r\n"
      "\r\n"
      "This bounds ONE LOOP. It does not bound a session: a pipeline that has been triggered "
      "fifty times has had its limit reset fifty times over. That is what stage 6 is for.",
      w=320, h=340)

# --------------------------------------------------------------------------- 4 the prompt side

title(D, 2040, 400, "4 - WHAT IT IS TOLD", w=320, h=44)
sysp = place(D, "System Prompt", 2240, 480, nick="System Prompt")
blank_input(D, sysp, "Preamble", 2040, 455, label="no preamble file")
blank_input(D, sysp, "Schema", 2040, 500, label="no schema file")
extra = input_panel(D, 2000, 545,
                    "You are minding a Grasshopper model while nobody is watching. Be brief. If "
                    "nothing needs doing, say so in one line and stop.",
                    w=250, h=100, nick="my own instructions")
wire(sysp, "Additional Prompt", extra, 0)
proj = place(D, "Project Folder", 2240, 900, nick="Project Folder")
tctl = place(D, "Trigger Control", 2240, 960, nick="Trigger Control")
folder = panel(D, 2020, 1010, "the folder the watcher is watching", w=300, h=80,
               colour=OUTPUT_GREY)
folder.AddSource(pin(proj, "out", "Folder"))
panel(D, 2040, 1120,
      "\"Be brief, and if nothing needs doing say so\" earns its place in an unattended prompt. A "
      "model woken by a timer every five minutes with no instruction to stop will find something "
      "to say every time, and each of those is a paid round.\r\n"
      "\r\n"
      "TRIGGER CONTROL is the human tool that makes this manageable. It puts the list of triggers "
      "in the chat window - one switch each, plus arm-all and switch-all-off - because a node's "
      "own right-click menu is fine for one trigger and useless for finding the three that are "
      "armed somewhere inside a harness. The list is read live off the canvas every tick, since "
      "arming changes no data and runs no solution.\r\n"
      "\r\n"
      "One thing to know about it: switching ONE trigger off does what that node's own menu does, "
      "so switching Watch Modelling off SENDS the recording. SWITCH ALL OFF is the kill switch and "
      "discards. The page says which is which.",
      w=320, h=420)

# --------------------------------------------------------------------------- 5 the loop

title(D, 2460, 400, "5 - THE RUNNING CONVERSATION", w=320, h=44)
log = place(D, "Conversation Log", 2640, 560, nick="Conversation Log")
wire(log, "System Prompt", sysp, "System Prompt")
wire(log, "Prompt Signal", chat, "Prompt Signal")
wire(log, "Prompt Signal", limiter, "Within Limit")
wire(log, "Grounding", proj, 0)
wire(log, "Human Tools", tctl, "Human Tool")
panel(D, 2460, 700,
      "Note what arrives on PROMPT SIGNAL: the Chat AND the trigger chain, both on the same input. "
      "It takes a list.\r\n"
      "\r\n"
      "That is all a trigger is, as far as the Conversation Log is concerned - something that "
      "produced a user turn. There is no separate machinery for automated rounds, which is why "
      "everything you learned in presets 01 to 07 still applies here unchanged.",
      w=320, h=280)

# --------------------------------------------------------------------------- 6 the budget

title(D, 2880, 400, "6 - BOUND THE SESSION", w=320, h=44)
budget = place(D, "Budget Guard", 3060, 560, nick="Budget Guard")
wire(budget, "Signal", log, "Signal")
b_tok = slider(D, 2840, 640, 300000, 10000, 2000000, nick="max tokens")
b_calls = slider(D, 2840, 675, 60, 1, 500, nick="max calls")
b_ext = slider(D, 2840, 710, 50, 0, 100, nick="extension %")
wire(budget, "Max Tokens", b_tok, 0)
wire(budget, "Max Calls", b_calls, 0)
wire(budget, "Extension", b_ext, 0)
spent = panel(D, 3320, 640, "what has been spent so far", w=280, h=110, colour=OUTPUT_GREY)
spent.AddSource(pin(budget, "out", "Spent"))
refused = panel(D, 3320, 780, "refusals land here", w=280, h=100, colour=ERROR_PINK)
ref_dec = place(D, "Deconstruct Signal", 3120, 840, nick="Deconstruct Signal")
wire(ref_dec, "Signal", budget, "Fail Signal")
refused.AddSource(pin(ref_dec, "out", "Payload"))
panel(D, 2880, 920,
      "BUDGET GUARD sits INLINE, between the Conversation Log and the LLM Call, and it is the last "
      "thing between an armed trigger and an unbounded bill.\r\n"
      "\r\n"
      "It reads what this session has already spent - the LLM Call records it, with no wire between "
      "them - and refuses a call that would go past the caps. The refusal comes out on FAIL SIGNAL "
      "with a reason on it, so a loop can react rather than just stalling.\r\n"
      "\r\n"
      "It is checked BEFORE a call, against what is already spent, so a pipeline can overrun by up "
      "to one call. The cost of a call is not knowable until it is made, and a runaway loop is "
      "stopped just as dead one call late.\r\n"
      "\r\n"
      "MAX CALLS is the cap that always works. A call that reports no token figure at all still "
      "counts as a call - which is exactly the case with the command-line models, since they report "
      "nothing useful about the prompt they were sent. On Claude Code or Codex, a token cap is "
      "meaningless and Max Calls is the only real bound.\r\n"
      "\r\n"
      "EXTENSION is how much more it may offer you when it runs out - it asks in the chat window, "
      "as a card, and it fails closed if nobody answers. Set it to 0 and the budget is final and "
      "no card appears.",
      w=320, h=520)

# --------------------------------------------------------------------------- 7 the call

title(D, 3300, 400, "7 - ASKING THE MODEL", w=300, h=44)
model = place(D, "Claude Code Model", 3440, 480, nick="Claude Code Model")
call = place(D, "LLM Call", 3660, 560, nick="LLM Call")
wire(call, "Model", model, "Model")
wire(call, "Signal", budget, "Success Signal")
cancel = boolean(D, 3530, 605, False, nick="stop", toggle=False)
wire(call, "Cancel", cancel, 0)
reply = panel(D, 3900, 480, "the reply lands here", w=280, h=150, colour=OUTPUT_GREY)
rep_dec = place(D, "Deconstruct Signal", 3700, 700, nick="Deconstruct Signal")
wire(rep_dec, "Signal", call, "Success Signal")
reply.AddSource(pin(rep_dec, "out", "Payload"))
panel(D, 3660, 780,
      "CLAUDE CODE, keyless, nothing to set up. No tools on this preset - see preset 03 for those, "
      "and note that adding a Router means changing the model.\r\n"
      "\r\n"
      "If you want the Watch Modelling recording to be REPEATED rather than just described, that is "
      "the piece to add: Drive Rhino plus a tool-capable model, and the recording arrives as an "
      "ordinary user turn the model can act on.",
      w=280, h=260)

# --------------------------------------------------------------------------- 8 the way back

back(D, call, "Success Signal", log, "Response Signal",
     3780, 1200, 2400, 1200, nick="reply back to the log")
panel(D, 2560, 1130,
      "THE WAY BACK, as always.\r\n"
      "\r\n"
      "One last thing worth knowing about triggers, because it is invisible: Grasshopper drops "
      "scheduled work on a document it thinks is switched off, and a harness's inner document only "
      "gets switched back on when its proxy solves. An autonomous trigger fires when nothing has "
      "solved for hours - so every wake-up re-enables the harness first. Without that a Timer "
      "would fire into a void, silently, and only inside a harness.",
      w=520, h=280, colour=WARN_ORANGE)

# --------------------------------------------------------------------------- 9 what to try

panel(D, 4260, 400,
      "THINGS TO TRY - AND HOW TO STOP IT\r\n"
      "\r\n"
      "STOPPING FIRST, because you will want it. Open the chat window: TRIGGER CONTROL lists every "
      "trigger with a switch, and has a switch-all-off. There is also a Disarm button on the "
      "harness panel that appears while you are inside a harness. Nothing is armed when a file "
      "opens.\r\n"
      "\r\n"
      "1. Right-click the TIMER and arm it, with the interval at 30 seconds or so and the Budget "
      "Guard's Max Calls at 3. Watch it run out and refuse, and read the refusal panel.\r\n"
      "\r\n"
      "2. Arm RHINO CHANGED with Watch Selection on. Select something in Rhino. A round starts.\r\n"
      "\r\n"
      "3. Arm WATCH MODELLING (its menu says \"Recording\"), draw a couple of lines and offset "
      "them by hand, then switch it off from the chat window's trigger list. Read the Steps panel - "
      "commands, parameters, and what was selected for each.\r\n"
      "\r\n"
      "4. Drop a CSV into the project folder named in stage 4 and watch the Folder Watcher fire "
      "once for it, with the path on its Changed Files output.\r\n"
      "\r\n"
      "5. Set the Throttle to 120 seconds and arm two triggers at once. Only one round starts.",
      w=560, h=580)

commit_build(D, "build preset 08")
solve(D)
pick(D, model, "Model", "sonnet")
solve(D)
write_dump(D, DUMP)
say("throttle sources:", pin(throttle, "in", "Signal").SourceCount)
say("prompt signal sources:", pin(log, "in", "Prompt Signal").SourceCount)
bad = sweep(D, NAME)
save_phy(H, OUT,
         description="Triggers: a clock, a folder, the Rhino document, canvas data, and a recording "
                     "of you modelling by hand - so a round can start with nobody there. Plus the "
                     "three bounds that keep that affordable: throttle, limiter, budget guard.",
         chat_text="Running Without You\r\n\r\n"
                   "This pipeline can start work on its own: a Timer, a Folder Watcher, Rhino "
                   "Changed, Data Changed, and Watch Modelling, which records what you do by hand "
                   "and hands the procedure over.\r\n\r\n"
                   "NOTHING IS ARMED YET. Arming is never saved, so a file always opens switched "
                   "off. Use the trigger list on this window to switch things on - and to switch "
                   "them all off again.\r\n\r\n"
                   "Set the Budget Guard's Max Calls low while you experiment. A pipeline with a "
                   "trigger armed and no budget has no upper bound on what it can spend.")
say("PROBLEMS:", bad)
