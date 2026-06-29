// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using Grasshopper.Kernel;
using Physalia.Core.Grounding;
using Physalia.GH.Goo;
using Physalia.GH.Parameters;

namespace Physalia.GH.Components;

/// <summary>
/// Scaffold producer: emits a <see cref="ClusterGrounding"/> describing a Grasshopper cluster
/// (.ghx) so the Composer can ground the model with it. Wire its output into the Composer's
/// Grounding input.
/// </summary>
/// <remarks>
/// WIP: today the cluster is described by a hand-written name and description. TODO: accept a
/// .ghx path and extract the cluster's input/output parameter specs to build the description.
/// </remarks>
public class ClusterGrounder : PhyBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ClusterGrounder"/> class.
    /// </summary>
    public ClusterGrounder()
        : base("Cluster Grounding", "ClGnd", "Grounds the model with an available Grasshopper cluster (.ghx). WIP scaffold.", "Resources")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("A7E2D9C4-1F36-4B8A-B0E5-2C9D4F8A3B61");

    /// <inheritdoc/>
    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddTextParameter("Name", "N", "The cluster's display name.", GH_ParamAccess.item, string.Empty);
        int descIndex = pManager.AddTextParameter("Description", "D", "What the cluster does and its inputs/outputs.", GH_ParamAccess.item, string.Empty);
        pManager[descIndex].Optional = true;
    }

    /// <inheritdoc/>
    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddParameter(new Param_Grounding(), "Grounding", "Gnd", "Cluster grounding for the Composer.", GH_ParamAccess.item);
    }

    /// <inheritdoc/>
    protected override void SolveInstance(IGH_DataAccess DA)
    {
        string name = string.Empty;
        string description = string.Empty;
        DA.GetData(0, ref name);
        DA.GetData(1, ref description);

        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        DA.SetData(0, new GH_Grounding(new ClusterGrounding(name, description)));
    }
}
