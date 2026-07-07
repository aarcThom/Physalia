// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Physalia.GH.Attributes;
using Rhino.Geometry;

namespace Physalia.GH.Components;

/// <summary>
/// Test component. Links to any document component via the bottom-centre bezier grip and, when
/// its single Boolean input transitions to true, zooms the Perspective viewport onto the geometry
/// output of the linked component (its preview clipping box). Does nothing when the linked
/// component produces no previewable geometry. Drag the grip to link; Ctrl+drag to unlink.
/// </summary>
public class ZoomGuid : PhyBase
{
    private Guid _linkedGuid = Guid.Empty;
    private bool _observedLevel;
    private bool _previousLevel;

    /// <summary>
    /// Initializes a new instance of the <see cref="ZoomGuid"/> class.
    /// </summary>
    public ZoomGuid()
        : base(
            "Zoom Guid",
            "ZoomG",
            "Test: zooms the Perspective viewport onto the linked component's geometry output when the input is set true. Drag the bottom grip to any component.",
            "Extra")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("6F00D2EA-1984-42CA-8E49-1B4D265F5376");

    /// <summary>
    /// Gets the InstanceGuid of the linked component, or <see cref="Guid.Empty"/> if unlinked.
    /// </summary>
    public Guid LinkedGuid => _linkedGuid;

    /// <inheritdoc/>
    public override void CreateAttributes()
    {
        m_attributes = new ZoomGuidAttrib(this);
    }

    /// <summary>
    /// Links this component to the target whose geometry the Perspective viewport will zoom to.
    /// Called by <see cref="ZoomGuidAttrib"/> when the user drops the wire.
    /// </summary>
    /// <param name="guid">The InstanceGuid of the component to link.</param>
    public void LinkTo(Guid guid)
    {
        _linkedGuid = guid;
    }

    /// <summary>
    /// Removes the current link. Called by <see cref="ZoomGuidAttrib"/> on Ctrl+drop.
    /// </summary>
    public void Unlink()
    {
        _linkedGuid = Guid.Empty;
    }

    /// <inheritdoc/>
    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddBooleanParameter(
            "Zoom",
            "Z",
            "Set true (e.g. with a Button) to zoom the Perspective viewport onto the linked component's geometry output.",
            GH_ParamAccess.item,
            false);
    }

    /// <inheritdoc/>
    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        // No outputs — this is a viewport-side-effect test component.
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Acts only on a false→true transition (and never on the first, baseline solve, so a
    /// document that loads with the input already true does not auto-zoom). The zoom itself is
    /// deferred to <c>RhinoApp.Idle</c> because it mutates the viewport and cannot run inside the
    /// solution.
    /// </remarks>
    protected override void SolveInstance(IGH_DataAccess DA)
    {
        bool level = false;
        DA.GetData(0, ref level);

        bool firstObservation = !_observedLevel;
        _observedLevel = true;
        bool wasTrue = _previousLevel;
        _previousLevel = level;

        bool risingEdge = !firstObservation && level && !wasTrue;
        if (!risingEdge)
        {
            return;
        }

        if (_linkedGuid == Guid.Empty)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No component linked. Drag the bottom grip to a component.");
            return;
        }

        Rhino.RhinoApp.Idle += OnIdleZoom;
    }

    /// <inheritdoc/>
    public override void RemovedFromDocument(GH_Document document)
    {
        Rhino.RhinoApp.Idle -= OnIdleZoom;
        base.RemovedFromDocument(document);
    }

    /// <inheritdoc/>
    public override bool Write(GH_IWriter writer)
    {
        writer.SetGuid("LinkedGuid", _linkedGuid);
        return base.Write(writer);
    }

    /// <inheritdoc/>
    public override bool Read(GH_IReader reader)
    {
        if (reader.ItemExists("LinkedGuid"))
            _linkedGuid = reader.GetGuid("LinkedGuid");
        return base.Read(reader);
    }

    /// <summary>
    /// One-shot idle handler: zooms the Perspective viewport onto the linked component's preview
    /// clipping box, then redraws. No-ops when the link, the geometry, or the viewport is missing.
    /// </summary>
    /// <param name="sender">The event sender.</param>
    /// <param name="e">The event arguments.</param>
    private void OnIdleZoom(object? sender, EventArgs e)
    {
        Rhino.RhinoApp.Idle -= OnIdleZoom;

        try
        {
            if (OnPingDocument()?.FindObject(_linkedGuid, false) is not IGH_PreviewObject preview)
            {
                return;
            }

            BoundingBox box = preview.ClippingBox;
            if (!box.IsValid)
            {
                return;
            }

            Rhino.Display.RhinoView? view = Rhino.RhinoDoc.ActiveDoc?.Views?.Find("Perspective", false);
            if (view is null)
            {
                return;
            }

            box.Inflate(box.Diagonal.Length * 0.05);
            view.ActiveViewport.ZoomBoundingBox(box);
            view.Redraw();
        }
        catch (Exception ex)
        {
            Rhino.RhinoApp.WriteLine($"[Physalia] Zoom Guid failed: {ex.Message}");
        }
    }
}
