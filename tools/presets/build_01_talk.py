# Copyright (c) 2026 Physalia Contributors
# SPDX-License-Identifier: AGPL-3.0-or-later
"""
Preset 01 - "Talk to a Model".

The smallest pipeline that does anything: you type, the model answers, and the answer is written
back into the conversation so the next thing you type has it as context. Everything else Physalia
does is this loop with more components hung off it.

Read left to right. Every stage gets a blue heading and a yellow note in plain English.
"""

# The ONE machine-specific line in this file. Set ROOT before exec'ing this script to build
# from a checkout somewhere else:  ROOT = r"D:\\code\\Physalia"
try:
    ROOT
except NameError:
    ROOT = r"C:\Users\rober\repos\Physalia"
exec(open(ROOT + r"\tools\presets\phybuild.py").read())

NAME = "01 - Talk to a Model"
OUT = PRESETS + r"\%s.phy" % NAME
DUMP = SCRATCH + r"\dump01.txt"

host = clear_host()
H, D = new_harness(host, 200, 200, name="talk-to-a-model")

# --------------------------------------------------------------------------- the intro

panel(D, 40, 40,
      "TALK TO A MODEL\r\n"
      "\r\n"
      "This is the whole of Physalia in five components. Read it left to right.\r\n"
      "\r\n"
      "You type in the chat window. The Conversation Log keeps the running conversation. The LLM "
      "Call sends it to a model and gets an answer back. The answer travels back to the "
      "Conversation Log, so it becomes part of the context for whatever you say next.\r\n"
      "\r\n"
      "Everything else Physalia can do - writing Grasshopper definitions, running scripts, looking "
      "at your model, searching the web - is this same loop with more components hung off it. "
      "Learn this one first.",
      w=820, h=230, colour=INTRO_GREEN)

# --------------------------------------------------------------------------- 1 the chat

title(D, 60, 330, "1 - WHERE YOU TYPE", w=280, h=44)
chat = place(D, "Chat", 120, 470, nick="Chat")
panel(D, 60, 560,
      "The CHAT component is the door onto the chat window. Double-click the Harness node out on "
      "your canvas to open it.\r\n"
      "\r\n"
      "When you send a message, Chat mints a SIGNAL: a small parcel carrying your words and a "
      "sequence number. Signals are how every Physalia component talks to the next one, and a "
      "signal wire is never just a trigger - the parcel carries the data too.",
      w=255, h=230)

# --------------------------------------------------------------------------- 2 system prompt

title(D, 400, 330, "2 - WHAT THE MODEL IS TOLD FIRST", w=350, h=44)
sysp = place(D, "System Prompt", 600, 470, nick="System Prompt")
# Preamble and Schema each auto-place a Picker when they have no source at all - and that happens
# again every time the file is READ, after which the Picker snaps to the first file in the folder.
# So both get a real source holding nothing instead.
blank_input(D, sysp, "Preamble", 400, 410, label="no preamble file")
blank_input(D, sysp, "Schema", 400, 456, label="no schema file")
extra = input_panel(D, 330, 540,
                    "You are helping someone inside Rhino and Grasshopper. Keep answers short and "
                    "concrete. If you are not sure what they are looking at, ask.",
                    w=250, h=105, nick="my own instructions")
wire(sysp, "Additional Prompt", extra, 0)
panel(D, 400, 690,
      "The SYSTEM PROMPT is the standing instruction the model is given before it sees any of your "
      "messages.\r\n"
      "\r\n"
      "PREAMBLE and SCHEMA pull ready-made text out of Files/SYSTEM_PROMPTS. That is how the "
      "canvas-building presets teach the model exactly what JSON to answer in.\r\n"
      "\r\n"
      "This preset is only a conversation, so both are left empty - the two little white boxes - "
      "and the instruction is typed straight into ADDITIONAL PROMPT instead. Edit the white panel "
      "on the left to change what the model is told.",
      w=350, h=250)

# --------------------------------------------------------------------------- 3 human tools

title(D, 800, 330, "3 - BUTTONS FOR YOU", w=260, h=44)
img = place(D, "Add Image", 880, 470, nick="Add Image")
exp = place(D, "Export Conversation", 880, 520, nick="Export Conversation")
tkc = place(D, "Token Count", 880, 570, nick="Token Count")
panel(D, 800, 690,
      "HUMAN TOOLS add things to the chat window for the person using it. They are never offered "
      "to the model.\r\n"
      "\r\n"
      "ADD IMAGE lets you paste, drag or pick pictures into the message box - without it, image "
      "intake is switched off completely.\r\n"
      "\r\n"
      "EXPORT CONVERSATION puts a save-transcript button in the header.\r\n"
      "\r\n"
      "TOKEN COUNT shows a running token total in the bottom-right corner.",
      w=260, h=250)

# --------------------------------------------------------------------------- 4 conversation log

title(D, 1120, 330, "4 - THE RUNNING CONVERSATION", w=350, h=44)
log = place(D, "Conversation Log", 1280, 470, nick="Conversation Log")
wire(log, "System Prompt", sysp, "System Prompt")
wire(log, "Prompt Signal", chat, "Prompt Signal")
wire(log, "Human Tools", img, "Human Tool")
wire(log, "Human Tools", exp, "Human Tool")
wire(log, "Human Tools", tkc, "Human Tool")
panel(D, 1120, 690,
      "The CONVERSATION LOG is the memory of the pipeline. Every turn - yours and the model's - is "
      "appended here, and it hands the whole lot on as INSTRUCTIONS: the system prompt plus the "
      "conversation, packed into one signal for the model to answer.\r\n"
      "\r\n"
      "It is append-only, and each signal is consumed exactly once. Grasshopper re-solving in the "
      "background can never make a turn happen twice.\r\n"
      "\r\n"
      "Its seven inputs, top to bottom: what to tell the model, what you said, what it should know "
      "about your document (Grounding - see the next preset), your own chat buttons, and then "
      "three RETURN paths: the model's reply, feedback from guardrails, and tool results.",
      w=350, h=300)

# --------------------------------------------------------------------------- 5 the call

title(D, 1620, 330, "5 - ASKING THE MODEL", w=350, h=44)
model = place(D, "Claude Code Model", 1780, 400, nick="Claude Code Model")
call = place(D, "LLM Call", 1780, 540, nick="LLM Call")
wire(call, "Model", model, "Model")
wire(call, "Signal", log, "Signal")
cancel = boolean(D, 1650, 585, False, nick="stop", toggle=False)
wire(call, "Cancel", cancel, 0)
panel(D, 1620, 690,
      "The LLM CALL is the only component that ever talks to a model. It reads the Instructions off "
      "the signal the Conversation Log sent, streams the answer back, and mints a new signal "
      "carrying the reply.\r\n"
      "\r\n"
      "It runs in the background, so Grasshopper is not frozen while the model thinks. The STOP "
      "button cancels a call that is already in flight.\r\n"
      "\r\n"
      "The MODEL component above it says which model to use. This preset uses CLAUDE CODE, which "
      "drives the Claude command-line tool you have already signed into - so there is no API key "
      "to set up. Swap it for an Anthropic, Gemini or OpenAI-compatible Model node if you would "
      "rather use a key.",
      w=350, h=290)

# --------------------------------------------------------------------------- 6 reading the reply

title(D, 2120, 330, "6 - SEEING WHAT CAME BACK", w=350, h=44)
dec = place(D, "Deconstruct Signal", 2280, 470, nick="Deconstruct Signal")
wire(dec, "Signal", call, "Success Signal")
reply = panel(D, 2560, 400, "the model's reply lands here", w=300, h=170, colour=OUTPUT_GREY)
reply.AddSource(pin(dec, "out", "Payload"))
errs = panel(D, 2560, 600, "errors from the model land here", w=300, h=120, colour=ERROR_PINK)
errs.AddSource(pin(call, "out", "Fail Signal"))
panel(D, 2120, 760,
      "DECONSTRUCT SIGNAL opens a signal up so you can see inside it: the sequence number, whether "
      "it succeeded, the text it carries, which component sent it, and the full Instructions.\r\n"
      "\r\n"
      "It is PASSIVE. Looking at a signal never consumes it, so you can hang one anywhere while "
      "you are learning without changing how the pipeline behaves.\r\n"
      "\r\n"
      "Note the LLM Call has a separate FAIL SIGNAL. A network problem or a bad key comes out "
      "there, never on Success - which is what lets a pipeline react to a failure differently.",
      w=350, h=270)

# --------------------------------------------------------------------------- 7 the way back

back(D, call, "Success Signal", log, "Response Signal",
     2340, 1260, 1080, 1260, nick="reply back to the log")
panel(D, 1250, 1190,
      "THE WAY BACK - the one part of Physalia that looks strange until you know why.\r\n"
      "\r\n"
      "The model's reply has to reach the Conversation Log, which sits UPSTREAM of the LLM Call. An "
      "ordinary wire back there would be a loop, and Grasshopper refuses loops outright: "
      "\"Recursive data stream found, this component depends on itself\".\r\n"
      "\r\n"
      "So FEEDBACK (right) and FEEDBACK COLLECTOR (left) are a wireless pair. The Feedback node "
      "swallows the signal and the Collector hands it out again, with no wire between them - which "
      "breaks the loop as far as Grasshopper can see. Drag the little arrow on a Feedback node "
      "onto a Collector to pair them.\r\n"
      "\r\n"
      "Every backward hop in every Physalia pipeline works this way, and each one needs its OWN "
      "pair - collectors are not shared between destinations.",
      w=520, h=350, colour=WARN_ORANGE)

# --------------------------------------------------------------------------- 8 token counting

title(D, 1900, 1580, "8 - HOW BIG IS THE CONVERSATION?", w=350, h=44)
tech = place(D, "Tokenization Techniques", 1740, 1720, nick="Tokenization Techniques")
est = place(D, "Token Estimator", 2080, 1720, nick="Token Estimator")
wire(est, "Tokenization Technique", tech, "Tokenization Technique")
wire(est, "Data", log, "Signal")
tkc.LinkTo(est.InstanceGuid)
panel(D, 2360, 1670,
      "A conversation grows every turn, and every turn resends all of it. The TOKEN ESTIMATOR "
      "measures what is about to be sent; TOKENIZATION TECHNIQUES says how to count it. Heuristic "
      "needs nothing; the Anthropic and Gemini methods ask the provider for an exact figure.\r\n"
      "\r\n"
      "Counting and displaying are two separate jobs. The TOKEN COUNT button back in stage 3 is "
      "grip-linked to this estimator - drag its little arrow onto an estimator to link them - and "
      "that link is what puts the number in the chat window. An estimator on its own counts but "
      "shows nothing.\r\n"
      "\r\n"
      "When conversations get long enough to matter, the Tokens & Compaction components can trim "
      "them before they are sent. That is a preset of its own.",
      w=350, h=310)

commit_build(D, "build preset 01")
solve(D)
# The Picker snaps to the first entry in its list until it is told otherwise, so the model is
# pinned on purpose rather than left on whatever happens to sort first.
pick(D, model, "Model", "sonnet")
solve(D)
write_dump(D, DUMP)
bad = sweep(D, NAME)
save_phy(H, OUT,
         description="The smallest working pipeline: you type, a model answers, and the answer is "
                     "written back into the conversation. Start here.",
         chat_text="Talk to a Model\r\n\r\n"
                   "This is the core Physalia loop and nothing else. Send a message; it goes to "
                   "Claude Code; the reply comes back and joins the conversation, so your next "
                   "message has it as context.\r\n\r\n"
                   "Try asking what it can see about your Rhino document. It cannot see anything "
                   "yet, and it will say so - giving it eyes is what the Grounding preset is for.")
say("PROBLEMS:", bad)
