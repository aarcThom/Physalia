// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Windows.Forms;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Physalia.Core.Common;

namespace Physalia.GH.Components;

/// <summary>
/// Routes data wirelessly back to a paired <see cref="FeedbackCollector"/> without
/// participating in GH's normal DAG execution model.
/// Breaks the acyclic constraint deliberately — the radio waves icon calls this out visually.
/// </summary>
public class Feedback : PhyBase
{
    private Guid _collectorGuid = Guid.Empty;
    private bool _lastTrigger;

    /// <summary>
    /// Initializes a new instance of the <see cref="Feedback"/> class.
    /// </summary>
    public Feedback()
        : base("Feedback", "FB", "Routes data wirelessly to a paired Feedback Collector.", "Regulators")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("4636FC1D-16B4-48B1-9A16-81AF2F8AE483");

    /// <inheritdoc/>
    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddTextParameter("data", "D", "Feedback string to route wirelessly to the paired Feedback Collector.", GH_ParamAccess.item, string.Empty);
        pManager.AddBooleanParameter("trigger", "T", "Rising edge routes data to the paired Feedback Collector.", GH_ParamAccess.item, false);
    }

    /// <inheritdoc/>
    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        // No physical outputs — data is routed wirelessly to the paired FeedbackCollector.
    }

    /// <inheritdoc/>
    public override void AppendAdditionalMenuItems(ToolStripDropDown menu)
    {
        base.AppendAdditionalMenuItems(menu);
        Menu_AppendSeparator(menu);
        Menu_AppendItem(menu, "Link Feedback Collector", OnLinkFeedbackCollector);
    }

    /// <inheritdoc/>
    public override bool Write(GH_IWriter writer)
    {
        writer.SetGuid("CollectorGuid", _collectorGuid);
        return base.Write(writer);
    }

    /// <inheritdoc/>
    public override bool Read(GH_IReader reader)
    {
        if (reader.ItemExists("CollectorGuid"))
        {
            _collectorGuid = reader.GetGuid("CollectorGuid");
        }

        return base.Read(reader);
    }

    /// <inheritdoc/>
    protected override void SolveInstance(IGH_DataAccess DA)
    {
        string data = string.Empty;
        bool trigger = false;

        DA.GetData(0, ref data);
        DA.GetData(1, ref trigger);

        if (trigger && !_lastTrigger && StringHelpers.IsNonBlank(data))
        {
            var collector = FindCollector();

            if (collector != null)
            {
                collector.Inject(data, true);
            }
            else if (_collectorGuid != Guid.Empty)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Paired Feedback Collector not found. Use right-click → Link Feedback Collector.");
            }
            else
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No Feedback Collector linked. Use right-click → Link Feedback Collector.");
            }
        }

        _lastTrigger = trigger;
    }

    private FeedbackCollector? FindCollector()
    {
        if (_collectorGuid == Guid.Empty)
        {
            return null;
        }

        var doc = OnPingDocument();
        if (doc == null)
        {
            return null;
        }

        return doc.FindObject(_collectorGuid, false) as FeedbackCollector;
    }

    private void OnLinkFeedbackCollector(object sender, EventArgs e)
    {
        var doc = OnPingDocument();
        if (doc == null)
        {
            return;
        }

        FeedbackCollector? found = null;
        foreach (IGH_DocumentObject obj in doc.Objects)
        {
            if (obj is FeedbackCollector fc)
            {
                found = fc;
                break;
            }
        }

        if (found == null)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No Feedback Collector found in the document. Add one first.");
            ExpireSolution(true);
            return;
        }

        _collectorGuid = found.InstanceGuid;
        ExpireSolution(true);
    }
}
