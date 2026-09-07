# Copyright (c) 2026 Physalia Contributors
# SPDX-License-Identifier: AGPL-3.0-or-later
"""
Preset 03 - "Tools the Model Can Call".

Grounding tells the model things. Tools let it DO things and find things out mid-answer. The new
machinery is the Router - it takes the calls the model made and hands each one to the node that
implements it - plus a second and third wireless return path.
"""

# The ONE machine-specific line in this file. Set ROOT before exec'ing this script to build
# from a checkout somewhere else:  ROOT = r"D:\\code\\Physalia"
try:
    ROOT
except NameError:
    ROOT = r"C:\Users\rober\repos\Physalia"
exec(open(ROOT + r"\tools\presets\phybuild.py").read())

NAME = "03 - Tools the Model Can Call"
OUT = PRESETS + r"\%s.phy" % NAME
DUMP = SCRATCH + r"\dump03.txt"

host = clear_host()
H, D = new_harness(host, 200, 200, name="tools-the-model-can-call")

# --------------------------------------------------------------------------- the intro

panel(D, 40, 40,
      "TOOLS THE MODEL CAN CALL\r\n"
      "\r\n"
      "Grounding tells the model things. TOOLS let it do things, and find things out in the middle "
      "of answering.\r\n"
      "\r\n"
      "The new component is the ROUTER. When the model decides to call a tool, the LLM Call hands "
      "the call to the Router, which sends it to whichever node implements that tool. The node "
      "does the work, its answer goes back to the Router, and the whole round of results is added "
      "to the conversation as a turn - so the model carries on with the answer in hand. All of "
      "that happens before you see a reply.\r\n"
      "\r\n"
      "This preset has three wireless return paths instead of one. Read them in stages 8, 9 and "
      "10; they are the only genuinely new wiring here.",
      w=900, h=260, colour=INTRO_GREEN)

# --------------------------------------------------------------------------- 1 the chat

title(D, 60, 340, "1 - WHERE YOU TYPE", w=270, h=44)
chat = place(D, "Chat", 120, 470, nick="Chat")
panel(D, 60, 560,
      "As before. If the loop itself is still unfamiliar, read \"01 - Talk to a Model\" and "
      "\"02 - What the Model Knows\" first.",
      w=250, h=110)

# --------------------------------------------------------------------------- 2 system prompt

title(D, 390, 340, "2 - THE SYSTEM PROMPT", w=330, h=44)
sysp = place(D, "System Prompt", 590, 470, nick="System Prompt")
blank_input(D, sysp, "Schema", 410, 496, label="no schema file")
panel(D, 390, 700,
      "This one uses a real PREAMBLE file - \"Rhino Scripting\" out of "
      "Files/SYSTEM_PROMPTS/PREAMBLE. It teaches the model how to write Python against the live "
      "Rhino document, which is what the Drive Rhino tool in stage 7 runs.\r\n"
      "\r\n"
      "Pick a different file from the little dropdown to the left. SCHEMA is left empty here - "
      "schemas are for pipelines where the model has to answer in strict JSON, which is presets 04 "
      "and 05.",
      w=330, h=260)

# --------------------------------------------------------------------------- 3 grounding

title(D, 800, 340, "3 - GROUNDING", w=290, h=44)
rhino = place(D, "Rhino Document", 870, 440, nick="Rhino Document")
toolsp = place(D, "Tools Present", 870, 500, nick="Tools Present")
panel(D, 800, 600,
      "RHINO DOCUMENT as in preset 02.\r\n"
      "\r\n"
      "TOOLS PRESENT is the one that matters here. It scans the canvas for tool nodes wired to a "
      "Router and tells the model which tools it has - by name, with what each one is for.\r\n"
      "\r\n"
      "Without it the tools are still wired and would still answer, but the model is never told "
      "they exist, so it never calls one. If a tool seems to be ignored, check this is wired "
      "first.\r\n"
      "\r\n"
      "Each tool node also has an ADVERTISE TO THE MODEL switch on its own right-click menu. "
      "Turning one off parks it: still wired, still able to answer, never mentioned. That is how "
      "you narrow the choice without unwiring anything.",
      w=290, h=400)

# --------------------------------------------------------------------------- 4 conversation log

title(D, 1180, 340, "4 - THE RUNNING CONVERSATION", w=330, h=44)
log = place(D, "Conversation Log", 1340, 470, nick="Conversation Log")
wire(log, "System Prompt", sysp, "System Prompt")
wire(log, "Prompt Signal", chat, "Prompt Signal")
wire(log, "Grounding", rhino, 0)
wire(log, "Grounding", toolsp, 0)
panel(D, 1180, 600,
      "Same component, and now two of its three return inputs are in use.\r\n"
      "\r\n"
      "RESPONSE SIGNAL takes the model's reply.\r\n"
      "\r\n"
      "LLM TOOL SIGNAL takes the results of a round of tool calls, which are recorded as a real "
      "conversation turn - so the model can see what its own tool call returned.\r\n"
      "\r\n"
      "FEEDBACK SIGNAL is for guardrail complaints, and is unused here. Preset 04 uses it.",
      w=330, h=330)

# --------------------------------------------------------------------------- 5 the call

title(D, 1600, 340, "5 - ASKING THE MODEL", w=310, h=44)
model = place(D, "Codex Model", 1760, 430, nick="Codex Model")
call = place(D, "LLM Call", 1760, 540, nick="LLM Call")
wire(call, "Model", model, "Model")
wire(call, "Signal", log, "Signal")
cancel = boolean(D, 1630, 585, False, nick="stop", toggle=False)
wire(call, "Cancel", cancel, 0)
panel(D, 1600, 700,
      "The LLM Call has a third output: TOOL CALLS. When the model asks to use a tool, the call "
      "comes out there rather than as text on Success Signal.\r\n"
      "\r\n"
      "A round can carry several calls at once - Anthropic models often ask for two or three "
      "together. The Router deals with that."
      "\r\n\r\n" "READ THIS BEFORE SWAPPING THE MODEL. The model here is CODEX, not Claude Code, and it has "
      "to be one that can call tools at all. Of the two keyless command-line models, only Codex "
      "can: Claude Code ignores the tool list completely, so it is told the tools exist by the "
      "grounding, tries to call one, and reports that no such tool is available. Nothing on the "
      "canvas looks wrong when that happens."
      "\r\n\r\n" "Anthropic, Gemini and OpenAI-compatible Model nodes all support tools properly - they just "
      "need an API key, set up from the chat window's home screen."
      "\r\n" "\r\n"
      "One practical thing about Codex: its model list is fetched LIVE from the CLI and it "
      "changes. If a round fails saying the model does not exist or you do not have access "
      "to it, open the little dropdown beside the Codex Model node and pick again - the list "
      "you are looking at is current.",
      w=310, h=600)

# --------------------------------------------------------------------------- 6 the router

title(D, 1980, 340, "6 - THE ROUTER", w=300, h=44)
router = place(D, "Router", 2100, 505, nick="Router")
wire(router, "Tool Calls", call, "Tool Calls")
panel(D, 1960, 860,
      "The ROUTER is the switchboard. One output per tool, plus a FEEDBACK output carrying the "
      "whole round's results back to the conversation.\r\n"
      "\r\n"
      "Add and remove outputs with the little + and - on its edge when you zoom in, then wire each "
      "one to a tool node.\r\n"
      "\r\n"
      "The outputs RENAME THEMSELVES after the tool they end up connected to - so an output still "
      "reading \"T1\" is a wire you have not finished, and that rename is the cheapest proof that a "
      "tool node is set up correctly.\r\n"
      "\r\n"
      "If the model calls something that is not wired, the Router tells it so and lists the tools "
      "that do exist, rather than failing silently.",
      w=300, h=300)

# --------------------------------------------------------------------------- 7 the tools

title(D, 2400, 340, "7 - THE TOOLS THEMSELVES", w=330, h=44)
drive = place(D, "Drive Rhino", 2470, 440, nick="Drive Rhino")
rcs = place(D, "RhinoCommon Search", 2470, 500, nick="RhinoCommon Search")
readurl = place(D, "Read URL", 2470, 560, nick="Read URL")
websearch = place(D, "Web Search", 2470, 620, nick="Web Search")
askh = place(D, "Ask Human", 2470, 680, nick="Ask Human")
mem = place(D, "Memory", 2470, 756, nick="Memory")
memfolder = input_panel(D, 2250, 778, "tools-demo", w=190, h=40, nick="memory folder")
wire(mem, "Memory Folder", memfolder, 0)
csearch = place(D, "Component Search", 2470, 836, nick="Component Search")
catalog = place(D, "Component Catalog", 2330, 858, nick="Component Catalog", sub="Grounding")
wire(csearch, "Component Catalog", catalog, 0)
rhinogeo = place(D, "Create/Ref. Rhino Geometry", 2470, 906, nick="Create/Ref. Rhino Geometry")

TOOLS = [drive, rcs, readurl, websearch, askh, mem, csearch, rhinogeo]
router_slots(router, len(TOOLS) - 1)   # the Router ships with one slot already
for i, t in enumerate(TOOLS):
    wire(t, "Signal", router, i)

panel(D, 2400, 960,
      "EIGHT TOOLS, each its own node, each with its own switches on its right-click menu.\r\n"
      "\r\n"
      "DRIVE RHINO runs Python against your live Rhino document. This is the big one: whatever the "
      "model prints comes straight back to it, so it can ask your document any question it likes "
      "by writing three lines of code - which is why Physalia ships no separate \"inspect the "
      "document\" tool. The whole run is one undo step.\r\n"
      "\r\n"
      "RHINOCOMMON SEARCH looks up the Rhino API, so the Python it writes uses methods that "
      "actually exist.\r\n"
      "\r\n"
      "READ URL fetches a web page as text. No key needed.\r\n"
      "\r\n"
      "WEB SEARCH searches the web. This one DOES need a Tavily key - set it up on the chat "
      "window's home screen. Without a key the tool is wired but every call fails, so switch its "
      "Advertise flag off until you have one.\r\n"
      "\r\n"
      "ASK HUMAN puts a question to you as a card in the chat window - typed answer, a choice, or "
      "\"select the ones you mean in Rhino\". Every way it can go wrong returns \"unanswered\" "
      "rather than a guess.\r\n"
      "\r\n"
      "MEMORY is notes the model writes for its future self, kept as files. The white box gives "
      "this pipeline's own set a name; two Memory nodes given the same name share their notes, "
      "which is how a rebuilt pipeline picks up where it left off."
      "\r\n" "\r\n"
      "COMPONENT SEARCH looks a Grasshopper component up by name or by what it does, in the "
      "catalog wired beside it. Cheap, and it is what stops the model guessing at a component "
      "name - see preset 04, where guessing costs a whole round of correction."
      "\r\n" "\r\n"
      "CREATE/REF. RHINO GEOMETRY does two things and the second is the interesting one: it "
      "makes geometry in the RHINO document, and then drops a parameter on your Grasshopper "
      "canvas REFERENCING it. So the model can hand your definition a real Rhino input rather "
      "than only describing one.",
      w=330, h=760)

# --------------------------------------------------------------------------- 8 results home

fb_res, co_res = back(D, drive, "Result", router, "Results",
                      2760, 1250, 1400, 1400, nick="tool results")
for t in TOOLS[1:]:
    wire(fb_res, "Signal", t, "Result")
panel(D, 1920, 1180,
      "RETURN PATH 1 - the tool answers.\r\n"
      "\r\n"
      "Every tool's RESULT goes into ONE Feedback node, and one Collector hands the whole lot to "
      "the Router's RESULTS input. The Collector gathers everything that arrives in a round into a "
      "single signal, which is exactly what the Router wants: the model may have made three calls, "
      "and it needs all three answers before the round is finished.\r\n"
      "\r\n"
      "So a new tool needs one extra wire here, not a new pair.",
      w=460, h=300, colour=WARN_ORANGE)

# --------------------------------------------------------------------------- 9 tool round home

back(D, router, "Feedback", log, "LLM Tool Signal",
     2760, 1620, 1000, 1620, nick="tool round to the log")
panel(D, 1920, 1520,
      "RETURN PATH 2 - the round becomes a turn.\r\n"
      "\r\n"
      "Once the Router has all the results it emits them on FEEDBACK, and this pair carries them "
      "to the Conversation Log's LLM TOOL SIGNAL. They are written into the conversation as a "
      "real turn, which is what lets the model see its own tool output and keep going.\r\n"
      "\r\n"
      "A tool that answers with a picture - Take Snapshot, or a rendered PDF page - rides along "
      "here as an attachment on the same turn.",
      w=460, h=300, colour=WARN_ORANGE)

back(D, call, "Success Signal", log, "Response Signal",
     1600, 1900, 1000, 1900, nick="reply back to the log")
panel(D, 1920, 1860,
      "RETURN PATH 3 - the reply, exactly as in presets 01 and 02.\r\n"
      "\r\n"
      "Three separate pairs, because a Collector cannot be shared between two different "
      "destination inputs. Give each return path its own.",
      w=460, h=200, colour=WARN_ORANGE)

# --------------------------------------------------------------------------- 10 the reply

title(D, 2840, 340, "8 - WHAT CAME BACK", w=300, h=44)
dec = place(D, "Deconstruct Signal", 2900, 470, nick="Deconstruct Signal")
wire(dec, "Signal", call, "Success Signal")
reply = panel(D, 3180, 400, "the model's reply lands here", w=290, h=170, colour=OUTPUT_GREY)
reply.AddSource(pin(dec, "out", "Payload"))
script = panel(D, 3180, 600, "the last Python it ran lands here", w=290, h=190, colour=OUTPUT_GREY)
script.AddSource(pin(drive, "out", "Last Script"))
panel(D, 2840, 820,
      "The grey panels are worth having while you learn. The lower one shows the Python that Drive "
      "Rhino actually ran - the fastest way to see what the model is really doing, and the fastest "
      "way to spot that it has misunderstood your document.\r\n"
      "\r\n"
      "Several tools publish something on a wire like this: Ask Human puts the answer and the "
      "Rhino selection on outputs, Download File puts the paths of what it fetched. That is the "
      "point of a tool being a node - the result is available to the rest of your definition, not "
      "just to the model.",
      w=300, h=330)

# --------------------------------------------------------------------------- 11 what to try

panel(D, 2840, 1180,
      "THINGS TO TRY\r\n"
      "\r\n"
      "1. \"How many curves are on the Beams layer? Check, do not guess.\" Watch the Router output "
      "light up and the script appear in the grey panel.\r\n"
      "\r\n"
      "2. \"Draw a 2m cube at the origin on a new layer called Test.\" Then undo it - the whole "
      "run is one undo step.\r\n"
      "\r\n"
      "3. \"Remember that this project is in millimetres and the grid is 6m.\" Then start a fresh "
      "conversation and ask what it remembers.\r\n"
      "\r\n"
      "4. Turn Web Search's Advertise switch off and ask something that needs the web. It will say "
      "it cannot look things up rather than trying.",
      w=520, h=340)

commit_build(D, "build preset 03")
solve(D)
# The Codex model list is fetched LIVE from the CLI and changes under you - it went from
# gpt-5.5/5.4/5.4-mini to gpt-5.6-sol/terra/luna/5.5/5.4-mini inside one session here. So the
# Picker is deliberately NOT pinned: left alone it snaps to whatever the CLI offers first,
# which self-heals. A pinned name that the account cannot use answers 404 and does not.
pick(D, sysp, "Preamble", "Rhino Scripting.txt")
solve(D)
solve(D)   # the Router renames its outputs once the tool nodes have advertised
write_dump(D, DUMP)
say("router outputs:", [p.NickName for p in router.Params.Output])
bad = sweep(D, NAME)
save_phy(H, OUT,
         description="Adds tools: a Router plus eight tool nodes the model can call mid-answer - "
                     "run Python in Rhino, search the RhinoCommon API and the Grasshopper "
                     "catalog, read a web page, search the web, ask you a question, keep notes, "
                     "and make Rhino geometry. Three wireless return paths.",
         chat_text="Tools the Model Can Call\r\n\r\n"
                   "The model can now do things as well as say them: run Python against your Rhino "
                   "document, look up the Rhino API and the Grasshopper component catalog, read a "
                   "web page, ask you a question, make geometry in Rhino, and keep "
                   "notes between conversations.\r\n\r\n"
                   "Try: \"how many curves are on each layer? Check, do not guess.\" Then look at "
                   "the panel showing the Python it ran.\r\n\r\n"
                   "Two notes. This preset uses CODEX rather than Claude Code, because Claude "
                   "Code cannot call Physalia's tools at all. And Web Search needs a Tavily "
                   "key, set up from the home screen - everything else works as it stands.")
say("PROBLEMS:", bad)
