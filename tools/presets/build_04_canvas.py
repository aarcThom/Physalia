# Copyright (c) 2026 Physalia Contributors
# SPDX-License-Identifier: AGPL-3.0-or-later
"""
Preset 04 - "Building on the Canvas".

The flagship pipeline: the model writes a Grasshopper definition as JSON, seven guardrails check it
one after another, and the Component Transmitter places it on your canvas. Anything a guardrail
objects to goes back to the model as a complaint, and it tries again.

No tools here on purpose - see the note in stage 16. This is the guardrail chain, and it runs on
Claude Code with nothing to set up.
"""

exec(open(r"C:\Users\rober\repos\Physalia\tools\presets\phybuild.py").read())

NAME = "04 - Building on the Canvas"
OUT = r"C:\Users\rober\repos\Physalia\wip_presets\%s.phy" % NAME
DUMP = r"C:\Users\rober\AppData\Local\Temp\claude\dump04.txt"

host = clear_host()
H, D = new_harness(host, 200, 200, name="building-on-the-canvas")

SPINE = 470
TITLE_Y = 340
NOTE_Y = 570

# --------------------------------------------------------------------------- the intro

panel(D, 40, 40,
      "BUILDING ON THE CANVAS\r\n"
      "\r\n"
      "This is what Physalia is really for. You describe what you want; the model answers with a "
      "Grasshopper definition written as JSON; and it gets placed on your canvas as real "
      "components you can then edit by hand.\r\n"
      "\r\n"
      "The interesting part is everything in between. A model writing a definition gets things "
      "wrong - a component name that does not exist, an input left unwired, a graph that solves to "
      "nothing. So the JSON runs a GAUNTLET of seven checks, in stages 7 to 15. Each one either "
      "passes the definition on or sends a complaint back to the model, which fixes it and tries "
      "again. You watch that happen in the chat window.\r\n"
      "\r\n"
      "Nothing reaches your canvas until every check has passed. That is the difference between "
      "this and pasting code out of a chat window.",
      w=980, h=280, colour=INTRO_GREEN)

# --------------------------------------------------------------------------- 1 the chat

title(D, 60, TITLE_Y, "1 - WHERE YOU TYPE", w=280, h=44)
chat = place(D, "Chat", 130, SPINE, nick="Chat")
panel(D, 60, NOTE_Y,
      "Ask for what you want in plain language: \"a grid of circles that get bigger towards the "
      "middle\".\r\n"
      "\r\n"
      "Presets 01 to 03 explain the loop, the grounding and the tools. This one assumes them.",
      w=260, h=180)

# --------------------------------------------------------------------------- 2 system prompt

title(D, 400, TITLE_Y, "2 - THE RULES OF THE ANSWER", w=320, h=44)
sysp = place(D, "System Prompt", 600, SPINE, nick="System Prompt")
panel(D, 400, NOTE_Y,
      "Both slots are filled here, and that is what makes this preset work.\r\n"
      "\r\n"
      "PREAMBLE is \"Incremental Node Graph\" - it teaches the model how Grasshopper definitions "
      "are described, and tells it to build in measurable STAGES rather than emitting one huge "
      "graph nobody can check.\r\n"
      "\r\n"
      "SCHEMA is the matching JSON schema. The model is handed the exact shape its answer must "
      "take, and the same schema is used to check the answer in stage 9 - so what is advertised "
      "and what is enforced cannot drift apart. Notice the wire from the SCHEMA output running "
      "right along the canvas to the Schema Validator.\r\n"
      "\r\n"
      "Swap both to the plain \"Node Graph\" pair if you want everything in one shot instead of in "
      "stages.",
      w=320, h=420)

# --------------------------------------------------------------------------- 3 grounding

title(D, 780, TITLE_Y, "3 - WHAT IT KNOWS", w=300, h=44)
catalog = place(D, "Component Catalog", 850, 420, nick="Component Catalog", sub="Grounding")
canvas = place(D, "Canvas State", 850, 470, nick="Canvas State")
groupc = place(D, "Physalia Group Components", 850, 520, nick="Physalia Group Components")
units = place(D, "Document Units Grounding", 850, 570, nick="Document Units")
panel(D, 780, 640,
      "COMPONENT CATALOG is the important one here - it is the vocabulary. Right-click it to "
      "choose which ribbon tabs to include. All of Grasshopper is over a thousand components; a "
      "model given all of them writes worse definitions than one given the four tabs you actually "
      "work in.\r\n"
      "\r\n"
      "The same catalog is used again by the Component Resolver in stage 11, to check that every "
      "name the model used is real - hence the long wire.\r\n"
      "\r\n"
      "CANVAS STATE and PHYSALIA GROUP COMPONENTS together are what make this iterative. They "
      "tell the model what is already on the canvas and, specifically, what THIS pipeline put "
      "there - so \"make the circles bigger\" edits the definition instead of building a second "
      "one beside it.\r\n"
      "\r\n"
      "DOCUMENT UNITS stops it building a 3-metre pavilion in a file measured in millimetres.",
      w=300, h=480)

# --------------------------------------------------------------------------- 4 human tools

title(D, 1140, TITLE_Y, "4 - BUTTONS FOR YOU", w=280, h=44)
img = place(D, "Add Image", 1210, 430, nick="Add Image")
vsnap = place(D, "View Snapshot", 1210, 480, nick="View Snapshot")
strace = place(D, "Signal Trace", 1210, 530, nick="Signal Trace")
expc = place(D, "Export Conversation", 1210, 580, nick="Export Conversation")
panel(D, 1140, 650,
      "ADD IMAGE lets you sketch something, drop the sketch in and say \"like this\".\r\n"
      "\r\n"
      "VIEW SNAPSHOT sends whatever your viewport is showing, as a picture. Reach for it when "
      "words are losing.\r\n"
      "\r\n"
      "SIGNAL TRACE is the debugging one, and on this preset it is the one to know about. It opens "
      "a window listing every signal that has moved through the pipeline, in order, with which "
      "component sent it and which consumed it. When a round does something you did not expect, "
      "that list tells you which guardrail objected and what it said.\r\n"
      "\r\n"
      "EXPORT CONVERSATION saves the transcript.",
      w=280, h=420)

# --------------------------------------------------------------------------- 5 conversation log

title(D, 1500, TITLE_Y, "5 - THE RUNNING CONVERSATION", w=320, h=44)
log = place(D, "Conversation Log", 1660, SPINE, nick="Conversation Log")
wire(log, "System Prompt", sysp, "System Prompt")
wire(log, "Prompt Signal", chat, "Prompt Signal")
for g in (catalog, canvas, groupc, units):
    wire(log, "Grounding", g, 0)
for t in (img, vsnap, strace, expc):
    wire(log, "Human Tools", t, "Human Tool")
panel(D, 1500, NOTE_Y,
      "The input that matters on this preset is FEEDBACK SIGNAL - the second of the three return "
      "paths, and the one that makes the pipeline self-correcting.\r\n"
      "\r\n"
      "A guardrail complaint arrives there and is written into the conversation as an ordinary "
      "USER turn. That is why the model reacts to it exactly as it would react to you pointing out "
      "a mistake: it apologises, works out what went wrong, and sends a corrected definition.\r\n"
      "\r\n"
      "There is nothing clever behind it. The whole self-correcting behaviour is \"put the "
      "complaint in the conversation and ask again\".",
      w=320, h=350)

# --------------------------------------------------------------------------- 6 the call

title(D, 1900, TITLE_Y, "6 - ASKING THE MODEL", w=300, h=44)
model = place(D, "Claude Code Model", 2040, 430, nick="Claude Code Model")
call = place(D, "LLM Call", 2040, SPINE + 60, nick="LLM Call")
wire(call, "Model", model, "Model")
wire(call, "Signal", log, "Signal")
cancel = boolean(D, 1910, SPINE + 105, False, nick="stop", toggle=False)
wire(call, "Cancel", cancel, 0)
errs = panel(D, 1900, 630, "errors from the model land here", w=300, h=110, colour=ERROR_PINK)
errs.AddSource(pin(call, "out", "Fail Signal"))
panel(D, 1900, 770,
      "CLAUDE CODE, which drives the Claude command-line tool you have already signed into - so "
      "this preset works with nothing set up.\r\n"
      "\r\n"
      "Writing a whole definition is a lot of output, so expect a round to take a little while. "
      "The chat window streams the answer as it arrives, and you can watch the JSON appear.\r\n"
      "\r\n"
      "An Anthropic, Gemini or OpenAI-compatible Model node works just as well; they need an API "
      "key, set up from the chat window's home screen.",
      w=300, h=260)

# --------------------------------------------------------------------------- 7 detect json

title(D, 2280, TITLE_Y, "7 - IS THIS EVEN AN ANSWER?", w=300, h=44)
detect = place(D, "Detect JSON", 2400, SPINE, nick="Detect JSON")
wire(detect, "Signal", call, "Success Signal")
panel(D, 2280, NOTE_Y,
      "DETECT JSON is a gate, and the friendliest component in the chain: it sorts a definition "
      "from a conversation.\r\n"
      "\r\n"
      "If the reply contains an attempt at JSON - even broken JSON - it passes through to be "
      "checked. If the model just answered your question in prose, the round quietly ends here. "
      "Nothing fails, nothing complains.\r\n"
      "\r\n"
      "That is why you can ask this pipeline \"what would you do?\" and have a normal "
      "conversation, then ask it to build the thing.",
      w=300, h=320)

# --------------------------------------------------------------------------- 8 build plan

title(D, 2660, TITLE_Y, "8 - THE BUILD PLAN", w=300, h=44)
plan = place(D, "Build Plan", 2780, SPINE, nick="Build Plan")
wire(plan, "Signal", detect, "Signal")
progress = panel(D, 2660, 1000, "the plan and how far it has got appear here", w=300, h=160,
                 colour=OUTPUT_GREY)
progress.AddSource(pin(plan, "out", "Progress"))
panel(D, 2660, NOTE_Y,
      "The \"Incremental Node Graph\" preamble asks the model to open its answer with a PLAN - the "
      "stages it intends to build in - and then to build one stage per reply.\r\n"
      "\r\n"
      "BUILD PLAN reads that plan out of each answer and keeps track of which stage is done. It is "
      "a tap, not a gate: the definition passes straight through untouched.\r\n"
      "\r\n"
      "Its PROGRESS output is the running tally, and it goes all the way along to the Geometry "
      "Report in stage 15 - which is how each report ends by telling the model which stage to do "
      "next, rather than just describing what it built.",
      w=300, h=400)

# --------------------------------------------------------------------------- 9 schema validator

title(D, 3040, TITLE_Y, "9 - IS THE JSON THE RIGHT SHAPE?", w=300, h=44)
schemav = place(D, "Schema Validator", 3160, SPINE, nick="Schema Validator")
wire(schemav, "Signal", plan, "Signal")
wire(schemav, "Schema", sysp, "Schema")
panel(D, 3040, NOTE_Y,
      "GUARDRAIL 1. The first real check, and the same schema the model was given back in stage 2 "
      "- hence the long wire.\r\n"
      "\r\n"
      "It pulls the JSON out of whatever prose surrounds it and checks it against the schema. A "
      "missing field or the wrong type of value fails here.\r\n"
      "\r\n"
      "From here on, every component in the chain has TWO signal outputs: SUCCESS carries the "
      "definition to the next check, FAIL carries a complaint. Follow the fail wires down to stage "
      "16 - they all end up in the same place.",
      w=300, h=340)

# --------------------------------------------------------------------------- 10 definition validator

title(D, 3420, TITLE_Y, "10 - IS IT A REAL DEFINITION?", w=300, h=44)
defv = place(D, "GH Definition Validator", 3540, SPINE, nick="GH Definition Validator")
wire(defv, "Signal", schemav, "Success Signal")
panel(D, 3420, NOTE_Y,
      "GUARDRAIL 2. Correctly-shaped JSON can still be nonsense as a Grasshopper definition.\r\n"
      "\r\n"
      "This one parses it as a real definition and checks it hangs together structurally: that "
      "wires point at parameters that exist, that the ids referred to are ids that were declared, "
      "that the graph is not self-contradictory.",
      w=300, h=250)

# --------------------------------------------------------------------------- 11 component resolver

title(D, 3800, TITLE_Y, "11 - DO THESE COMPONENTS EXIST?", w=300, h=44)
resolver = place(D, "Component Resolver", 3920, SPINE, nick="Component Resolver")
wire(resolver, "Signal", defv, "Success Signal")
wire(resolver, "Component Catalog", catalog, 0)
panel(D, 3800, NOTE_Y,
      "GUARDRAIL 3, and the one that fires most often.\r\n"
      "\r\n"
      "It looks up every component the model named in the catalog from stage 3 - hence the second "
      "long wire. A model asking for \"Divide Curve By Length\" when Grasshopper calls it \"Divide "
      "Distance\" is caught here and told the right name.\r\n"
      "\r\n"
      "It also refuses obsolete components, which still exist inside Grasshopper but should not be "
      "placed in new definitions.",
      w=300, h=300)

# --------------------------------------------------------------------------- 12 required inputs

title(D, 4180, TITLE_Y, "12 - IS ANYTHING LEFT DANGLING?", w=300, h=44)
reqin = place(D, "Required Input Check", 4300, SPINE, nick="Required Input Check")
wire(reqin, "Signal", resolver, "Success Signal")
panel(D, 4180, NOTE_Y,
      "GUARDRAIL 4. Everything that can be known about the definition without placing it.\r\n"
      "\r\n"
      "A required input with nothing wired into it and no value typed in. Two wires into an input "
      "that only accepts one item. A wire pointing at a parameter number that does not exist. A "
      "slider sitting there feeding nothing.\r\n"
      "\r\n"
      "All of these produce a definition that places but does nothing, which is the most annoying "
      "failure of the lot because it looks like success.",
      w=300, h=300)

# --------------------------------------------------------------------------- 13 the transmitter

title(D, 4560, TITLE_Y, "13 - PUT IT ON THE CANVAS", w=300, h=44)
tx = place(D, "Component Transmitter", 4680, SPINE, nick="Component Transmitter")
wire(tx, "Signal", reqin, "Success Signal")
panel(D, 4560, NOTE_Y,
      "The COMPONENT TRANSMITTER is the harness's way out. It places the definition on YOUR canvas "
      "as real Grasshopper components, wired up, with sliders given proper names.\r\n"
      "\r\n"
      "It works two ways: a whole new graph, or a patch that edits what it placed last time. That "
      "is what makes \"now make the spacing adjustable\" possible.\r\n"
      "\r\n"
      "Placement can still fail - Grasshopper can refuse something the checks could not predict - "
      "so it has a Fail output like the rest.\r\n"
      "\r\n"
      "Its right-click menu has UNDO LAST PLACEMENT, which is the one to remember.\r\n"
      "\r\n"
      "Notice this node is where the harness grows its drag arrow: look at the Harness node out on "
      "your own canvas and you will see a grip labelled \"node\" on its edge. Drag from there to "
      "aim the output somewhere specific.",
      w=300, h=440)

# --------------------------------------------------------------------------- 14 runtime health

title(D, 4940, TITLE_Y, "14 - DID IT ACTUALLY WORK?", w=300, h=44)
health = place(D, "Runtime Health Check", 5060, SPINE, nick="Runtime Health Check")
wire(health, "Signal", tx, "Success Signal")
panel(D, 4940, NOTE_Y,
      "GUARDRAIL 5, and the first one that looks at the definition RUNNING rather than at its "
      "description.\r\n"
      "\r\n"
      "It scans what was just placed for Grasshopper's own errors, for components producing "
      "nothing, and for nulls travelling down wires, and samples the values that came out. A "
      "definition that placed perfectly and computes nothing is caught here and nowhere else.\r\n"
      "\r\n"
      "Whether warnings count as failure is a toggle on its right-click menu, not an input.",
      w=300, h=320)

# --------------------------------------------------------------------------- 15 what it built

title(D, 5320, TITLE_Y, "15 - LOOK AT WHAT IT BUILT", w=300, h=44)
geoobs = place(D, "Geometry Observation", 5440, 420, nick="Geometry Observation")
georep = place(D, "Geometry Report", 5440, 560, nick="Geometry Report")
wire(geoobs, "Signal", health, "Success Signal")
wire(georep, "Signal", health, "Success Signal")
blank_geo = input_panel(D, 5230, 442, "", w=150, h=40, nick="no extra message")
wire(geoobs, "Message", blank_geo, 0)
wire(georep, "Message", plan, "Progress")
panel(D, 5320, 680,
      "GUARDRAIL 6 and 7, and they are a pair: the same thing seen two ways.\r\n"
      "\r\n"
      "GEOMETRY OBSERVATION frames the camera on the geometry that was just built and sends the "
      "model a PICTURE of it. That is the only check that can catch \"technically correct, "
      "obviously wrong\".\r\n"
      "\r\n"
      "GEOMETRY REPORT describes the same geometry in WORDS: bounding boxes per component, which "
      "groups of things are separate from each other and by how far, what contains what. Cheaper "
      "than an image and often more precise - \"the roof is 4m from the wall it should be sitting "
      "on\" is a sentence a picture does not say.\r\n"
      "\r\n"
      "The BUILD PLAN's progress arrives on the Report's Message input, and that is what turns a "
      "description into an instruction: the report ends by telling the model which stage is next. "
      "Without it, the report just asks \"is this what you meant?\" and waits.",
      w=300, h=460)

# --------------------------------------------------------------------------- 16 complaints home

title(D, 3800, 1180, "16 - WHEN A CHECK OBJECTS", w=320, h=44)
stall_limit = slider(D, 3580, 1250, 3, 1, 10, nick="how many identical failures")
stall = place(D, "Stall Guard", 4020, 1300, nick="Stall Guard")
wire(stall, "Stall Limit", stall_limit, 0)
for g in (schemav, defv, resolver, reqin, tx, health):
    wire(stall, "Signal", g, "Fail Signal")
panel(D, 3800, 1400,
      "Every FAIL wire in the chain arrives here.\r\n"
      "\r\n"
      "STALL GUARD is the circuit breaker. A complaint goes back to the model, it tries again, and "
      "usually gets it right. But sometimes it makes the SAME mistake over and over, and each "
      "attempt costs money and gets no closer.\r\n"
      "\r\n"
      "So the guard fingerprints each failure. Identical failures repeating past the limit on the "
      "slider stop being sent - the loop parks, the node reads STALLED, and you can step in. "
      "Different failures are not counted against each other: a pipeline working through a series "
      "of different problems is allowed to keep going.\r\n"
      "\r\n"
      "This is the component that stands between an unattended pipeline and a large bill. Three is "
      "a sensible number.",
      w=320, h=400)

# --------------------------------------------------------------------------- 17 the return paths

fb_stall, co_fb = back(D, stall, "Success Signal", log, "Feedback Signal",
                       4400, 1300, 1300, 1300, nick="complaints to the log")
fb_rep = place(D, "Feedback", 5520, 1300, nick="geometry report to the log")
wire(fb_rep, "Signal", georep, "Signal")
fb_rep.AddCollector(co_fb.InstanceGuid)

back(D, call, "Success Signal", log, "Response Signal",
     2260, 1500, 1300, 1500, nick="reply back to the log")

panel(D, 1560, 1230,
      "TWO RETURN PATHS on this preset.\r\n"
      "\r\n"
      "COMPLAINTS - the Stall Guard's output and the Geometry Report both go to the Conversation "
      "Log's FEEDBACK SIGNAL. Note that is TWO Feedback nodes sharing ONE Collector: they have the "
      "same destination, and the Collector gathers everything that arrives in a round into a "
      "single signal.\r\n"
      "\r\n"
      "REPLY - the model's answer, to Response Signal, as in every preset.\r\n"
      "\r\n"
      "A Collector can be shared by several senders going to the same input. It can NEVER be "
      "shared between two different destinations.",
      w=520, h=330, colour=WARN_ORANGE)

panel(D, 1560, 1620,
      "NO TOOLS ON THIS PRESET, deliberately.\r\n"
      "\r\n"
      "Preset 03 shows how to add them, and the wiring is exactly the same here: a Router off the "
      "LLM Call's Tool Calls output, a Tools Present grounder, and two more return paths. "
      "COMPONENT SEARCH is the one worth adding - it lets the model look a component up in the "
      "same catalog the Resolver checks against, instead of guessing and being corrected.\r\n"
      "\r\n"
      "It is left out here for two reasons. The chain is the lesson, and adding tools doubles the "
      "wiring you have to read to see it. And tools need a model that can call them: Claude Code "
      "cannot, so the moment you add a Router you also have to change the model.",
      w=520, h=330)

# --------------------------------------------------------------------------- 18 what to try

panel(D, 5800, 1180,
      "THINGS TO TRY\r\n"
      "\r\n"
      "1. \"Build me a grid of circles, 10 by 10, that get bigger towards the middle.\" Watch the "
      "chat window: you will probably see at least one guardrail complaint and a correction before "
      "anything lands.\r\n"
      "\r\n"
      "2. When it has built something, say \"now make the spacing adjustable with a slider\". It "
      "edits what it placed rather than building a second one - that is Canvas State and Physalia "
      "Group Components doing their job.\r\n"
      "\r\n"
      "3. Open SIGNAL TRACE from the chat window header and read the round backwards. Every signal, "
      "in order, with who sent it.\r\n"
      "\r\n"
      "4. Right-click Component Catalog and cut it down to two or three tabs. The definitions get "
      "simpler and more reliable.\r\n"
      "\r\n"
      "5. Ask for something impossible and watch the Stall Guard park the loop instead of letting "
      "it grind.\r\n"
      "\r\n"
      "6. When you dislike a result: Component Transmitter's right-click menu, UNDO LAST "
      "PLACEMENT.",
      w=520, h=520)

commit_build(D, "build preset 04")
solve(D)
pick(D, model, "Model", "sonnet")
pick(D, sysp, "Preamble", "Incremental Node Graph.txt")
pick(D, sysp, "Schema", "Incremental Node Graph.json")
solve(D)
write_dump(D, DUMP)
say("preamble:", [str(v) for v in pin(sysp, "in", "Preamble").VolatileData.AllData(True)])
say("schema:", [str(v) for v in pin(sysp, "in", "Schema").VolatileData.AllData(True)])
bad = sweep(D, NAME)
save_phy(H, OUT,
         description="The flagship: the model writes a Grasshopper definition as JSON, seven "
                     "guardrails check it, and the Component Transmitter places it on your canvas. "
                     "Anything a guardrail objects to goes back as a complaint and it tries again.",
         chat_text="Building on the Canvas\r\n\r\n"
                   "Describe what you want and this pipeline builds it on your Grasshopper canvas "
                   "as real, editable components - after seven checks have agreed the definition "
                   "is sound.\r\n\r\n"
                   "Try: \"build me a grid of circles, 10 by 10, that get bigger towards the "
                   "middle.\" Then: \"now make the spacing adjustable with a slider.\"\r\n\r\n"
                   "It builds in stages and shows you each one. If you dislike a result, use Undo "
                   "Last Placement on the Component Transmitter's right-click menu.")
say("PROBLEMS:", bad)
