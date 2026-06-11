// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Physalia.Core.Common;
using Physalia.GH.Attributes;

namespace Physalia.GH.Components;

/// <summary>
/// Routes data wirelessly back to one or more paired <see cref="FeedbackCollector"/> components
/// without participating in GH's normal DAG execution model.
/// Drag from the bottom grip to a FeedbackCollector to connect; Ctrl+drag to disconnect.
/// </summary>
public class Feedback : PhyBase
{
    private readonly List<Guid> _collectorGuids = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="Feedback"/> class.
    /// </summary>
    public Feedback()
        : base("Feedback", "FB", "Routes data wirelessly to paired Feedback Collectors.", "Regulators")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("4636FC1D-16B4-48B1-9A16-81AF2F8AE483");

    /// <summary>
    /// Gets the GUIDs of all FeedbackCollectors currently linked to this component.
    /// </summary>
    public IReadOnlyList<Guid> CollectorGuids => _collectorGuids;

    /// <inheritdoc/>
    public override void CreateAttributes()
    {
        m_attributes = new FeedbackAttrib(this);
    }

    /// <inheritdoc/>
    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddTextParameter("Data", "D", "Feedback string to route wirelessly to paired Feedback Collectors.", GH_ParamAccess.item, string.Empty);
        pManager.AddBooleanParameter("Trigger", "T", "While true, routes data to all paired Feedback Collectors.", GH_ParamAccess.item, false);
    }

    /// <inheritdoc/>
    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        // No physical outputs — data is routed wirelessly to paired FeedbackCollectors.
    }

    /// <summary>
    /// Adds a FeedbackCollector GUID to the linked set. No-op if already present.
    /// Called by <see cref="FeedbackAttrib"/> when the user drags to a collector.
    /// </summary>
    /// <param name="guid">The InstanceGuid of the FeedbackCollector to link.</param>
    public void AddCollector(Guid guid)
    {
        if (!_collectorGuids.Contains(guid))
        {
            _collectorGuids.Add(guid);
        }
    }

    /// <summary>
    /// Removes a FeedbackCollector GUID from the linked set.
    /// Called by <see cref="FeedbackAttrib"/> when the user Ctrl+drags to a collector.
    /// </summary>
    /// <param name="guid">The InstanceGuid of the FeedbackCollector to unlink.</param>
    public void RemoveCollector(Guid guid)
    {
        _collectorGuids.Remove(guid);
    }

    /// <inheritdoc/>
    public override bool Write(GH_IWriter writer)
    {
        writer.SetInt32("CollectorCount", _collectorGuids.Count);
        for (int i = 0; i < _collectorGuids.Count; i++)
        {
            writer.SetGuid("CollectorGuid_" + i, _collectorGuids[i]);
        }

        return base.Write(writer);
    }

    /// <inheritdoc/>
    public override bool Read(GH_IReader reader)
    {
        _collectorGuids.Clear();

        if (reader.ItemExists("CollectorCount"))
        {
            int count = reader.GetInt32("CollectorCount");
            for (int i = 0; i < count; i++)
            {
                _collectorGuids.Add(reader.GetGuid("CollectorGuid_" + i));
            }
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

        if (!trigger || !StringHelpers.IsNonBlank(data))
        {
            return;
        }

        if (_collectorGuids.Count == 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No Feedback Collectors linked. Drag from the bottom grip to connect.");
            return;
        }

        var doc = OnPingDocument();
        foreach (var guid in _collectorGuids)
        {
            if (doc?.FindObject(guid, false) is FeedbackCollector collector)
            {
                collector.Inject(data);
            }
        }
    }
}
