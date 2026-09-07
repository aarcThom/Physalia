# Copyright (c) 2026 Physalia Contributors
# SPDX-License-Identifier: AGPL-3.0-or-later
"""
Preset 05 - "Writing Python for You".

The other way a pipeline can produce something: instead of placing components, the model writes
Python into a Rhino 8 Script component that is already on your canvas. The Set Script I/O grounder
tells it exactly what inputs and outputs that component has, so the code it writes fits the wires
you already drew.
"""

exec(open(r"C:\Users\rober\repos\Physalia\tools\presets\phybuild.py").read())

NAME = "05 - Writing Python for You"
OUT = r"C:\Users\rober\repos\Physalia\wip_presets\%s.phy" % NAME
DUMP = r"C:\Users\rober\AppData\Local\Temp\claude\dump05.txt"

host = clear_host()
H, D = new_harness(host, 200, 200, name="writing-python-for-you")

SPINE = 470
TITLE_Y = 340
NOTE_Y = 570

# --------------------------------------------------------------------------- the intro

panel(D, 40, 40,
      "WRITING PYTHON FOR YOU\r\n"
      "\r\n"
      "Preset 04 had the model place components. This one has it write CODE - Python, straight into "
      "a Rhino 8 Script component sitting on your own canvas.\r\n"
      "\r\n"
      "Which you want depends on the job. Components stay visible and editable and are what most "
      "Grasshopper work should be. Code is better when the logic is genuinely awkward: a loop, a "
      "recursive subdivision, something with a condition in it that would be twenty components and "
      "a headache.\r\n"
      "\r\n"
      "BEFORE THIS WILL DO ANYTHING you have to give it somewhere to write. Drop a Python 3 Script "
      "component on your Grasshopper canvas, then right-click the PY TRANSMITTER in stage 8 and "
      "choose \"Link to Script Component\". A preset cannot remember that link for you, because "
      "the component it points at lives in your file, not in this one.",
      w=900, h=280, colour=INTRO_GREEN)

# --------------------------------------------------------------------------- 1 the chat

title(D, 60, TITLE_Y, "1 - WHERE YOU TYPE", w=280, h=44)
chat = place(D, "Chat", 130, SPINE, nick="Chat")
panel(D, 60, NOTE_Y,
      "\"Take the curves on the input and return every third one, reversed.\"\r\n"
      "\r\n"
      "Presets 01 to 04 explain the loop, the grounding, the tools and the guardrails. This one "
      "assumes them.",
      w=260, h=190)

# --------------------------------------------------------------------------- 2 system prompt

title(D, 400, TITLE_Y, "2 - THE RULES OF THE ANSWER", w=320, h=44)
sysp = place(D, "System Prompt", 600, SPINE, nick="System Prompt")
panel(D, 400, NOTE_Y,
      "PREAMBLE is \"Python3 Script\" and SCHEMA is the matching \"Python3 Script.json\". Between "
      "them they tell the model that its answer must be a JSON object carrying the code and the "
      "parameter declarations - not a code block in prose.\r\n"
      "\r\n"
      "There is a \"Python3 Script (Small Model)\" preamble in the same folder, written for local "
      "models that need shorter instructions.\r\n"
      "\r\n"
      "For C# instead, swap to the \"C# Script\" pair and use a C# TRANSMITTER in stage 8. Almost "
      "everything else here is identical - the one real difference is that C# declares its "
      "parameters twice, in the JSON and again in the RunScript signature, so the transmitter "
      "checks the two agree before it pushes anything.",
      w=320, h=430)

# --------------------------------------------------------------------------- 3 grounding

title(D, 800, TITLE_Y, "3 - WHAT IT KNOWS", w=300, h=44)
scriptio = place(D, "Set Script I/O", 870, 430, nick="Set Script I/O")
rhino = place(D, "Rhino Document", 870, 500, nick="Rhino Document")
units = place(D, "Document Units Grounding", 870, 550, nick="Document Units")
panel(D, 800, 660,
      "SET SCRIPT I/O is the component this preset is really about, and it is worth understanding "
      "properly.\r\n"
      "\r\n"
      "It grip-links to the Py Transmitter in stage 8 - the arrow on its bottom edge - and through "
      "it reads the script component on your canvas. Then it tells the model, exactly: these are "
      "your inputs, with these names and these type hints and item-or-list access; these are your "
      "outputs. Written as JSON entries the model can copy verbatim.\r\n"
      "\r\n"
      "It also reads what your canvas DOWNSTREAM of each output is expecting. If an untyped output "
      "called \"walls\" is plugged into a Mesh parameter, the model is told to put a mesh there. "
      "And it reports what is actually ARRIVING on each input - \"2 Curves\" - and says so when "
      "that disagrees with the declaration.\r\n"
      "\r\n"
      "The names are LOCKED: the model may not rename your parameters, because your wires are "
      "attached to them. It MAY correct a type hint or change item to list, and the transmitter "
      "applies those in place so the wires survive. A lock that reported a problem and forbade the "
      "fix would just be a ratchet.\r\n"
      "\r\n"
      "Disable this component to suspend the lock without unlinking it.",
      w=300, h=580)

# --------------------------------------------------------------------------- 4 conversation log

title(D, 1180, TITLE_Y, "4 - THE RUNNING CONVERSATION", w=320, h=44)
log = place(D, "Conversation Log", 1340, SPINE, nick="Conversation Log")
wire(log, "System Prompt", sysp, "System Prompt")
wire(log, "Prompt Signal", chat, "Prompt Signal")
for g in (scriptio, rhino, units):
    wire(log, "Grounding", g, 0)
panel(D, 1180, NOTE_Y,
      "As before. The interesting thing on this preset is that the grounding CHANGES when you "
      "touch the script component on your canvas - add a parameter, rename one, wire something "
      "into an output - and the next message already knows.\r\n"
      "\r\n"
      "Renaming an output is the awkward case, because it runs no Grasshopper solution at all, so "
      "Set Script I/O watches the component itself for it. Worth knowing if you ever wonder why "
      "the model is talking about a parameter you renamed ten minutes ago.",
      w=320, h=330)

# --------------------------------------------------------------------------- 5 the call

title(D, 1580, TITLE_Y, "5 - ASKING THE MODEL", w=300, h=44)
model = place(D, "Claude Code Model", 1720, 430, nick="Claude Code Model")
call = place(D, "LLM Call", 1720, 540, nick="LLM Call")
wire(call, "Model", model, "Model")
wire(call, "Signal", log, "Signal")
cancel = boolean(D, 1590, 585, False, nick="stop", toggle=False)
wire(call, "Cancel", cancel, 0)
errs = panel(D, 1580, 630, "errors from the model land here", w=300, h=110, colour=ERROR_PINK)
errs.AddSource(pin(call, "out", "Fail Signal"))
panel(D, 1580, 770,
      "CLAUDE CODE, which drives the Claude command-line tool you have already signed into, so this "
      "preset works with nothing set up. Any of the key-based Model nodes works too.\r\n"
      "\r\n"
      "Code is one of the things models are best at, so this preset tends to need fewer rounds "
      "than preset 04 does.",
      w=300, h=210)

# --------------------------------------------------------------------------- 6 detect json

title(D, 1960, TITLE_Y, "6 - IS THIS EVEN AN ANSWER?", w=300, h=44)
detect = place(D, "Detect JSON", 2080, SPINE, nick="Detect JSON")
wire(detect, "Signal", call, "Success Signal")
panel(D, 1960, NOTE_Y,
      "Sorts a submission from a conversation. An attempt at JSON goes on to be checked; plain "
      "prose ends the round quietly.\r\n"
      "\r\n"
      "So you can ask \"how would you approach this?\" and talk about it before asking for the "
      "code.",
      w=300, h=230)

# --------------------------------------------------------------------------- 7 schema validator

title(D, 2340, TITLE_Y, "7 - IS IT THE RIGHT SHAPE?", w=300, h=44)
schemav = place(D, "Schema Validator", 2460, SPINE, nick="Schema Validator")
wire(schemav, "Signal", detect, "Signal")
wire(schemav, "Schema", sysp, "Schema")
panel(D, 2340, NOTE_Y,
      "The same schema the model was handed in stage 2 - hence the long wire along the canvas. "
      "Advertised and enforced cannot drift apart.\r\n"
      "\r\n"
      "It also pulls the JSON out of whatever prose came with it, so a model that cannot resist "
      "adding \"here you go!\" is not punished for it.\r\n"
      "\r\n"
      "SUCCESS goes on to the transmitter; FAIL becomes a complaint.",
      w=300, h=290)

# --------------------------------------------------------------------------- 8 the transmitter

title(D, 2720, TITLE_Y, "8 - PUSH IT INTO THE SCRIPT", w=320, h=44)
pytx = place(D, "Py Transmitter", 2840, SPINE, nick="Py Transmitter")
wire(pytx, "Signal", schemav, "Success Signal")
scriptio.LinkTo(pytx.InstanceGuid)
panel(D, 2720, NOTE_Y,
      "The PY TRANSMITTER writes the code into the linked script component and lets it solve.\r\n"
      "\r\n"
      "RIGHT-CLICK IT AND USE \"LINK TO SCRIPT COMPONENT\" FIRST. It shows a list of the Python 3 "
      "Script components on your canvas. Until you pick one, this node has nowhere to write.\r\n"
      "\r\n"
      "The link is a picker rather than a dragged wire because the target is on your canvas and "
      "this node is inside a harness - a drag cannot cross between two documents. The harness node "
      "out on your canvas does carry a grip labelled \"py\" for aiming the output.\r\n"
      "\r\n"
      "It pushes CODE, and it will not restructure your parameters. A submission naming an input "
      "that does not exist is refused with a complaint saying which names are real.\r\n"
      "\r\n"
      "It also quietly repairs the two settings that make list outputs work in the Python engine - "
      "no type hint on outputs, and output marshalling on. Get those wrong by hand and you get "
      "\"Data conversion failed from Goo to ...\" on any output that returns a list.",
      w=320, h=520)

# --------------------------------------------------------------------------- 10 complaints

title(D, 2780, 1120, "10 - WHEN A CHECK OBJECTS", w=320, h=44)
stall_limit = slider(D, 2560, 1200, 3, 1, 10, nick="how many identical failures")
stall = place(D, "Stall Guard", 3020, 1250, nick="Stall Guard")
wire(stall, "Stall Limit", stall_limit, 0)
wire(stall, "Signal", schemav, "Fail Signal")
wire(stall, "Signal", pytx, "Fail Signal")
panel(D, 2780, 1350,
      "Both fail wires arrive here: a submission that does not match the schema, and one the "
      "transmitter refused.\r\n"
      "\r\n"
      "STALL GUARD fingerprints each failure and stops sending the SAME one once it has repeated "
      "past the limit on the slider. Different failures do not count against each other, so a "
      "pipeline working through a series of different problems keeps going.\r\n"
      "\r\n"
      "Without this, a model that has misunderstood something structural will make the identical "
      "mistake indefinitely, at full price.",
      w=320, h=340)

# --------------------------------------------------------------------------- 11 the return paths

back(D, call, "Success Signal", log, "Response Signal",
     2440, 1450, 1000, 1450, nick="reply back to the log")
back(D, stall, "Success Signal", log, "Feedback Signal",
     3300, 1250, 1000, 1600, nick="complaints to the log")

panel(D, 1120, 1230,
      "THE TWO RETURN PATHS - the two collectors down the left."
      "\r\n" "\r\n"
      "REPLY goes to the Conversation Log's RESPONSE SIGNAL, as in every preset."
      "\r\n" "\r\n"
      "COMPLAINTS go to FEEDBACK SIGNAL, and arrive as an ordinary user turn - which is exactly "
      "why the model reacts to a guardrail the way it would react to you pointing out a mistake. "
      "There is nothing clever behind the self-correction; it is \"put the complaint in the "
      "conversation and ask again\"."
      "\r\n" "\r\n"
      "Two DIFFERENT destination inputs, so two separate collectors. A collector can be shared by "
      "several senders going to the same input, but never between two destinations."
      "\r\n" "\r\n"
      "NO TOOLS ON THIS PRESET, deliberately - it runs on Claude Code with nothing to set up, and "
      "Claude Code cannot call tools. Preset 03 shows the wiring and it is identical here. "
      "RHINOCOMMON SEARCH is the one worth adding: it searches the real Rhino API, so the model "
      "can check that Curve.DivideByLength takes the arguments it thinks it does instead of "
      "inventing a plausible signature. Invented API calls are the commonest way generated Rhino "
      "code fails.",
      w=520, h=560, colour=WARN_ORANGE)

# --------------------------------------------------------------------------- 12 what to try

panel(D, 3200, 1400,
      "THINGS TO TRY\r\n"
      "\r\n"
      "1. Drop a Python 3 Script component on your canvas. Give it an input called \"crvs\" with "
      "the Curve type hint and List access, and an output called \"out\". Link the Py Transmitter "
      "to it.\r\n"
      "\r\n"
      "2. Wire some curves into crvs, then ask: \"return every second curve, reversed\". Look at "
      "the code that lands.\r\n"
      "\r\n"
      "3. Rename \"out\" to \"picked\" by hand and ask for a change. It uses the new name without "
      "being told.\r\n"
      "\r\n"
      "4. Plug the output into a Mesh parameter and ask for something that returns geometry. It "
      "will be told a mesh is expected there.\r\n"
      "\r\n"
      "5. Disable Set Script I/O and ask for something that needs a new input. Now it is allowed "
      "to restructure - and you can see what the lock was protecting.",
      w=520, h=420)

commit_build(D, "build preset 05")
solve(D)
pick(D, model, "Model", "sonnet")
pick(D, sysp, "Preamble", "Python3 Script.txt")
pick(D, sysp, "Schema", "Python3 Script.json")
solve(D)
solve(D)
write_dump(D, DUMP)
say("preamble:", [str(v) for v in pin(sysp, "in", "Preamble").VolatileData.AllData(True)])
say("schema:", [str(v) for v in pin(sysp, "in", "Schema").VolatileData.AllData(True)])
say("script I/O linked to transmitter:", scriptio.LinkedGuid == pytx.InstanceGuid)
bad = sweep(D, NAME)
save_phy(H, OUT,
         description="The model writes Python into a Rhino 8 Script component on your canvas. Set "
                     "Script I/O tells it exactly what parameters that component has, so the code "
                     "fits the wires you already drew. Link the Py Transmitter to a script "
                     "component before use.",
         chat_text="Writing Python for You\r\n\r\n"
                   "This pipeline writes Python into a Rhino 8 Script component on your canvas, "
                   "fitted to the inputs and outputs it already has.\r\n\r\n"
                   "SET THIS UP FIRST: drop a Python 3 Script component on your canvas, then "
                   "right-click the Py Transmitter inside this harness and choose \"Link to Script "
                   "Component\". Nothing can be written until you do.\r\n\r\n"
                   "Then try: \"take the curves on the input and return every second one, "
                   "reversed.\"")
say("PROBLEMS:", bad)
