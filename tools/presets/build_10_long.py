# Copyright (c) 2026 Physalia Contributors
# SPDX-License-Identifier: AGPL-3.0-or-later
"""
Preset 10 - "When the Conversation Gets Long".

A conversation grows every turn and every turn resends all of it, so a long session gets slower and
more expensive with nothing changing. COMPACTION sits inline between the Conversation Log and the
LLM Call and trims what is about to be sent - but only when it needs to, which is what the Token
Threshold is for.
"""

# The ONE machine-specific line in this file. Set ROOT before exec'ing this script to build
# from a checkout somewhere else:  ROOT = r"D:\\code\\Physalia"
try:
    ROOT
except NameError:
    ROOT = r"C:\Users\rober\repos\Physalia"
exec(open(ROOT + r"\tools\presets\phybuild.py").read())

NAME = "10 - When the Conversation Gets Long"
OUT = PRESETS + r"\%s.phy" % NAME
DUMP = SCRATCH + r"\dump10.txt"

host = clear_host()
H, D = new_harness(host, 200, 200, name="when-the-conversation-gets-long")

TITLE_Y = 400
SPINE = 540

# --------------------------------------------------------------------------- the intro

panel(D, 40, 40,
      "WHEN THE CONVERSATION GETS LONG\r\n"
      "\r\n"
      "A conversation grows every turn, and EVERY TURN RESENDS ALL OF IT. That is how these models "
      "work - they have no memory between calls, so the whole history goes over the wire each "
      "time. A long session therefore gets slower and more expensive with nothing having "
      "changed.\r\n"
      "\r\n"
      "COMPACTION is the answer: components that sit inline between the Conversation Log and the "
      "LLM Call and trim what is about to be sent. The Conversation Log itself is untouched - it "
      "keeps everything, and the chat window still shows the whole conversation. Only what goes "
      "to the model is shortened.\r\n"
      "\r\n"
      "Two things worth knowing before you read the wires. Compaction is only worth doing WHEN "
      "NEEDED, which is why there is a Token Threshold in stage 5 sending short conversations "
      "straight past it. And every compaction component FAILS OPEN: if it cannot do its job it "
      "forwards the conversation uncompacted with a warning, rather than stalling your turn. "
      "Losing a saving is not worth losing an answer.",
      w=940, h=310, colour=INTRO_GREEN)

# --------------------------------------------------------------------------- 1 the chat

title(D, 60, TITLE_Y, "1 - WHERE YOU TYPE", w=260, h=44)
chat = place(D, "Chat", 130, SPINE, nick="Chat")
tkc = place(D, "Token Count", 130, 640, nick="Token Count")
panel(D, 60, 700,
      "TOKEN COUNT puts the running total in the bottom-right corner of the chat window. On this "
      "preset it is the thing to watch: it is how you see compaction actually happening.\r\n"
      "\r\n"
      "It is grip-linked to the Token Estimator in stage 4 - counting and displaying are two "
      "different jobs, and an estimator on its own shows nothing.",
      w=240, h=280)

# --------------------------------------------------------------------------- 2 system prompt

title(D, 380, TITLE_Y, "2 - THE SYSTEM PROMPT", w=300, h=44)
sysp = place(D, "System Prompt", 560, SPINE, nick="System Prompt")
blank_input(D, sysp, "Preamble", 380, 500, label="no preamble file")
blank_input(D, sysp, "Schema", 380, 546, label="no schema file")
extra = input_panel(D, 320, 620,
                    "Keep answers short. You are in a long working session and everything you say "
                    "will be sent back to you many times.",
                    w=240, h=110, nick="my own instructions")
wire(sysp, "Additional Prompt", extra, 0)
panel(D, 380, 760,
      "The system prompt is NEVER compacted, and it is resent on every single call. That makes it "
      "the one part of what you send where being brief pays off every turn rather than once.\r\n"
      "\r\n"
      "Grounding rides in the system prompt too, so a very chatty set of grounders is a permanent "
      "cost, not a one-off.",
      w=300, h=250)

# --------------------------------------------------------------------------- 3 conversation log

title(D, 740, TITLE_Y, "3 - THE FULL RECORD", w=300, h=44)
log = place(D, "Conversation Log", 900, SPINE, nick="Conversation Log")
wire(log, "System Prompt", sysp, "System Prompt")
wire(log, "Prompt Signal", chat, "Prompt Signal")
wire(log, "Human Tools", tkc, "Human Tool")
panel(D, 740, 700,
      "The Conversation Log keeps EVERYTHING. Compaction happens downstream of it, on the way to "
      "the model, and never edits the record.\r\n"
      "\r\n"
      "That is what makes compaction safe to experiment with: turn it up too far, get a worse "
      "answer, turn it back down, and nothing has been lost. The chat window is still showing you "
      "the whole conversation either way.\r\n"
      "\r\n"
      "It is also why the transcript saved to disk is the full one.",
      w=300, h=330)

# --------------------------------------------------------------------------- 4 measuring

title(D, 1120, TITLE_Y, "4 - MEASURING IT", w=300, h=44)
tech = place(D, "Tokenization Techniques", 1300, 490, nick="Tokenization Techniques")
est = place(D, "Token Estimator", 1300, 600, nick="Token Estimator")
wire(est, "Tokenization Technique", tech, "Tokenization Technique")
wire(est, "Data", log, "Signal")
tkc.LinkTo(est.InstanceGuid)
count = panel(D, 1120, 660, "tokens about to be sent", w=300, h=80, colour=OUTPUT_GREY)
count.AddSource(pin(est, "out", "Token Count"))
panel(D, 1120, 760,
      "TOKENIZATION TECHNIQUES chooses HOW to count. HEURISTIC needs nothing and is close enough "
      "to decide whether to compact. The ANTHROPIC and GEMINI methods ask the provider for an "
      "exact figure, which costs a round trip and a key.\r\n"
      "\r\n"
      "TOKEN ESTIMATOR measures whatever it is handed - a whole set of Instructions, a "
      "conversation, or plain text. The Conversation Log's signal goes straight in.\r\n"
      "\r\n"
      "The grey panel only fills in on the solve where a turn actually happened, which is the "
      "moment that matters.",
      w=300, h=330)

# --------------------------------------------------------------------------- 5 the threshold

title(D, 1500, TITLE_Y, "5 - ONLY WHEN NEEDED", w=300, h=44)
thresh = place(D, "Token Threshold", 1680, SPINE, nick="Token Threshold")
wire(thresh, "Signal", log, "Signal")
wire(thresh, "Tokenization Technique", tech, "Tokenization Technique")
th_val = slider(D, 1480, 640, 20000, 1000, 200000, nick="over N tokens")
wire(thresh, "Threshold", th_val, 0)
panel(D, 1500, 700,
      "TOKEN THRESHOLD is a fork, not a compactor. A conversation under the limit goes out of "
      "UNDER LIMIT and travels straight to the model untouched. One over the limit goes out of "
      "OVER LIMIT and through the trimming in stages 6 and 7 first.\r\n"
      "\r\n"
      "Follow both wires: they meet again at the LLM Call, which takes a list on its Signal "
      "input.\r\n"
      "\r\n"
      "This shape is worth copying. Compaction always loses something, so paying for it on a "
      "six-turn conversation is a bad trade. Set the threshold to somewhere near where you notice "
      "the session slowing down.",
      w=300, h=350)

# --------------------------------------------------------------------------- 6 pruning

title(D, 1880, TITLE_Y, "6 - DROP WHAT IS CHEAPEST TO LOSE", w=340, h=44)
pruner = place(D, "Content Pruner", 2140, SPINE, nick="Content Pruner")
wire(pruner, "Signal", thresh, "Over Limit")
p_img = boolean(D, 1900, 480, True, nick="drop old images", toggle=True)
p_tool = boolean(D, 1900, 515, False, nick="drop tool exchanges", toggle=True)
p_fb = boolean(D, 1900, 550, False, nick="drop guardrail feedback", toggle=True)
p_tr = slider(D, 1880, 590, 2000, 200, 20000, nick="max tool chars")
p_tx = slider(D, 1880, 625, 8000, 500, 50000, nick="max text chars")
wire(pruner, "Drop Images", p_img, 0)
wire(pruner, "Drop Tool Exchanges", p_tool, 0)
wire(pruner, "Drop Feedback", p_fb, 0)
wire(pruner, "Max Tool Result Chars", p_tr, 0)
wire(pruner, "Max Text Chars", p_tx, 0)
panel(D, 1880, 700,
      "CONTENT PRUNER goes first because it removes what costs the most and is worth the least.\r\n"
      "\r\n"
      "DROP OLD IMAGES is on by default here, and on a pipeline that takes snapshots it is the "
      "single biggest saving available. An image is expensive, and a screenshot from fifteen turns "
      "ago is almost never load-bearing.\r\n"
      "\r\n"
      "DROP TOOL EXCHANGES throws away the calls and their results, keeping the prose. Powerful "
      "and a bit risky - the model can lose track of what it already checked - so it is off "
      "here.\r\n"
      "\r\n"
      "DROP GUARDRAIL FEEDBACK removes complaints and corrections. Off here, because on a "
      "canvas-building pipeline that history is exactly what stops it repeating a mistake.\r\n"
      "\r\n"
      "The two sliders truncate over-long individual items rather than removing them: a tool that "
      "returned ten thousand characters keeps its first two thousand.",
      w=340, h=480)

# --------------------------------------------------------------------------- 7 windowing

title(D, 2300, TITLE_Y, "7 - KEEP THE ENDS, DROP THE MIDDLE", w=340, h=44)
anchored = place(D, "Anchored Window", 2560, SPINE, nick="Anchored Window")
wire(anchored, "Signal", pruner, "Signal")
a_first = slider(D, 2300, 590, 2, 0, 20, nick="keep first N")
a_last = slider(D, 2300, 625, 12, 2, 60, nick="keep last N")
wire(anchored, "Keep First", a_first, 0)
wire(anchored, "Keep Last", a_last, 0)
panel(D, 2300, 700,
      "ANCHORED WINDOW keeps the first few turns AND the last several, and drops the middle.\r\n"
      "\r\n"
      "Keeping the start is the whole idea. The opening turns are where you said what you are "
      "doing and what the constraints are, and a plain sliding window throws exactly that away "
      "first. Two is usually enough: your request and its first answer.\r\n"
      "\r\n"
      "One trap you will not see until it bites: a tool call and its result are a PAIR, and some "
      "providers reject a request outright if a window cuts between them. The compaction here "
      "pairs them in both directions, so a cut lands outside the pair - which is why Keep First = "
      "2 is safe rather than a coin toss.",
      w=340, h=380)

# --------------------------------------------------------------------------- 8 the call

title(D, 2720, TITLE_Y, "8 - ASKING THE MODEL", w=300, h=44)
model = place(D, "Claude Code Model", 2880, 480, nick="Claude Code Model")
call = place(D, "LLM Call", 2880, 620, nick="LLM Call")
wire(call, "Model", model, "Model")
wire(call, "Signal", thresh, "Under Limit")
wire(call, "Signal", anchored, "Signal")
cancel = boolean(D, 2750, 665, False, nick="stop", toggle=False)
wire(call, "Cancel", cancel, 0)
reply = panel(D, 3120, 480, "the reply lands here", w=290, h=150, colour=OUTPUT_GREY)
rep_dec = place(D, "Deconstruct Signal", 2920, 760, nick="Deconstruct Signal")
wire(rep_dec, "Signal", call, "Success Signal")
reply.AddSource(pin(rep_dec, "out", "Payload"))
panel(D, 2720, 860,
      "TWO WIRES ARRIVE ON THE SIGNAL INPUT: the short conversations from the Threshold's Under "
      "Limit, and the trimmed ones out of the Anchored Window. It takes a list, and a signal is "
      "consumed exactly once, so there is no risk of a turn being answered twice.\r\n"
      "\r\n"
      "The LLM Call has no idea any of this happened. It reads the Instructions off whichever "
      "signal arrived and sends them.",
      w=300, h=280)

# --------------------------------------------------------------------------- 9 the way back

back(D, call, "Success Signal", log, "Response Signal",
     3000, 1180, 700, 1180, nick="reply back to the log")
panel(D, 900, 1120,
      "THE WAY BACK, as always - and note it goes to the CONVERSATION LOG, not to the compaction. "
      "The full record grows; only the copy sent to the model was trimmed.",
      w=520, h=180, colour=WARN_ORANGE)

# --------------------------------------------------------------------------- 10 alternatives

title(D, 60, 1420, "10 - THE OTHER THREE, AND WHEN TO USE THEM", w=520, h=44)
sliding = place(D, "Sliding Window", 780, 1560, nick="Sliding Window")
s_max = slider(D, 460, 1540, 20, 2, 100, nick="keep last N")
wire(sliding, "Max Messages", s_max, 0)

tokwin = place(D, "Token Window", 780, 1700, nick="Token Window")
wire(tokwin, "Tokenization Technique", tech, "Tokenization Technique")
tw_max = slider(D, 460, 1680, 8000, 1000, 100000, nick="fit in N tokens")
wire(tokwin, "Max Tokens", tw_max, 0)

summ = place(D, "Summarizer", 780, 1860, nick="Summarizer")
wire(summ, "Model", model, "Model")
s_prompt = input_panel(D, 400, 1830,
                       "Summarise the conversation so far in under 200 words, keeping every "
                       "decision, constraint and dimension.",
                       w=230, h=90, nick="how to summarise")
wire(summ, "Summary Prompt", s_prompt, 0)
s_recent = slider(D, 400, 1940, 6, 2, 30, nick="keep last N")
wire(summ, "Keep Recent", s_recent, 0)

panel(D, 60, 1490,
      "These three are NOT wired in. They are alternatives to stage 7 - unplug the Anchored Window "
      "and drop one of these in its place.\r\n"
      "\r\n"
      "SLIDING WINDOW keeps the last N messages and nothing else. Simplest and cheapest, and it "
      "loses the beginning of the conversation, so it suits a pipeline where each round is "
      "self-contained.\r\n"
      "\r\n"
      "TOKEN WINDOW fits the conversation inside a token budget rather than a message count. Reach "
      "for it when your turns vary wildly in size - one message with three images in it is worth "
      "twenty short ones, and a message count cannot tell the difference.\r\n"
      "\r\n"
      "SUMMARIZER is the expensive, best-quality option: it ASKS A MODEL to summarise the old "
      "turns and keeps the recent ones verbatim. It costs a whole extra inference call, so it is "
      "worth it on a long design conversation where the reasoning matters, and not worth it on a "
      "loop that is mostly tool calls. Give it its own cheap model if you like - a small local one "
      "summarises perfectly well.\r\n"
      "\r\n"
      "You can also CHAIN them, which is what stages 6 and 7 already do: prune first, then window. "
      "Each one fails open independently, so a chain cannot leave you with no answer at all.",
      w=340, h=560)

panel(D, 1080, 1490,
      "WHICH TO REACH FOR\r\n"
      "\r\n"
      "Mostly conversation, decisions matter: ANCHORED WINDOW, keep first 2. That is the default "
      "for a reason.\r\n"
      "\r\n"
      "Lots of images or snapshots: CONTENT PRUNER with drop-images on, and often nothing else.\r\n"
      "\r\n"
      "Wildly uneven message sizes: TOKEN WINDOW.\r\n"
      "\r\n"
      "Each round independent: SLIDING WINDOW.\r\n"
      "\r\n"
      "Very long design session where the early reasoning is load-bearing: SUMMARIZER, on a cheap "
      "model.\r\n"
      "\r\n"
      "Tool-heavy loop that has stopped making progress: CONTENT PRUNER with drop-tool-exchanges "
      "on. Be aware it can make the model re-check things it already checked.",
      w=340, h=420)

panel(D, 1500, 1490,
      "THINGS TO TRY\r\n"
      "\r\n"
      "1. Open the chat window and watch the counter in the bottom-right corner as you talk. It is "
      "the whole feedback loop for this preset.\r\n"
      "\r\n"
      "2. Set the threshold in stage 5 down to 2000 and have a short conversation. You will see it "
      "start compacting almost immediately - and you can compare the answers.\r\n"
      "\r\n"
      "3. Set Keep Last to 2 and ask it something that depends on what you said four turns ago. "
      "Then set Keep First to 0 and ask again about your original constraints. Feeling both "
      "failures is worth more than reading about them.\r\n"
      "\r\n"
      "4. Unplug the Anchored Window and put the Summarizer in its place. Watch the extra call go "
      "out before the answer.\r\n"
      "\r\n"
      "5. Disable the Content Pruner and see the counter jump on a conversation with images in "
      "it.",
      w=520, h=440)

commit_build(D, "build preset 10")
solve(D)
pick(D, model, "Model", "sonnet")
solve(D)
write_dump(D, DUMP)
say("llm call signal sources:", pin(call, "in", "Signal").SourceCount)
bad = sweep(D, NAME)
save_phy(H, OUT,
         description="Compaction: a Token Threshold that only trims when it needs to, a Content "
                     "Pruner and an Anchored Window inline on the way to the model, and the other "
                     "three compaction components alongside with notes on when each is the right "
                     "one.",
         chat_text="When the Conversation Gets Long\r\n\r\n"
                   "Every turn resends the whole conversation, so a long session gets slower and "
                   "more expensive on its own. This pipeline trims what is sent - but only once it "
                   "crosses a threshold, and never the record itself.\r\n\r\n"
                   "Watch the token counter in the bottom-right corner as you talk. That is the "
                   "whole point of the preset.\r\n\r\n"
                   "Then go and set the threshold much lower, and see what compaction costs you as "
                   "well as what it saves.")
say("PROBLEMS:", bad)
