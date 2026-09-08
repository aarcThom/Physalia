# Copyright (c) 2026 Physalia Contributors
# SPDX-License-Identifier: AGPL-3.0-or-later
"""
Scenario S09 - "Build the Site Context from Open Data".

The half-day at the start of every project: find the city's LiDAR, the cadastre, the contours;
download them; work out what format they are in; get them into Rhino at the right coordinates.
"""

# The ONE machine-specific line in this file. Set ROOT before exec'ing this script to build
# from a checkout somewhere else:  ROOT = r"D:\\code\\Physalia"
try:
    ROOT
except NameError:
    ROOT = r"C:\Users\rober\repos\Physalia"
exec(open(ROOT + r"\tools\presets\phybuild.py").read())

NAME = "S09 - Build the Site Context from Open Data"
OUT = PRESETS + r"\%s.phy" % NAME
DUMP = SCRATCH + r"\dumpS09.txt"

host = clear_host()
H, D = new_harness(host, 200, 200, name="build-the-site-context")

panel(D, 40, 40,
      "BUILD THE SITE CONTEXT FROM OPEN DATA\r\n"
      "\r\n"
      "The half-day nobody costs for. The city publishes LiDAR, a cadastre, contours, building "
      "footprints and a tree inventory, all free, all in a different format, all on a portal built "
      "in 2014. You need them in Rhino, at the right coordinates, by lunchtime.\r\n"
      "\r\n"
      "ASK FOR IT IN ENGLISH\r\n"
      "  \"find the LiDAR tile covering 49.2827 N, 123.1207 W and download it\"\r\n"
      "  \"what's in that file? tell me the coordinate system and the extents before importing "
      "anything\"\r\n"
      "  \"import it, move it so the site centre is at the origin, and put it on a layer called "
      "CONTEXT-TERRAIN\"\r\n"
      "  \"now the building footprints for the two blocks either side, extruded to their recorded "
      "heights\"\r\n"
      "\r\n"
      "IT DOES THE WHOLE CHAIN: reads the portal, fetches the file, works out what it got, writes "
      "the Python to import it, and puts it on the right layer at the right place. The paths of "
      "everything it fetched come out onto your canvas, because a LiDAR tile is something to "
      "IMPORT, not something to read about.\r\n"
      "\r\n"
      "THE COORDINATE SYSTEM IS WHERE THIS GOES WRONG, every time and for everyone. Ask it what "
      "the file says before you import, and check the answer. A model 400km from the origin looks "
      "exactly like a model at the origin until you try to render it.",
      w=940, h=470, colour=INTRO_GREEN)

SPINE = 720

# --------------------------------------------------------------------------- the loop

L = core_loop(D, 130, SPINE, model_name="Codex Model",
              instruction="You gather site context data and get it into Rhino.\r\n"
                          "\r\n"
                          "BEFORE DOWNLOADING: say what you are about to fetch and roughly how "
                          "big it is. Before IMPORTING anything, read enough of the file to state "
                          "its format, its coordinate system and its extents, and tell the user "
                          "those. Do not import a file whose coordinate system you could not "
                          "determine - say so and ask.\r\n"
                          "\r\n"
                          "Put everything on clearly named CONTEXT- layers, and report what you "
                          "created with counts.\r\n"
                          "\r\n"
                          "If a download is refused as a bot challenge, do NOT retry it and do not "
                          "try a neighbouring URL. Tell the user, and use the project folder path "
                          "so they can save it there themselves - you will see it when it "
                          "arrives.")

proj = place(D, "Project Folder", 400, 620, nick="Project Folder")
rhino = place(D, "Rhino Document", 560, 620, nick="Rhino Document")
units = place(D, "Document Units Grounding", 720, 620, nick="Document Units")
toolsp = place(D, "Tools Present", 880, 620, nick="Tools Present")
for g in (proj, rhino, units, toolsp):
    wire(L["log"], "Grounding", g, 0)
img = place(D, "Add Image", 880, 670, nick="Add Image")
expc = place(D, "Export Conversation", 880, 720, nick="Export Conversation")
for t in (img, expc):
    wire(L["log"], "Human Tools", t, "Human Tool")

panel(D, 130, 1000,
      "PROJECT FOLDER is the centre of this one. Every pipeline gets a folder of its own, named "
      "after the harness, and this tells the model its ABSOLUTE path plus what is currently in it.\r\n"
      "\r\n"
      "The absolute path is not a detail: the import script has to be able to open the file by "
      "name, and it cannot do that from a relative one.\r\n"
      "\r\n"
      "IT WATCHES THE FOLDER. A file appearing is not something Grasshopper would ever notice - "
      "nothing on the canvas changed - so this has a file watcher of its own. Save something into "
      "that folder by hand and the model knows about it on your next message.",
      w=380, h=340)

# --------------------------------------------------------------------------- trigger

title(D, 60, 1400, "THE FILE YOU FETCHED BY HAND", w=380, h=44)
watchf = place(D, "Folder Watcher", 220, 1520, nick="Folder Watcher")
wire(watchf, "Project Folder", proj, "Folder")
wire(L["log"], "Prompt Signal", watchf, "Signal")
w_filter = input_panel(D, 40, 1500, "*.*", w=140, h=40, nick="which files to watch for")
wire(watchf, "Filter", w_filter, 0)
w_settle = slider(D, 40, 1560, 3, 1, 30, nick="seconds to settle")
wire(watchf, "Settle", w_settle, 0)
changed = panel(D, 320, 1620, "the files that appeared", w=300, h=120, colour=OUTPUT_GREY)
changed.AddSource(pin(watchf, "out", "Changed Files"))

panel(D, 60, 1790,
      "WHY THIS IS HERE, AND IT IS NOT A GIMMICK. Some data portals sit behind a bot challenge - "
      "Vancouver's LiDAR host is one - which answers every automated request with a 403 while "
      "downloading perfectly in a browser. Physalia detects that specifically, says it is NOT worth "
      "retrying, and offers a FETCH IN BROWSER button in the chat so you can satisfy the challenge "
      "yourself. The file lands straight in the project folder.\r\n"
      "\r\n"
      "THIS WATCHER IS WHAT CLOSES THAT LOOP. The file appears, the pipeline wakes up on its own, "
      "and the model tells you what arrived. You never have to go back and say \"ok, I got it\".\r\n"
      "\r\n"
      "It ignores the pipeline's OWN downloads, or every fetch would start a round about itself. A "
      "browser fetch is deliberately NOT ignored - that path exists precisely so the watcher hands "
      "the file over.\r\n"
      "\r\n"
      "ARMING IS NEVER SAVED. This opens switched off, on your machine and on anyone else's. Arm "
      "it from the trigger list in the chat window.",
      w=380, h=460)

# --------------------------------------------------------------------------- tools

title(D, 1500, 480, "GETTING IT AND USING IT", w=400, h=44)
router = place(D, "Router", 1580, 720, nick="Router")
wire(router, "Tool Calls", L["call"], "Tool Calls")
router_slots(router, 4)
dl = place(D, "Download File", 1920, 640, nick="Download File")
rf = place(D, "Read File", 1920, 740, nick="Read File")
api = place(D, "API Call", 1920, 840, nick="API Call")
readurl = place(D, "Read URL", 1920, 940, nick="Read URL")
drive = place(D, "Drive Rhino", 1920, 1040, nick="Drive Rhino")
wire(dl, "Signal", router, 0)
wire(rf, "Signal", router, 1)
wire(api, "Signal", router, 2)
wire(readurl, "Signal", router, 3)
wire(drive, "Signal", router, 4)
for t in (dl, rf):
    wire(t, "Project Folder", proj, "Folder")
max_dl = slider(D, 1600, 570, 500, 1, 4000, nick="max MB per download")
wire(dl, "Max Download", max_dl, 0)

api_desc = input_panel(D, 1620, 880,
                       "Describe the API you configured here - what it returns, which paths are "
                       "useful, what the query parameters mean. This text goes into the prompt, so "
                       "the model knows the API exists before it decides to look anything up.",
                       w=200, h=180, nick="what your API offers")
wire(api, "Description", api_desc, 0)

files_out = panel(D, 2200, 600, "what it downloaded - PATHS, not text", w=340, h=140,
                  colour=OUTPUT_GREY)
files_out.AddSource(pin(dl, "out", "Downloaded Files"))
api_out = panel(D, 2200, 760, "records from the API, one per item", w=340, h=140,
                colour=OUTPUT_GREY)
api_out.AddSource(pin(api, "out", "Response"))
script = panel(D, 2200, 920, "the import script it ran", w=340, h=180, colour=OUTPUT_GREY)
script.AddSource(pin(drive, "out", "Last Script"))

panel(D, 1500, 1180,
      "DOWNLOAD FILE fetches into the project folder, and THE PATH ON THE WIRE IS THE POINT - a "
      "LiDAR tile is to be imported, not read about. Zips unpack by default, safely. The size cap "
      "is enforced while the file is streaming, not taken from what the server claims.\r\n"
      "\r\n"
      "It asks before downloading and before unpacking, and both prompts are ON by default: a "
      "download spends your bandwidth and fills your disk. Note they fail CLOSED, so with the chat "
      "window shut a download is refused - switch them off deliberately if you want this "
      "unattended.\r\n"
      "\r\n"
      "READ FILE is for the small stuff that tells you which big file to reach for: the index, the "
      "readme, the metadata sidecar, the CSV of tile names. It refuses a binary file with a "
      "description of what it is rather than handing over rubbish.\r\n"
      "\r\n"
      "API CALL reads an HTTP API you configured in the chat window - a city's open data endpoint, "
      "your own asset database. The model supplies a path and a query, never a URL and never a "
      "header, and it walks the paging itself so a 145-record answer arrives whole rather than as "
      "the first page. Records land on the wire ONE PER RECORD.\r\n"
      "\r\n"
      "IT ADVERTISES NOTHING UNTIL YOU PICK AN ENDPOINT - chat window, API calls. An unconfigured "
      "tool that failed every call would read to the model as a broken API.\r\n"
      "\r\n"
      "READ URL needs no key at all and is often enough: point it at the portal page and let it "
      "read the download links itself.",
      w=400, h=620)

# --------------------------------------------------------------------------- out

title(D, 2700, 560, "ONTO THE CANVAS", w=320, h=44)
out_files = place(D, "Harness Out", 2780, 660, nick="Harness Out")
out_files.Params.Input[0].NickName = "downloaded files"
wire(out_files, "Data", dl, "Downloaded Files")
out_rec = place(D, "Harness Out", 2780, 760, nick="Harness Out")
out_rec.Params.Input[0].NickName = "API records"
wire(out_rec, "Data", api, "Response")

panel(D, 2700, 860,
      "TWO GRIPS ON THE RIGHT EDGE of the Harness node. Drag them onto your own canvas.\r\n"
      "\r\n"
      "DOWNLOADED FILES gives you the paths, so a File Path parameter and an import component can "
      "do the rest inside Grasshopper - useful when you want the import parametric rather than "
      "baked.\r\n"
      "\r\n"
      "API RECORDS gives you the data one item per record, already unwrapped from whatever envelope "
      "the API puts round it and joined across pages. Feed it into a Panel to see the shape, then "
      "pull the fields you want.\r\n"
      "\r\n"
      "That is the difference between a chatbot that describes a dataset and a pipeline that hands "
      "it to you.",
      w=320, h=420)

# --------------------------------------------------------------------------- bounds + returns

budget = place(D, "Budget Guard", 980, 1520, nick="Budget Guard")
b_calls = slider(D, 760, 1600, 60, 1, 300, nick="max calls")
wire(budget, "Max Calls", b_calls, 0)
wire(budget, "Signal", L["log"], "Signal")
wire(L["call"], "Signal", budget, "Success Signal")

fb_res, co_res = back(D, dl, "Result", router, "Results",
                      2320, 1500, 1300, 1500, nick="tool results")
for t in (rf, api, readurl, drive):
    wire(fb_res, "Signal", t, "Result")
back(D, router, "Feedback", L["log"], "LLM Tool Signal",
     1300, 1880, 700, 1880, nick="tool round to the log")

panel(D, 2700, 1320,
      "THINGS TO TRY\r\n"
      "\r\n"
      "1. Start with READING, not downloading. \"Read this portal page and tell me what datasets "
      "are on it and in what formats.\" One cheap round, and you find out whether the site is "
      "readable at all.\r\n"
      "\r\n"
      "2. Then fetch ONE small file and ask what it is before importing it. The coordinate system "
      "answer is the one to check by hand.\r\n"
      "\r\n"
      "3. Then import it. Read the grey panel with the script in it - that is where a wrong "
      "assumption about units or axes will be visible.\r\n"
      "\r\n"
      "4. Try a host that blocks you. When the Fetch in browser button appears, use it, and watch "
      "the folder watcher pick the file up on its own.\r\n"
      "\r\n"
      "5. Configure a real API in the chat window - your city's open data endpoint is a good first "
      "one - and give the description box a proper description. That text is the only thing telling "
      "the model the API exists.\r\n"
      "\r\n"
      "6. Save the whole thing as a .phy when it works. The package carries the KNOWLEDGE of what "
      "was downloaded rather than the bytes, so a 400MB tile costs about 200 bytes and the other "
      "end re-fetches it.",
      w=440, h=560)

commit_build(D, "build scenario S09")
solve(D)
solve(D)
write_dump(D, DUMP)
say("router outputs:", [q.NickName for q in router.Params.Output])
bad = sweep(D, NAME)
save_phy(H, OUT,
         description="The half-day at the start of every project: find the city's open data, "
                     "download it, work out what format and coordinate system it is in, and get it "
                     "into Rhino on the right layers. The paths and the records come out onto your "
                     "canvas.",
         chat_text="Build the Site Context from Open Data\r\n\r\n"
                   "Start by READING, not downloading:\r\n"
                   "  \"read this portal page and tell me what datasets are on it\"\r\n\r\n"
                   "Then one file at a time:\r\n"
                   "  \"download that tile and tell me its coordinate system and extents - don't "
                   "import it yet\"\r\n"
                   "  \"now import it onto a layer called CONTEXT-TERRAIN\"\r\n\r\n"
                   "CHECK THE COORDINATE SYSTEM YOURSELF. It is where this goes wrong for "
                   "everyone, and a model 400km from the origin looks fine until it doesn't.\r\n\r\n"
                   "If a host blocks the download, a Fetch in browser button appears here. Use it "
                   "- the file lands in the project folder and the pipeline picks it up.")
say("PROBLEMS:", bad)
