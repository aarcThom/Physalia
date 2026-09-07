# Copyright (c) 2026 Physalia Contributors
# SPDX-License-Identifier: AGPL-3.0-or-later
"""
Scenario S05 - "Write Me a C# Component".

You draw the wires; it writes the code inside. Set Script I/O tells it exactly what parameters your
script component has, so the code fits the definition you already built.
"""

# The ONE machine-specific line in this file. Set ROOT before exec'ing this script to build
# from a checkout somewhere else:  ROOT = r"D:\\code\\Physalia"
try:
    ROOT
except NameError:
    ROOT = r"C:\Users\rober\repos\Physalia"
exec(open(ROOT + r"\tools\presets\phybuild.py").read())

NAME = "S05 - Write Me a C# Component"
OUT = PRESETS + r"\%s.phy" % NAME
DUMP = SCRATCH + r"\dumpS05.txt"

host = clear_host()
H, D = new_harness(host, 200, 200, name="write-me-a-component")

panel(D, 40, 40,
      "WRITE ME A C# COMPONENT\r\n"
      "\r\n"
      "You draw the wires; it writes the code inside them. The result is a normal Rhino 8 C# Script "
      "component sitting on your canvas, which you can open, read and edit like any other.\r\n"
      "\r\n"
      "SET IT UP - THREE MINUTES, ONCE\r\n"
      "1. Put a C# SCRIPT component on your own canvas (Maths > Script > C#).\r\n"
      "2. Give it the inputs and outputs you want, with type hints and the right access mode - "
      "item, list or tree. Wire them up to your definition.\r\n"
      "3. Inside this harness, right-click C# TRANSMITTER and choose \"Link to Script Component\", "
      "then pick the one you just made.\r\n"
      "4. That is it. Ask for what you want.\r\n"
      "\r\n"
      "STEP 2 IS THE ONE PEOPLE SKIP AND IT IS THE ONE THAT MATTERS. Deciding the interface "
      "yourself is what stops this being a black box: you have said what goes in and what comes "
      "out, so the model is filling in a shape you designed rather than inventing its own.\r\n"
      "\r\n"
      "WHEN TO REACH FOR CODE RATHER THAN COMPONENTS\r\n"
      "  A loop that has to remember something between iterations.\r\n"
      "  A recursive subdivision.\r\n"
      "  Anything you have built with forty components and it still is not right.\r\n"
      "  Anything where the component version would be slower than a for loop.\r\n"
      "\r\n"
      "AND WHEN NOT TO: if it can be done with six components, do it with six components. Those "
      "stay editable by whoever gets your file next; a script does not.",
      w=940, h=520, colour=INTRO_GREEN)

SPINE = 700

# --------------------------------------------------------------------------- what it knows

title(D, 60, 620, "1 - IT READS YOUR COMPONENT FIRST", w=340, h=44)
scriptio = place(D, "Set Script I/O", 160, 740, nick="Set Script I/O")
rhino = place(D, "Rhino Document", 160, 800, nick="Rhino Document")
units = place(D, "Document Units Grounding", 160, 850, nick="Document Units")

panel(D, 60, 900,
      "SET SCRIPT I/O is the component that makes this work at all. It is already linked to the "
      "C# Transmitter over on the right - that is the long arrow, and it is how this node knows "
      "which script component you mean. It follows the transmitter's own link to your canvas.\r\n"
      "\r\n"
      "It then hands the model the exact interface: every input with its name, type hint and "
      "access mode, every output, written out in the shape the answer has to take. So the code "
      "comes back fitting the wires you already drew instead of inventing its own names.\r\n"
      "\r\n"
      "IT ALSO REPORTS WHAT THE CANVAS DOWNSTREAM EXPECTS. If your untyped output goes into a Mesh "
      "parameter, it is told to produce a Mesh. And it reports what is ACTUALLY ARRIVING on each "
      "input - so if you declared an item but two curves turn up, it says so and tells the model to "
      "use list access.\r\n"
      "\r\n"
      "THE LOCK FREEZES NAMES, NOT TYPES. The model may correct a type hint or an access mode you "
      "got wrong, and those corrections are applied in place, so your wires survive. Freezing "
      "everything would make the lock a ratchet: it would report the problem and forbid the fix.\r\n"
      "\r\n"
      "Disable the component to suspend the lock without unlinking - do that when you want it to "
      "ADD a parameter.",
      w=340, h=600)

# --------------------------------------------------------------------------- loop

chat = place(D, "Chat", 560, SPINE, nick="Chat")
sysp = place(D, "System Prompt", 940, SPINE, nick="System Prompt")
log = place(D, "Conversation Log", 1420, SPINE, nick="Conversation Log")
wire(log, "System Prompt", sysp, "System Prompt")
wire(log, "Prompt Signal", chat, "Prompt Signal")
for g in (scriptio, rhino, units):
    wire(log, "Grounding", g, 0)
img = place(D, "Add Image", 1180, 600, nick="Add Image")
expc = place(D, "Export Conversation", 1180, 650, nick="Export Conversation")
for t in (img, expc):
    wire(log, "Human Tools", t, "Human Tool")

model = place(D, "Claude Code Model", 1800, 600, nick="Claude Code Model")
call = place(D, "LLM Call", 1800, SPINE + 40, nick="LLM Call")
wire(call, "Model", model, "Model")
wire(call, "Signal", log, "Signal")
cancel = boolean(D, 1670, SPINE + 80, False, nick="stop", toggle=False)
wire(call, "Cancel", cancel, 0)

panel(D, 900, 900,
      "THE PREAMBLE AND SCHEMA ARE PICKED FOR C#, not Python - the dropdowns on the left of the "
      "System Prompt. They are different files because a C# submission has to declare its "
      "parameters TWICE: once in the JSON, and again in the RunScript signature the compiler "
      "reads.\r\n"
      "\r\n"
      "That is also why there is a signature check in stage 3. If those two disagree, the push is "
      "refused before anything reaches your canvas and the model is told the exact signature it "
      "should have written.\r\n"
      "\r\n"
      "This one runs on CLAUDE CODE - no API key, it uses the CLI you are already signed into. "
      "There are no tools in this pipeline, which is what makes that a fine choice here; Claude "
      "Code cannot call tools at all.",
      w=380, h=420)

# --------------------------------------------------------------------------- chain

title(D, 2240, 620, "3 - CHECK IT, THEN PUSH IT", w=340, h=44)
detect = place(D, "Detect JSON", 2240, SPINE, nick="Detect JSON")
wire(detect, "Signal", call, "Success Signal")
schemav = place(D, "Schema Validator", 2560, SPINE, nick="Schema Validator")
wire(schemav, "Signal", detect, "Signal")
wire(schemav, "Schema", sysp, "Schema")
cstx = place(D, "C# Transmitter", 2880, SPINE, nick="C# Transmitter")
wire(cstx, "Signal", schemav, "Success Signal")
health = place(D, "Runtime Health Check", 3200, SPINE, nick="Runtime Health Check")
wire(health, "Signal", cstx, "Success Signal")
scriptio.LinkTo(cstx.InstanceGuid)

panel(D, 3520, 620,
      "WHERE THE CODE ENDS UP: inside the C# Script component on your own canvas. Double-click it "
      "to read what was written.\r\n"
      "\r\n"
      "That is worth doing at least once. It is your component, it will be in the file you hand "
      "over, and nobody else is going to check it.\r\n"
      "\r\n"
      "Grasshopper's own undo covers the push, so Ctrl+Z puts the previous code back.",
      w=380, h=300)

panel(D, 2240, 900,
      "DETECT JSON lets ordinary conversation through without trying to compile it. Ask \"would a "
      "loop be faster here?\" and you get an answer, not a push.\r\n"
      "\r\n"
      "SCHEMA VALIDATOR checks the answer has the right shape before anyone tries to use it. It is "
      "checking against the SAME schema the model was given - that is the wire from the System "
      "Prompt.\r\n"
      "\r\n"
      "C# TRANSMITTER pushes the code into your component. It changes the CODE, not the parameter "
      "set - your wires are not touched. If the submission names a parameter your component does "
      "not have, it is refused with a note saying which.\r\n"
      "\r\n"
      "RUNTIME HEALTH CHECK is what catches a compile error. The code goes in, Grasshopper tries to "
      "run it, and the error comes straight back to the model with the line number. That loop is "
      "the reason you usually get working code within two or three rounds rather than pasting "
      "errors back by hand.",
      w=340, h=480)

# --------------------------------------------------------------------------- returns

stall_limit = slider(D, 2600, 1480, 3, 1, 10, nick="identical failures allowed")
stall = place(D, "Stall Guard", 3000, 1520, nick="Stall Guard")
wire(stall, "Stall Limit", stall_limit, 0)
for g in (schemav, cstx, health):
    wire(stall, "Signal", g, "Fail Signal")
back(D, stall, "Success Signal", log, "Feedback Signal",
     3380, 1520, 1520, 1520, nick="complaints to the log")
back(D, call, "Success Signal", log, "Response Signal",
     1920, 1680, 1520, 1680, nick="reply back to the log")

panel(D, 2640, 1620,
      "STALL GUARD stops a compile error that will not go away from costing you all afternoon. "
      "Three identical failures and it parks; say something in the chat to start it again.\r\n"
      "\r\n"
      "A DIFFERENT error each round is progress and is allowed to continue.",
      w=340, h=260)

panel(D, 440, 1400,
      "THINGS TO TRY\r\n"
      "\r\n"
      "1. Make a C# component with a Curve input (list access) and a Curve output, link it, and "
      "ask: \"sort these curves by length, longest first\". Small enough to check by eye.\r\n"
      "\r\n"
      "2. Then something a definition is bad at: \"walk along each curve in 200mm steps and stop "
      "when the tangent turns more than 5 degrees from where it started\". That is a loop with "
      "memory, which is exactly what components cannot do neatly.\r\n"
      "\r\n"
      "3. Get the type hint wrong on purpose - declare a Curve input as Generic. Watch it correct "
      "the hint in place without breaking your wire.\r\n"
      "\r\n"
      "4. Ask for something that needs a parameter you have not made. It will tell you rather than "
      "adding one. Then DISABLE Set Script I/O and ask again - now it is allowed to.\r\n"
      "\r\n"
      "5. Ask it to add comments explaining the algorithm. You will have to read this code in six "
      "months.\r\n"
      "\r\n"
      "PRESET 05 IS THE PYTHON VERSION of this, and covers the Py Transmitter's own quirks. "
      "Everything else about it is the same.",
      w=560, h=520)

pick(D, model, "Model", "sonnet")
pick(D, sysp, "Preamble", "C# Script.txt")
pick(D, sysp, "Schema", "C# Script.json")

commit_build(D, "build scenario S05")
solve(D)
solve(D)
write_dump(D, DUMP)
bad = sweep(D, NAME)
save_phy(H, OUT,
         description="You draw the wires; it writes the code inside them. Set Script I/O reads your "
                     "C# Script component's exact interface and hands it to the model, so the code "
                     "comes back fitting the definition you already built - and a compile error "
                     "goes straight back for another try.",
         chat_text="Write Me a C# Component\r\n\r\n"
                   "SET UP FIRST:\r\n"
                   "1. Put a C# Script component on your canvas and give it the inputs and outputs "
                   "you want, with type hints and access modes.\r\n"
                   "2. Right-click C# TRANSMITTER inside this harness and choose \"Link to Script "
                   "Component\". Set Script I/O is already linked to that transmitter, so it will "
                   "pick your component up from there.\r\n\r\n"
                   "Then ask: \"sort these curves by length, longest first\".\r\n\r\n"
                   "It writes into the component you linked and never changes your parameters. If "
                   "it does not compile, the error goes straight back to it - you should not need "
                   "to paste anything.")
say("PROBLEMS:", bad)
