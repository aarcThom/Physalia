// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Windows.Forms;
using Grasshopper.Kernel;
using Physalia.Core.Grounding;
using Physalia.Core.Grounding.Clusters;
using Physalia.GH.Generation;
using Physalia.GH.Goo;
using Physalia.GH.Parameters;

namespace Physalia.GH.Components;

/// <summary>
/// Reads the user's <c>Files/CLUSTERS</c> folder and emits a <see cref="ClusterCatalogGrounding"/>
/// describing every available cluster — its name, its introspected input/output signature, and the
/// optional description from <c>clusters.json</c>. Wire its output into the Conversation Log's Grounding
/// input; the chat window then lets the user pick which clusters the model may use. Has no inputs;
/// right-click to refresh after adding or editing cluster files.
/// </summary>
public class ClusterGrounder : PhyBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ClusterGrounder"/> class.
    /// </summary>
    public ClusterGrounder()
        : base("Cluster Grounding", "ClGnd", "Tells the model which saved Grasshopper clusters it may use, read from Files/CLUSTERS. Right-click to read the folder again. Unfinished — a scaffold.", "Grounding")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("A7E2D9C4-1F36-4B8A-B0E5-2C9D4F8A3B61");

    /// <inheritdoc/>
    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        // No inputs: the catalog is read from the Files/CLUSTERS folder.
    }

    /// <inheritdoc/>
    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddParameter(new Param_Grounding(), "Grounding", "Gnd", "The clusters on offer, with what each one takes and gives back. Wire into a Conversation Log's Grounding input.", GH_ParamAccess.item);
    }

    /// <inheritdoc/>
    public override void AppendAdditionalMenuItems(ToolStripDropDown menu)
    {
        base.AppendAdditionalMenuItems(menu);
        Menu_AppendSeparator(menu);
        Menu_AppendItem(menu, "Refresh clusters", OnRefresh);
    }

    /// <inheritdoc/>
    protected override void SolveInstance(IGH_DataAccess DA)
    {
        ClusterCatalog catalog = ClusterCatalogProvider.GetCatalog();
        DA.SetData(0, new GH_Grounding(new ClusterCatalogGrounding(catalog)));
    }

    private void OnRefresh(object? sender, EventArgs e)
    {
        ClusterCatalogProvider.GetCatalog(forceRefresh: true);
        ExpireSolution(true);
    }
}
