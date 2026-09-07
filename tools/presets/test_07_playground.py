# Copyright (c) 2026 Physalia Contributors
# SPDX-License-Identifier: AGPL-3.0-or-later
"""
Drive preset 07's playground with no model involved: press each button, solve, and read what came
out of the relay. Deterministic, instant and free - which is exactly why the playground is on the
canvas in the first place.
"""

exec(open(r"C:\Users\rober\repos\Physalia\tools\presets\phybuild.py").read())

H = find_harnesses()[0]
D = H.InnerDocument
D.Enabled = True

BY_NICK = {}
for o in D.Objects:
    BY_NICK.setdefault(o.NickName, []).append(o)


def one(nick):
    got = BY_NICK.get(nick, [])
    if len(got) != 1:
        raise Exception("expected exactly one %r, found %d" % (nick, len(got)))
    return got[0]


def press(nick):
    """
    A false->true->false edge on a Button, solving in between - one minted signal.

    Note GH_ButtonObject has no Value property; the one that exists is ButtonDown. Assigning
    `Value` from Python succeeds silently and does nothing at all, which reads on the canvas as
    "the trigger never fires".
    """
    b = one(nick)
    b.ButtonDown = True
    b.ExpireSolution(False)
    D.NewSolution(True)
    b.ButtonDown = False
    b.ExpireSolution(False)
    D.NewSolution(True)


def payload_after(dec_owner_nick, out_name):
    """The text a relay put out, read off the Deconstruct Signal wired to it."""
    src = one(dec_owner_nick)
    p = pin(src, "out", out_name)
    for r in p.Recipients:
        owner = r.Attributes.GetTopLevel.DocObject
        if owner.Name == "Deconstruct Signal":
            return [str(v) for v in pin(owner, "out", "Payload").VolatileData.AllData(True)]
    return None


say("=== A: Merge Signal is a JOIN ===")
press("press A1")
say("  after A1 only:", payload_after("Merge Signal", "Signal"),
    "| caption:", one("Merge Signal").Message)
press("press A2")
say("  after A2 too :", payload_after("Merge Signal", "Signal"),
    "| caption:", one("Merge Signal").Message)

say("=== C: Signal Switch matches the payload text ===")
press("press C")
say("  Match   :", payload_after("Signal Switch", "Match"))
say("  No Match:", payload_after("Signal Switch", "No Match"))

say("=== D: Signal Throttle lets one through per interval ===")
press("press D fast")
first = payload_after("Signal Throttle", "Signal")
press("press D fast")
press("press D fast")
say("  after three presses:", payload_after("Signal Throttle", "Signal"),
    "| caption:", one("Signal Throttle").Message)

say("=== B: Hold Signal waits for its Release ===")
press("press B")
say("  held    - Released:", payload_after("Hold Signal", "Released"),
    "| caption:", one("Hold Signal").Message)
one("let it go").Value = True   # a Toggle DOES have Value
D.NewSolution(True)
D.NewSolution(True)
say("  released- Released:", payload_after("Hold Signal", "Released"),
    "| caption:", one("Hold Signal").Message)
one("let it go").Value = False

say("=== E: For Each steps through the list ===")
fe = one("For Each")
press("press E to start")
say("  after start:", [str(v) for v in pin(fe, "out", "Item").VolatileData.AllData(True)],
    "index", [str(v) for v in pin(fe, "out", "Index").VolatileData.AllData(True)],
    "| caption:", fe.Message)
for n in range(3):
    press("press for the next one")
    say("  step %d     :" % (n + 1),
        [str(v) for v in pin(fe, "out", "Item").VolatileData.AllData(True)],
        "index", [str(v) for v in pin(fe, "out", "Index").VolatileData.AllData(True)],
        "| caption:", fe.Message)
say("  Done payload:", payload_after("For Each", "Done Signal"))

say("=== error sweep after all that pressing ===")
bad = 0
for o in D.Objects:
    for m in o.RuntimeMessages(GH_RuntimeMessageLevel.Error):
        say("  ERROR", o.NickName or o.Name, m)
        bad += 1
say("errors:", bad)
