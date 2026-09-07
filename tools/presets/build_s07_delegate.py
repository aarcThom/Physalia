# Copyright (c) 2026 Physalia Contributors
# SPDX-License-Identifier: AGPL-3.0-or-later
"""
Scenario S07 - "Send the Legwork to a Specialist".

Two helper harnesses, each with its own conversation and its own tools, called as tools by the
pipeline that needs them. The point is scoping: what it takes to answer one question stays inside
the helper and never fills up the conversation you are having.
"""

exec(open(r"C:\Users\rober\repos\Physalia\tools\presets\phybuild.py").read())

NAME = "S07 - Send the Legwork to a Specialist"
OUT = r"C:\Users\rober\repos\Physalia\wip_presets\%s.phy" % NAME
DUMP = r"C:\Users\rober\AppData\Local\Temp\claude\dumpS07.txt"

host = clear_host()
H, D = new_harness(host, 200, 200, name="send-the-legwork-out")

panel(D, 40, 40,
      "SEND THE LEGWORK TO A SPECIALIST\r\n"
      "\r\n"
      "A Physalia pipeline has ONE conversation, and everything ever said stays in it. That is "
      "fine for an afternoon and a problem by Thursday: the model is re-reading four hours of "
      "false starts every time you ask it anything, you are paying for that, and the useful part "
      "is getting harder to find in it.\r\n"
      "\r\n"
      "A HELPER HARNESS IS THE FIX. It is a whole second pipeline - its own conversation, its own "
      "model, its own tools - that this one can call as though it were a tool. It goes away, does "
      "whatever it takes, and comes back with ONE answer. Every wrong turn on the way stays inside "
      "it.\r\n"
      "\r\n"
      "THERE ARE TWO HELPERS HERE, and that is the realistic case:\r\n"
      "  THE SURVEYOR measures your Rhino model. It writes and runs Python, gets it wrong, tries "
      "again, and hands back a number.\r\n"
      "  THE RESEARCHER reads things on the web. Searching is noisy - five pages to find one "
      "answer - and none of that noise reaches you.\r\n"
      "\r\n"
      "READ THE HELPERS: right-click either harness node in stage 2 and choose EDIT HARNESS. Each "
      "one is an ordinary pipeline you could run on its own, and that is deliberate - the "
      "alternative was a hidden sub-agent, which would have been the one part of Physalia you "
      "could not open, edit or check.\r\n"
      "\r\n"
      "WHAT TO DELEGATE: anything self-contained where you want the ANSWER and not the working. "
      "What not to: the design conversation itself, which is the thing you want the history of.",
      w=960, h=520, colour=INTRO_GREEN)

SPINE = 720

# --------------------------------------------------------------------------- the main loop

L = core_loop(D, 130, SPINE, model_name="Codex Model",
              instruction="You are the lead. You have two specialists you can send work to.\r\n"
                          "\r\n"
                          "Send MEASUREMENT of the Rhino model to the surveyor, and anything that "
                          "needs looking up on the web to the researcher. Give each one a complete, "
                          "self-contained brief - they cannot see this conversation, so a task that "
                          "says \"the same as before\" will fail.\r\n"
                          "\r\n"
                          "Do the thinking and the writing yourself. Delegate the legwork.")
rhino = place(D, "Rhino Document", 400, 620, nick="Rhino Document")
toolsp = place(D, "Tools Present", 560, 620, nick="Tools Present")
for g in (rhino, toolsp):
    wire(L["log"], "Grounding", g, 0)
expc = place(D, "Export Conversation", 400, 940, nick="Export Conversation")
wire(L["log"], "Human Tools", expc, "Human Tool")

panel(D, 130, 1000,
      "THE LEAD'S INSTRUCTION MATTERS MORE THAN USUAL. A model with helpers available will happily "
      "do the work itself instead, and you will never know it could have been cheaper.\r\n"
      "\r\n"
      "Note the sentence about a self-contained brief. A helper cannot see this conversation - that "
      "is the entire point of it - so \"check the other one too\" means nothing on the far side. "
      "The lead has to write a task that stands on its own.",
      w=380, h=300)

# --------------------------------------------------------------------------- the delegates

title(D, 1440, 560, "2 - THE TWO SPECIALISTS", w=340, h=44)
router = place(D, "Router", 1520, 700, nick="Router")
wire(router, "Tool Calls", L["call"], "Tool Calls")
router_slots(router, 1)

d_surv = place(D, "Delegate", 1860, 640, nick="surveyor")
d_res = place(D, "Delegate", 1860, 780, nick="researcher")
wire(d_surv, "Signal", router, 0)
wire(d_res, "Signal", router, 1)

surv_desc = input_panel(D, 1620, 900,
                        "Measures the Rhino model. Ask it for areas, lengths, counts, distances, "
                        "clashes, or anything else that requires actually looking at the geometry. "
                        "Give it one clear question and the layer or selection it applies to.",
                        w=200, h=170, nick="what the surveyor is for")
res_desc = input_panel(D, 1620, 1090,
                       "Looks things up on the web. Ask it for product data, a standard's "
                       "requirements, a supplier's dimensions. Say what you need and how sure you "
                       "need to be; it will give you the answer and where it came from.",
                       w=200, h=170, nick="what the researcher is for")
wire(d_surv, "Description", surv_desc, 0)
wire(d_res, "Description", res_desc, 0)

panel(D, 1440, 1560,
      "A DELEGATE ADVERTISES NOTHING UNTIL IT IS BOTH LINKED AND DESCRIBED. An unfinished one is "
      "simply not offered to the model, rather than being offered and failing every call - a tool "
      "that always fails reads as broken rather than unconfigured.\r\n"
      "\r\n"
      "THE DESCRIPTION IS THE INTERFACE. It is the only thing the lead knows about the helper, so "
      "write it as you would brief a person you are not going to be in the room with. The two "
      "white boxes are ordinary text, saved in this file, so they travel with the pipeline.\r\n"
      "\r\n"
      "NAMES ARE NAMESPACED - delegate__surveyor, delegate__researcher - so two of these cannot "
      "collide. The node's nickname is the name, which is why they are called what they are.\r\n"
      "\r\n"
      "ONE TASK AT A TIME per helper. Two at once would interleave in its single conversation and "
      "neither answer would be worth having. It is also most of what stops a delegate loop; a "
      "delegate pointed at its own harness is refused outright.",
      w=340, h=500)

# --------------------------------------------------------------------------- helper 1

title(D, 2320, 560, "3 - THE SURVEYOR", w=340, h=44)
surv = place(D, "Harness", 2400, 660, nick="the-surveyor")
SD = surv.EnsureInnerDocument()
d_surv.LinkTo(surv.InstanceGuid)

panel(D, 2320, 740,
      "RIGHT-CLICK, EDIT HARNESS, and read it. It is a small pipeline: Task In, a Conversation Log, "
      "a model, Drive Rhino, Task Out.\r\n"
      "\r\n"
      "It measures by writing Python and running it against your document. It will often take three "
      "or four goes - a wrong layer name, an empty selection, a units mix-up - and NONE of that "
      "reaches the conversation you are having. You get the number.\r\n"
      "\r\n"
      "It is grip-linked to the surveyor Delegate on the left. That link is why the harness has to "
      "be a peer inside this pipeline: no wire crosses a harness boundary, so the two ends find "
      "each other through this document.",
      w=340, h=380)

# ---- surveyor contents ----
panel(SD, 40, 40,
      "THE SURVEYOR\r\n"
      "\r\n"
      "Called as a tool by the pipeline one level up. Everything between TASK IN and TASK OUT is an "
      "ordinary Physalia loop - presets 01 to 03 apply to it unchanged.\r\n"
      "\r\n"
      "Its conversation is its own and starts empty for each task. Whatever it takes to answer - a "
      "failed script, a corrected one, a second look - stays in here.\r\n"
      "\r\n"
      "IT HAS ITS OWN CHAT, and that is worth using: talk to it directly while you are getting it "
      "right, before anything is delegated to it. Reaching Task Out with nobody waiting is a "
      "remark, not an error.",
      w=740, h=290, colour=INTRO_GREEN)

s_in = place(SD, "Task In", 130, 460, nick="Task In")
s_chat = place(SD, "Chat", 130, 560, nick="Chat")
s_task = panel(SD, 130, 660, "the task that arrived", w=280, h=90, colour=OUTPUT_GREY)
s_task.AddSource(pin(s_in, "out", "Task"))

s_sysp = place(SD, "System Prompt", 700, 480, nick="System Prompt")
blank_input(SD, s_sysp, "Preamble", 520, 420, label="no preamble file")
blank_input(SD, s_sysp, "Schema", 520, 466, label="no schema file")
s_instr = input_panel(SD, 460, 540,
                      "You measure Rhino models. Write Python with run_rhino_script, print what "
                      "you find, and check it looks sensible before you answer.\r\n"
                      "\r\n"
                      "Answer with the NUMBER and its units, one short line of how you got it, and "
                      "nothing else. Say plainly if you could not find what was asked for - a "
                      "guessed measurement is worse than no measurement.",
                      w=200, h=190, nick="what the surveyor is told")
wire(s_sysp, "Additional Prompt", s_instr, 0)

s_log = place(SD, "Conversation Log", 1060, 480, nick="Conversation Log")
wire(s_log, "System Prompt", s_sysp, "System Prompt")
wire(s_log, "Prompt Signal", s_in, "Signal")
wire(s_log, "Prompt Signal", s_chat, "Prompt Signal")
s_rhino = place(SD, "Rhino Document", 900, 420, nick="Rhino Document")
s_tools = place(SD, "Tools Present", 900, 620, nick="Tools Present")
for g in (s_rhino, s_tools):
    wire(s_log, "Grounding", g, 0)

s_model = place(SD, "Codex Model", 1420, 420, nick="Codex Model")
s_call = place(SD, "LLM Call", 1420, 520, nick="LLM Call")
wire(s_call, "Model", s_model, "Model")
wire(s_call, "Signal", s_log, "Signal")
s_stop = boolean(SD, 1300, 570, False, nick="stop", toggle=False)
wire(s_call, "Cancel", s_stop, 0)

s_router = place(SD, "Router", 1780, 520, nick="Router")
wire(s_router, "Tool Calls", s_call, "Tool Calls")
s_drive = place(SD, "Drive Rhino", 2060, 480, nick="Drive Rhino")
wire(s_drive, "Signal", s_router, 0)
s_script = panel(SD, 2260, 420, "the script it ran", w=300, h=180, colour=OUTPUT_GREY)
s_script.AddSource(pin(s_drive, "out", "Last Script"))

s_out = place(SD, "Task Out", 2060, 700, nick="Task Out")
s_out.Params.Input[0].NickName = "the answer"
wire(s_out, "Signal", s_call, "Success Signal")

back(SD, s_drive, "Result", s_router, "Results", 2460, 1000, 1640, 1000, nick="tool results")
back(SD, s_router, "Feedback", s_log, "LLM Tool Signal", 1780, 1140, 1140, 1140, nick="tool round")
back(SD, s_call, "Success Signal", s_log, "Response Signal", 1500, 1280, 1140, 1280, nick="reply back")

panel(SD, 60, 790,
      "TASK IN is ACTIVE, unlike Harness In: a task is an event, and nothing else in here would "
      "start a round. It has no Armed switch, because it only ever fires when another pipeline "
      "calls it - and that caller's own budget already bounds how often.\r\n"
      "\r\n"
      "Note it shares the Prompt Signal input with the Chat. Both mint a prompt; the log does not "
      "care which one it came from, which is exactly why you can test this by hand.",
      w=320, h=300)

panel(SD, 2060, 880,
      "TASK OUT is an endpoint - no outputs at all. What reaches it goes back to whoever called.\r\n"
      "\r\n"
      "It sends the WHOLE SIGNAL, not just its text, so a helper that LOOKED at something hands "
      "the picture back as a tool attachment. That is the machinery a tool already has for "
      "answering with something that is not words.\r\n"
      "\r\n"
      "Its input is wired to the LLM Call's Success Signal, so the helper answers as soon as the "
      "model has finished - after however many tool rounds it needed.",
      w=340, h=340)

# --------------------------------------------------------------------------- helper 2

title(D, 2860, 560, "4 - THE RESEARCHER", w=340, h=44)
res = place(D, "Harness", 2940, 660, nick="the-researcher")
RD = res.EnsureInnerDocument()
d_res.LinkTo(res.InstanceGuid)

panel(D, 2860, 740,
      "THE SECOND HELPER, and it is here to make a point: adding one is placing a harness, placing "
      "a Delegate, linking them and writing a description. Nothing else changes - not the Router, "
      "not the lead's wiring, not the conversation.\r\n"
      "\r\n"
      "This one reads the web. Searching is the noisiest work a model does: five pages to find one "
      "number, half of them wrong. Doing that inside the main conversation would fill it with "
      "quotations nobody will read again.\r\n"
      "\r\n"
      "It needs a Tavily key for web_search; read_url needs nothing. See preset 03.",
      w=340, h=380)

# ---- researcher contents ----
panel(RD, 40, 40,
      "THE RESEARCHER\r\n"
      "\r\n"
      "Called as a tool by the pipeline one level up. It looks things up and reports back with the "
      "answer AND where it came from.\r\n"
      "\r\n"
      "The sources are the part that matters. An answer with no provenance cannot be checked, and "
      "in this line of work an unchecked number eventually gets built.",
      w=740, h=230, colour=INTRO_GREEN)

r_in = place(RD, "Task In", 130, 400, nick="Task In")
r_chat = place(RD, "Chat", 130, 500, nick="Chat")
r_sysp = place(RD, "System Prompt", 700, 420, nick="System Prompt")
blank_input(RD, r_sysp, "Preamble", 520, 360, label="no preamble file")
blank_input(RD, r_sysp, "Schema", 520, 406, label="no schema file")
r_instr = input_panel(RD, 460, 480,
                      "You look things up. Search, then READ the promising pages rather than "
                      "trusting a search snippet.\r\n"
                      "\r\n"
                      "Answer in a few lines: what you found, how confident you are, and the URLs "
                      "it came from. If sources disagree, say so and give both. If you could not "
                      "find it, say that - do not fill the gap with something plausible.",
                      w=200, h=200, nick="what the researcher is told")
wire(r_sysp, "Additional Prompt", r_instr, 0)

r_log = place(RD, "Conversation Log", 1060, 420, nick="Conversation Log")
wire(r_log, "System Prompt", r_sysp, "System Prompt")
wire(r_log, "Prompt Signal", r_in, "Signal")
wire(r_log, "Prompt Signal", r_chat, "Prompt Signal")
r_tools = place(RD, "Tools Present", 900, 360, nick="Tools Present")
wire(r_log, "Grounding", r_tools, 0)

r_model = place(RD, "Codex Model", 1420, 360, nick="Codex Model")
r_call = place(RD, "LLM Call", 1420, 460, nick="LLM Call")
wire(r_call, "Model", r_model, "Model")
wire(r_call, "Signal", r_log, "Signal")
r_stop = boolean(RD, 1300, 510, False, nick="stop", toggle=False)
wire(r_call, "Cancel", r_stop, 0)

r_router = place(RD, "Router", 1780, 460, nick="Router")
wire(r_router, "Tool Calls", r_call, "Tool Calls")
r_router_slots = router_slots(r_router, 1)
r_search = place(RD, "Web Search", 2060, 400, nick="Web Search")
r_read = place(RD, "Read URL", 2060, 500, nick="Read URL")
wire(r_search, "Signal", r_router, 0)
wire(r_read, "Signal", r_router, 1)

r_out = place(RD, "Task Out", 2060, 640, nick="Task Out")
r_out.Params.Input[0].NickName = "the answer"
wire(r_out, "Signal", r_call, "Success Signal")

fb_r, co_r = back(RD, r_search, "Result", r_router, "Results", 2280, 940, 1640, 940, nick="tool results")
wire(fb_r, "Signal", r_read, "Result")
back(RD, r_router, "Feedback", r_log, "LLM Tool Signal", 1780, 1080, 1140, 1080, nick="tool round")
back(RD, r_call, "Success Signal", r_log, "Response Signal", 1500, 1220, 1140, 1220, nick="reply back")

panel(RD, 60, 660,
      "SAME SHAPE AS THE SURVEYOR, different tools. That is the whole idea: a helper is an ordinary "
      "pipeline with Task In at one end and Task Out at the other.\r\n"
      "\r\n"
      "WEB SEARCH needs a Tavily key (chat window > API keys). READ URL needs nothing at all - it "
      "fetches a page and hands over the readable text, so it works out of the box.\r\n"
      "\r\n"
      "Talk to this one directly through its own Chat while you are getting the instruction right. "
      "That is much easier than debugging it through two layers of delegation.",
      w=340, h=340)

# --------------------------------------------------------------------------- bounds + returns

budget = place(D, "Budget Guard", 900, 1500, nick="Budget Guard")
b_calls = slider(D, 680, 1580, 80, 1, 400, nick="max calls for the LEAD")
wire(budget, "Max Calls", b_calls, 0)
wire(budget, "Signal", L["log"], "Signal")
wire(L["call"], "Signal", budget, "Success Signal")

fb_res, co_res = back(D, d_surv, "Result", router, "Results",
                      2200, 1500, 1240, 1500, nick="answers from the helpers")
wire(fb_res, "Signal", d_res, "Result")
back(D, router, "Feedback", L["log"], "LLM Tool Signal",
     1300, 1900, 700, 1900, nick="tool round to the log")

panel(D, 60, 1400,
      "ONE COLLECTOR, TWO DELEGATES. Both helpers' answers go to the same place, so they share a "
      "Feedback Collector. A collector can be shared by several senders aimed at ONE destination "
      "input; it cannot be shared across different destinations.\r\n"
      "\r\n"
      "THE BUDGET GUARD ONLY BOUNDS THE LEAD. Each helper spends inside its own harness and is not "
      "counted here. Give a helper its own guard if you are going to leave this running - or just "
      "keep the lead's cap low, since a helper only ever runs because the lead called it.",
      w=380, h=340)

panel(D, 100, 2100,
      "THINGS TO TRY\r\n"
      "\r\n"
      "1. Read both helpers before running anything: right-click, Edit Harness. Use the return "
      "widget or the harness panel to come back out.\r\n"
      "\r\n"
      "2. Talk to a helper directly first. Open its own Chat and give it a task by hand. Getting "
      "the instruction right is far easier one layer down.\r\n"
      "\r\n"
      "3. Then ask the lead something that needs both: \"what's the total glazed area, and what "
      "U-value would we need to hit Part L?\"\r\n"
      "\r\n"
      "4. Watch what you DON'T see. The surveyor's failed scripts and the researcher's dead ends "
      "never appear in your conversation. That is the feature.\r\n"
      "\r\n"
      "5. Add a third helper. Place a harness, place a Delegate, link them, write a description. "
      "Nothing else changes.\r\n"
      "\r\n"
      "6. Try delegating something vague - \"look into the facade\" - and watch it come back with "
      "something unhelpful. A helper cannot ask you what you meant.",
      w=560, h=520)

commit_build(D, "build scenario S07")
solve(D)
solve(D)
write_dump(D, DUMP)
say("delegates linked:", d_surv.LinkedGuid == surv.InstanceGuid, d_res.LinkedGuid == res.InstanceGuid)
say("router outputs:", [q.NickName for q in router.Params.Output])
bad = sweep(D, NAME)
bad += sweep(SD, NAME + " / surveyor")
bad += sweep(RD, NAME + " / researcher")
save_phy(H, OUT,
         description="Two helper harnesses - a surveyor that measures your Rhino model and a "
                     "researcher that reads the web - called as tools by the pipeline that needs "
                     "them. Whatever it takes to answer stays inside the helper; you get the "
                     "answer.",
         chat_text="Send the Legwork to a Specialist\r\n\r\n"
                   "This pipeline has two helpers, each a whole harness of its own with its own "
                   "conversation:\r\n"
                   "  THE SURVEYOR measures your Rhino model.\r\n"
                   "  THE RESEARCHER looks things up on the web.\r\n\r\n"
                   "Read them first - right-click either harness node and choose Edit Harness.\r\n\r\n"
                   "Then ask something that needs both: \"what's the total glazed area, and what "
                   "U-value would we need to hit Part L?\"\r\n\r\n"
                   "Whatever it takes them to answer stays inside them. You get the answer.")
say("PROBLEMS:", bad)
