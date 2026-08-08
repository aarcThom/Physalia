// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using GH_IO.Serialization;
using Grasshopper;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Undo;
using Grasshopper.Kernel.Undo.Actions;
using Physalia.GH.Components;

namespace Physalia.GH.Harness;

/// <summary>
/// A proxy node holding a Physalia pipeline in its own Grasshopper document. Double-clicking it
/// takes the canvas into that document — a secondary screen — the way double-clicking a cluster
/// does; the canvas return widget (or File, "Save and Return") brings you back.
///
/// <para>Unlike a cluster this needs no input or output hooks. A Chat has no inputs and one
/// output, and a Physalia pipeline never exchanges dataflow with the user's canvas: it only
/// <em>scans</em> it (grounders, guardrails) and <em>writes to it by side effect</em> (component
/// placement). So the whole pipeline, Chat included, lives inside and nothing crosses the boundary
/// as a wire.</para>
///
/// <para>The inner document is kept enabled while it is off-canvas so the pipeline keeps solving
/// while you work on your model — an async LLM response must be able to land at any time. It is
/// persisted inside this component's own archive, so a harness travels in the host .gh file.</para>
///
/// <para>Components inside the harness see the inner document from <c>OnPingDocument()</c>; anything
/// that means "the user's canvas" must go through <see cref="PhyDocuments"/>.</para>
/// </summary>
public sealed class HarnessComponent : PhyBase
{
    // Archive chunk holding the inner document, written by GH_Document.Write and rehydrated by
    // GH_Document.Read. This is the only nested-archive persistence in the plug-in; every other
    // Write/Read override in Physalia writes flat keys and guid references.
    private const string DocumentChunk = "HarnessDocument";

    // Margin (canvas units) around the harness contents when the view zooms to fit on entry.
    private const int ZoomMargin = 5;

    // Duration in ms of the animated zoom-to-fit, matching GH's own cluster-editing transition.
    private const int ZoomDuration = 250;

    // Maps a harness document back to the component holding it.
    //
    // Deliberately NOT GH_Document.Owner, even though that is what a cluster uses. Setting Owner
    // makes GH_Canvas paint its own hard-coded cluster icon at the top-left of the canvas (a
    // private _regionCluster, not a widget, so it cannot be removed from the widget list) whose
    // menu runs "Save and Return" — and that calls GH_DocumentServer.RemoveDocument, which
    // DISPOSES the document and would destroy the running pipeline. Owning the mapping ourselves
    // leaves the harness return widget as the only affordance, and as a bonus keeps the document's
    // render queue enabled (Owner's setter disables it), so previews from inside a harness survive.
    //
    // Weak on the document, so a discarded harness document is collected normally.
    private static readonly ConditionalWeakTable<GH_Document, HarnessComponent> Owners = new();

    private GH_Document? _inner;

    /// <summary>
    /// Initializes a new instance of the <see cref="HarnessComponent"/> class.
    /// </summary>
    public HarnessComponent()
        : base("Harness", "Harness", "Holds a Physalia pipeline in its own document. Double-click to edit it on a secondary canvas.", "Pipeline")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("C4E1A9D2-6B33-4F58-9A07-2D5E8C1B4F60");

    /// <summary>
    /// Gets the document holding the pipeline, or null when this harness has never been opened
    /// or populated. Does not create one — use <see cref="EnsureInnerDocument"/> for that, so a
    /// mere lookup never leaves empty documents behind.
    /// </summary>
    public GH_Document? InnerDocument => _inner;

    /// <summary>
    /// Gets the number of objects held in the harness.
    /// </summary>
    public int Count => _inner?.ObjectCount ?? 0;

    /// <inheritdoc/>
    public override void CreateAttributes()
    {
        m_attributes = new Attributes.HarnessAttrib(this);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Editing the harness is the right-click action, because double-click is spent on the chat
    /// window — the harness node stands in for the Chat that moved inside it, so it behaves like
    /// one.
    /// </remarks>
    public override void AppendAdditionalMenuItems(ToolStripDropDown menu)
    {
        base.AppendAdditionalMenuItems(menu);
        Menu_AppendSeparator(menu);
        Menu_AppendItem(menu, "Edit Harness", (_, _) => OpenInCanvas());
    }

    /// <summary>
    /// Moves a set of objects off a document and into a new harness placed where they were.
    ///
    /// <para>The objects are carried across by archive rather than by reference: Grasshopper ties a
    /// document object to the document that holds it, so a move is a serialize-delete-deserialize.
    /// Wires between the moved objects survive because they are recorded by instance guid inside the
    /// same archive; wires to objects left behind do not, which is why the Chat menu item pulls the
    /// whole pipeline in at once.</para>
    /// </summary>
    /// <param name="host">The document the objects currently live on.</param>
    /// <param name="objects">The objects to move; harness proxies are ignored.</param>
    /// <returns>The harness left in their place, or null when there was nothing to move.</returns>
    public static HarnessComponent? CreateFromSelection(GH_Document host, IReadOnlyList<IGH_DocumentObject> objects)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(objects);

        var moving = objects.Where(o => o is not null and not HarnessComponent).ToList();
        if (moving.Count == 0)
        {
            return null;
        }

        // Where the group sat, so the proxy lands in the space it frees up.
        PointF anchor = Centre(moving);

        var chunk = new GH_LooseChunk("HarnessMove");
        host.Write(chunk, moving);

        var inner = new GH_Document();
        if (!inner.Read(chunk))
        {
            return null;
        }

        var harness = new HarnessComponent();
        harness.Adopt(inner);

        // One undo record for the whole move, so a single Ctrl+Z restores the originals AND takes
        // the harness away. Two records would let the user undo half of it and end up with both.
        var undo = new GH_UndoRecord("Move into Harness");
        foreach (IGH_DocumentObject obj in moving)
        {
            undo.AddAction(new GH_RemoveObjectAction(obj));
        }

        host.RemoveObjects(moving, false);
        host.AddObject(harness, false);
        harness.Attributes.Pivot = anchor;

        undo.AddAction(new GH_AddObjectAction(harness));
        host.UndoServer.PushUndoRecord(undo);

        harness.ExpireSolution(true);

        return harness;
    }

    /// <summary>
    /// Gets the harness document, creating it on first use.
    /// </summary>
    /// <returns>The inner document, owned by this component.</returns>
    public GH_Document EnsureInnerDocument()
    {
        if (_inner is null)
        {
            Adopt(new GH_Document());
        }

        return _inner!;
    }

    /// <summary>
    /// Takes the canvas into the harness document, registering it with the document server on
    /// first entry and zooming to fit its contents. This is the same sequence Grasshopper's own
    /// <c>GH_Cluster.EditClusterAsSeparateDocument</c> runs, minus the duplicate: Physalia edits
    /// the live document because conversations, signals and solve state are session-only, so a
    /// serialize/deserialize round-trip would reset the running pipeline.
    /// </summary>
    public void OpenInCanvas()
    {
        GH_Canvas? canvas = Instances.ActiveCanvas;
        if (canvas is null)
        {
            return;
        }

        GH_Document inner = EnsureInnerDocument();
        if (ReferenceEquals(canvas.Document, inner))
        {
            return; // already inside
        }

        // The document is deliberately NOT registered with the document server. The canvas setter
        // only calls PromoteDocument on it, which no-ops for an unregistered document, so pointing
        // the canvas here is enough. Staying out of the server keeps the harness off the document
        // dropdown, out of the exit-time "save changes?" sweep, and — most importantly — out of
        // reach of RemoveDocument, which disposes.
        canvas.Document = inner;
        inner.Enabled = true;

        ZoomToFit(canvas, inner);
        inner.NewSolution(expireAllObjects: false);
    }

    /// <summary>
    /// Returns the canvas to the document this harness sits on, leaving the pipeline running.
    /// </summary>
    public void ReturnToHost()
    {
        GH_Canvas? canvas = Instances.ActiveCanvas;
        GH_Document? host = OnPingDocument();
        if (canvas is null || host is null)
        {
            return;
        }

        canvas.Document = host;

        // Swapping the canvas document disables the outgoing one, which would stop the pipeline
        // from solving the moment you leave it. Re-enable so an in-flight LLM response can still
        // land while you work on your model.
        ReviveInner();

        Attributes?.ExpireLayout();
        canvas.Refresh();
    }

    /// <summary>
    /// Gets the harness holding a document, or null when the document is an ordinary file.
    /// </summary>
    /// <param name="document">The document to look up.</param>
    /// <returns>The owning harness component, or null.</returns>
    internal static HarnessComponent? OwnerOf(GH_Document? document) =>
        document is not null && Owners.TryGetValue(document, out HarnessComponent? owner) ? owner : null;

    /// <summary>
    /// Finds the single transmitter inside this harness whose drag arrow the proxy should host.
    /// </summary>
    /// <param name="arrow">The sole transmitter, when there is exactly one.</param>
    /// <returns>True when exactly one transmitter is present.</returns>
    internal bool TryGetSoleArrow(out IHarnessArrow? arrow)
    {
        arrow = null;
        if (_inner is null)
        {
            return false;
        }

        List<IHarnessArrow> found = _inner.Objects.OfType<IHarnessArrow>().Take(2).ToList();
        if (found.Count != 1)
        {
            return false; // none to host, or several with no way to choose between them
        }

        arrow = found[0];
        return true;
    }

    /// <summary>
    /// Finds the Chat driving this harness's pipeline, so the proxy can open the chat window.
    /// </summary>
    /// <returns>The first Chat inside the harness, or null when it holds none.</returns>
    internal Chat? FindChat() => _inner?.Objects.OfType<Chat>().FirstOrDefault();

    /// <inheritdoc/>
    public override bool Write(GH_IWriter writer)
    {
        if (_inner is not null)
        {
            _inner.Write(writer.CreateChunk(DocumentChunk));

            // The harness contents are saved inside the host file, so the sub-document is no longer
            // dirty in its own right. Clearing the flag stops Grasshopper prompting to save it
            // separately when it tears subsidiary documents down on exit.
            _inner.IsModified = false;
        }

        return base.Write(writer);
    }

    /// <inheritdoc/>
    public override bool Read(GH_IReader reader)
    {
        GH_IReader? chunk = reader.ChunkExists(DocumentChunk) ? reader.FindChunk(DocumentChunk) : null;
        if (chunk is not null)
        {
            var doc = new GH_Document();
            if (doc.Read(chunk))
            {
                Adopt(doc);
            }
        }

        return base.Read(reader);
    }

    /// <inheritdoc/>
    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        // No inputs — the pipeline inside is self-contained and crosses no wires.
    }

    /// <inheritdoc/>
    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        // No outputs — the pipeline acts on the host canvas by side effect, never by wire.
    }

    /// <inheritdoc/>
    protected override void SolveInstance(IGH_DataAccess DA)
    {
        // The harness itself computes nothing; it keeps the inner document alive and reports what
        // it holds. Enabled is re-asserted every solve because leaving the harness on the canvas
        // clears it, and a disabled document ignores the scheduled solutions the pipeline runs on.
        ReviveInner();

        int count = Count;
        Message = count == 0 ? "empty" : $"{count} component{(count == 1 ? string.Empty : "s")}";
    }

    // Adopts a document as this harness's contents: recorded in the owner table (so PhyDocuments
    // can climb back to the user's canvas) and enabled so it keeps solving while off-canvas.
    private void Adopt(GH_Document document)
    {
        if (_inner is not null)
        {
            Owners.Remove(_inner);
        }

        Owners.Remove(document);
        Owners.Add(document, this);

        document.Enabled = true;
        _inner = document;
    }

    // Re-asserts the inner document's enabled state unless it is the one currently on the canvas,
    // where Grasshopper owns the flag.
    private void ReviveInner()
    {
        if (_inner is null || ReferenceEquals(Instances.ActiveCanvas?.Document, _inner))
        {
            return;
        }

        _inner.Enabled = true;
    }

    // The centre of a set of objects' bounds, used to drop the proxy where the group used to be.
    private static PointF Centre(IReadOnlyList<IGH_DocumentObject> objects)
    {
        RectangleF box = RectangleF.Empty;
        foreach (IGH_DocumentObject obj in objects)
        {
            RectangleF bounds = obj.Attributes?.Bounds ?? RectangleF.Empty;
            box = box.IsEmpty ? bounds : RectangleF.Union(box, bounds);
        }

        return new PointF(box.X + (box.Width / 2f), box.Y + (box.Height / 2f));
    }

    // Frames the harness contents in the viewport, mirroring GH's own cluster-editing transition.
    private static void ZoomToFit(GH_Canvas canvas, GH_Document document)
    {
        if (!document.Objects.Any())
        {
            return;
        }

        Rectangle port = canvas.Viewport.ScreenPort;
        Rectangle contents = GH_Convert.ToRectangle(document.BoundingBox());
        contents.Inflate(ZoomMargin, ZoomMargin);
        port.Inflate(-ZoomMargin, -ZoomMargin);

        new GH_NamedView(port, contents).SetToViewport(canvas, ZoomDuration);
    }
}
