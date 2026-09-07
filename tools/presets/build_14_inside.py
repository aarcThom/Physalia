# Copyright (c) 2026 Physalia Contributors
# SPDX-License-Identifier: AGPL-3.0-or-later
"""
Preset 14 - "Looking Inside the Pipeline".

The debugging preset. Four ways of seeing what is actually going on, in rough order of how often
you will reach for them: read a signal, read the exact prompt the model was sent, watch every signal
that moved, and build a conversation by hand to send whatever you like.
"""

# The ONE machine-specific line in this file. Set ROOT before exec'ing this script to build
# from a checkout somewhere else:  ROOT = r"D:\\code\\Physalia"
try:
    ROOT
except NameError:
    ROOT = r"C:\Users\rober\repos\Physalia"
exec(open(ROOT + r"\tools\presets\phybuild.py").read())

NAME = "14 - Looking Inside the Pipeline"
OUT = PRESETS + r"\%s.phy" % NAME
DUMP = SCRATCH + r"\dump14.txt"

host = clear_host()
H, D = new_harness(host, 200, 200, name="looking-inside-the-pipeline")

TITLE_Y = 400
SPINE = 540

# --------------------------------------------------------------------------- the intro

panel(D, 40, 40,
      "LOOKING INSIDE THE PIPELINE\r\n"
      "\r\n"
      "When a pipeline does something you did not expect, there are four places to look, and this "
      "preset has all four wired up at once.\r\n"
      "\r\n"
      "DECONSTRUCT SIGNAL opens a signal up. Passive - looking never consumes - so you can hang one "
      "anywhere without changing anything.\r\n"
      "\r\n"
      "THE DECOMPOSITORS take the Instructions apart and show you the EXACT system prompt and the "
      "exact turns the model was sent. Read this once and most confusion about a disappointing "
      "answer resolves itself.\r\n"
      "\r\n"
      "SIGNAL TRACE lists every signal that has moved through the pipeline, in order, with who sent "
      "it and who consumed it. This is the one for \"why did nothing happen?\".\r\n"
      "\r\n"
      "THE COMPOSITORS go the other way: build a conversation by hand and send it. That is how you "
      "reproduce a problem without having to type your way back into the state that caused it.",
      w=940, h=320, colour=INTRO_GREEN)

# --------------------------------------------------------------------------- 1 the loop

title(D, 60, TITLE_Y, "1 - AN ORDINARY LOOP", w=300, h=44)
chat = place(D, "Chat", 130, SPINE, nick="Chat")
sysp = place(D, "System Prompt", 420, SPINE, nick="System Prompt")
blank_input(D, sysp, "Preamble", 220, 490, label="no preamble file")
blank_input(D, sysp, "Schema", 220, 536, label="no schema file")
extra = input_panel(D, 170, 590,
                    "Answer briefly. This pipeline is being used to inspect itself.",
                    w=210, h=70, nick="my own instructions")
wire(sysp, "Additional Prompt", extra, 0)
rhino = place(D, "Rhino Document", 200, 700, nick="Rhino Document")
strace = place(D, "Signal Trace", 200, 750, nick="Signal Trace")
expc = place(D, "Export Conversation", 200, 800, nick="Export Conversation")
log = place(D, "Conversation Log", 700, SPINE, nick="Conversation Log")
wire(log, "System Prompt", sysp, "System Prompt")
wire(log, "Prompt Signal", chat, "Prompt Signal")
wire(log, "Grounding", rhino, 0)
wire(log, "Human Tools", strace, "Human Tool")
wire(log, "Human Tools", expc, "Human Tool")
model = place(D, "Claude Code Model", 980, 480, nick="Claude Code Model")
call = place(D, "LLM Call", 980, 600, nick="LLM Call")
wire(call, "Model", model, "Model")
wire(call, "Signal", log, "Signal")
cancel = boolean(D, 850, 645, False, nick="stop", toggle=False)
wire(call, "Cancel", cancel, 0)
back(D, call, "Success Signal", log, "Response Signal",
     1100, 900, 600, 900, nick="reply back to the log")
panel(D, 60, 870,
      "Nothing new: preset 01's loop with a Rhino Document grounder on it, so there is something in "
      "the system prompt worth reading in stage 3.\r\n"
      "\r\n"
      "SIGNAL TRACE and EXPORT CONVERSATION are the two human tools that exist for this. Signal "
      "Trace opens a window from the chat header; the trace itself covers the whole session and "
      "every pipeline in it, not just this conversation.",
      w=300, h=280)

# --------------------------------------------------------------------------- 2 reading a signal

title(D, 1300, TITLE_Y, "2 - WHAT IS IN A SIGNAL", w=320, h=44)
dec = place(D, "Deconstruct Signal", 1420, SPINE, nick="Deconstruct Signal")
wire(dec, "Signal", call, "Success Signal")
p_seq = panel(D, 1660, 450, "sequence number", w=260, h=60, colour=OUTPUT_GREY)
p_seq.AddSource(pin(dec, "out", "Sequence"))
p_ok = panel(D, 1660, 520, "did it succeed?", w=260, h=60, colour=OUTPUT_GREY)
p_ok.AddSource(pin(dec, "out", "Success"))
p_pay = panel(D, 1660, 590, "the text it carries", w=260, h=130, colour=OUTPUT_GREY)
p_pay.AddSource(pin(dec, "out", "Payload"))
p_src = panel(D, 1660, 730, "who sent it", w=260, h=60, colour=OUTPUT_GREY)
p_src.AddSource(pin(dec, "out", "Source"))
p_time = panel(D, 1660, 800, "when", w=260, h=60, colour=OUTPUT_GREY)
p_time.AddSource(pin(dec, "out", "Time"))
panel(D, 1300, 900,
      "Every signal carries all of this, and all of it is worth knowing about.\r\n"
      "\r\n"
      "THE SEQUENCE NUMBER is the important one, and it is not a timestamp. Sequence order IS "
      "causal order across the whole plug-in, and each receiver remembers the highest it has "
      "consumed - which is how a signal is consumed exactly once no matter how many times "
      "Grasshopper re-solves in the background. If two things happened in a surprising order, "
      "compare sequence numbers rather than reasoning about timing.\r\n"
      "\r\n"
      "SUCCESS is what a Signal Gate's Open input wants when you need to branch on whether "
      "something worked - see preset 07. After a Merge Signal it is the only way to reach the "
      "combined outcome at all.\r\n"
      "\r\n"
      "WHO SENT IT is more than the immediate sender: a signal remembers the trail of components "
      "an event ultimately came from, which is why a feedback turn in the chat window can be "
      "badged with the node that produced it even though three aggregators re-minted it on the "
      "way.",
      w=320, h=460)

# --------------------------------------------------------------------- 3 the actual prompt

title(D, 2000, TITLE_Y, "3 - THE EXACT PROMPT IT WAS SENT", w=340, h=44)
log_dec = place(D, "Deconstruct Signal", 2120, SPINE, nick="Deconstruct Signal")
wire(log_dec, "Signal", log, "Signal")
idecomp = place(D, "Instructions Decompositor", 2380, SPINE, nick="Instructions Decompositor")
wire(idecomp, "Instructions", log_dec, "Instructions")
sys_out = panel(D, 2660, 440, "THE WHOLE SYSTEM PROMPT, grounding and all", w=320, h=260,
                colour=OUTPUT_GREY)
sys_out.AddSource(pin(idecomp, "out", "System Prompt"))
mdecomp = place(D, "Message Decompositor", 2380, 730, nick="Message Decompositor")
wire(mdecomp, "Message", idecomp, "Messages")
role_out = panel(D, 2660, 720, "every turn's role", w=320, h=90, colour=OUTPUT_GREY)
role_out.AddSource(pin(mdecomp, "out", "Role"))
content_out = panel(D, 2660, 830, "every turn's content", w=320, h=160, colour=OUTPUT_GREY)
content_out.AddSource(pin(mdecomp, "out", "Content"))
panel(D, 2000, 1010,
      "THIS IS THE MOST USEFUL THING ON THE CANVAS. Do it once on any pipeline you are unhappy "
      "with.\r\n"
      "\r\n"
      "Deconstruct the CONVERSATION LOG's signal rather than the LLM Call's - that is the one "
      "carrying the Instructions on their way OUT. Then INSTRUCTIONS DECOMPOSITOR splits them into "
      "the system prompt and the list of turns, and MESSAGE DECOMPOSITOR splits each turn into its "
      "role and its content.\r\n"
      "\r\n"
      "The grey panel top-right is the whole system prompt the model actually received - every "
      "grounding section, the tool list, every standing instruction, assembled. Reading it explains "
      "more about a disappointing answer than anything else in Physalia: grounding you thought was "
      "wired and is not, a preamble folded in that you did not mean, a catalogue of eleven hundred "
      "components where you wanted four tabs.\r\n"
      "\r\n"
      "It is also how you see what COMPACTION did. Put these three nodes downstream of an Anchored "
      "Window instead and you are reading the trimmed conversation rather than the full one - see "
      "preset 10.",
      w=340, h=480)

# --------------------------------------------------------------- 4 building one by hand

title(D, 60, 1560, "4 - BUILDING A CONVERSATION BY HAND", w=400, h=44)
role_in = input_panel(D, 60, 1660, "User", w=140, h=44, nick="role")
content_in = input_panel(D, 60, 1730,
                         "Pretend we have been discussing a timber pavilion and I have just asked "
                         "you for the beam spacing.",
                         w=220, h=110, nick="what it says")
mcomp = place(D, "Message Compositor", 400, 1700, nick="Message Compositor")
wire(mcomp, "Role", role_in, 0)
wire(mcomp, "Content", content_in, 0)
ccomp = place(D, "Conversation Compositor", 640, 1700, nick="Conversation Compositor")
wire(ccomp, "Messages", mcomp, "Message")
sys_in = input_panel(D, 640, 1800,
                     "You are a structural engineer. Answer in one sentence.",
                     w=200, h=80, nick="a system prompt of your own")
icomp = place(D, "Instructions Compositor", 900, 1700, nick="Instructions Compositor")
wire(icomp, "Conversation", ccomp, "Conversation")
wire(icomp, "System Prompt", sys_in, 0)
panel(D, 60, 1930,
      "THE COMPOSITORS run the other way, and between them they say something worth knowing: "
      "INSTRUCTIONS ARE JUST DATA. A conversation is a list of messages, a message is a role and "
      "some content, and Instructions are a conversation plus a system prompt. Nothing about them "
      "is privileged.\r\n"
      "\r\n"
      "So you can build one. Type a role and some content in the white panels, and you have a "
      "conversation that never happened.\r\n"
      "\r\n"
      "WHY THAT IS USEFUL: reproducing a problem. When a pipeline goes wrong on the fifteenth turn "
      "of a long conversation, you do not want to type your way back there. Compose the state "
      "directly instead, and you have a test you can run as many times as you like.\r\n"
      "\r\n"
      "Also worth knowing: a Conversation refuses two turns from the same speaker in a row, "
      "because every provider requires strict alternation. If you compose something a provider "
      "would reject, you find out here rather than in a failed call.\r\n"
      "\r\n"
      "To SEND it you need a signal, which is what stage 5 is about.",
      w=400, h=520)

# --------------------------------------------------------------- 5 minting signals by hand

title(D, 560, 1560, "5 - MINTING SIGNALS BY HAND", w=400, h=44)
press = boolean(D, 1180, 1700, False, nick="press to send", toggle=False)
cs = place(D, "Construct Signal", 1420, 1700, nick="Construct Signal")
wire(cs, "Trigger", press, 0)
cs_pay = input_panel(D, 1140, 1760, "How many objects are in my Rhino document?", w=210, h=60,
                     nick="what it carries")
wire(cs, "Payload", cs_pay, 0)
fail_toggle = boolean(D, 1180, 1820, False, nick="mark it a failure", toggle=True)
wire(cs, "Failure", fail_toggle, 0)
wire(log, "Prompt Signal", cs, "Signal")

press2 = boolean(D, 1180, 1940, False, nick="press to call a tool", toggle=False)
ctc = place(D, "Construct Tool Call", 1420, 1940, nick="Construct Tool Call")
wire(ctc, "Trigger", press2, 0)
tool_name = input_panel(D, 1160, 2000, "run_rhino_script", w=190, h=44, nick="which tool")
wire(ctc, "Tool Name", tool_name, 0)
tool_args = input_panel(D, 1160, 2060,
                        "{\"script\": \"print(1+1)\", \"description\": \"a test\"}",
                        w=190, h=70, nick="its arguments")
wire(ctc, "Arguments", tool_args, 0)

drive = place(D, "Drive Rhino", 1720, 2200, nick="Drive Rhino")
wire(drive, "Signal", ctc, "Signal")
drive_out = panel(D, 1700, 2280, "the script it just ran", w=300, h=140, colour=OUTPUT_GREY)
drive_out.AddSource(pin(drive, "out", "Last Script"))

panel(D, 1660, 1560,
      "CONSTRUCT SIGNAL mints a signal carrying whatever you typed, one per press of the button. It "
      "is the ONE sanctioned place a Grasshopper Button drives a Physalia pipeline - everywhere "
      "else a bare boolean into a Signal input is a hard error, because it carries no payload and "
      "so is not a signal at all.\r\n"
      "\r\n"
      "Its FAILURE toggle mints the signal as a failure instead, which is how you test a fail "
      "branch without having to cause a real failure. Genuinely useful on a guardrail chain.\r\n"
      "\r\n"
      "It is wired into the Conversation Log's Prompt Signal here, alongside the Chat - so pressing "
      "the button is exactly like typing.\r\n"
      "\r\n"
      "CONSTRUCT TOOL CALL is the same idea for tools: it mints a signal carrying a tool call, so "
      "you can run ANY tool node from the canvas without a model being involved at all. Wire its "
      "output into a tool node's Signal input and press. Instant, free and repeatable, which makes "
      "it the right way to test a tool you have just wired up.\r\n"
      "\r\n"
      "One thing to know: a hand-made tool call is marked as such, and a hand-made batch emits NO "
      "Result signal. Whatever the tool produced reaches you through the node's own outputs "
      "instead. That is deliberate - a provider rejects a result answering a call it never made, "
      "so there is nothing safe to send back."
      "\r\n" "\r\n"
      "The DRIVE RHINO node below is wired to it so you can try exactly that: press the button "
      "and a script runs against your Rhino document with no model in the loop. The grey panel "
      "beside it shows what ran - which is where the answer goes, since a hand-made call gets "
      "no Result signal.",
      w=400, h=500)

# --------------------------------------------------------------- 6 the canvas utilities

title(D, 2120, 1560, "6 - AND TWO CANVAS UTILITIES", w=340, h=44)
ser = place(D, "Serializer", 2380, 1700, nick="Serializer")
ser_run = boolean(D, 2130, 1700, False, nick="export the canvas", toggle=False)
wire(ser, "Run", ser_run, 0)
ser_note = input_panel(D, 2130, 1755, "before I let it loose", w=200, h=44, nick="a comment")
wire(ser, "Comment", ser_note, 0)
des = place(D, "Deserializer", 2380, 1870, nick="Deserializer")
des_run = boolean(D, 2130, 1870, False, nick="import one back", toggle=False)
wire(des, "Run", des_run, 0)
des_path = input_panel(D, 2110, 1925, "", w=220, h=44, nick="a .ghjson file to read")
wire(des, "File Path", des_path, 0)
zoom = place(D, "Zoom Guid", 2380, 2040, nick="Zoom Guid")
zoom_run = boolean(D, 2130, 2040, False, nick="go and look at it", toggle=False)
wire(zoom, "Zoom", zoom_run, 0)
panel(D, 2120, 2100,
      "Three small things that are only ever useful when something has gone wrong.\r\n"
      "\r\n"
      "SERIALIZER writes your canvas out as .ghjson - the same format the model writes definitions "
      "in. Press it before letting a pipeline loose on a canvas you care about, and you have "
      "something to compare against afterwards. It is also the best way to learn what the model is "
      "being asked to produce: export a definition you built by hand and read the JSON.\r\n"
      "\r\n"
      "DESERIALIZER reads one back onto the canvas.\r\n"
      "\r\n"
      "ZOOM GUID jumps the canvas to a component by its instance id. Sounds obscure, and then a "
      "guardrail tells you that component 7d3f1a94 has an unwired input and you want to be looking "
      "at it rather than hunting.",
      w=340, h=420)

# --------------------------------------------------------------------------- 7 what to try

panel(D, 2620, 1560,
      "THINGS TO TRY\r\n"
      "\r\n"
      "1. Send a message, then read the big grey panel in stage 3. That is what the model was "
      "really told. Compare it with what you thought it was told.\r\n"
      "\r\n"
      "2. Disable the Rhino Document grounder and send another message. Watch the system prompt "
      "shrink.\r\n"
      "\r\n"
      "3. Open SIGNAL TRACE from the chat window header and read a round backwards.\r\n"
      "\r\n"
      "4. Type something into stage 4's panels and press stage 5's button. You have just sent a "
      "conversation that never happened.\r\n"
      "\r\n"
      "5. Flip the FAILURE toggle and press again. Everything downstream now sees a failed signal - "
      "which is how you test a fail branch on a Friday afternoon without breaking anything.\r\n"
      "\r\n"
      "6. Wire CONSTRUCT TOOL CALL into a tool node in any other preset in this set and press it. "
      "No model, no waiting, no cost.\r\n"
      "\r\n"
      "7. Press the SERIALIZER and read the .ghjson it writes. It is the clearest possible answer "
      "to \"what is the model actually being asked for?\".",
      w=520, h=560)

commit_build(D, "build preset 14")
solve(D)
pick(D, model, "Model", "sonnet")
solve(D)
write_dump(D, DUMP)
say("prompt signal sources:", pin(log, "in", "Prompt Signal").SourceCount)
say("composed instructions:", pin(icomp, "out", "Instructions").VolatileDataCount)
bad = sweep(D, NAME)
save_phy(H, OUT,
         description="The debugging preset: read a signal, read the exact system prompt the model "
                     "was sent, watch every signal that moved, and compose a conversation or a tool "
                     "call by hand to reproduce a problem without a model involved.",
         chat_text="Looking Inside the Pipeline\r\n\r\n"
                   "Four ways of seeing what is really happening, all wired up at once.\r\n\r\n"
                   "Send a message, then go and read the big grey panel in stage 3 - that is the "
                   "exact system prompt the model received, grounding and all. It is the single "
                   "most useful thing in this preset and it explains most disappointing "
                   "answers.\r\n\r\n"
                   "Then try the buttons in stage 5. Construct Signal and Construct Tool Call let "
                   "you drive a pipeline with no model involved: instant, free and repeatable.")
say("PROBLEMS:", bad)
