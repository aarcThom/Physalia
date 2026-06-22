// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Physalia.Core.Signals;
using Physalia.GH.Attributes;
using Physalia.GH.Parameters;

namespace Physalia.GH.Components;

/// <summary>
/// Routes signals wirelessly back to one or more paired <see cref="FeedbackCollector"/>
/// components without participating in GH's normal DAG execution model.
/// Drag from the bottom grip to a FeedbackCollector to connect; Ctrl+drag to disconnect.
///
/// <para>Each incoming signal is consumed exactly once and forwarded as-is (original
/// sequence preserved), so an upstream Fail Signal can never be re-injected by later
/// solves the way the old level-triggered Data+Trigger pair could.</para>
/// </summary>
public class Feedback : StatefulComponentBase
{
    private const int InSignal = 0;

    private readonly List<Guid> _collectorGuids = new();
    private bool _doLatch;

    /// <summary>
    /// Initializes a new instance of the <see cref="Feedback"/> class.
    /// </summary>
    public Feedback()
        : base("Feedback", "FB", "Routes signals wirelessly to paired Feedback Collectors.", "Regulators")
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
        int idx = pManager.AddParameter(
            new Param_Signal(),
            "Signal",
            "S",
            "Signals to route wirelessly to paired Feedback Collectors. Each signal is forwarded exactly once; the payload carries the feedback text.",
            GH_ParamAccess.list);
        pManager[idx].Optional = true;
    }

    /// <inheritdoc/>
    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        // No physical outputs — signals are routed wirelessly to paired FeedbackCollectors.
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
        ObserveSignalInputs(DA, InSignal);

        if (_doLatch)
        {
            // Caption rhythm only — forwarding already happened on the consume solve.
            _doLatch = false;
            LatchSuccess(string.Empty, emitSignal: false);

            if (HasUnconsumedSignals(InSignal))
            {
                ScheduleStateSolve(1, () => { });
            }

            return;
        }

        if (State == SolveState.Active)
        {
            return;
        }

        IReadOnlyList<ConsumedSignal> consumed = ConsumeAllSignals(InSignal);
        if (consumed.Count == 0)
        {
            return;
        }

        EnterActive();

        if (_collectorGuids.Count == 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No Feedback Collectors linked. Drag from the bottom grip to connect.");
        }
        else
        {
            var doc = OnPingDocument();
            foreach (ConsumedSignal item in consumed)
            {
                // Forward as-is: the original sequence is what lets downstream consumers
                // order this event correctly against its cause.
                foreach (Guid guid in _collectorGuids)
                {
                    if (doc?.FindObject(guid, false) is FeedbackCollector collector)
                    {
                        collector.Inject(item.Signal);
                    }
                }
            }
        }

        ScheduleStateSolve(SolveDelayMs, () => _doLatch = true);
    }

    /// <inheritdoc/>
    protected override void OnCleared()
    {
        _doLatch = false;
    }
}
