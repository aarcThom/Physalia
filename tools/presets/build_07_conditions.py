# Copyright (c) 2026 Physalia Contributors
# SPDX-License-Identifier: AGPL-3.0-or-later
"""
Preset 07 - "Making the Pipeline Decide".

Two halves. The top row is a real pipeline where the MODEL picks which way the work should go, with
the Declare tool, and a Signal Gate acts on its answer. The bottom half is a playground: five
control-flow components, each with a button wired to it, so you can press things and watch what
happens without spending anything on inference.
"""

# The ONE machine-specific line in this file. Set ROOT before exec'ing this script to build
# from a checkout somewhere else:  ROOT = r"D:\\code\\Physalia"
try:
    ROOT
except NameError:
    ROOT = r"C:\Users\rober\repos\Physalia"
exec(open(ROOT + r"\tools\presets\phybuild.py").read())

NAME = "07 - Making the Pipeline Decide"
OUT = PRESETS + r"\%s.phy" % NAME
DUMP = SCRATCH + r"\dump07.txt"

host = clear_host()
H, D = new_harness(host, 200, 200, name="making-the-pipeline-decide")

SPINE = 470
TITLE_Y = 340


def readout(x, y, src, out_name, label, w=280, h=150):
    """A Deconstruct Signal plus a panel showing its payload - how you see what a relay did."""
    dec = place(D, "Deconstruct Signal", x, y, nick="Deconstruct Signal")
    wire(dec, "Signal", src, out_name)
    p = panel(D, x + 200, y - 40, label, w=w, h=h, colour=OUTPUT_GREY)
    p.AddSource(pin(dec, "out", "Payload"))
    return dec, p


# --------------------------------------------------------------------------- the intro

panel(D, 40, 40,
      "MAKING THE PIPELINE DECIDE\r\n"
      "\r\n"
      "So far every pipeline has done the same thing every time. This one branches.\r\n"
      "\r\n"
      "THE TOP ROW is a working pipeline where the MODEL chooses the route. The Declare tool offers "
      "it a fixed set of routes that this pipeline actually supports, it picks one by name, and a "
      "Signal Gate acts on the choice. That is very different from guessing what it meant from its "
      "prose.\r\n"
      "\r\n"
      "THE BOTTOM HALF is a playground. Five control-flow components, each with a button wired "
      "into it and a panel showing what came out. Press things. Nothing here talks to a model, so "
      "it is instant and free, and it is much the fastest way to understand what these components "
      "do.\r\n"
      "\r\n"
      "One rule holds all of them together: a relay FORWARDS the original signal, it never makes a "
      "new one. So you can drop a Gate anywhere in a pipeline and it cannot strip out what the "
      "signal was carrying.",
      w=940, h=290, colour=INTRO_GREEN)

# =========================================================================== TOP: the real thing

title(D, 60, TITLE_Y, "1 - WHERE YOU TYPE", w=260, h=44)
chat = place(D, "Chat", 130, SPINE, nick="Chat")
panel(D, 60, 560,
      "\"I want a shading screen on the south face.\" Is that a request to build something, or a "
      "question to talk about? The pipeline is about to ask the model which.",
      w=240, h=170)

title(D, 370, TITLE_Y, "2 - THE SYSTEM PROMPT", w=300, h=44)
sysp = place(D, "System Prompt", 550, SPINE, nick="System Prompt")
blank_input(D, sysp, "Preamble", 370, 410, label="no preamble file")
blank_input(D, sysp, "Schema", 370, 456, label="no schema file")
extra = input_panel(D, 310, 540,
                    "You are helping with a Grasshopper model. Decide what kind of request you "
                    "have been given before answering it.",
                    w=240, h=100, nick="my own instructions")
wire(sysp, "Additional Prompt", extra, 0)
panel(D, 370, 670,
      "Nothing special. The routes themselves do not need to be described here - the Declare tool "
      "in stage 5 generates them into its own definition, so what is offered and what is accepted "
      "can never drift apart.",
      w=300, h=200)

title(D, 720, TITLE_Y, "3 - WHAT IT KNOWS", w=250, h=44)
toolsp = place(D, "Tools Present", 790, 440, nick="Tools Present")
panel(D, 720, 510,
      "TOOLS PRESENT, and on this preset it does something extra. Declare is one of only two tools "
      "that carry a STANDING INSTRUCTION - a line in the prompt saying it MUST be called, not just "
      "that it exists.\r\n"
      "\r\n"
      "That instruction is necessary. A model merely told it CAN declare a route will usually just "
      "answer in prose, which is the exact thing this pipeline exists to stop."
      "\r\n" "\r\n"
      "There is a second tool on the Router: PIPELINE STATE, in stage 6b.",
      w=250, h=350)

title(D, 1030, TITLE_Y, "4 - THE LOOP", w=300, h=44)
log = place(D, "Conversation Log", 1150, SPINE, nick="Conversation Log")
wire(log, "System Prompt", sysp, "System Prompt")
wire(log, "Prompt Signal", chat, "Prompt Signal")
wire(log, "Grounding", toolsp, 0)
model = place(D, "Codex Model", 1480, 430, nick="Codex Model")
call = place(D, "LLM Call", 1480, 545, nick="LLM Call")
wire(call, "Model", model, "Model")
wire(call, "Signal", log, "Signal")
cancel = boolean(D, 1350, 590, False, nick="stop", toggle=False)
wire(call, "Cancel", cancel, 0)
panel(D, 1030, 660,
      "The usual Conversation Log and LLM Call. CODEX, because Declare is a tool and Claude Code "
      "cannot call tools - see preset 03."
      "\r\n" "\r\n"
      "One practical thing about Codex: its model list is fetched LIVE from the CLI and it "
      "changes. If a round fails saying the model does not exist or you do not have access "
      "to it, open the little dropdown beside the Codex Model node and pick again - the list "
      "you are looking at is current.",
      w=300, h=300)

# ------------------------------------------------------------------- 5 the model picks a route

title(D, 1780, TITLE_Y, "5 - THE MODEL PICKS A ROUTE", w=320, h=44)
router = place(D, "Router", 1860, SPINE, nick="Router")
wire(router, "Tool Calls", call, "Tool Calls")
router_slots(router, 1)
declare = place(D, "Declare", 2180, SPINE, nick="Declare")
wire(declare, "Signal", router, 0)
routes = list_panel(D, 1900, 620, ["build", "explain", "ask"], w=170, h=90,
                    nick="the routes on offer")
wire(declare, "Routes", routes, 0)
decl_instr = input_panel(D, 1900, 730,
                         "Declare 'build' to make geometry, 'explain' to talk it through, or 'ask' "
                         "if you need more from me first.",
                         w=170, h=110, nick="what the routes mean")
wire(declare, "Instruction", decl_instr, 0)
panel(D, 1780, 870,
      "DECLARE turns the model's INTENT into something the graph can switch on.\r\n"
      "\r\n"
      "You type the routes on the node - one per line in the white panel - and they are generated "
      "straight into the tool definition the model is given, as a fixed list it must choose from. "
      "It cannot invent a fourth one.\r\n"
      "\r\n"
      "Because the routes live on the NODE, they travel: they are saved in your file and they ship "
      "inside a preset. The second white panel says what each route MEANS, which is what stops the "
      "model choosing sensibly-named routes at random.\r\n"
      "\r\n"
      "Its ROUTE output is plain text - \"build\", \"explain\" or \"ask\" - and that is what stage "
      "6 compares.\r\n"
      "\r\n"
      "Reach for Declare rather than trying to read intent out of the reply text. \"Should I build "
      "that?\" and \"build that\" look almost identical to a keyword match and are opposites.",
      w=320, h=440)

# --------------------------------------------------------------------------- 6 acting on it

title(D, 2500, TITLE_Y, "6 - ACTING ON THE CHOICE", w=320, h=44)
want = input_panel(D, 2560, 415, "build", w=150, h=40, nick="the route to open on")
match = place(D, "Match Text", 2790, 440, nick="Match Text", category="Sets", sub="Text")
wire(match, "Text", declare, "Route")
wire(match, "Pattern", want, 0)
gate = place(D, "Signal Gate", 2790, 570, nick="Signal Gate")
wire(gate, "Signal", call, "Success Signal")
wire(gate, "Open", match, "Match")
readout(3020, 640, gate, "Passed", "the reply took the BUILD branch", w=280, h=130)
readout(3020, 800, gate, "Blocked", "it went some other way", w=280, h=130)
panel(D, 2500, 960,
      "SIGNAL GATE decides NOW: a signal arrives, its OPEN input is true or false, and the signal "
      "leaves by PASSED or by BLOCKED. Nothing waits.\r\n"
      "\r\n"
      "Here Open comes from a plain MATCH TEXT comparing the declared route against the word in "
      "the white box. Change the word to \"explain\" and the other branch lights up instead. This "
      "is ordinary Grasshopper - the condition can be anything you can compute.\r\n"
      "\r\n"
      "In a real pipeline PASSED would go on to a Component Transmitter and BLOCKED would go "
      "nowhere. Follow the same pattern for each route.\r\n"
      "\r\n"
      "Worth knowing: a Gate is also how you branch on SUCCESS or FAILURE. Deconstruct Signal hands "
      "out a Success boolean; wire that into Open. After a Merge Signal that is the only way to "
      "reach the combined outcome at all.",
      w=320, h=400)

# ------------------------------------------------------- 6b state the graph can branch on

title(D, 2900, 1400, "6b - STATE THE GRAPH CAN READ", w=320, h=44)
pstate = place(D, "Pipeline State", 3040, 1520, nick="Pipeline State")
wire(pstate, "Signal", router, 1)
st_key = input_panel(D, 2840, 1560, "phase", w=170, h=44, nick="a key to watch")
wire(pstate, "Key", st_key, 0)
st_instr = input_panel(D, 2840, 1620,
                       "Keep a key called 'phase' set to survey, design or check, so the pipeline "
                       "knows where we are.",
                       w=170, h=110, nick="which keys matter")
wire(pstate, "Instruction", st_instr, 0)
st_keys = panel(D, 3260, 1470, "every key it has set", w=280, h=80, colour=OUTPUT_GREY)
st_keys.AddSource(pin(pstate, "out", "Keys"))
st_vals = panel(D, 3260, 1560, "their values", w=280, h=80, colour=OUTPUT_GREY)
st_vals.AddSource(pin(pstate, "out", "Values"))
st_one = panel(D, 3260, 1650, "the value of the key above", w=280, h=80, colour=OUTPUT_GREY)
st_one.AddSource(pin(pstate, "out", "Value"))
panel(D, 2900, 1760,
      "PIPELINE STATE lets the model put a NAMED VALUE somewhere the graph can read it, and that "
      "third grey panel is the point: wire it into a Match Text and a Signal Gate exactly as stage "
      "6 does with the declared route, and you have a pipeline that branches on something the "
      "model decided several rounds ago."
      
      "\r\n" "\r\n"
      "IT IS NOT THE MEMORY TOOL, and the difference is worth being clear about. Memory is prose "
      "files the model writes for its future self, and the pipeline never looks inside them. This "
      "puts a value on a WIRE."
      
      "\r\n" "\r\n"
      "A DECLARE is a decision about THIS round; state persists across rounds. Use Declare for "
      "\"what should happen next\" and state for \"where have we got to\"."
      
      "\r\n" "\r\n"
      "The white instruction panel earns its place. An author who does not say which keys matter "
      "gets keys the model invented, and a Gate watching one nobody ever set. Unlike the Memory "
      "tool it does NOT make calling mandatory, because a pipeline that never branches on state has "
      "no use for it."
      
      "\r\n" "\r\n"
      "Session-only, per harness, and capped - 64 keys, 8KB a value. It is scratch space, not a "
      "database.",
      w=320, h=420)

# --------------------------------------------------------------------------- return paths

fb_res, co_res = back(D, declare, "Result", router, "Results",
                      2540, 1450, 1600, 1450, nick="tool results")
wire(fb_res, "Signal", pstate, "Result")
back(D, router, "Feedback", log, "LLM Tool Signal",
     1860, 1620, 1000, 1620, nick="tool round to the log")
back(D, call, "Success Signal", log, "Response Signal",
     1700, 1780, 1000, 1780, nick="reply back to the log")
panel(D, 320, 1420,
      "THE THREE RETURN PATHS, as in preset 03. Nothing new.\r\n"
      "\r\n"
      "One detail specific to Declare: its signal fires in the SAME solve as the tool result, "
      "because that is the only moment the node is awake - \"the round finished\" is not something "
      "a tool can observe from inside itself. So a declaration wired onwards joins the "
      "tool-result turn rather than arriving later.",
      w=520, h=240, colour=WARN_ORANGE)

# =========================================================================== BOTTOM: playground

panel(D, 60, 2000,
      "THE PLAYGROUND\r\n"
      "\r\n"
      "Five components, five buttons. Press a button and a signal is minted carrying the text in "
      "the white box beside it; the grey panel shows what came out the other end.\r\n"
      "\r\n"
      "CONSTRUCT SIGNAL is the component doing the minting, and it is the ONE sanctioned place a "
      "Grasshopper Button drives a Physalia pipeline. Everywhere else, wiring a Button into a "
      "Signal input is a hard error - a bare boolean carries no payload, so it is not a signal and "
      "pretending otherwise would break the exactly-once guarantee the whole system rests on. "
      "Construct Signal has a dedicated Boolean Trigger input that mints exactly one signal per "
      "press.",
      w=520, h=330, colour=INTRO_GREEN)


def trigger(x, y, text, label):
    """A Button plus a Construct Signal: one signal per press, carrying `text`."""
    b = boolean(D, x, y + 4, False, nick=label, toggle=False)
    payload = input_panel(D, x - 10, y + 60, text, w=170, h=44, nick="what it carries")
    cs = place(D, "Construct Signal", x + 220, y, nick="Construct Signal")
    wire(cs, "Trigger", b, 0)
    wire(cs, "Payload", payload, 0)
    return cs


ROW1 = 2420
ROW2 = 3200

# --- A: merge ------------------------------------------------------------
X = 700
title(D, X, ROW1 - 80, "A - MERGE SIGNAL", w=300, h=44)
m1 = trigger(X, ROW1, "from branch one", "press A1")
m2 = trigger(X, ROW1 + 200, "from branch two", "press A2")
merge = place(D, "Merge Signal", X + 430, ROW1 + 100, nick="Merge Signal")
wire(merge, 0, m1, "Signal")
wire(merge, 1, m2, "Signal")
readout(X + 640, ROW1 + 100, merge, "Signal", "both branches, joined", w=250, h=130)
panel(D, X, ROW1 + 380,
      "MERGE SIGNAL is a JOIN, not a passthrough. Press A1 and nothing comes out; press A2 and "
      "BOTH arrive together as one signal."
      "\r\n" "\r\n"
      "That is the whole point. Parallel branches finish at different moments, so a component that "
      "emitted per branch would give you two conversation turns for one round. It holds the newest "
      "signal per wired input and emits once the whole wired set is in - watch its caption count "
      "1 / 2 while it waits."
      "\r\n" "\r\n"
      "Add and remove inputs with the + and - when you zoom in. They go on the END only.",
      w=520, h=300)

# --- B: hold -------------------------------------------------------------
X = 2100
title(D, X, ROW1 - 80, "B - HOLD SIGNAL", w=300, h=44)
h1 = trigger(X, ROW1, "waiting to be let out", "press B")
hold = place(D, "Hold Signal", X + 430, ROW1 + 10, nick="Hold Signal")
wire(hold, "Signal", h1, "Signal")
release = boolean(D, X + 250, ROW1 + 40, False, nick="let it go", toggle=True)
wire(hold, "Release", release, 0)
timeout = slider(D, X + 160, ROW1 + 120, 60, 5, 600, nick="give up after (seconds)")
wire(hold, "Timeout", timeout, 0)
readout(X + 640, ROW1 + 10, hold, "Released", "let through", w=250, h=110)
readout(X + 640, ROW1 + 170, hold, "Timed Out", "waited too long", w=250, h=110)
panel(D, X, ROW1 + 380,
      "HOLD SIGNAL waits. Press B with the toggle off and the signal sits there; flip the toggle "
      "and it goes. Leave it long enough and it comes out of TIMED OUT instead."
      "\r\n" "\r\n"
      "It holds the OLDEST signal, so a queue keeps its order and nothing jumps a hold."
      "\r\n" "\r\n"
      "Its RECHECK input also expires whatever is wired into RELEASE, and it has to: expiring this "
      "node alone would re-read nothing, because Grasshopper only recomputes what it has marked "
      "stale. That is the whole mechanism by which a wait on something OUTSIDE the data graph - a "
      "file appearing, a job finishing - can ever end.",
      w=520, h=300)

# --- C: switch -----------------------------------------------------------
X = 3500
title(D, X, ROW1 - 80, "C - SIGNAL SWITCH", w=300, h=44)
s1 = trigger(X, ROW1, "please build the screen", "press C")
switch = place(D, "Signal Switch", X + 430, ROW1 + 10, nick="Signal Switch")
wire(switch, "Signal", s1, "Signal")
pattern = input_panel(D, X + 230, ROW1 + 120, "build", w=160, h=40, nick="what to look for")
wire(switch, "Pattern", pattern, 0)
readout(X + 640, ROW1 + 10, switch, "Match", "the text matched", w=250, h=110)
readout(X + 640, ROW1 + 170, switch, "No Match", "it did not", w=250, h=110)
panel(D, X, ROW1 + 380,
      "SIGNAL SWITCH looks at the TEXT a signal is carrying and sends it one way or the other. "
      "Change the white box to something the payload does not contain and press again."
      "\r\n" "\r\n"
      "Its right-click menu turns on regular expressions. A pattern that will not compile sends "
      "everything to NO MATCH rather than quietly pretending to match."
      "\r\n" "\r\n"
      "Useful, but reach for DECLARE first when what you are working out is what the MODEL meant. "
      "Keyword matching on prose is how a pipeline ends up building something in answer to a "
      "question about whether it should.",
      w=520, h=290)

# --- D: throttle ---------------------------------------------------------
X = 700
title(D, X, ROW2 - 80, "D - SIGNAL THROTTLE", w=300, h=44)
t1 = trigger(X, ROW2, "one of many", "press D fast")
throttle = place(D, "Signal Throttle", X + 430, ROW2 + 10, nick="Signal Throttle")
wire(throttle, "Signal", t1, "Signal")
interval = slider(D, X + 160, ROW2 + 120, 10, 1, 120, nick="one every (seconds)")
wire(throttle, "Interval", interval, 0)
readout(X + 640, ROW2 + 10, throttle, "Signal", "what got through", w=250, h=130)
panel(D, X, ROW2 + 320,
      "SIGNAL THROTTLE lets one signal through per interval. Press D several times quickly and "
      "count how many come out."
      "\r\n" "\r\n"
      "When it is holding something and a newer signal arrives, the NEWER one wins - an overtaken "
      "event is stale by definition. That is the opposite policy from Hold Signal, and it is the "
      "right one here."
      "\r\n" "\r\n"
      "Where it belongs: downstream of several triggers meeting, so the rate is stated once in one "
      "place rather than argued about at each source.",
      w=520, h=280)

# --- E: for each ---------------------------------------------------------
X = 2100
title(D, X, ROW2 - 80, "E - FOR EACH", w=300, h=44)
items = list_panel(D, X, ROW2, ["north wall", "east wall", "south wall"], w=200, h=100,
                   nick="the list to walk")
start_b = boolean(D, X, ROW2 + 140, False, nick="press E to start", toggle=False)
starter = place(D, "Construct Signal", X + 230, ROW2 + 140, nick="start")
wire(starter, "Trigger", start_b, 0)
next_b = boolean(D, X, ROW2 + 215, False, nick="press for the next one", toggle=False)
nexter = place(D, "Construct Signal", X + 230, ROW2 + 215, nick="next")
wire(nexter, "Trigger", next_b, 0)
foreach = place(D, "For Each", X + 480, ROW2 + 60, nick="For Each")
wire(foreach, "Items", items, 0)
wire(foreach, "Start", starter, "Signal")
wire(foreach, "Next", nexter, "Signal")
item_p = panel(D, X + 700, ROW2, "the item it is on", w=250, h=90, colour=OUTPUT_GREY)
item_p.AddSource(pin(foreach, "out", "Item"))
idx_p = panel(D, X + 700, ROW2 + 110, "which number it is", w=250, h=70, colour=OUTPUT_GREY)
idx_p.AddSource(pin(foreach, "out", "Index"))
done_dec = place(D, "Deconstruct Signal", X + 480, ROW2 + 230, nick="Deconstruct Signal")
wire(done_dec, "Signal", foreach, "Done Signal")
done_p = panel(D, X + 700, ROW2 + 210, "finished the list", w=250, h=90, colour=OUTPUT_GREY)
done_p.AddSource(pin(done_dec, "out", "Payload"))
panel(D, X, ROW2 + 340,
      "FOR EACH walks a list one item at a time. Press START, then press NEXT to step."
      "\r\n" "\r\n"
      "In a real pipeline NEXT is wired from the END of the per-item work, so each item round asks "
      "for the next one when it has finished. That is what makes it a loop."
      "\r\n" "\r\n"
      "STRICTLY SEQUENTIAL, and that is a rule rather than a limitation. There is ONE Conversation "
      "Log downstream, so twelve items at once would interleave into one conversation and none of "
      "the answers would be trustworthy."
      "\r\n" "\r\n"
      "INDEX is what carries anything that is not text: put a List Item on the far end and it "
      "picks out the matching geometry. The list is snapshotted at START, or a pipeline that edits "
      "the canvas would extend the very list it is walking. An empty list is DONE, not broken.",
      w=520, h=340)

commit_build(D, "build preset 07")
solve(D)
# The Codex model list is fetched LIVE from the CLI and changes under you - it went from
# gpt-5.5/5.4/5.4-mini to gpt-5.6-sol/terra/luna/5.5/5.4-mini inside one session here. So the
# Picker is deliberately NOT pinned: left alone it snaps to whatever the CLI offers first,
# which self-heals. A pinned name that the account cannot use answers 404 and does not.
solve(D)
solve(D)
write_dump(D, DUMP)
say("router outputs:", [q.NickName for q in router.Params.Output])
say("declare routes:", [str(v) for v in pin(declare, "in", "Routes").VolatileData.AllData(True)])
bad = sweep(D, NAME)
save_phy(H, OUT,
         description="Branching. The Declare tool lets the MODEL pick which route the pipeline "
                     "should take, and a Signal Gate acts on it - plus a button-driven playground "
                     "for Merge, Hold, Switch, Throttle and For Each that costs nothing to try.",
         chat_text="Making the Pipeline Decide\r\n\r\n"
                   "The top row asks the model to DECLARE what kind of request it has been given - "
                   "build, explain or ask - and a Signal Gate acts on the answer.\r\n\r\n"
                   "Try: \"I want a shading screen on the south face.\" Then look at which of the "
                   "two grey panels in stage 6 filled in.\r\n\r\n"
                   "The bottom half of the canvas is a playground with buttons. It costs nothing "
                   "and is the fastest way to learn what these components do - go and press "
                   "things.")
say("PROBLEMS:", bad)
