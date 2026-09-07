# Copyright (c) 2026 Physalia Contributors
# SPDX-License-Identifier: AGPL-3.0-or-later
"""
Audit every written .phy for anything that should not travel: absolute paths, this machine's user
name, a provider or endpoint that exists only here.

A preset is meant to be sent to somebody, so anything in it that is true only of the machine that
wrote it is a defect - whether or not it self-heals at the other end.

RUNS OUTSIDE RHINO, on purpose. The first version read each package back through
HarnessComponent.ReadDocumentFile, which instantiates every component and therefore fires
AddedToDocument on every model node - each of which spawns a CLI process to fetch its model list.
Across fourteen presets that took minutes and eventually outlasted the MCP call's 300-second
response timeout. A .phy is an ordinary zip, so reading the bytes is both faster and more honest:
what is IN the file is exactly the question being asked.

    python tools/presets/audit.py
"""

import glob
import json
import os
import re
import sys
import zlib
import zipfile

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
PRESETS = os.path.join(ROOT, "wip_presets", "*.phy")

# Two lists, because the two places have different rules. A PATH or this user's name is wrong
# wherever it appears, prose included. A provider or endpoint NAME is only wrong as stored data - in
# prose it is usually a legitimate example ("Vancouver, Toronto and London all publish one"), and a
# check with a standing false positive is one people learn to skip.
# A PATH is unambiguous, so it is checked everywhere including prose. The user's name is only
# checked in PATH form: a bare "rober" also matches "Robert McNeel & Associates", which is in every
# archive Grasshopper writes.
PATHY = ["c:" + chr(92), "/users/", "users" + chr(92) + "rober", "appdata",
         "repos" + chr(92) + "physalia"]

# Nothing else generic is safe to scan for. A bare PROVIDER name is no use - "Tavily" and
# "DeepSeek" are in the shipped component descriptions of every preset with a Web Search or an
# OpenAI-compatible node. Nor is the "api__" tool-name prefix: Construct Tool Call's own
# description explains namespacing with the example "api__vancouver". The only reliable signal
# beyond a path is a name that is configured on THIS machine - see configured_names().
MACHINE = PATHY


def inflate(blob):
    """
    The Grasshopper archive inside a .phy is RAW DEFLATE, and this is the whole reason the first
    version of this check was worthless: it scanned the compressed bytes, found none of the strings
    that must be in there - not "Conversation Log", not "Claude", not even "Physalia" - and reported
    every preset clean. A check that cannot fail is worse than no check.
    """
    for wbits in (-15, 15, 47):          # raw deflate, zlib, gzip
        try:
            return zlib.decompress(blob, wbits)
        except zlib.error:
            continue
    return blob                           # not compressed: scan it as it is


def strings_in(blob):
    """Every run of printable characters in the inflated archive, as UTF-8 and as UTF-16."""
    data = inflate(blob)
    out = []
    for text in (data.decode("utf-8", "ignore"), data.decode("utf-16-le", "ignore")):
        out.extend(re.findall(r"[ -~]{4,}", text))
    return out


def self_test(blob):
    """
    Prove the scan is reading the archive before trusting a clean verdict. Every preset in this set
    has a Conversation Log and a Chat in it, so their names must turn up.
    """
    low = [s.lower() for s in strings_in(blob)]
    return [w for w in ("conversation log", "chat", "physalia") if not any(w in s for s in low)]


def configured_names():
    """
    The API endpoints and MCP servers configured on THIS machine, lower-cased.

    Read from the stores rather than hard-coded, so the check keeps working as they change - and so
    it says something true about the machine that wrote the file rather than about mine.
    """
    names = []
    base = os.path.join(os.environ.get("LOCALAPPDATA", ""), "Physalia")
    for filename, key in (("api-endpoints.json", "apiEndpoints"), ("mcp-servers.json", "mcpServers")):
        try:
            with open(os.path.join(base, filename), encoding="utf-8-sig") as f:
                names.extend(k.lower() for k in (json.load(f).get(key) or {}))
        except Exception:
            continue
    return names


def audit(path):
    findings = []
    with zipfile.ZipFile(path) as z:
        names = z.namelist()
        if "manifest.json" not in names:
            findings.append("no manifest.json - the loader would refuse this")
        if "harness.gh" not in names:
            findings.append("no harness.gh - there is no pipeline in it")

        for entry in names:
            if entry.startswith("files/") and not entry.endswith("/"):
                findings.append("carries a project file: %s" % entry)

        if "manifest.json" in names:
            mani = {}
            try:
                mani = json.loads(z.read("manifest.json").decode("utf-8-sig"))
            except Exception as ex:
                findings.append("manifest will not parse: %s" % ex)
            for field in ("name", "description", "chatText"):
                value = (mani.get(field) or "").lower()
                for bad in PATHY:
                    if bad in value:
                        findings.append("manifest %s mentions %r" % (field, bad))
            if not mani.get("description"):
                findings.append("manifest has no description - the gallery would show none")
            if not mani.get("chatText"):
                findings.append("manifest has no chat text - the window shows the generic greeting")

        if "harness.gh" in names:
            blob = z.read("harness.gh")
            missing = self_test(blob)
            if missing:
                findings.append("SCAN IS NOT WORKING - these must be in every preset and are not: %s"
                                % ", ".join(missing))
            found = strings_in(blob)
            lowered = [s.lower() for s in found]
            for bad in MACHINE:
                hit = next((s for s in lowered if bad in s), None)
                if hit is not None:
                    findings.append("archive holds %r: %s" % (bad, hit[:80]))

            # An endpoint or server that only exists on this machine, stored as a node's own
            # setting. Read from the live stores so the check stays true as they change, rather
            # than from a list that goes stale.
            for name in configured_names():
                # Both spellings: as stored on a node, and as a Router or tool node derives a
                # namespaced tool name from it (spaces become underscores).
                for spelling in (name, name.replace(" ", "_")):
                    hit = next((s for s in lowered if spelling in s), None)
                    if hit is not None:
                        findings.append("archive holds this machine's %r: %s" % (name, hit[:80]))
                        break
    return findings


def main():
    files = sorted(glob.glob(PRESETS))
    if not files:
        print("no presets found at %s" % PRESETS)
        return 1
    total = 0
    for path in files:
        found = audit(path)
        print("%-42s %s" % (os.path.basename(path),
                            "clean" if not found else "%d finding(s)" % len(found)))
        for f in found:
            print("      " + f)
        total += len(found)
    print("---- %d preset(s), %d finding(s) ----" % (len(files), total))
    return 1 if total else 0


if __name__ == "__main__":
    sys.exit(main())
