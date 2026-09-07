# Copyright (c) 2026 Physalia Contributors
# SPDX-License-Identifier: AGPL-3.0-or-later
"""
Preset 11 - "Reading Live Data".

Two ways of reaching outside Rhino for real information: an HTTP API you have set up yourself, and
an MCP server - somebody else's tools, borrowed. Both are configured in the chat window rather than
on the canvas, because the settings are per-machine and often contain a key.
"""

exec(open(r"C:\Users\rober\repos\Physalia\tools\presets\phybuild.py").read())

NAME = "11 - Reading Live Data"
OUT = r"C:\Users\rober\repos\Physalia\wip_presets\%s.phy" % NAME
DUMP = r"C:\Users\rober\AppData\Local\Temp\claude\dump11.txt"

host = clear_host()
H, D = new_harness(host, 200, 200, name="reading-live-data")

TITLE_Y = 400
SPINE = 540

# --------------------------------------------------------------------------- the intro

panel(D, 40, 40,
      "READING LIVE DATA\r\n"
      "\r\n"
      "Web search and reading a page get you prose. This preset gets you DATA - the actual "
      "records, on a wire, ready for your definition to build from.\r\n"
      "\r\n"
      "API CALL reads an HTTP API you have configured: a city's open data portal, your practice's "
      "own project database, a weather service. The model picks the path and the query; it can "
      "never pick the host, and it can never set a header. That is the whole security posture, and "
      "it is enforced rather than asked for.\r\n"
      "\r\n"
      "MCP SERVER connects to somebody else's tool server - the growing ecosystem of MCP servers "
      "for Notion, Figma, filesystems, databases, whatever - and advertises ALL of its tools at "
      "once. It is the only node in Physalia that offers more than one tool, because what a server "
      "offers is only discovered when you connect to it.\r\n"
      "\r\n"
      "BOTH ARE CONFIGURED IN THE CHAT WINDOW, on its Home screen, not here. Those settings are "
      "per-machine and often hold a key, so they deliberately do not travel inside a preset.",
      w=940, h=320, colour=INTRO_GREEN)

# --------------------------------------------------------------------------- 1 the chat

title(D, 60, TITLE_Y, "1 - WHERE YOU TYPE", w=260, h=44)
chat = place(D, "Chat", 130, SPINE, nick="Chat")
panel(D, 60, 620,
      "\"How many street trees are there on this block, and what species?\"\r\n"
      "\r\n"
      "Presets 01 to 03 explain the loop, the grounding and the Router. This one assumes them.",
      w=240, h=200)

# --------------------------------------------------------------------------- 2 system prompt

title(D, 380, TITLE_Y, "2 - THE SYSTEM PROMPT", w=300, h=44)
sysp = place(D, "System Prompt", 560, SPINE, nick="System Prompt")
blank_input(D, sysp, "Preamble", 380, 500, label="no preamble file")
blank_input(D, sysp, "Schema", 380, 546, label="no schema file")
extra = input_panel(D, 320, 620,
                    "When you query an API, say what you asked for and how many records came back. "
                    "Never claim a query returned everything unless the count says so.",
                    w=240, h=120, nick="my own instructions")
wire(sysp, "Additional Prompt", extra, 0)
panel(D, 380, 770,
      "That second sentence is worth having. A paged API hands back its first hundred rows, and a "
      "model that is not paying attention will happily summarise them as if they were the whole "
      "answer.\r\n"
      "\r\n"
      "The tool works hard to prevent that - see stage 6 - but saying it in the prompt as well "
      "costs nothing.",
      w=300, h=250)

# --------------------------------------------------------------------------- 3 grounding

title(D, 740, TITLE_Y, "3 - WHAT IT KNOWS", w=280, h=44)
rhino = place(D, "Rhino Document", 810, 480, nick="Rhino Document")
units = place(D, "Document Units Grounding", 810, 530, nick="Document Units")
toolsp = place(D, "Tools Present", 810, 580, nick="Tools Present")
panel(D, 740, 640,
      "TOOLS PRESENT does something extra on this preset. As well as listing the tools, it collects "
      "each node's standing INSTRUCTION and puts it in the prompt - and the API Call node's "
      "instruction is the DESCRIPTION you type on it in stage 6.\r\n"
      "\r\n"
      "That is why the description goes in the prompt rather than in the tool definition. A tool "
      "description is read once the model is already weighing that call; a prompt is read before it "
      "has decided there is anything worth calling. If it does not know your API has a "
      "species_name field, it will not think to ask about species.\r\n"
      "\r\n"
      "DOCUMENT UNITS matters more than usual here, because open data arrives in metres and your "
      "file may well be in millimetres.",
      w=280, h=400)

# --------------------------------------------------------------------------- 4 conversation log

title(D, 1100, TITLE_Y, "4 - THE RUNNING CONVERSATION", w=320, h=44)
log = place(D, "Conversation Log", 1260, SPINE, nick="Conversation Log")
wire(log, "System Prompt", sysp, "System Prompt")
wire(log, "Prompt Signal", chat, "Prompt Signal")
for g in (rhino, units, toolsp):
    wire(log, "Grounding", g, 0)
panel(D, 1100, 640,
      "Nothing new. Worth knowing what does NOT go into the conversation, though: the records "
      "themselves.\r\n"
      "\r\n"
      "What goes back to the model is a SUMMARY - how many records arrived, how many matched in "
      "total, the field names, and one sample record. The data itself goes on a wire. A hundred "
      "full records in a conversation would be resent on every subsequent turn for the rest of the "
      "session.",
      w=320, h=300)

# --------------------------------------------------------------------------- 5 the call

title(D, 1520, TITLE_Y, "5 - ASKING THE MODEL", w=300, h=44)
model = place(D, "Codex Model", 1660, 480, nick="Codex Model")
call = place(D, "LLM Call", 1660, 600, nick="LLM Call")
wire(call, "Model", model, "Model")
wire(call, "Signal", log, "Signal")
cancel = boolean(D, 1530, 645, False, nick="stop", toggle=False)
wire(call, "Cancel", cancel, 0)
router = place(D, "Router", 1660, 760, nick="Router")
wire(router, "Tool Calls", call, "Tool Calls")
router_slots(router, 1)
panel(D, 1520, 860,
      "CODEX, because this preset is all tools and Claude Code cannot call them - see preset 03.\r\n"
      "\r\n"
      "One practical thing about Codex: its model list is fetched LIVE from the CLI and it changes. "
      "If a round fails saying the model does not exist or you do not have access to it, open the "
      "dropdown beside the Codex Model node and pick again.\r\n"
      "\r\n"
      "The Router has two outputs here. An MCP server contributes ONE Router output no matter how "
      "many tools it turns out to have - the node fans them out internally, and it namespaces every "
      "name so two servers both offering \"search\" cannot collide.",
      w=300, h=340)

# --------------------------------------------------------------------------- 6 the API

title(D, 1900, TITLE_Y, "6 - AN HTTP API OF YOUR OWN", w=340, h=44)
api = place(D, "API Call", 2140, SPINE, nick="API Call")
wire(api, "Signal", router, 0)
api_pick = add_picker(D, api, "Endpoint", 1920, 500)
api_desc = input_panel(D, 1900, 610,
                       "Pick your endpoint from the dropdown, then describe it here: which "
                       "datasets exist, what the useful fields are called, and anything the model "
                       "would otherwise have to guess.",
                       w=220, h=140, nick="what this API is")
wire(api, "Description", api_desc, 0)
api_max = slider(D, 1920, 770, 500, 50, 5000, nick="max records")
wire(api, "Max Records", api_max, 0)
panel(D, 1900, 830,
      "SET THIS UP FIRST: chat window Home screen, API CALLS. Give the API a name and a base URL, "
      "and a key if it needs one. There is a TEST button that does a plain GET and writes "
      "nothing.\r\n"
      "\r\n"
      "Then pick it from the little dropdown here.\r\n"
      "\r\n"
      "THE DESCRIPTION lives on this NODE, not in the store, and that is deliberate: the store is "
      "per-machine, so a pipeline shared without it would arrive with its wiring and none of its "
      "knowledge. Typing what the fields are called is the single highest-value thing you can do "
      "for this node.\r\n"
      "\r\n"
      "THE TOOL WALKS THE PAGING ITSELF. A 100-record page against a 145-record query would "
      "otherwise deliver a fifth of the data with nothing saying so. It measures the page size "
      "rather than assuming one, keeps whatever it gathered if something fails part way, and says "
      "THIS IS NOT THE WHOLE RESULT SET with the numbers when it is incomplete.\r\n"
      "\r\n"
      "MAX RECORDS is your ceiling on all of that. The model asks for a number too, and gets the "
      "smaller of the two: its judgement about this question, bounded by your budget for all of "
      "them. Paging spends somebody's quota, so it defaults to one page unless asked.\r\n"
      "\r\n"
      "GET only, always. A model-authored request body is a far larger surface than a query "
      "string, and an API that writes belongs behind a node a human wired on purpose.",
      w=340, h=700)

# --------------------------------------------------------------------------- 7 MCP

title(D, 2320, TITLE_Y, "7 - SOMEBODY ELSE'S TOOLS", w=320, h=44)
mcp = place(D, "MCP Server", 2560, SPINE, nick="MCP Server")
wire(mcp, "Signal", router, 1)
mcp_pick = add_picker(D, mcp, "Server", 2340, 540)
mcp_stat = panel(D, 2320, 620, "which server, and how many tools it offers", w=320, h=90,
                 colour=OUTPUT_GREY)
mcp_stat.AddSource(pin(mcp, "out", "Status"))
panel(D, 2320, 1560,
      "SET THIS UP FIRST TOO: chat window Home screen, CONFIGURE MCP CONNECTIONS. You can paste a "
      "server's published command straight in, or fill the form. It takes the standard mcpServers "
      "block, so a config you already use with another MCP host pastes in whole.\r\n"
      "\r\n"
      "Then pick the server from the dropdown here. The STATUS output above tells you which server "
      "and how many tools came back, which is the quickest check that a connection is real.\r\n"
      "\r\n"
      "SIGN IN DURING SETUP, not on the first solve. The setup page has a \"Save and sign in\" "
      "button that connects then and there and reports the tool count - so a browser handshake "
      "happens while you are sitting in front of it, rather than the first time a pipeline you had "
      "forgotten about wakes up.\r\n"
      "\r\n"
      "Physalia is a CLIENT here, not a server. And it deliberately does not offer servers the "
      "ability to run inference through your pipeline: a third-party server that could do that "
      "would be spending your tokens with nothing on the canvas recording it.\r\n"
      "\r\n"
      "Local servers - the ones that start with a command like npx - work in-process. Remote ones "
      "go through a small bridge program that ships beside the plug-in; if it is missing you are "
      "told, but only when you actually ask for a remote server, so a local-only setup is "
      "unaffected.",
      w=320, h=640)

# --------------------------------------------------------------------------- 8 the data

title(D, 2720, TITLE_Y, "8 - THE RECORDS THEMSELVES", w=320, h=44)
records = panel(D, 2720, 480, "one item per RECORD lands here", w=320, h=200, colour=OUTPUT_GREY)
records.AddSource(pin(api, "out", "Response"))
api_stat = panel(D, 2720, 700, "how the query went", w=320, h=100, colour=OUTPUT_GREY)
api_stat.AddSource(pin(api, "out", "Status"))
panel(D, 2720, 820,
      "THIS PANEL IS WHY THE NODE EXISTS.\r\n"
      "\r\n"
      "The RESPONSE output is a LIST with ONE ITEM PER RECORD - already unwrapped from whatever "
      "envelope the API wraps its rows in, and already joined across every page the tool walked. "
      "Wire it into a JSON parser and you have data your definition can build from.\r\n"
      "\r\n"
      "Handing over the raw page bodies instead was the first design, and it was wrong in a way "
      "worth knowing about: the consumer had to unwrap each envelope, know which key THAT api "
      "nests its rows under, and concatenate - and worse, the shape CHANGED with the result size, "
      "so a script written against a one-page test query broke on the real multi-page one.\r\n"
      "\r\n"
      "The model is told this shape in three separate places, because saying it once was the "
      "original mistake.\r\n"
      "\r\n"
      "A body with no record collection in it at all - a single document, or something that is not "
      "JSON - comes through as one item for the body, and the FIRST page decides the shape for the "
      "whole call, so the list can never be a mixture.",
      w=320, h=560)

# --------------------------------------------------------------------------- 9 return paths

fb_res, co_res = back(D, api, "Result", router, "Results",
                      3120, 1240, 1200, 1240, nick="tool results")
wire(fb_res, "Signal", mcp, "Result")
back(D, router, "Feedback", log, "LLM Tool Signal",
     1680, 1400, 1200, 1400, nick="tool round to the log")
back(D, call, "Success Signal", log, "Response Signal",
     1520, 1560, 1200, 1560, nick="reply back to the log")
panel(D, 400, 1180,
      "THE THREE RETURN PATHS, as in preset 03: tool results to the Router, the finished round to "
      "the Conversation Log's LLM Tool Signal, and the reply to Response Signal. Both tool nodes "
      "share one Feedback node and one Collector, because they share a destination.\r\n"
      "\r\n"
      "An MCP tool that answers with an IMAGE comes back through here as an attachment on the "
      "tool-answering turn, exactly like Take Snapshot in preset 06.",
      w=520, h=280, colour=WARN_ORANGE)

# --------------------------------------------------------------------------- 10 what to try

panel(D, 400, 1500,
      "THINGS TO TRY\r\n"
      "\r\n"
      "1. Set up one API first - a city open-data portal is the easiest, since most need no key. "
      "Vancouver, Toronto, London and New York all publish an Opendatasoft or CKAN endpoint. Use "
      "the TEST button.\r\n"
      "\r\n"
      "2. Fill in the description panel properly. \"Datasets include street-trees with fields "
      "species_name, diameter, on_street\" changes what the model thinks to ask for.\r\n"
      "\r\n"
      "3. Ask something that needs paging: \"how many records are there in total, and fetch at "
      "least 300 of them\". Read the Status panel and check the totals agree.\r\n"
      "\r\n"
      "4. Wire the Response output into a Grasshopper JSON parser and pull the coordinates out. "
      "That is the whole point - the model found the data, your definition uses it.\r\n"
      "\r\n"
      "5. For MCP, the easiest first server is the reference one: npx -y "
      "@modelcontextprotocol/server-everything. Paste that command into the setup page and see the "
      "Status output fill in with a tool count.",
      w=520, h=460)

commit_build(D, "build preset 11")
solve(D)
solve(D)
write_dump(D, DUMP)
say("router outputs:", [p.NickName for p in router.Params.Output])
say("endpoints offered:", picker_values(D, api, "Endpoint"))
say("servers offered:", picker_values(D, mcp, "Server"))
bad = sweep(D, NAME)
# Which endpoints exist is a fact about THIS machine. Blank the saved choice before writing, or the
# preset ships with somebody else's endpoint name in it - and on a machine with no endpoints at all
# a stale name reports "not found" where an empty one asks to be picked.
# Blanking the Picker is NOT enough. ApiCall serializes its own _endpointName, and the Router and
# the Tool output both carry the derived tool name (api__Vancouver_Open_Data) - so the file shipped
# with this machine's endpoint in it three times over. Found by reading the archive bytes, not by
# any component complaining. Clear all three.
clear_pick(D, api, "Endpoint")
clear_pick(D, mcp, "Server")
forget_setting(api, "_endpointName")
reset_derived_names(router, api, mcp)
save_phy(H, OUT,
         description="Reading real data: an HTTP API you configure yourself, with the tool walking "
                     "the paging and every record landing on a wire, plus an MCP server connection "
                     "for borrowing somebody else's tools. Both are set up in the chat window.",
         chat_text="Reading Live Data\r\n\r\n"
                   "This pipeline can read an HTTP API and an MCP server - and the records come "
                   "back onto your canvas, one item per record, not just into the "
                   "conversation.\r\n\r\n"
                   "SET UP AT LEAST ONE FIRST, from this window's Home screen: API CALLS for an "
                   "HTTP endpoint, or CONFIGURE MCP CONNECTIONS for a tool server. Then pick it "
                   "from the dropdown on the node inside, and describe what the API holds - that "
                   "description is what the model reads before it decides what to ask for.\r\n\r\n"
                   "A city open-data portal is the easiest place to start; most need no key.")
say("PROBLEMS:", bad)
