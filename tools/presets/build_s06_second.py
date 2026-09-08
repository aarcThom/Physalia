# Copyright (c) 2026 Physalia Contributors
# SPDX-License-Identifier: AGPL-3.0-or-later
"""
Scenario S06 - "Get a Second Opinion".

Two models from two different vendors in one pipeline: one does the work, the other criticises it,
and nothing reaches you until the critic is satisfied. Two conversations, one harness.
"""

# The ONE machine-specific line in this file. Set ROOT before exec'ing this script to build
# from a checkout somewhere else:  ROOT = r"D:\\code\\Physalia"
try:
    ROOT
except NameError:
    ROOT = r"C:\Users\rober\repos\Physalia"
exec(open(ROOT + r"\tools\presets\phybuild.py").read())

NAME = "S06 - Get a Second Opinion"
OUT = PRESETS + r"\%s.phy" % NAME
DUMP = SCRATCH + r"\dumpS06.txt"

host = clear_host()
H, D = new_harness(host, 200, 200, name="get-a-second-opinion")

panel(D, 40, 40,
      "GET A SECOND OPINION\r\n"
      "\r\n"
      "Two models from two different vendors, in one pipeline. One does the work; the other reads "
      "it and says what is wrong with it. Nothing reaches you until the second one is satisfied.\r\n"
      "\r\n"
      "WHY BOTHER. A model is a poor proofreader of its own writing - it agrees with itself, "
      "because the same reasoning that produced the answer is the reasoning judging it. A model "
      "from a different vendor was trained differently and does not share the blind spot. In "
      "practice the critic catches the confidently-stated wrong number, the requirement that was "
      "quietly dropped, and the answer that is fine except it answered a different question.\r\n"
      "\r\n"
      "WHAT IS UNUSUAL HERE: THERE ARE TWO CONVERSATION LOGS. A Physalia pipeline normally has "
      "one, and everything shares it. This has two independent ones - the WRITER's and the "
      "CRITIC's - which is what keeps the critic honest: it never sees the writer's reasoning, "
      "only the finished answer. That is the same reason you send a drawing to a checker rather "
      "than sitting them next to you while you draw it.\r\n"
      "\r\n"
      "USE IT FOR: anything with a number in it you would be embarrassed to get wrong. Sizing, "
      "quantities, a specification summary, an approach to a problem.\r\n"
      "\r\n"
      "DO NOT USE IT FOR: chatting, or anything where you are going to check the answer yourself "
      "anyway. It is twice the cost and roughly twice the wait.",
      w=940, h=470, colour=INTRO_GREEN)

SPINE = 700

# --------------------------------------------------------------------------- writer

title(D, 60, 540, "1 - THE WRITER", w=300, h=44)
chat = place(D, "Chat", 130, SPINE, nick="Chat")
sysp = place(D, "System Prompt", 460, SPINE, nick="System Prompt")
blank_input(D, sysp, "Preamble", 280, SPINE - 60, label="no preamble file")
blank_input(D, sysp, "Schema", 280, SPINE - 14, label="no schema file")
w_instr = input_panel(D, 200, SPINE + 80,
                      "You do the work. Answer fully and show your reasoning and any numbers you "
                      "used.\r\n"
                      "\r\n"
                      "A reviewer will read your answer and may send it back with objections. When "
                      "that happens, take each one seriously: either fix it or say plainly why you "
                      "disagree. Do not simply restate what you said the first time.",
                      w=250, h=190, nick="what the writer is told")
wire(sysp, "Additional Prompt", w_instr, 0)

log = place(D, "Conversation Log", 900, SPINE, nick="writer's log")
wire(log, "System Prompt", sysp, "System Prompt")
wire(log, "Prompt Signal", chat, "Prompt Signal")
rhino = place(D, "Rhino Document", 720, 620, nick="Rhino Document")
units = place(D, "Document Units Grounding", 720, 670, nick="Document Units")
for g in (rhino, units):
    wire(log, "Grounding", g, 0)
img = place(D, "Add Image", 720, 880, nick="Add Image")
expc = place(D, "Export Conversation", 720, 930, nick="Export Conversation")
for t in (img, expc):
    wire(log, "Human Tools", t, "Human Tool")

wmodel = place(D, "Claude Code Model", 1300, 620, nick="Claude Code Model")
wcall = place(D, "LLM Call", 1300, SPINE + 40, nick="writer's call")
wire(wcall, "Model", wmodel, "Model")
wire(wcall, "Signal", log, "Signal")
cancel = boolean(D, 1170, SPINE + 80, False, nick="stop", toggle=False)
wire(wcall, "Cancel", cancel, 0)

panel(D, 60, 1000,
      "THIS HALF IS AN ORDINARY PIPELINE. Chat, System Prompt, Conversation Log, Model, LLM Call - "
      "exactly preset 01, and the writer has no idea it is being checked.\r\n"
      "\r\n"
      "The white box is what it is told. Note the last line: a model that is criticised will "
      "otherwise politely restate its answer in different words, which looks like a response and "
      "is not one.\r\n"
      "\r\n"
      "IT IS ALSO THE ONLY HALF WITH A CHAT. You talk to the writer; the critic works behind it.",
      w=380, h=340)

# --------------------------------------------------------------------------- critic

title(D, 1700, 540, "2 - THE CRITIC", w=300, h=44)
csysp = place(D, "System Prompt", 1700, SPINE, nick="critic's prompt")
blank_input(D, csysp, "Preamble", 1520, SPINE - 60, label="no preamble file")
blank_input(D, csysp, "Schema", 1520, SPINE - 14, label="no schema file")
c_instr = input_panel(D, 1440, SPINE + 80,
                      "You are a reviewer. You are shown an answer written by someone else. You "
                      "cannot see how they got there and you should not assume they were "
                      "careful.\r\n"
                      "\r\n"
                      "Check: are the numbers right and do they follow from each other? Was any "
                      "part of the question left unanswered? Is anything asserted that would need "
                      "checking before anyone relied on it?\r\n"
                      "\r\n"
                      "If it is sound, reply with the single word APPROVED and nothing else. "
                      "Otherwise list your objections, shortest first, and do not rewrite the "
                      "answer yourself.",
                      w=250, h=230, nick="what the critic is told")
wire(csysp, "Additional Prompt", c_instr, 0)

clog = place(D, "Conversation Log", 2140, SPINE, nick="critic's log")
wire(clog, "System Prompt", csysp, "System Prompt")
wire(clog, "Prompt Signal", wcall, "Success Signal")

cmodel = place(D, "Codex Model", 2520, 620, nick="Codex Model")
ccall = place(D, "LLM Call", 2520, SPINE + 40, nick="critic's call")
wire(ccall, "Model", cmodel, "Model")
wire(ccall, "Signal", clog, "Signal")
wire(ccall, "Cancel", cancel, 0)
back(D, ccall, "Success Signal", clog, "Response Signal",
     2600, 1900, 2240, 1900, nick="critic hears itself")

panel(D, 1700, 1000,
      "THE JOIN IS ONE WIRE: the writer's Success Signal goes straight into the critic's PROMPT "
      "SIGNAL. A signal carries its text, so the writer's answer simply becomes the critic's "
      "question. Nothing else is needed.\r\n"
      "\r\n"
      "The critic gets its own System Prompt and its own Conversation Log, which means it "
      "accumulates its own history and can say \"you have now made this mistake twice\".\r\n"
      "\r\n"
      "A DIFFERENT VENDOR IS THE POINT. Writer on Claude Code, critic on Codex - both use a "
      "command-line tool you are already signed into, so neither needs an API key. Swap either for "
      "any Model component you like; preset 13 covers what is on offer.\r\n"
      "\r\n"
      "SAME VENDOR, DIFFERENT SIZE also works and is cheaper: a big model writes, a small fast one "
      "checks for the obvious.\r\n"
      "\r\n"
      "THE ONE WORD 'APPROVED' is doing real work - it is what stage 3 matches on. Change that "
      "word and change it in the Signal Switch too.",
      w=380, h=460)

# --------------------------------------------------------------------------- the verdict

title(D, 2900, 540, "3 - WHAT HAPPENS TO THE VERDICT", w=340, h=44)
switch = place(D, "Signal Switch", 2960, SPINE, nick="approved?")
wire(switch, "Signal", ccall, "Success Signal")
pat = input_panel(D, 2740, SPINE + 90, "APPROVED", w=200, h=40, nick="the word to look for")
wire(switch, "Pattern", pat, 0)

limiter = place(D, "Signal Limiter", 2960, 900, nick="Signal Limiter")
lim_n = slider(D, 2740, 950, 3, 1, 10, nick="review rounds allowed")
wire(limiter, "Count", lim_n, 0)
wire(limiter, "Signal", switch, "No Match")

out = place(D, "Harness Out", 3320, SPINE, nick="Harness Out")
out.Params.Input[0].NickName = "approved answer"
decon = place(D, "Deconstruct Signal", 3320, 620, nick="Deconstruct Signal")
wire(decon, "Signal", switch, "Match")
wire(out, "Data", decon, "Payload")

panel(D, 2900, 1000,
      "SIGNAL SWITCH reads the critic's text and sends it one way or the other. APPROVED goes "
      "right, to the canvas; anything else goes down and back to the writer as an objection.\r\n"
      "\r\n"
      "It matches plain text by default; turn REGEX on from its right-click menu if you want "
      "something cleverer. A broken pattern sends everything to No Match rather than pretending to "
      "match - it fails the safe way, which here means the answer goes back for another look "
      "rather than straight to you.\r\n"
      "\r\n"
      "SIGNAL LIMITER caps the argument. Two models can disagree indefinitely and each round costs "
      "twice. Three exchanges is usually enough; if they have not converged by then you should "
      "read both and decide.\r\n"
      "\r\n"
      "HARNESS OUT drops the approved text onto your canvas - drag the grip from the right edge of "
      "the Harness node onto a Panel.",
      w=420, h=420)

fb_obj, co_obj = back(D, limiter, "Within Limit", log, "Feedback Signal",
                      3060, 1560, 1000, 1560, nick="objections to the writer")
back(D, wcall, "Success Signal", log, "Response Signal",
     1420, 1720, 1000, 1720, nick="writer hears itself")

panel(D, 60, 1400,
      "WATCH IT ARGUE. Open the SIGNAL TRACE from the chat header - it lists every signal in "
      "order, so you can see the writer answer, the critic object, the writer revise and the "
      "critic approve. It is the clearest way to understand what a two-model pipeline is actually "
      "doing.\r\n"
      "\r\n"
      "THINGS TO TRY\r\n"
      "\r\n"
      "1. Ask something with arithmetic in it. \"How many 1200x600 panels for a 14.3m by 8.1m "
      "wall, and what's the waste?\" Watch whether the critic checks the sum.\r\n"
      "\r\n"
      "2. Ask something with several parts and see whether the critic notices a dropped one.\r\n"
      "\r\n"
      "3. Loosen the critic - tell it to be encouraging. Notice how much less useful it becomes. "
      "A critic that approves everything is worse than no critic, because you stop reading.\r\n"
      "\r\n"
      "4. Turn it round: make the CRITIC the expensive model and the writer the cheap one. That is "
      "often the better trade, since judging is easier than producing.\r\n"
      "\r\n"
      "5. Add a third opinion by chaining another log and call after the critic. Nothing stops "
      "you, and nothing about the wiring changes.",
      w=560, h=520)

commit_build(D, "build scenario S06")
solve(D)
solve(D)
write_dump(D, DUMP)
say("logs:", len([o for o in D.Objects if o.Name == "Conversation Log"]))
say("calls:", len([o for o in D.Objects if o.Name == "LLM Call"]))
bad = sweep(D, NAME)
save_phy(H, OUT,
         description="Two models from two different vendors: one does the work, the other reads it "
                     "and objects. Nothing reaches you until the critic replies APPROVED. Two "
                     "independent conversations in one harness, which is what keeps the critic "
                     "from sharing the writer's blind spot.",
         chat_text="Get a Second Opinion\r\n\r\n"
                   "You are talking to the WRITER. Everything it says goes to a CRITIC running on "
                   "a different vendor's model, which either objects or replies APPROVED - and "
                   "only an approved answer reaches your canvas.\r\n\r\n"
                   "Try something with arithmetic in it:\r\n"
                   "  \"how many 1200x600 panels for a 14.3m by 8.1m wall, and what's the waste?\"\r\n\r\n"
                   "Open SIGNAL TRACE in the header to watch them argue. Three rounds, then it "
                   "stops and you decide.")
say("PROBLEMS:", bad)
