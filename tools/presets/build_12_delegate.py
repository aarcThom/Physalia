# Copyright (c) 2026 Physalia Contributors
# SPDX-License-Identifier: AGPL-3.0-or-later
"""
Preset 12 - "Handing Work to a Helper".

A pipeline has exactly ONE Conversation Log, so every subtask it has ever been asked stays in that
context forever. Delegation is the way out: a HARNESS INSIDE A HARNESS, with its own conversation,
called as a tool and answering with a single result.

This preset therefore contains two pipelines - the caller, and the helper inside it. Read the caller
first, then go into the harness in stage 7.
"""

# The ONE machine-specific line in this file. Set ROOT before exec'ing this script to build
# from a checkout somewhere else:  ROOT = r"D:\\code\\Physalia"
try:
    ROOT
except NameError:
    ROOT = r"C:\Users\rober\repos\Physalia"
exec(open(ROOT + r"\tools\presets\phybuild.py").read())

NAME = "12 - Handing Work to a Helper"
OUT = PRESETS + r"\%s.phy" % NAME
DUMP = SCRATCH + r"\dump12.txt"

host = clear_host()
H, D = new_harness(host, 200, 200, name="handing-work-to-a-helper")

TITLE_Y = 400
SPINE = 540

# =========================================================================== THE CALLER

panel(D, 40, 40,
      "HANDING WORK TO A HELPER\r\n"
      "\r\n"
      "A pipeline has exactly ONE Conversation Log. Everything it has ever been asked stays in that "
      "conversation, and gets resent on every turn for the rest of the session. So a long job with "
      "a lot of small investigations in it fills up with the chatter of those investigations, and "
      "the model gets worse at the actual work.\r\n"
      "\r\n"
      "DELEGATION is the way out. The DELEGATE tool in stage 6 hands a task to ANOTHER HARNESS - "
      "one sitting right there inside this one - and waits. That harness has its own conversation, "
      "its own model, its own tools. It does the work, however many rounds that takes, and answers "
      "with ONE result. Only that result joins this conversation.\r\n"
      "\r\n"
      "So this preset holds two pipelines. Read this one, then right-click the harness in stage 7 "
      "and choose EDIT HARNESS to read the helper.\r\n"
      "\r\n"
      "Why a visible harness rather than a hidden sub-agent: a black box would be the one part of "
      "Physalia you could not see, edit, validate or send to a colleague.",
      w=940, h=330, colour=INTRO_GREEN)

title(D, 60, TITLE_Y, "1 - WHERE YOU TYPE", w=260, h=44)
chat = place(D, "Chat", 130, SPINE, nick="Chat")
panel(D, 60, 620,
      "\"Work out the overall extents of everything on the Structure layer, then tell me whether it "
      "fits in a 40 by 25 metre site.\"\r\n"
      "\r\n"
      "The measuring is a job for the helper. The judgement is a job for this pipeline.",
      w=240, h=210)

title(D, 380, TITLE_Y, "2 - THE SYSTEM PROMPT", w=300, h=44)
sysp = place(D, "System Prompt", 560, SPINE, nick="System Prompt")
blank_input(D, sysp, "Preamble", 380, 500, label="no preamble file")
blank_input(D, sysp, "Schema", 380, 546, label="no schema file")
extra = input_panel(D, 320, 620,
                    "You have a helper you can hand self-contained investigations to. Use it for "
                    "anything that needs several steps to find out but only one sentence to "
                    "report. Do the thinking yourself.",
                    w=240, h=130, nick="my own instructions")
wire(sysp, "Additional Prompt", extra, 0)
panel(D, 380, 780,
      "Telling the model WHEN to delegate is most of the work. Left to itself it will either never "
      "delegate or delegate everything.\r\n"
      "\r\n"
      "The rule that works: hand over anything that takes several steps to find out and one "
      "sentence to report. Keep the reasoning, the judgement and the conversation with you.",
      w=300, h=240)

title(D, 740, TITLE_Y, "3 - WHAT IT KNOWS", w=280, h=44)
rhino = place(D, "Rhino Document", 810, 490, nick="Rhino Document")
toolsp = place(D, "Tools Present", 810, 540, nick="Tools Present")
panel(D, 740, 600,
      "TOOLS PRESENT lists the delegate alongside every other tool, using the name and description "
      "you type on it in stage 6. As far as the model is concerned a whole sub-pipeline is just "
      "another tool, which is exactly the right level of detail for it to have.\r\n"
      "\r\n"
      "An UNLINKED or UNDESCRIBED delegate advertises NOTHING - the same rule the API node "
      "follows. A tool that fails every call reads to a model as broken rather than "
      "unconfigured.",
      w=280, h=300)

title(D, 1100, TITLE_Y, "4 - THE RUNNING CONVERSATION", w=320, h=44)
log = place(D, "Conversation Log", 1260, SPINE, nick="Conversation Log")
wire(log, "System Prompt", sysp, "System Prompt")
wire(log, "Prompt Signal", chat, "Prompt Signal")
wire(log, "Grounding", rhino, 0)
wire(log, "Grounding", toolsp, 0)
panel(D, 1100, 620,
      "THIS is the log that delegation protects. Whatever the helper did - three scripts, a failed "
      "attempt, a correction - none of it appears here. One task went out and one answer came "
      "back.\r\n"
      "\r\n"
      "Compare that with running the same investigation inline: every intermediate step becomes a "
      "turn, and every turn is resent on every subsequent call for the rest of the session.",
      w=320, h=300)

title(D, 1520, TITLE_Y, "5 - ASKING THE MODEL", w=300, h=44)
model = place(D, "Codex Model", 1660, 480, nick="Codex Model")
call = place(D, "LLM Call", 1660, 600, nick="LLM Call")
wire(call, "Model", model, "Model")
wire(call, "Signal", log, "Signal")
cancel = boolean(D, 1530, 645, False, nick="stop", toggle=False)
wire(call, "Cancel", cancel, 0)
router = place(D, "Router", 1660, 760, nick="Router")
wire(router, "Tool Calls", call, "Tool Calls")
panel(D, 1520, 850,
      "CODEX, because a delegate is a tool and Claude Code cannot call tools - see preset 03.\r\n"
      "\r\n"
      "One practical thing about Codex: its model list is fetched LIVE from the CLI and it changes. "
      "If a round fails saying the model does not exist or you do not have access to it, open the "
      "dropdown beside the Codex Model node and pick again.\r\n"
      "\r\n"
      "The helper inside has its own Model node, and it does not have to be this one. A cheap fast "
      "model is often exactly right for \"go and measure this\".",
      w=300, h=320)

title(D, 1900, TITLE_Y, "6 - THE DELEGATE", w=320, h=44)
deleg = place(D, "Delegate", 2100, SPINE, nick="Delegate")
wire(deleg, "Signal", router, 0)
d_name = input_panel(D, 1880, 480, "measure", w=160, h=44, nick="what to call it")
wire(deleg, "Tool Name", d_name, 0)
d_desc = input_panel(D, 1880, 560,
                     "Hands a measuring or counting job to a helper that can run scripts against "
                     "the Rhino document. Say exactly what you want measured and in what units. "
                     "It answers in one or two sentences.",
                     w=160, h=160, nick="what it is for")
wire(deleg, "Description", d_desc, 0)
d_timeout = slider(D, 1880, 745, 300, 30, 1800, nick="give up after (s)")
wire(deleg, "Timeout", d_timeout, 0)
last_task = panel(D, 1900, 790, "the last task it was handed", w=320, h=100, colour=OUTPUT_GREY)
last_task.AddSource(pin(deleg, "out", "Last Task"))
last_ans = panel(D, 1900, 910, "and what came back", w=320, h=100, colour=OUTPUT_GREY)
last_ans.AddSource(pin(deleg, "out", "Last Answer"))
panel(D, 1900, 1040,
      "THE DELEGATE is grip-linked to the harness in stage 7 - drag the arrow on its edge onto the "
      "harness node. That link is why the harness has to be a PEER inside this pipeline rather "
      "than somewhere else on your canvas: a drag cannot cross between two documents.\r\n"
      "\r\n"
      "TOOL NAME is what the model calls it. It gets namespaced, so two delegates in one pipeline "
      "cannot collide.\r\n"
      "\r\n"
      "WHAT IT IS FOR is the description the model reads. Write it like a job description, "
      "including what the helper CANNOT do - that is what stops the caller handing over work it "
      "should be doing itself.\r\n"
      "\r\n"
      "The two grey panels are the whole debugging story for delegation: what went over, and what "
      "came back. When a delegated task disappoints, read those two before anything else.\r\n"
      "\r\n"
      "ONE TASK AT A TIME per helper harness. Two at once would interleave in the helper's single "
      "conversation and neither answer would be trustworthy - so a second call is refused straight "
      "away with a message saying so, rather than queued and timed out much later. It is also what "
      "stops most accidental recursion; a delegate linked to its OWN harness is caught separately "
      "and told so.",
      w=320, h=520)

# =========================================================================== THE HELPER

title(D, 2320, TITLE_Y, "7 - THE HELPER", w=340, h=44)
helper = place(D, "Harness", 2480, SPINE, nick="the-measuring-helper")
HD = helper.EnsureInnerDocument()
deleg.LinkTo(helper.InstanceGuid)
panel(D, 2320, 640,
      "RIGHT-CLICK THIS AND CHOOSE \"EDIT HARNESS\" TO GO IN AND READ IT.\r\n"
      "\r\n"
      "It is an ordinary Physalia pipeline. It has its own Conversation Log, its own Model, its own "
      "tool - Drive Rhino, so it can measure things by writing Python - and the two nodes that make "
      "it callable:\r\n"
      "\r\n"
      "TASK IN, where a delegated task arrives and becomes a signal. It is ACTIVE, unlike Harness "
      "In: a task is an event and nothing else would start it. It has no Armed switch, because it "
      "only ever fires when another pipeline calls it, and the caller's own budget already bounds "
      "that.\r\n"
      "\r\n"
      "TASK OUT, where the answer leaves. It has no outputs at all - it is an endpoint. It sends "
      "back the WHOLE SIGNAL, so a helper that LOOKED at something hands the picture back as a "
      "tool attachment, using the machinery a tool already has for answering with something that is "
      "not text.\r\n"
      "\r\n"
      "The two ends find each other through this harness's own document, not through a wire. No "
      "wire crosses a harness boundary.\r\n"
      "\r\n"
      "It also has a CHAT of its own, which is worth knowing about: a callable harness is still an "
      "ordinary pipeline, and talking to it directly is how you get it working before wiring the "
      "delegate up. Reaching Task Out with nobody waiting is a remark, not an error.",
      w=340, h=620)

# ---- the helper's own contents ----

panel(HD, 40, 40,
      "THE MEASURING HELPER\r\n"
      "\r\n"
      "This is the sub-pipeline. It is called as a tool by the Delegate out in the harness above, "
      "and it is otherwise an ordinary Physalia pipeline - so everything in presets 01 to 03 "
      "applies to it unchanged.\r\n"
      "\r\n"
      "The two nodes that make it callable are TASK IN on the left and TASK OUT on the right. "
      "Everything between them is a normal loop.\r\n"
      "\r\n"
      "Its conversation is its own. Whatever it takes to answer one task - several scripts, a "
      "mistake, a correction - stays in here and never reaches the caller.",
      w=760, h=250, colour=INTRO_GREEN)

title(HD, 60, 330, "A - THE TASK ARRIVES", w=280, h=44)
taskin = place(HD, "Task In", 130, 460, nick="Task In")
hchat = place(HD, "Chat", 130, 560, nick="Chat")
task_txt = panel(HD, 340, 430, "the task text arrives here", w=270, h=90, colour=OUTPUT_GREY)
task_txt.AddSource(pin(taskin, "out", "Task"))
panel(HD, 60, 640,
      "TASK IN mints a signal carrying the task the caller sent. Its second output is the task as "
      "plain text, for anything on the canvas that needs it.\r\n"
      "\r\n"
      "The CHAT below it is here so you can drive this pipeline BY HAND while you are getting it "
      "right. Both feed the Conversation Log's Prompt Signal, which takes a list.",
      w=280, h=250)

title(HD, 660, 330, "B - ITS OWN INSTRUCTION", w=300, h=44)
hsysp = place(HD, "System Prompt", 860, 460, nick="System Prompt")
blank_input(HD, hsysp, "Preamble", 660, 430, label="no preamble file")
blank_input(HD, hsysp, "Schema", 660, 476, label="no schema file")
hextra = input_panel(HD, 620, 540,
                     "You measure and count things in the Rhino document by writing Python. Work "
                     "it out with as many scripts as you need, then answer in one or two "
                     "sentences with the numbers and their units. Do not explain how you did it.",
                     w=230, h=140, nick="its own instructions")
wire(hsysp, "Additional Prompt", hextra, 0)
panel(HD, 660, 700,
      "The helper's instruction is nothing like the caller's, and that is the point of giving it a "
      "conversation of its own.\r\n"
      "\r\n"
      "\"ANSWER IN ONE OR TWO SENTENCES, DO NOT EXPLAIN HOW\" is the important line. Whatever the "
      "helper says goes into the CALLER's conversation, so a chatty helper undoes the saving that "
      "delegating was for.",
      w=300, h=280)

title(HD, 1020, 330, "C - ITS OWN LOOP", w=300, h=44)
hrhino = place(HD, "Rhino Document", 1080, 420, nick="Rhino Document")
htools = place(HD, "Tools Present", 1080, 470, nick="Tools Present")
hlog = place(HD, "Conversation Log", 1220, 570, nick="Conversation Log")
wire(hlog, "System Prompt", hsysp, "System Prompt")
wire(hlog, "Prompt Signal", taskin, "Signal")
wire(hlog, "Prompt Signal", hchat, "Prompt Signal")
wire(hlog, "Grounding", hrhino, 0)
wire(hlog, "Grounding", htools, 0)
hmodel = place(HD, "Codex Model", 1600, 480, nick="Codex Model")
hcall = place(HD, "LLM Call", 1600, 600, nick="LLM Call")
wire(hcall, "Model", hmodel, "Model")
wire(hcall, "Signal", hlog, "Signal")
hcancel = boolean(HD, 1470, 645, False, nick="stop", toggle=False)
wire(hcall, "Cancel", hcancel, 0)
hlimit = place(HD, "Signal Limiter", 1600, 760, nick="Signal Limiter")
hlimit_n = slider(HD, 1360, 800, 12, 1, 60, nick="at most N rounds")
wire(hlimit, "Count", hlimit_n, 0)
panel(HD, 1020, 900,
      "An ordinary Conversation Log and LLM Call, with its OWN model - it does not have to be the "
      "caller's. A cheap fast model is often exactly right for \"go and measure this\".\r\n"
      "\r\n"
      "The SIGNAL LIMITER is worth having in any helper. The caller is waiting on a timeout and "
      "cannot see what is happening in here, so a helper that has got stuck in a loop would burn "
      "the whole timeout and then answer nothing. Twelve rounds is generous for a measuring job.",
      w=300, h=320)

title(HD, 1900, 330, "D - THE TOOL IT HAS", w=300, h=44)
hrouter = place(HD, "Router", 1960, 470, nick="Router")
wire(hrouter, "Tool Calls", hcall, "Tool Calls")
hdrive = place(HD, "Drive Rhino", 2240, 470, nick="Drive Rhino")
wire(hdrive, "Signal", hrouter, 0)
hscript = panel(HD, 2440, 430, "the last script it ran", w=280, h=160, colour=OUTPUT_GREY)
hscript.AddSource(pin(hdrive, "out", "Last Script"))
panel(HD, 1900, 620,
      "DRIVE RHINO is all this helper needs: it writes Python against the live Rhino document and "
      "whatever it prints comes straight back to it. So it can ask the document anything at all by "
      "writing three lines of code.\r\n"
      "\r\n"
      "The grey panel shows the last script. Read it when an answer looks wrong - it is almost "
      "always faster than reasoning about what the helper must have done.\r\n"
      "\r\n"
      "Add more tools here if the helper needs them. It is a whole pipeline; nothing about being "
      "callable restricts it.",
      w=300, h=340)

title(HD, 2320, 330, "E - THE ANSWER LEAVES", w=300, h=44)
taskout = place(HD, "Task Out", 2400, 700, nick="Task Out")
wire(taskout, "Signal", hlimit, "Within Limit")
wire(hlimit, "Signal", hcall, "Success Signal")
panel(HD, 2320, 780,
      "TASK OUT is an endpoint - no outputs at all. Whatever signal reaches it is what the caller "
      "gets back.\r\n"
      "\r\n"
      "Note the reply goes through the SIGNAL LIMITER on its way here, so the round is counted. "
      "Reaching Task Out with nobody waiting is a Remark rather than an error, which is what lets "
      "you test this pipeline by hand from its own Chat.\r\n"
      "\r\n"
      "It sends the WHOLE SIGNAL, so a helper that had LOOKED at something would hand the picture "
      "back as an attachment rather than describing it.",
      w=300, h=340)

# the helper's own return paths
back(HD, hcall, "Success Signal", hlog, "Response Signal",
     1780, 1300, 900, 1300, nick="reply back to the log")
back(HD, hrouter, "Feedback", hlog, "LLM Tool Signal",
     1960, 1460, 900, 1460, nick="tool round to the log")
back(HD, hdrive, "Result", hrouter, "Results",
     2560, 1140, 1760, 1140, nick="tool results")
panel(HD, 60, 1080,
      "THE HELPER'S OWN RETURN PATHS, exactly as in preset 03: tool results to its Router, the "
      "finished round to its Conversation Log's LLM Tool Signal, and the reply to Response "
      "Signal.\r\n"
      "\r\n"
      "Nothing about delegation changes any of this. A callable harness is an ordinary pipeline "
      "with two extra nodes on the ends.",
      w=520, h=280, colour=WARN_ORANGE)

panel(HD, 660, 1560,
      "THINGS TO TRY IN HERE\r\n"
      "\r\n"
      "1. Use this pipeline's own CHAT first, before involving the caller. \"How many objects are "
      "on each layer?\" Get it answering well on its own.\r\n"
      "\r\n"
      "2. Watch the grey panel in stage D for the Python it wrote.\r\n"
      "\r\n"
      "3. Then go back out to the caller and ask it something that needs measuring. Compare the "
      "caller's conversation - one task, one answer - with what happened in here.\r\n"
      "\r\n"
      "4. Loosen the helper's instruction so it explains its working, and see the caller's "
      "conversation fill up with detail it did not need. That is the thing delegation is for, felt "
      "rather than described.",
      w=520, h=380)

# =========================================================================== caller return paths

fb_res, co_res = back(D, deleg, "Result", router, "Results",
                      2100, 1620, 1200, 1620, nick="tool results")
back(D, router, "Feedback", log, "LLM Tool Signal",
     1680, 1780, 1200, 1780, nick="tool round to the log")
back(D, call, "Success Signal", log, "Response Signal",
     1520, 1940, 1200, 1940, nick="reply back to the log")
panel(D, 400, 1560,
      "THE CALLER'S THREE RETURN PATHS, as in preset 03. The delegate's Result is an ordinary tool "
      "result - the Router has no idea a whole pipeline ran to produce it.\r\n"
      "\r\n"
      "That is the property worth taking away: a harness called as a tool looks, from every "
      "direction, exactly like a tool.",
      w=520, h=250, colour=WARN_ORANGE)

panel(D, 400, 1860,
      "THINGS TO TRY\r\n"
      "\r\n"
      "1. Read the helper first: right-click the harness in stage 7, EDIT HARNESS.\r\n"
      "\r\n"
      "2. Ask this pipeline something that needs measuring: \"what are the overall extents of "
      "everything on the Structure layer, and does it fit a 40 by 25 metre site?\"\r\n"
      "\r\n"
      "3. Read the two grey panels in stage 6 - the task that went over and the answer that came "
      "back. Then read this pipeline's conversation and notice what is NOT in it.\r\n"
      "\r\n"
      "4. Give the helper a slow job and set the delegate's timeout to 30 seconds, to see what "
      "giving up looks like.\r\n"
      "\r\n"
      "5. Add a SECOND helper harness with a different job and a second Delegate node. The names "
      "are namespaced, so they cannot collide.",
      w=520, h=420)

commit_build(HD, "build preset 12 helper")
commit_build(D, "build preset 12")
solve(HD)
solve(D)
solve(D)
write_dump(D, DUMP)
write_dump(HD, DUMP.replace("dump12", "dump12_helper"))
say("caller router outputs:", [p.NickName for p in router.Params.Output])
say("helper router outputs:", [p.NickName for p in hrouter.Params.Output])
say("delegate linked:", deleg.LinkedGuid == helper.InstanceGuid, "| state:", deleg.Message)
say("helper inner objects:", HD.ObjectCount)
bad = sweep(D, NAME + " (caller)")
bad += sweep(HD, NAME + " (helper)")
save_phy(H, OUT,
         description="A harness inside a harness, called as a tool. The helper has its own "
                     "conversation, model and tools, does the work in however many rounds it takes, "
                     "and answers with one result - so the caller's conversation stays clean.",
         chat_text="Handing Work to a Helper\r\n\r\n"
                   "This pipeline can hand a self-contained job to a HELPER - another harness, "
                   "sitting inside this one, with its own conversation. Whatever it takes to answer "
                   "stays in there; only the answer comes back here.\r\n\r\n"
                   "Read the helper first: right-click the harness in stage 7 and choose Edit "
                   "Harness.\r\n\r\n"
                   "Then try: \"what are the overall extents of everything on the Structure layer, "
                   "and does it fit a 40 by 25 metre site?\"")
say("PROBLEMS:", bad)
