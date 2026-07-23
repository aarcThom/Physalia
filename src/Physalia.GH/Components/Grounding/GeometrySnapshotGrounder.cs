// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using Grasshopper.Kernel;
using Physalia.Core.Grounding;
using Physalia.GH.Goo;
using Physalia.GH.Parameters;

namespace Physalia.GH.Components;

/// <summary>
/// Emits a <see cref="GeometrySnapshotGrounding"/> that arms the chat window's geometry button:
/// while it is wired into the Conversation Log's Grounding input and a transmitter has generated
/// geometry (the script a Py Transmitter targets, or components a Component Transmitter placed),
/// the prompt box shows a geometry button. Pressing it sends a Rhino viewport snapshot of that
/// geometry — the same capture the Geometry Observation guardrail performs — as its own user
/// message; a snapshot is never attached to a typed prompt automatically. The accompanying message
/// has a built-in default and is editable from the chat window's grounding panel. Has no inputs.
/// </summary>
public class GeometrySnapshotGrounder : PhyBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GeometrySnapshotGrounder"/> class.
    /// </summary>
    public GeometrySnapshotGrounder()
        : base("Geometry Snapshot Grounding", "GeoGnd", "Adds a geometry button to the chat window that sends a viewport snapshot of the transmitter-generated geometry as its own message.", "Grounding")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("D5B8F2A6-7E31-4C94-A0D8-3F6E1B9C5A27");

    /// <inheritdoc/>
    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        // No inputs: the snapshot is captured live when the chat window's geometry button is
        // pressed; the message is edited in the chat window's grounding panel.
    }

    /// <inheritdoc/>
    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddParameter(new Param_Grounding(), "Grounding", "Gnd", "Geometry-snapshot grounding for the Conversation Log.", GH_ParamAccess.item);
    }

    /// <inheritdoc/>
    protected override void SolveInstance(IGH_DataAccess DA)
    {
        DA.SetData(0, new GH_Grounding(new GeometrySnapshotGrounding(GeometrySnapshotGrounding.DefaultMessage)));
    }
}
