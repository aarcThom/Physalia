// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
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
public sealed class HarnessComponent : PhyBase, IGH_DocumentOwner
{
    // Archive chunk holding the inner document, written by GH_Document.Write and rehydrated by
    // GH_Document.Read. This is the only nested-archive persistence in the plug-in; every other
    // Write/Read override in Physalia writes flat keys and guid references.
    private const string DocumentChunk = "HarnessDocument";

    // Margin (canvas units) around the harness contents when the view zooms to fit on entry.
    private const int ZoomMargin = 5;

    // Duration in ms of the animated zoom-to-fit, matching GH's own cluster-editing transition.
    private const int ZoomDuration = 250;

    private GH_Document? _inner;

    // Whether the inner document has been handed to the document server. It is registered on first
    // entry and stays registered for the session: GH_DocumentServer.RemoveDocument DISPOSES the
    // document, so un-registering on the way out would destroy the live pipeline.
    private bool _registered;

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

        if (!_registered)
        {
            // Nested documents are deliberately unknown to the document server; clear the flag
            // before registering. The canvas setter clears it too, but AddDocument comes first.
            inner.Nested = false;
            Instances.DocumentServer.AddDocument(inner);
            _registered = true;
        }

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

    /// <inheritdoc/>
    /// <remarks>The document this harness node itself sits on — the user's canvas.</remarks>
    public GH_Document OwnerDocument() => OnPingDocument();

    /// <inheritdoc/>
    /// <remarks>
    /// Raised when Grasshopper is about to drop the harness document (File, "Save and Return", or
    /// closing it from the document list). The document is disposed immediately afterwards, so the
    /// live pipeline cannot be kept; the contents are captured here and rehydrated on the next
    /// entry. Session state — the conversation, latched signals, solve state — does not survive
    /// this path, which is why the canvas return widget exists.
    /// </remarks>
    public void DocumentClosed(GH_Document document)
    {
        if (!ReferenceEquals(document, _inner))
        {
            return;
        }

        var chunk = new GH_LooseChunk(DocumentChunk);
        document.Write(chunk);

        var revived = new GH_Document();
        Adopt(revived.Read(chunk) ? revived : new GH_Document());
        _registered = false;
    }

    /// <inheritdoc/>
    /// <remarks>Edits inside the harness dirty the host file, since that is where they are saved.</remarks>
    public void DocumentModified(GH_Document document)
    {
        if (ReferenceEquals(document, _inner))
        {
            OnPingDocument()?.Modified();
        }
    }

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

    // Adopts a document as this harness's contents: owned by us (so PhyDocuments can climb back to
    // the user's canvas and GH treats it as subsidiary), nested (unknown to the canvas and document
    // server until we deliberately register it), and enabled so it keeps solving off-canvas.
    private void Adopt(GH_Document document)
    {
        document.Owner = this;
        document.Nested = true;
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
