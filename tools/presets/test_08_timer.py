# Copyright (c) 2026 Physalia Contributors
# SPDX-License-Identifier: AGPL-3.0-or-later
"""
Arm preset 08's Timer with a tiny budget and let it run: the point is to see rounds start with
nobody typing, and then to see the Budget Guard refuse once Max Calls is used up.

Run test_08_timer_arm.py, wait, then test_08_timer_read.py. Set PHY first.
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
        raise Exception("expected one %r, found %d" % (nick, len(got)))
    return got[0]


def set_slider(nick, value):
    s = one(nick)
    s.SetSliderValue(System.Decimal(value))
    s.ExpireSolution(False)


# Small numbers so the test is short and cheap: a call every few seconds, and a budget of two.
set_slider("every N seconds", 5)
set_slider("one round every N seconds", 1)
set_slider("max calls", 2)
set_slider("max tokens", 2000000)
D.NewSolution(True)

timer = one("Timer")
say("timer before arming:", timer.Message)
timer.SetArmed(True)
D.NewSolution(True)
say("timer armed:", timer.Message)
say("budget:", one("Budget Guard").Message)
