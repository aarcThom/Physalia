# Copyright (c) 2026 Physalia Contributors
# SPDX-License-Identifier: AGPL-3.0-or-later
"""
Preset 09 - "Files, Downloads and Drawings".

The pipeline's own folder on disk, and the three tools that work with it: fetch a file from the
web, read one that is already there, and read a PDF a page or a REGION at a time. Plus the human
side of PDFs - attaching a set in the chat window without paying for all of it.
"""

exec(open(r"C:\Users\rober\repos\Physalia\tools\presets\phybuild.py").read())

NAME = "09 - Files, Downloads and Drawings"
OUT = r"C:\Users\rober\repos\Physalia\wip_presets\%s.phy" % NAME
DUMP = r"C:\Users\rober\AppData\Local\Temp\claude\dump09.txt"

host = clear_host()
H, D = new_harness(host, 200, 200, name="files-downloads-and-drawings")

TITLE_Y = 400

# --------------------------------------------------------------------------- the intro

panel(D, 40, 40,
      "FILES, DOWNLOADS AND DRAWINGS\r\n"
      "\r\n"
      "Every harness has a FOLDER of its own, named after it, under Files/PROJECT_FILES. That is "
      "where site data, downloads and reference drawings live, and it travels with the pipeline "
      "when you save it out.\r\n"
      "\r\n"
      "Three tools work with it. DOWNLOAD FILE fetches something from the web into the folder. "
      "READ FILE reads what is already there. READ PDF reads drawings - a page, a page range, a "
      "search, or a rendered crop of one corner of a sheet.\r\n"
      "\r\n"
      "The thing to notice as you read is that files reach the CANVAS, not just the model. "
      "Download File puts the path of what it fetched on a wire, because a LiDAR tile is something "
      "to import, not something to read about. That is the whole reason these are nodes.\r\n"
      "\r\n"
      "A word on trust: Read File will not go outside the project folder, but that is a guard "
      "against accidents, not a sandbox. Anything you wire up that can run code can reach the "
      "whole disk. Do not treat the folder as a security boundary.",
      w=940, h=310, colour=INTRO_GREEN)

# --------------------------------------------------------------------------- 1 the chat

title(D, 60, TITLE_Y, "1 - WHERE YOU TYPE", w=270, h=44)
chat = place(D, "Chat", 130, 530, nick="Chat")
panel(D, 60, 620,
      "\"Fetch the 2023 LiDAR tile for this block and tell me the ground level at the corners.\"\r\n"
      "\r\n"
      "Presets 01 to 03 explain the loop, the grounding and the tools. This one assumes them.",
      w=250, h=200)

# --------------------------------------------------------------------------- 2 system prompt

title(D, 380, TITLE_Y, "2 - THE SYSTEM PROMPT", w=320, h=44)
sysp = place(D, "System Prompt", 580, 530, nick="System Prompt")
blank_input(D, sysp, "Preamble", 380, 470, label="no preamble file")
blank_input(D, sysp, "Schema", 380, 516, label="no schema file")
extra = input_panel(D, 320, 600,
                    "Before downloading anything, say what you are fetching and why. Prefer "
                    "reading an index or a readme to guessing at a URL.",
                    w=250, h=110, nick="my own instructions")
wire(sysp, "Additional Prompt", extra, 0)
panel(D, 380, 730,
      "Worth telling a model to say what it is about to fetch. Downloads spend your bandwidth and "
      "fill your disk, and a model that has decided to work through a whole tile index will do "
      "exactly that.\r\n"
      "\r\n"
      "The approval prompts in stage 7 are the real defence. This is just politeness that saves "
      "you clicking through them.",
      w=320, h=280)

# --------------------------------------------------------------------------- 3 grounding

title(D, 760, TITLE_Y, "3 - WHERE THE FOLDER IS", w=300, h=44)
proj = place(D, "Project Folder", 830, 500, nick="Project Folder")
toolsp = place(D, "Tools Present", 830, 560, nick="Tools Present")
folder = panel(D, 760, 620, "the folder, as an absolute path", w=300, h=80, colour=OUTPUT_GREY)
folder.AddSource(pin(proj, "out", "Folder"))
panel(D, 760, 730,
      "PROJECT FOLDER does two jobs, and the second is the one this preset is built on.\r\n"
      "\r\n"
      "Its GROUNDING output tells the model where the folder is and what is in it. Its FOLDER "
      "output is the absolute path as plain text, and it is wired into all three tools in stage 7 "
      "- so the folder is configured ONCE and the model cannot be told about one folder while a "
      "tool reads another.\r\n"
      "\r\n"
      "The path must be absolute in the prompt, or code the model writes cannot open anything it "
      "names.\r\n"
      "\r\n"
      "Leave its input blank for the folder named after this harness. A bare name means a folder "
      "of that name; anything with a slash in it is relative to your saved .gh file; a full path "
      "is used as it stands.\r\n"
      "\r\n"
      "It also watches the folder, so a file you drop in by hand is known about on the next "
      "message without anything being re-wired.",
      w=300, h=460)

# --------------------------------------------------------------------------- 4 conversation log

title(D, 1160, TITLE_Y, "4 - THE RUNNING CONVERSATION", w=320, h=44)
log = place(D, "Conversation Log", 1320, 530, nick="Conversation Log")
wire(log, "System Prompt", sysp, "System Prompt")
wire(log, "Prompt Signal", chat, "Prompt Signal")
wire(log, "Grounding", proj, 0)
wire(log, "Grounding", toolsp, 0)
panel(D, 1160, 650,
      "Nothing new here. The human tool in stage 7 - the PDF one - plugs into HUMAN TOOLS, and "
      "everything else arrives through the Router.\r\n"
      "\r\n"
      "Worth knowing where a PDF's pages go: attaching a 400-sheet set does NOT put 400 sheets in "
      "the conversation. Only a short descriptor does. The pages are pulled one at a time by the "
      "tool, on demand. That split is the only reason attaching a whole set is affordable.",
      w=320, h=300)

# --------------------------------------------------------------------------- 5 the call

title(D, 1560, TITLE_Y, "5 - ASKING THE MODEL", w=300, h=44)
model = place(D, "Codex Model", 1700, 490, nick="Codex Model")
call = place(D, "LLM Call", 1700, 600, nick="LLM Call")
wire(call, "Model", model, "Model")
wire(call, "Signal", log, "Signal")
cancel = boolean(D, 1570, 645, False, nick="stop", toggle=False)
wire(call, "Cancel", cancel, 0)
panel(D, 1560, 690,
      "CODEX, because this preset is all tools and Claude Code cannot call them - see preset 03.\r\n"
      "\r\n"
      "For the PDF work you also want a model that can SEE, since a rendered page comes back as a "
      "picture. Codex, Anthropic and Gemini models all can."
      "\r\n" "\r\n"
      "One practical thing about Codex: its model list is fetched LIVE from the CLI and it "
      "changes. If a round fails saying the model does not exist or you do not have access "
      "to it, open the little dropdown beside the Codex Model node and pick again - the list "
      "you are looking at is current.",
      w=300, h=380)

# --------------------------------------------------------------------------- 6 the router

title(D, 1940, TITLE_Y, "6 - THE ROUTER", w=260, h=44)
router = place(D, "Router", 2060, 530, nick="Router")
wire(router, "Tool Calls", call, "Tool Calls")
router_slots(router, 2)
panel(D, 1940, 650,
      "Three tools, three outputs, plus Feedback. As always the names are the tool names the "
      "Router took on once the wires were finished - an output still reading \"T1\" is an "
      "unfinished wire.",
      w=260, h=280)

# --------------------------------------------------------------------------- 7 the tools

title(D, 2280, TITLE_Y, "7 - THE TOOLS", w=340, h=44)
dl = place(D, "Download File", 2440, 490, nick="Download File")
rf = place(D, "Read File", 2440, 620, nick="Read File")
rpdf = place(D, "Read PDF", 2440, 730, nick="Read PDF", sub="LLM Tools")
pdfin = place(D, "Read PDF", 2440, 850, nick="Attach PDFs", sub="Human Tools")

# A short nickname on purpose: a slider is only as wide as its label, and there are 250
# canvas units between the Router and the tool column. The note explains what it means.
max_dl = slider(D, 2210, 560, 200, 1, 2000, nick="max MB")
wire(dl, "Max Download", max_dl, 0)
wire(dl, "Signal", router, 0)
wire(rf, "Signal", router, 1)
wire(rpdf, "Signal", router, 2)
for t in (dl, rf, rpdf):
    wire(t, "Project Folder", proj, "Folder")
refdir = input_panel(D, 2200, 760, "", w=200, h=44, nick="no reference folder")
wire(rpdf, "Reference Folder", refdir, 0)
wire(log, "Human Tools", pdfin, "Human Tool")

panel(D, 2280, 960,
      "DOWNLOAD FILE fetches a URL into the folder. Every guard is on the DESTINATION rather than "
      "the source - the name is reduced to a single segment and checked back against the folder, "
      "the size budget is enforced WHILE streaming rather than trusted from the header, and a file "
      "of the same size already there is reported rather than fetched again. Zips are unpacked "
      "safely by default.\r\n"
      "\r\n"
      "It has two ASK-BEFORE toggles on its right-click menu, both ON: one before downloading, one "
      "before unpacking. They appear as cards in the chat window and they FAIL CLOSED - with no "
      "chat window open, a download is refused immediately. Right way round, but a pipeline meant "
      "to run unattended has to switch them off deliberately.\r\n"
      "\r\n"
      "Some hosts answer every request with a bot challenge. That is detected and NAMED, rather "
      "than reported as a plain 403 the model will keep retrying - and a FETCH IN BROWSER button "
      "appears in the chat window. Press it, sign in like a person, and the file lands in the "
      "project folder; the folder watcher then hands it on.\r\n"
      "\r\n"
      "READ FILE does list, stat, text and search. Honestly sized: it is for the readmes, indexes "
      "and CSVs that say WHICH big file to reach for. It refuses a binary file with a description "
      "of what it is rather than handing back rubbish. The MAX MB slider beside Download File is "
      "the per-file size ceiling, enforced while the bytes are arriving.\r\n"
      "\r\n"
      "READ PDF (the LLM tool) is the interesting one. Four actions - list, text, search and "
      "render - and the loop it is built for is: get an overview, search for the thing, render "
      "THAT REGION at high resolution. An A1 sheet at readable DPI is about 4900 pixels wide and "
      "gets shrunk on the way to the model, so 4pt dimension text becomes seven pixels tall and "
      "unreadable. Rendering a corner instead is what makes a title block legible.\r\n"
      "\r\n"
      "It also tells the model when a page has NO TEXT LAYER, because a scan and a blank sheet "
      "extract identically and only one of them means \"look at it instead\".\r\n"
      "\r\n"
      "ATTACH PDFs is the human tool - a button and drag-and-drop in the chat window. Files are "
      "referenced where they sit, never copied. Its REFERENCE FOLDER input, left empty here, is "
      "for the one thing a project folder cannot express: an office-wide standards library shared "
      "across every job.",
      w=340, h=900)

# --------------------------------------------------------------------------- 8 what lands on wires

title(D, 2760, TITLE_Y, "8 - WHAT LANDS ON YOUR CANVAS", w=300, h=44)
dl_files = panel(D, 2760, 480, "the paths of what it downloaded", w=300, h=110,
                 colour=OUTPUT_GREY)
dl_files.AddSource(pin(dl, "out", "Downloaded Files"))
dl_stat = panel(D, 2760, 610, "download status", w=300, h=80, colour=OUTPUT_GREY)
dl_stat.AddSource(pin(dl, "out", "Status"))
rf_stat = panel(D, 2760, 710, "read-file status", w=300, h=80, colour=OUTPUT_GREY)
rf_stat.AddSource(pin(rf, "out", "Status"))
pdf_list = panel(D, 2760, 810, "the PDFs it can see", w=300, h=110, colour=OUTPUT_GREY)
pdf_list.AddSource(pin(rpdf, "out", "Available PDFs"))
panel(D, 2760, 960,
      "THIS COLUMN IS THE POINT OF THE PRESET.\r\n"
      "\r\n"
      "DOWNLOADED FILES carries absolute paths. Wire it into an Import component and a tile the "
      "model fetched becomes geometry in your definition, with nobody copying a path by hand.\r\n"
      "\r\n"
      "The STATUS outputs are how you tell a quiet failure from a success. A download that was "
      "refused, a file that was already there, a folder that could not be resolved - all of that "
      "shows up here rather than only in the conversation.\r\n"
      "\r\n"
      "THE PDFs IT CAN SEE lists what is attached and what is in the folder, so you can check the "
      "model is looking at the drawing you think it is.\r\n"
      "\r\n"
      "A tool being a node is what makes any of this possible. The result is available to the rest "
      "of your definition, not just to the model.",
      w=300, h=440)

# --------------------------------------------------------------------------- 9 return paths

fb_res, co_res = back(D, dl, "Result", router, "Results",
                      2760, 1600, 1800, 1600, nick="tool results")
wire(fb_res, "Signal", rf, "Result")
wire(fb_res, "Signal", rpdf, "Result")
back(D, router, "Feedback", log, "LLM Tool Signal",
     2060, 1760, 1200, 1760, nick="tool round to the log")
back(D, call, "Success Signal", log, "Response Signal",
     1900, 1920, 1200, 1920, nick="reply back to the log")
panel(D, 400, 1560,
      "THE THREE RETURN PATHS, as in preset 03: tool results to the Router, the finished round to "
      "the Conversation Log's LLM Tool Signal, and the reply to Response Signal.\r\n"
      "\r\n"
      "All three tools share one Feedback node and one Collector, because they share a "
      "destination.\r\n"
      "\r\n"
      "Read PDF's rendered pages come back through here as ATTACHMENTS on the tool-answering turn, "
      "the same machinery Take Snapshot uses in preset 06. A tool result is text on every provider "
      "there is, so a picture cannot be one - it rides alongside, ordered after the text results.",
      w=520, h=380, colour=WARN_ORANGE)

# --------------------------------------------------------------------------- 10 what to try

panel(D, 3200, 400,
      "THINGS TO TRY\r\n"
      "\r\n"
      "1. Read the grey panel in stage 3 for the folder path, and open it - every node that "
      "touches project files also has OPEN PROJECT FOLDER on its right-click menu.\r\n"
      "\r\n"
      "2. Drop a CSV in the folder and ask what is in it. Then ask for a summary of one column.\r\n"
      "\r\n"
      "3. \"Download https://www.gutenberg.org/files/11/11-0.txt and tell me the first line.\" You "
      "will get an approval card - that is the ask-before toggle working.\r\n"
      "\r\n"
      "4. Drag a PDF drawing onto the chat window, then ask \"what is the drawing number and the "
      "sheet size?\" Then: \"render the title block so I can read it.\" Watch it search first and "
      "render a region rather than the whole page.\r\n"
      "\r\n"
      "5. Ask it to download something from a host that challenges bots. You should get a refusal "
      "that says it is not worth retrying, plus a Fetch In Browser button.\r\n"
      "\r\n"
      "6. Turn both ask-before toggles off on Download File and try again. Now consider whether "
      "you want them off in a pipeline with a trigger armed.",
      w=520, h=560)

commit_build(D, "build preset 09")
solve(D)
# The Codex model list is fetched LIVE from the CLI and changes under you - it went from
# gpt-5.5/5.4/5.4-mini to gpt-5.6-sol/terra/luna/5.5/5.4-mini inside one session here. So the
# Picker is deliberately NOT pinned: left alone it snaps to whatever the CLI offers first,
# which self-heals. A pinned name that the account cannot use answers 404 and does not.
solve(D)
solve(D)
write_dump(D, DUMP)
say("router outputs:", [p.NickName for p in router.Params.Output])
say("folder:", [str(v) for v in pin(proj, "out", "Folder").VolatileData.AllData(True)])
bad = sweep(D, NAME)
save_phy(H, OUT,
         description="The pipeline's own folder on disk, and the three tools that use it: Download "
                     "File, Read File and Read PDF - plus PDF attachment from the chat window. The "
                     "paths of what it fetched land on a wire, not just in the conversation.",
         chat_text="Files, Downloads and Drawings\r\n\r\n"
                   "This pipeline has a folder of its own. It can fetch files into it, read what "
                   "is there, and read PDF drawings a page or a corner at a time.\r\n\r\n"
                   "Try: drag a PDF drawing onto this window, then ask \"what is the drawing "
                   "number and sheet size?\" and then \"render the title block so I can read "
                   "it.\"\r\n\r\n"
                   "Downloads and unpacking both ask you first, as a card in this window. That is "
                   "deliberate, and with no window open they are refused rather than allowed.")
say("PROBLEMS:", bad)
