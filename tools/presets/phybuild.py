# Copyright (c) 2026 Physalia Contributors
# SPDX-License-Identifier: AGPL-3.0-or-later
"""
Helpers for building Physalia harnesses from a script, driven through the Rhino MCP.

Loaded by every build script with:

    exec(open(r"C:\\Users\\rober\\repos\\Physalia\\tools\\presets\\phybuild.py").read())

Everything here is deliberately blunt: it wraps the traps recorded in the
`building-harnesses-programmatically` memory note so a build script cannot fall into them again.
"""

import clr, System, os, re
clr.AddReference("Grasshopper")
clr.AddReference("GH_IO")
import Grasshopper
import GH_IO
from Grasshopper import Instances
from Grasshopper.Kernel import GH_Document, GH_ParamAccess, GH_RuntimeMessageLevel, GH_ParameterSide
from Grasshopper.Kernel.Special import GH_Panel
from System.Drawing import PointF, RectangleF, Color
from System.Reflection import BindingFlags

LOG = []


def say(*bits):
    line = " ".join(str(b) for b in bits)
    LOG.append(line)
    print(line)


# --------------------------------------------------------------------------- proxies

_PROXY = {}


def _index_proxies():
    if _PROXY:
        return
    for p in Instances.ComponentServer.ObjectProxies:
        _PROXY.setdefault((p.Desc.Category, p.Desc.SubCategory, p.Desc.Name), p.Guid)
        _PROXY.setdefault((p.Desc.Category, p.Desc.Name), p.Guid)
        _PROXY.setdefault(p.Desc.Name, p.Guid)


def proxy_guid(name, category="Physalia", sub=None):
    """
    The ComponentGuid for a component.

    The ribbon SECTION has to be spellable, because Physalia has names that collide inside its own
    category: "Component Catalog" is both a Grounding component and a Params type, and "Read PDF"
    is both an LLM tool and a human tool. Without the section, whichever loaded first wins.
    """
    _index_proxies()
    for key in ((category, sub, name), (category, name), name):
        if sub is None and key == (category, sub, name):
            continue
        if key in _PROXY:
            return _PROXY[key]
    raise Exception("no proxy named %r in %r / %r" % (name, category, sub))


# --------------------------------------------------------------------------- placing

def place(doc, name, x, y, nick=None, category="Physalia", sub=None):
    """Emit a component and drop it at a canvas pivot. Returns the object."""
    obj = Instances.ComponentServer.EmitObject(proxy_guid(name, category, sub))
    if obj is None:
        raise Exception("EmitObject returned None for %r" % name)
    if obj.Attributes is None:
        obj.CreateAttributes()
    obj.Attributes.Pivot = PointF(float(x), float(y))
    doc.AddObject(obj, False)
    if nick:
        obj.NickName = nick
    return obj


NOTE_YELLOW = Color.FromArgb(255, 255, 250, 205)
TITLE_BLUE = Color.FromArgb(255, 205, 232, 255)
INTRO_GREEN = Color.FromArgb(255, 214, 240, 214)
OUTPUT_GREY = Color.FromArgb(255, 236, 236, 236)
ERROR_PINK = Color.FromArgb(255, 255, 218, 218)
WARN_ORANGE = Color.FromArgb(255, 255, 232, 200)
INPUT_WHITE = Color.FromArgb(255, 255, 255, 255)


def panel(doc, x, y, text, w=260, h=120, colour=None, align="Left"):
    """
    A note on the canvas. Panels are the annotation these presets use: they wrap, they hold as much
    prose as they need, and nobody mistakes one for part of the pipeline.
    """
    p = GH_Panel()
    p.CreateAttributes()
    p.UserText = text
    p.Properties.Multiline = True
    p.Properties.Wrap = True
    p.Properties.Alignment = System.Enum.Parse(
        p.Properties.GetType().GetProperty("Alignment").PropertyType, align)
    p.Properties.Colour = colour if colour is not None else NOTE_YELLOW
    p.Attributes.Pivot = PointF(float(x), float(y))
    doc.AddObject(p, False)
    p.Attributes.Bounds = RectangleF(float(x), float(y), float(w), float(h))
    return p


def title(doc, x, y, text, w=520, h=54):
    """A heading panel - paler, for naming a whole stage of the pipeline."""
    return panel(doc, x, y, text, w, h, TITLE_BLUE, align="Center")


def input_panel(doc, x, y, text, w=280, h=90, nick=None):
    """
    A panel used as a live INPUT rather than as a note: the user edits it on the canvas and the
    text goes straight into whatever it is wired to. Much better than a Text parameter, which
    renders as an anonymous little capsule with the value hidden inside it.
    """
    p = panel(doc, x, y, text, w, h, INPUT_WHITE)
    if nick:
        p.NickName = nick
    return p


def slider(doc, x, y, value, lo=0.0, hi=100.0, nick=None, integer=True):
    from Grasshopper.Kernel.Special import GH_NumberSlider
    s = GH_NumberSlider()
    s.CreateAttributes()
    s.Slider.Minimum = System.Decimal(lo)
    s.Slider.Maximum = System.Decimal(hi)
    s.Slider.DecimalPlaces = 0 if integer else 2
    s.SetSliderValue(System.Decimal(value))
    s.Attributes.Pivot = PointF(float(x), float(y))
    doc.AddObject(s, False)
    if nick:
        s.NickName = nick
    return s


def boolean(doc, x, y, value=False, nick=None, toggle=True):
    from Grasshopper.Kernel.Special import GH_BooleanToggle, GH_ButtonObject
    b = GH_BooleanToggle() if toggle else GH_ButtonObject()
    b.CreateAttributes()
    if toggle:
        b.Value = value
    b.Attributes.Pivot = PointF(float(x), float(y))
    doc.AddObject(b, False)
    if nick:
        b.NickName = nick
    return b


def text_param(doc, x, y, value, nick=None):
    """A Text parameter holding internalized text - the plain way to feed a text input."""
    from Grasshopper.Kernel.Parameters import Param_String
    p = Param_String()
    p.CreateAttributes()
    p.Attributes.Pivot = PointF(float(x), float(y))
    doc.AddObject(p, False)
    if nick:
        p.NickName = nick
    setdata(p, value)
    return p


# --------------------------------------------------------------------------- params

def pin(obj, which, name):
    """Find an input or output param by name (case-insensitive), or raise with the real list."""
    coll = obj.Params.Input if which == "in" else obj.Params.Output
    for p in coll:
        if p.Name.lower() == name.lower() or p.NickName.lower() == name.lower():
            return p
    raise Exception("%s has no %sput %r; it has %s"
                    % (obj.Name, which, name, [q.Name for q in coll]))


def pins(obj):
    return ("IN " + ", ".join(p.Name for p in obj.Params.Input)
            + " | OUT " + ", ".join(p.Name for p in obj.Params.Output))


def setdata(param, value):
    """
    Internalize data on an input.

    Two traps, both wrapped here for good: SetPersistentData APPENDS to whatever default the
    component registered (leaving two items on an item-access input, which solves the component
    twice), and a bare string resolves to IEnumerable<char> and stores one item per CHARACTER.
    So: clear first, and ALWAYS wrap.
    """
    try:
        param.Script_ClearPersistentData()
    except Exception:
        pass
    values = value if isinstance(value, (list, tuple)) else [value]
    param.SetPersistentData(System.Array[System.Object]([v for v in values]))


def wire(dst, dst_in, src, src_out):
    """A forward wire: dst.<dst_in> <- src.<src_out>."""
    d = pin(dst, "in", dst_in) if isinstance(dst_in, str) else dst.Params.Input[dst_in]
    if hasattr(src, "Params"):
        s = pin(src, "out", src_out) if isinstance(src_out, str) else src.Params.Output[src_out]
    else:
        s = src  # a floating param is its own output
    d.AddSource(s)
    return d


# --------------------------------------------------------------------------- wireless returns

def back(doc, src, src_out, dst, dst_in, fx, fy, cx, cy, nick=None):
    """
    A BACKWARD hop, which must be wireless.

    Every return path in a Physalia pipeline (LLM Call -> Conversation Log, Router -> Conversation
    Log, a tool's Result -> Router) closes a cycle Grasshopper refuses: an ordinary wire gets
    "Recursive data stream found, this component depends on itself". The Feedback / Feedback
    Collector pair exists to break the DAG, and collectors are NOT shareable across destinations -
    each return path gets its own pair.
    """
    fb = place(doc, "Feedback", fx, fy, nick=nick or "Feedback")
    fc = place(doc, "Feedback Collector", cx, cy, nick=nick or "Collector")
    wire(fb, "Signal", src, src_out)
    fb.AddCollector(fc.InstanceGuid)
    wire(dst, dst_in, fc, 0)
    return fb, fc


# --------------------------------------------------------------------------- pickers

def add_picker(doc, obj, input_name, x=None, y=None):
    """
    Attach a Picker to an input that does not auto-place one.

    Some nodes place their own (System Prompt, the Model nodes); API Call and MCP Server do not,
    because what they offer comes from a per-machine store. A Picker learns its list from whatever
    it is wired to, so wiring it up is all there is to it.
    """
    p = pin(obj, "in", input_name)
    pk = place(doc, "Picker", 0, 0, sub="Extra")
    pk.Attributes.Pivot = PointF(
        float(x) if x is not None else obj.Attributes.Pivot.X - 200.0,
        float(y) if y is not None else obj.Attributes.Pivot.Y)
    p.AddSource(pk.Params.Output[0])
    return pk


def picker_of(doc, obj, input_name):
    """The Picker that auto-placed itself on one of a component's inputs, if any."""
    p = pin(obj, "in", input_name)
    for s in p.Sources:
        owner = s.Attributes.GetTopLevel.DocObject if s.Attributes else None
        if owner is not None and owner.Name == "Picker":
            return owner
    return None


def pick(doc, obj, input_name, value):
    """
    Set a Picker's choice.

    Order matters and is not negotiable: Picker.SolveInstance resets to values[0] when the current
    value is not in its list, and the list is only repopulated by the OWNER's solve. So solve,
    then set, then solve again. SetSelectedValue is internal - reflection.
    """
    pk = picker_of(doc, obj, input_name)
    if pk is None:
        raise Exception("no Picker on %s.%s" % (obj.Name, input_name))
    doc.NewSolution(False)
    m = pk.GetType().GetMethod("SetSelectedValue",
                               BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
    if m is None:
        raise Exception("SetSelectedValue not found on Picker")
    m.Invoke(pk, System.Array[System.Object]([value]))
    doc.NewSolution(False)
    return pk


def clear_pick(doc, obj, input_name):
    """
    Blank a Picker's saved choice, so a per-machine value is not written into a shared preset.

    Call it AFTER the last solve and immediately before saving: a further solve snaps the Picker to
    values[0] again. On the reader's machine an empty pick lands on whatever their own list offers
    first, which is what self-healing looks like.
    """
    pk = picker_of(doc, obj, input_name)
    if pk is None:
        return None
    m = pk.GetType().GetMethod("SetSelectedValue",
                               BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
    m.Invoke(pk, System.Array[System.Object]([""]))
    return pk


def picker_values(doc, obj, input_name):
    pk = picker_of(doc, obj, input_name)
    if pk is None:
        return None
    doc.NewSolution(False)
    prop = pk.GetType().GetProperty("MenuValues",
                                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
    if prop is None:
        return "MenuValues not found"
    return [str(v) for v in prop.GetValue(pk)]


def list_panel(doc, x, y, lines, w=200, h=90, nick=None):
    """
    A panel used as a LIST input: one item per line.

    Multiline must be OFF for that. A Panel's only storage is one string, and its data collection
    splits that string BY LINE - but only when Multiline is off. Left on, the whole thing arrives
    as ONE item with newlines in it, which is how three routes typed into a Declare node become a
    single nonsense route and the node reports "one route means the model has nothing to choose
    between".
    """
    joined = ("\r\n").join(lines) if isinstance(lines, (list, tuple)) else lines
    p = panel(doc, x, y, joined,
              w, h, INPUT_WHITE)
    p.Properties.Multiline = False
    if nick:
        p.NickName = nick
    return p


def blank_input(doc, obj, input_name, x, y, label="(none)"):
    """
    Leave an input deliberately EMPTY, in a way that survives a save and reload.

    Removing an auto-placed Picker is not enough. AddedToDocument re-places one whenever the input
    has no source, and that fires again when a preset is read back - so the Picker returns, and on
    its SECOND solve (the first one its list is still empty) it snaps to values[0]. Measured: a
    plain conversational preset reloaded with the 11,900-character C# script preamble folded into
    its system prompt.

    So the input gets a real source holding nothing: SourceCount is 1, no Picker is ever added, and
    DA.GetData simply returns false.
    """
    drop_picker(doc, obj, input_name)
    t = input_panel(doc, x, y, "", w=140, h=40, nick=label)
    wire(obj, input_name, t, 0)
    return t


def drop_picker(doc, obj, input_name):
    """
    Remove a Picker that auto-placed itself on an input that should stay EMPTY. Left alone it snaps
    to values[0] - which is how another pipeline's JSON schema quietly folds itself into a prompt.
    """
    pk = picker_of(doc, obj, input_name)
    if pk is not None:
        doc.RemoveObject(pk, False)
    return pk is not None


# --------------------------------------------------------------------------- variable params

def router_slots(router, count):
    """Grow the Router's variable outputs, one per tool, inserted BEFORE the trailing Feedback."""
    made = []
    for _ in range(count):
        idx = router.Params.Output.Count - 1
        p = router.CreateParameter(GH_ParameterSide.Output, idx)
        router.Params.RegisterOutputParam(p, idx)
        made.append(p)
    router.Params.OnParametersChanged()
    router.VariableParameterMaintenance()
    return made


def merge_inputs(merge, count):
    """Grow Merge Signal's variable inputs. Added at the END only - the holds are index-keyed."""
    while merge.Params.Input.Count < count:
        idx = merge.Params.Input.Count
        p = merge.CreateParameter(GH_ParameterSide.Input, idx)
        merge.Params.RegisterInputParam(p, idx)
    merge.Params.OnParametersChanged()
    merge.VariableParameterMaintenance()


# --------------------------------------------------------------------------- harness

def host_document():
    """The document the user's canvas is pointed at, creating one if Grasshopper has none."""
    canvas = Instances.ActiveCanvas
    srv = Instances.DocumentServer
    if srv.DocumentCount == 0:
        # AddNewDocument is the one that works: AddDocument takes a PATH, not a GH_Document.
        srv.AddNewDocument()

    # A harness sub-document is NOT in the document server, and placing a harness from a script
    # points the canvas INTO it - so "the canvas's document" is not a reliable answer to "which is
    # the user's canvas". Ask the server. Compare by DocumentID: Python.NET hands out a different
    # proxy object for the same .NET document, so `is` reads False on two views of one document.
    host = srv[srv.DocumentCount - 1]
    if canvas.Document is None or canvas.Document.DocumentID != host.DocumentID:
        canvas.set_Document(host)
    return host


def find_harnesses():
    """Every Harness on every server document."""
    srv = Instances.DocumentServer
    out = []
    for i in range(srv.DocumentCount):
        for o in srv[i].Objects:
            if o.Name == "Harness":
                out.append(o)
    return out


def enter(harness):
    """Point the canvas at a harness's inner document, the way "Edit Harness" does."""
    inner = harness.EnsureInnerDocument()
    inner.Enabled = True
    Instances.ActiveCanvas.set_Document(inner)
    return inner


def new_harness(host, x=200, y=200, name=None):
    """
    Place a Harness on the host canvas and return (component, inner document).

    A harness emitted by ComponentServer comes up EMPTY - no Chat - so every build must add its
    own, or the preset loader will refuse the result.
    """
    h = place(host, "Harness", x, y)
    inner = h.EnsureInnerDocument()
    if name:
        h.NickName = name
    return h, inner


def _asm(name):
    for a in System.AppDomain.CurrentDomain.GetAssemblies():
        if a.GetName().Name == name:
            return a
    raise Exception("%s is not loaded" % name)


def _type(full_name):
    """
    A type by full name, from wherever it ended up.

    Physalia.Core is merged into the .gha by ILRepack, so at runtime its types live inside the
    Physalia.GH assembly and there is no Physalia.Core assembly to ask.
    """
    for a in System.AppDomain.CurrentDomain.GetAssemblies():
        n = a.GetName().Name
        if not n.startswith("Physalia"):
            continue
        t = a.GetType(full_name)
        if t is not None:
            return t
    raise Exception("type %s not found in any Physalia assembly" % full_name)


def clear_host():
    """Empty the host canvas, so a rebuild starts from nothing."""
    host = host_document()
    lst = System.Collections.Generic.List[Grasshopper.Kernel.IGH_DocumentObject]()
    for o in host.Objects:
        lst.Add(o)
    if lst.Count:
        host.RemoveObjects(lst, False)
    return host


def solve(doc):
    """
    Solve a harness sub-document.

    A sub-document's Enabled flag is Physalia's own invariant, re-asserted whenever the proxy
    solves - and the proxy has not solved here, so a scripted build must set it itself or every
    scheduled solution is silently dropped.
    """
    doc.Enabled = True
    doc.NewSolution(True)


def save_phy(harness, path, description=None, chat_text=None):
    """
    Write the harness out as a .phy without any dialog.

    SavePackage() prompts for a destination and confirms the payload size, which would block an
    MCP call forever; this is the same write underneath (PresetLibrary.TryWritePackage), with no
    project files since a demo preset carries none.
    """
    if description is not None:
        harness.ImportDescription = description
    if chat_text is not None:
        harness.ChatText = chat_text

    inner = harness.InnerDocument
    chats = [o for o in inner.Objects if o.Name == "Chat"]
    if not chats:
        raise Exception("refusing to write %s: the harness has no Chat, so the loader would refuse it" % path)

    mtype = _type("Physalia.Core.Packaging.PhyManifest")
    mfor = mtype.GetMethod("For", BindingFlags.Static | BindingFlags.Public)
    manifest = mfor.Invoke(None, System.Array[System.Object](
        [harness.NickName, harness.ImportDescription, harness.ChatText, None]))

    lib = _type("Physalia.GH.Harness.PresetLibrary")
    m = lib.GetMethod("TryWritePackage", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
    args = System.Array[System.Object]([path, manifest, inner, None, System.Int64(0), ""])
    ok = m.Invoke(None, args)
    if not ok:
        raise Exception("TryWritePackage failed: %s" % args[5])
    say("wrote", path, "(%d bytes)" % args[4])
    return int(args[4])


# --------------------------------------------------------------------------- checking

def sweep(doc, label=""):
    """Every runtime message in the document, plus the silent-defect checks. The build's own test."""
    say("---- sweep", label, "----", doc.ObjectCount, "objects")
    bad = 0
    for o in doc.Objects:
        for lvl in (GH_RuntimeMessageLevel.Error, GH_RuntimeMessageLevel.Warning, GH_RuntimeMessageLevel.Remark):
            try:
                msgs = o.RuntimeMessages(lvl)
            except Exception:
                continue
            for m in msgs:
                say("  %-8s %-26s %s" % (str(lvl).upper(), o.NickName or o.Name, m))
                if lvl == GH_RuntimeMessageLevel.Error:
                    bad += 1
    # doubled internalized data is silent and makes a component solve twice
    for o in doc.Objects:
        ps = getattr(o, "Params", None)
        if ps is None:
            continue
        for p in ps.Input:
            pd = getattr(p, "PersistentData", None)
            try:
                n = pd.DataCount if pd is not None else 0
            except Exception:
                n = 0
            if p.SourceCount == 0 and n > 1 and p.Access == GH_ParamAccess.item:
                say("  DOUBLED  %-26s %s has %d internalized items on an item input"
                    % (o.NickName or o.Name, p.Name, n))
                bad += 1
    layout(doc)
    bad += overlaps(doc)
    say("---- sweep end, %d problems ----" % bad)
    return bad


def overlaps(doc, tol=4.0):
    """
    Report objects sitting on top of each other.

    Component errors say nothing about layout, and a note panel half under another one is exactly
    the defect these presets cannot afford. Panels are also MEASURED at layout time rather than
    taking the size they were handed, so this has to be run after a solve.
    """
    boxes = []
    for o in doc.Objects:
        b = o.Attributes.Bounds
        boxes.append((o.Name, o.NickName or "", b.Left, b.Top, b.Right, b.Bottom))
    n = 0
    shown = 0
    for i in range(len(boxes)):
        for j in range(i + 1, len(boxes)):
            a, b = boxes[i], boxes[j]
            ox = min(a[4], b[4]) - max(a[2], b[2])
            oy = min(a[5], b[5]) - max(a[3], b[3])
            if ox > tol and oy > tol:
                n += 1
                if shown < 25:
                    shown += 1
                    say("  OVERLAP %.0fx%.0f: %s '%s' [%.0f..%.0f, %.0f..%.0f]  vs  %s '%s' [%.0f..%.0f, %.0f..%.0f]"
                        % (ox, oy, a[0], a[1], a[2], a[4], a[3], a[5],
                           b[0], b[1], b[2], b[4], b[3], b[5]))
    say("overlaps:", n)
    return n


def dump(doc):
    """The whole wire list plus pivots - how a build is read back and checked."""
    out = []
    for o in sorted(doc.Objects, key=lambda q: (q.Attributes.Pivot.X, q.Attributes.Pivot.Y)):
        piv = o.Attributes.Pivot
        out.append("== %-26s nick=%-24s (%.0f,%.0f)" % (o.Name, o.NickName, piv.X, piv.Y))
        ps = getattr(o, "Params", None)
        if ps is None:
            if hasattr(o, "Sources"):
                for s in o.Sources:
                    ow = s.Attributes.GetTopLevel.DocObject if s.Attributes else None
                    out.append("     <- %s.%s" % (ow.NickName if ow else "?", s.NickName))
            continue
        for p in ps.Input:
            srcs = []
            for s in p.Sources:
                ow = s.Attributes.GetTopLevel.DocObject if s.Attributes else None
                srcs.append("%s.%s" % ((ow.NickName or ow.Name) if ow else "?", s.NickName))
            extra = ""
            pd = getattr(p, "PersistentData", None)
            try:
                if pd is not None and pd.DataCount > 0:
                    extra = " {%s}" % "|".join(str(x) for x in pd.AllData(True))[:90]
            except Exception:
                pass
            out.append("   IN  %-22s <- %s%s" % (p.Name, ", ".join(srcs) if srcs else "-", extra))
        for p in ps.Output:
            out.append("   OUT %-22s -> %d" % (p.Name, p.Recipients.Count))
    return "\n".join(out)


def layout(doc):
    """
    Make every object work out its real size.

    Attributes.Bounds is whatever the constructor left there until Layout() has run, and layout is
    performed on SOLUTION rather than on paint - so a freshly scripted document reports every
    component as 150x20 at the origin, and any overlap check run against that is meaningless.
    """
    for o in doc.Objects:
        if o.Attributes is not None:
            o.Attributes.ExpireLayout()
    for o in doc.Objects:
        if o.Attributes is not None:
            o.Attributes.PerformLayout()


def write_log(path):
    """
    Dump everything say() has collected to a file.

    Worth doing at the end of any long sweep: the MCP call's RESPONSE times out at 300 seconds even
    when the work finished, and stdout goes with it. A file survives.
    """
    with open(path, "w") as f:
        f.write(chr(10).join(LOG))
    return path


def write_dump(doc, path):
    """Park the full wire dump in a file - it is the build's test, but far too long to read back
    through a tool call every time."""
    with open(path, "w") as f:
        f.write(dump(doc))
    say("wire dump ->", path)


def commit_build(doc, label):
    """One Ctrl+Z for the whole build, and one solve."""
    lst = System.Collections.Generic.List[Grasshopper.Kernel.IGH_DocumentObject]()
    for o in doc.Objects:
        lst.Add(o)
    doc.UndoUtil.RecordAddObjectEvent(label, lst)
    doc.NewSolution(False)
    layout(doc)
