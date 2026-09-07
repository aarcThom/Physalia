# Copyright (c) 2026 Physalia Contributors
# SPDX-License-Identifier: AGPL-3.0-or-later
"""
Preset 13 - "Choosing and Tuning a Model".

A reference card. The loop runs on Claude Code so it works with nothing set up, and beside it sits
one fully wired column per key-based provider, each with its Model API and its Tweaker - so
swapping is one wire. Plus Model Information, which answers the questions you actually have about a
model before you commit a pipeline to it.
"""

# The ONE machine-specific line in this file. Set ROOT before exec'ing this script to build
# from a checkout somewhere else:  ROOT = r"D:\\code\\Physalia"
try:
    ROOT
except NameError:
    ROOT = r"C:\Users\rober\repos\Physalia"
exec(open(ROOT + r"\tools\presets\phybuild.py").read())

NAME = "13 - Choosing and Tuning a Model"
OUT = PRESETS + r"\%s.phy" % NAME
DUMP = SCRATCH + r"\dump13.txt"

host = clear_host()
H, D = new_harness(host, 200, 200, name="choosing-and-tuning-a-model")

# --------------------------------------------------------------------------- the intro

panel(D, 40, 40,
      "CHOOSING AND TUNING A MODEL\r\n"
      "\r\n"
      "The LLM Call is the only component that talks to a model, and it takes ONE wire that says "
      "which. Everything on this canvas is about what goes on that wire.\r\n"
      "\r\n"
      "The loop in stage 1 runs on CLAUDE CODE, which drives a command-line tool you have already "
      "signed into - so it works with nothing set up at all. Stages 3 to 5 are the key-based "
      "alternatives, fully wired but not plugged in. Swapping one in is a single wire.\r\n"
      "\r\n"
      "Two things are worth knowing before you choose. Only some models can SEE, and only some can "
      "call TOOLS - and of the two keyless command-line models, Claude Code cannot call tools at "
      "all. It is told by the grounding that they exist, tries one, and reports that no such tool "
      "is available, with nothing on the canvas looking wrong. Stage 6 is how you check a model "
      "before you find that out the hard way.",
      w=940, h=300, colour=INTRO_GREEN)

# --------------------------------------------------------------------------- 1 the loop

title(D, 60, 380, "1 - A MINIMAL LOOP", w=300, h=44)
chat = place(D, "Chat", 130, 500, nick="Chat")
sysp = place(D, "System Prompt", 400, 500, nick="System Prompt")
blank_input(D, sysp, "Preamble", 200, 445, label="no preamble file")
blank_input(D, sysp, "Schema", 200, 491, label="no schema file")
extra = input_panel(D, 160, 545,
                    "Answer briefly. If you are asked what you are, say which model you are.",
                    w=200, h=80, nick="my own instructions")
wire(sysp, "Additional Prompt", extra, 0)
log = place(D, "Conversation Log", 660, 500, nick="Conversation Log")
wire(log, "System Prompt", sysp, "System Prompt")
wire(log, "Prompt Signal", chat, "Prompt Signal")
call = place(D, "LLM Call", 940, 500, nick="LLM Call")
wire(call, "Signal", log, "Signal")
cancel = boolean(D, 810, 545, False, nick="stop", toggle=False)
wire(call, "Cancel", cancel, 0)
reply = panel(D, 1180, 440, "the reply lands here", w=280, h=140, colour=OUTPUT_GREY)
rep_dec = place(D, "Deconstruct Signal", 980, 640, nick="Deconstruct Signal")
wire(rep_dec, "Signal", call, "Success Signal")
reply.AddSource(pin(rep_dec, "out", "Payload"))
errs = panel(D, 1180, 600, "errors land here", w=280, h=100, colour=ERROR_PINK)
errs.AddSource(pin(call, "out", "Fail Signal"))
back(D, call, "Success Signal", log, "Response Signal",
     1060, 780, 560, 780, nick="reply back to the log")
panel(D, 60, 740,
      "The smallest loop there is - preset 01 explains it properly. It is here only to give the "
      "model wire somewhere to go.\r\n"
      "\r\n"
      "The pink panel matters on this preset. A key that is missing, wrong, or belongs to an "
      "account without access to the model you named comes out on FAIL SIGNAL with the provider's "
      "own message on it. That message is almost always the fastest way to find out what is really "
      "wrong.",
      w=300, h=290)

# --------------------------------------------------------------------------- 2 keyless

title(D, 60, 1400, "2 - THE TWO KEYLESS MODELS", w=300, h=44)
cc = place(D, "Claude Code Model", 180, 1520, nick="Claude Code Model")
cx = place(D, "Codex Model", 180, 1640, nick="Codex Model")
wire(call, "Model", cc, "Model")
panel(D, 60, 1710,
      "These two drive a command-line tool you have already signed into, so there is no key to "
      "enter and nothing to configure. CLAUDE CODE is wired in.\r\n"
      "\r\n"
      "CLAUDE CODE CANNOT CALL TOOLS. It ignores the tool list completely. Everything else works: "
      "grounding, guardrails, transmitters, compaction. But the moment you add a Router you have to "
      "change the model.\r\n"
      "\r\n"
      "CODEX can call tools, and its model list is fetched LIVE from the CLI - which means it "
      "changes, and a name that worked yesterday can answer \"does not exist or you do not have "
      "access\". Re-pick from its dropdown when that happens. It is also markedly slower.\r\n"
      "\r\n"
      "Both are honest about what they cost you: they report no useful token figure at all, so a "
      "token budget is meaningless on them and Max Calls is the only cap that bounds one. See "
      "preset 08.",
      w=300, h=420)

# --------------------------------------------------------------------------- 3 anthropic

title(D, 1700, 380, "3 - ANTHROPIC", w=320, h=44)
a_api = place(D, "Model API", 1760, 480, nick="Model API", sub="Models")
a_model = place(D, "Anthropic Model", 2020, 500, nick="Anthropic Model")
wire(a_model, "Model API", a_api, "Model API")
a_max = slider(D, 1760, 570, 8192, 1024, 64000, nick="max tokens")
wire(a_model, "Max Tokens", a_max, 0)
a_tweak = place(D, "Anthropic Tweaker", 2280, 560, nick="Anthropic Tweaker")
wire(a_tweak, "Model", a_model, "Model")
a_temp = slider(D, 2020, 630, 1.0, 0.0, 1.0, nick="temperature", integer=False)
wire(a_tweak, "Temperature", a_temp, 0)
panel(D, 1700, 690,
      "THE SHAPE EVERY KEY-BASED PROVIDER USES: Model API, then the Model node, then optionally a "
      "Tweaker.\r\n"
      "\r\n"
      "MODEL API emits the endpoint AND the key on one wire, because they are one fact. A key on "
      "its own identifies nothing - several OpenAI-compatible services use the same protocol at "
      "different hosts. Set providers up on the chat window's Home screen; keys are stored "
      "encrypted for your user account, and an environment variable is checked first, which is the "
      "headless and shared-machine path. Nothing about your keys ever goes into a saved file or a "
      "preset.\r\n"
      "\r\n"
      "ANTHROPIC's temperature range is 0 to 1, not 0 to 2 - that slider is deliberately capped. "
      "MAX TOKENS is REQUIRED here, unlike everywhere else.\r\n"
      "\r\n"
      "THE TWEAKER is optional and the empty inputs are meaningful: leaving one blank means \"use "
      "the sensible default for this model\", which is not the same as any particular number. The "
      "newest Anthropic models REJECT a non-default temperature outright, and Physalia knows that "
      "and leaves the field off the request - so an untouched Tweaker is safe on any model, and "
      "that is the reason to leave it untouched unless you have a reason.\r\n"
      "\r\n"
      "THINKING BUDGET is the one worth playing with. Thinking arrives inline and the chat window "
      "shows it, which is worth seeing once.",
      w=320, h=620)

# --------------------------------------------------------------------------- 4 gemini

title(D, 1700, 1400, "4 - GEMINI", w=320, h=44)
g_api = place(D, "Model API", 1760, 1500, nick="Model API", sub="Models")
g_model = place(D, "Gemini Model", 2020, 1500, nick="Gemini Model")
wire(g_model, "Model API", g_api, "Model API")
g_tweak = place(D, "Gemini Tweaker", 2280, 1560, nick="Gemini Tweaker")
wire(g_tweak, "Model", g_model, "Model")
g_temp = slider(D, 2020, 1630, 1.0, 0.0, 2.0, nick="temperature", integer=False)
wire(g_tweak, "Temperature", g_temp, 0)
panel(D, 1700, 1690,
      "Same shape. Gemini's temperature goes up to 2.\r\n"
      "\r\n"
      "Gemini has no Max Tokens input on the Model node - it goes on the Tweaker instead, which is "
      "the sort of small per-provider difference the Model nodes exist to absorb so the rest of "
      "your pipeline never sees it.\r\n"
      "\r\n"
      "Worth knowing if you send it pictures from a URL: Gemini will not fetch an arbitrary public "
      "URL the way the others will. Images from disk or the clipboard are fine.",
      w=320, h=300)

# --------------------------------------------------------------------------- 5 openai-compatible

title(D, 2620, 380, "5 - OPENAI-COMPATIBLE (AND FRIENDS)", w=340, h=44)
o_api = place(D, "Model API", 2680, 480, nick="Model API", sub="Models")
o_model = place(D, "OpenAI Compatible Model", 2940, 500, nick="OpenAI Compatible Model")
wire(o_model, "Model API", o_api, "Model API")
o_max = slider(D, 2680, 560, 8192, 1024, 64000, nick="max tokens")
wire(o_model, "Max Tokens", o_max, 0)
o_tweak = place(D, "OpenAI Compatible Tweaker", 3220, 560, nick="OpenAI Compatible Tweaker")
wire(o_tweak, "Model", o_model, "Model")
o_temp = slider(D, 2940, 640, 1.0, 0.0, 2.0, nick="temperature", integer=False)
wire(o_tweak, "Temperature", o_temp, 0)
panel(D, 2620, 700,
      "This ONE pair covers most of the field. OpenAI itself, DeepSeek, Ollama, OpenRouter, Groq, "
      "Alibaba, Z.AI, Moonshot, LM Studio and anything else speaking the same protocol are all this "
      "node with a different endpoint on its Model API wire.\r\n"
      "\r\n"
      "Which is exactly why the endpoint and the key travel together, and why this node has no Base "
      "URL input of its own: the endpoint belongs to the credential, not to the node.\r\n"
      "\r\n"
      "REASONING EFFORT and THINKING on the Tweaker are for the reasoning models. Note that those "
      "models also REJECT a temperature and need a different token-limit field entirely - Physalia "
      "knows which ones and rewrites the request, so a Tweaker you have not touched is safe on any "
      "of them.\r\n"
      "\r\n"
      "OpenRouter model ids are namespaced, like anthropic/claude-sonnet-4-6. Ollama wants its own "
      "local endpoint and no key at all.",
      w=340, h=460)

# --------------------------------------------------------------------------- 6 model information

title(D, 2620, 1400, "6 - WHAT CAN THIS MODEL ACTUALLY DO?", w=360, h=44)
info = place(D, "Model Information", 2820, 1520, nick="Model Information")
wire(info, "Model", cc, "Model")
i_in = panel(D, 3040, 1460, "biggest prompt it accepts", w=280, h=70, colour=OUTPUT_GREY)
i_in.AddSource(pin(info, "out", "Max Input"))
i_out = panel(D, 3040, 1550, "longest answer it can give", w=280, h=70, colour=OUTPUT_GREY)
i_out.AddSource(pin(info, "out", "Max Output"))
i_img = panel(D, 3040, 1640, "can it see pictures?", w=280, h=70, colour=OUTPUT_GREY)
i_img.AddSource(pin(info, "out", "Image Capable"))
i_tool = panel(D, 3040, 1730, "can it call tools?", w=280, h=70, colour=OUTPUT_GREY)
i_tool.AddSource(pin(info, "out", "Tool Capable"))
panel(D, 2620, 1840,
      "MODEL INFORMATION answers the four questions you actually have before committing a pipeline "
      "to a model. Wire it to whichever Model node you are considering - it is wired to Claude Code "
      "here.\r\n"
      "\r\n"
      "IMAGE CAPABLE and TOOL CAPABLE are the two that will otherwise waste an afternoon. A "
      "pipeline built around Take Snapshot on a model that cannot see does not error - it just "
      "gets vague answers. Check first.\r\n"
      "\r\n"
      "MAX INPUT is what the compaction threshold in preset 10 should be set against, and MAX "
      "OUTPUT is the one that bites on canvas building: a long definition can be cut off "
      "mid-JSON, and a truncated answer produces a schema failure that looks like the model being "
      "careless.\r\n"
      "\r\n"
      "It merges two public catalogues, so an unusual or very new model may be unlisted, and "
      "UNKNOWN IS NOT NO."
      "\r\n" "\r\n"
      "You are looking at exactly that case right now. It is wired to Claude Code, whose model "
      "is the CLI shorthand \"sonnet\" rather than a full published id, so the catalogue does not "
      "recognise it and the node says so - reporting zeros and False rather than pretending. "
      "Move the wire onto one of the key-based Model nodes in stages 3 to 5, with a real model "
      "id picked, and the four panels fill in properly. Worth doing once so you know what a "
      "genuine answer looks like next to a blank one."
      "\r\n" "\r\n"
      "\r\n"
      "There is a LLAMACPP MODEL INFO node too, for a local llama.cpp server - no public "
      "catalogue knows anything about one of those, so it asks the server itself. It is not on "
      "this canvas because it would sit here warning about an unwired input forever.",
      w=360, h=640)

# --------------------------------------------------------------------------- 7 how to swap

panel(D, 660, 1400,
      "HOW TO SWAP THE MODEL\r\n"
      "\r\n"
      "1. Set the provider up first: chat window, Home screen, then its API endpoints page. Paste "
      "the key, and give it an endpoint if it is not the provider's default one.\r\n"
      "\r\n"
      "2. Pick the provider on the MODEL API node in stages 3, 4 or 5 - the little dropdown beside "
      "it lists whatever you have connected.\r\n"
      "\r\n"
      "3. Pick the model on the Model node.\r\n"
      "\r\n"
      "4. Drag ONE wire: from that column's last output into the LLM Call's MODEL input. The old "
      "wire is replaced automatically.\r\n"
      "\r\n"
      "That is the whole operation, and it is the point of the Model nodes being separate. Nothing "
      "else in any pipeline in this set has to change - the LLM Call does not care, the "
      "Conversation Log does not know, and the guardrails downstream are unaffected.\r\n"
      "\r\n"
      "Wire the TWEAKER'S output rather than the Model's if you are using one; leave it out "
      "entirely if you are not. An empty Tweaker input is not zero - it means \"use the sensible "
      "default for this model\", and Physalia already knows which models refuse which fields.",
      w=520, h=560, colour=WARN_ORANGE)

commit_build(D, "build preset 13")
solve(D)
pick(D, cc, "Model", "sonnet")
solve(D)
write_dump(D, DUMP)
say("model on the call:", [str(v) for v in pin(call, "in", "Model").VolatileData.AllData(True)])
say("model info out:", [(q.Name, [str(v) for v in q.VolatileData.AllData(True)])
                        for q in info.Params.Output])
bad = sweep(D, NAME)
save_phy(H, OUT,
         description="A reference card for the Model nodes: the two keyless command-line models, "
                     "the three key-based providers each wired with their Model API and Tweaker, "
                     "and Model Information to check what a model can do before you commit to it.",
         chat_text="Choosing and Tuning a Model\r\n\r\n"
                   "A reference card rather than a pipeline. The loop runs on Claude Code so it "
                   "works with nothing set up; the three key-based providers are wired up beside "
                   "it, ready to swap in with a single wire.\r\n\r\n"
                   "Read stage 7 for how to swap, and stage 6 for how to check whether a model can "
                   "actually see pictures and call tools before you build a pipeline that needs "
                   "both.\r\n\r\n"
                   "Try: \"what model are you, and how big a prompt can you take?\"")
say("PROBLEMS:", bad)
