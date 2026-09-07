# Copyright (c) 2026 Physalia Contributors
# SPDX-License-Identifier: AGPL-3.0-or-later
"""
Scenario S08 - "Check the Model Against the Document".

Drop a code, a spec or a consultant's drawing set into the chat and ask whether what you have
modelled complies with it. It reads the relevant pages, measures the model, and reports the
discrepancy with a clause reference and a number.
"""

# The ONE machine-specific line in this file. Set ROOT before exec'ing this script to build
# from a checkout somewhere else:  ROOT = r"D:\\code\\Physalia"
try:
    ROOT
except NameError:
    ROOT = r"C:\Users\rober\repos\Physalia"
exec(open(ROOT + r"\tools\presets\phybuild.py").read())

NAME = "S08 - Check the Model Against the Document"
OUT = PRESETS + r"\%s.phy" % NAME
DUMP = SCRATCH + r"\dumpS08.txt"

host = clear_host()
H, D = new_harness(host, 200, 200, name="check-against-the-document")

panel(D, 40, 40,
      "CHECK THE MODEL AGAINST THE DOCUMENT\r\n"
      "\r\n"
      "Every project runs on documents nobody has time to read against the model: a building code, "
      "a client's brief, a performance spec, a consultant's drawing set, a manufacturer's fixing "
      "detail. The model gets built from memory and a phone call, and the discrepancy turns up on "
      "site.\r\n"
      "\r\n"
      "THIS PIPELINE READS BOTH. Drop the document into the chat and ask a question that spans it "
      "and your model:\r\n"
      "\r\n"
      "  \"the stair - does it meet the rise, going and headroom in section 3.2?\"\r\n"
      "  \"our balustrades are 1050. what does this spec require, and where does it say so?\"\r\n"
      "  \"the structural drawings show a 450 deep transfer beam on grid C. is that what I've "
      "modelled?\"\r\n"
      "  \"list every dimension in this brief that my model doesn't match\"\r\n"
      "\r\n"
      "WHY IT CAN AFFORD TO. Attaching a 400-sheet set does not send you 400 sheets. It registers "
      "them and puts a short description in the conversation - name, page count, sheet size, which "
      "pages have text - and then the model pulls the pages it decides it needs. A drawing it "
      "cannot read as text it RENDERS, and it can zoom into a region of one page at high "
      "resolution, which is the only way 4pt dimension text on an A1 sheet is legible at all.\r\n"
      "\r\n"
      "READ THIS BEFORE YOU RELY ON IT. It is a competent, tireless reader that has no professional "
      "indemnity. Treat what it finds as a list of things to go and check, not as a compliance "
      "certificate. It is at its best telling you WHERE to look - clause and page - and that alone "
      "is worth the afternoon it saves.",
      w=960, h=520, colour=INTRO_GREEN)

SPINE = 760

# --------------------------------------------------------------------------- the loop

L = core_loop(D, 130, SPINE, model_name="Codex Model",
              instruction="You check models against documents.\r\n"
                          "\r\n"
                          "WORK IN THIS ORDER. First find the requirement: use read_pdf to search "
                          "the attached documents, and RENDER the page when it is a drawing or when "
                          "a table matters. Quote the clause and give the page. Then measure the "
                          "model with run_rhino_script and print what you found. Only then compare "
                          "them.\r\n"
                          "\r\n"
                          "Report each finding as: what the document requires (with the "
                          "reference), what the model has (with the number), and whether they "
                          "agree.\r\n"
                          "\r\n"
                          "NEVER state a requirement you have not read on a page, and never state "
                          "a dimension you have not measured. If a document is a scan with no text "
                          "layer, render it and say that you are reading an image. If you cannot "
                          "find something, say so - a missed clause is recoverable, an invented one "
                          "is not.\r\n"
                          "\r\n"
                          "Use ask_human when the question depends on which objects are meant.")

proj = place(D, "Project Folder", 400, 640, nick="Project Folder")
rhino = place(D, "Rhino Document", 560, 640, nick="Rhino Document")
units = place(D, "Document Units Grounding", 720, 640, nick="Document Units")
toolsp = place(D, "Tools Present", 400, 690, nick="Tools Present")
for g in (proj, rhino, units, toolsp):
    wire(L["log"], "Grounding", g, 0)

pdfin = place(D, "Read PDF", 560, 690, nick="Attach PDFs", sub="Human Tools")
img = place(D, "Add Image", 720, 690, nick="Add Image")
expc = place(D, "Export Conversation", 400, 1060, nick="Export Conversation")
for t in (pdfin, img, expc):
    wire(L["log"], "Human Tools", t, "Human Tool")

panel(D, 130, 1120,
      "ATTACH PDFs is the button. It puts a paperclip in the chat window and accepts drag-and-drop.\r\n"
      "\r\n"
      "USE THE BUTTON RATHER THAN DRAGGING for anything large. The picker records where the file "
      "already IS and moves no bytes at all, so a 300MB drawing set costs nothing to attach. "
      "Dragging has to send the file, because a browser will not tell a page where a dropped file "
      "came from, and it is capped at 100MB.\r\n"
      "\r\n"
      "PROJECT FOLDER is the other half. Anything sitting in this pipeline's own folder is readable "
      "without attaching it at all - put the code you always check against in there once and stop "
      "thinking about it. It tells the model the absolute path and what is in it.\r\n"
      "\r\n"
      "It refreshes when a file APPEARS, which is not something Grasshopper would notice on its "
      "own.",
      w=380, h=440)

# --------------------------------------------------------------------------- tools

title(D, 1560, 540, "2 - READING, MEASURING, ASKING", w=400, h=44)
router = place(D, "Router", 1640, 780, nick="Router")
wire(router, "Tool Calls", L["call"], "Tool Calls")
router_slots(router, 3)
rpdf = place(D, "Read PDF", 1980, 700, nick="Read PDF", sub="LLM Tools")
drive = place(D, "Drive Rhino", 1980, 800, nick="Drive Rhino")
askh = place(D, "Ask Human", 1980, 900, nick="Ask Human")
mem = place(D, "Memory", 1980, 1000, nick="Memory")
memfolder = input_panel(D, 1760, 1030, "compliance-notes", w=190, h=40, nick="memory folder")
wire(mem, "Memory Folder", memfolder, 0)
wire(rpdf, "Signal", router, 0)
wire(drive, "Signal", router, 1)
wire(askh, "Signal", router, 2)
wire(mem, "Signal", router, 3)
wire(rpdf, "Project Folder", proj, "Folder")

blank_input(D, rpdf, "Reference Folder", 1620, 640,
            label="a shared PDF library on your server - leave blank if you have none")

pdf_list = panel(D, 2240, 660, "the PDFs it can see", w=320, h=140, colour=OUTPUT_GREY)
pdf_list.AddSource(pin(rpdf, "out", "Available PDFs"))
script = panel(D, 2240, 820, "the script it ran on your model", w=320, h=180, colour=OUTPUT_GREY)
script.AddSource(pin(drive, "out", "Last Script"))
sel = panel(D, 2240, 1020, "what you selected when asked", w=320, h=80, colour=OUTPUT_GREY)
sel.AddSource(pin(askh, "out", "Selection"))

panel(D, 1560, 1140,
      "READ PDF IS FOUR ACTIONS, and the model chooses between them. LIST says what is available. "
      "TEXT pulls a page range as words. SEARCH finds a phrase and reports the page AND the region "
      "of the page it is in. RENDER turns a page - or a REGION of one - into a picture.\r\n"
      "\r\n"
      "SEARCH THEN RENDER THE REGION is the loop that makes drawings readable. An A1 sheet "
      "rendered whole is about 4900 pixels wide, and by the time that has been shrunk to fit what a "
      "model can be sent, 4pt dimension text is seven pixels tall and gone. Cropping to the "
      "detail first and rendering THAT at high resolution is the difference between reading a "
      "drawing and looking at a grey rectangle.\r\n"
      "\r\n"
      "A SCANNED PAGE IS REPORTED AS SUCH rather than as an empty one. A scan and a blank sheet "
      "extract identically, and only one of them means \"look at it as a picture instead\".\r\n"
      "\r\n"
      "TWO PLACES IT LOOKS, and they answer different needs. The PROJECT FOLDER is this job's own - "
      "drop the client's brief in there and it is readable without attaching anything. The "
      "REFERENCE FOLDER in the white box is the one thing a project folder cannot express: a "
      "SHARED library on the server that every job checks against. Point it at your practice's "
      "standards once. It is ordinary text saved in the file, so it travels inside this preset - "
      "which is how a pipeline set up once for an office arrives configured.\r\n"
      "\r\n"
      "MEMORY is where it writes down what it has already checked and what your office's standard "
      "answers are - which balustrade height you use, which code edition applies.",
      w=400, h=560)

# --------------------------------------------------------------------------- out

title(D, 2700, 540, "3 - THE FINDINGS", w=320, h=44)
decon = place(D, "Deconstruct Signal", 2780, 700, nick="Deconstruct Signal")
wire(decon, "Signal", L["call"], "Success Signal")
out = place(D, "Harness Out", 2780, 800, nick="Harness Out")
out.Params.Input[0].NickName = "the findings"
wire(out, "Data", decon, "Payload")
panel(D, 2700, 900,
      "HARNESS OUT puts the written findings onto your canvas - drag the grip from the right edge "
      "of the Harness node onto a Panel.\r\n"
      "\r\n"
      "Worth doing here more than anywhere else: a compliance check is something you will want to "
      "show somebody, and a panel beside the model is easier to point at than a scrolled-back "
      "conversation.\r\n"
      "\r\n"
      "EXPORT CONVERSATION in the chat header saves the whole exchange including the reasoning, "
      "which is the version to keep on file.",
      w=320, h=340)

# --------------------------------------------------------------------------- bounds + returns

budget = place(D, "Budget Guard", 1000, 1700, nick="Budget Guard")
b_calls = slider(D, 780, 1780, 80, 1, 400, nick="max calls")
wire(budget, "Max Calls", b_calls, 0)
wire(budget, "Signal", L["log"], "Signal")
wire(L["call"], "Signal", budget, "Success Signal")

fb_res, co_res = back(D, rpdf, "Result", router, "Results",
                      2380, 1700, 1360, 1700, nick="tool results")
for t in (drive, askh, mem):
    wire(fb_res, "Signal", t, "Result")
back(D, router, "Feedback", L["log"], "LLM Tool Signal",
     1640, 1860, 700, 1860, nick="tool round to the log")

panel(D, 60, 1620,
      "THINGS TO TRY\r\n"
      "\r\n"
      "1. Attach something and just ask WHAT IT IS. \"What's in this document and how is it "
      "organised?\" One cheap round that tells you whether it can read the file at all.\r\n"
      "\r\n"
      "2. Then a question about the document only, with no model in it. Get that right before "
      "adding the second half.\r\n"
      "\r\n"
      "3. Then the real one, spanning both: \"does what I've modelled match clause 4.1?\"\r\n"
      "\r\n"
      "4. Ask it to show you the page it read the requirement from. It can render the region. If "
      "the clause it quotes is not on that page, you have learned something important.\r\n"
      "\r\n"
      "5. Put your practice's standard details in the standing PDF folder and stop attaching them.\r\n"
      "\r\n"
      "6. Give it a scanned drawing with no text layer and see it say so, then read the image.\r\n"
      "\r\n"
      "CHECK ITS WORKING, NOT JUST ITS ANSWER. The two grey panels show the script it ran on your "
      "model and which documents it could see. A finding you cannot trace to a page and a "
      "measurement is a finding you should not act on.",
      w=600, h=560)

commit_build(D, "build scenario S08")
solve(D)
solve(D)
write_dump(D, DUMP)
say("router outputs:", [q.NickName for q in router.Params.Output])
bad = sweep(D, NAME)
save_phy(H, OUT,
         description="Drop a code, a spec or a consultant's drawing set into the chat and ask "
                     "whether your model complies. It searches the document, renders the page or "
                     "the region it needs, measures the model with Python, and reports the "
                     "discrepancy with a clause reference and a number.",
         chat_text="Check the Model Against the Document\r\n\r\n"
                   "Attach a PDF with the paperclip, then ask a question that spans it and your "
                   "Rhino model:\r\n\r\n"
                   "  \"the stair - does it meet the rise, going and headroom in section 3.2?\"\r\n"
                   "  \"list every dimension in this brief my model doesn't match\"\r\n\r\n"
                   "Start by asking what the document IS - one cheap round that tells you whether "
                   "it can read the file. Then ask about the document alone. Then both.\r\n\r\n"
                   "It reports what the document requires, what the model has, and whether they "
                   "agree. Ask it to show you the page it read - it can. Treat what it finds as a "
                   "list of things to check, not as a certificate.")
say("PROBLEMS:", bad)
