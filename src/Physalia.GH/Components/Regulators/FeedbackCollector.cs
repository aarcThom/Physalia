// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using Grasshopper.Kernel;
using Physalia.GH.Attributes;

namespace Physalia.GH.Components;

/// <summary>
/// Collects one or more wireless Feedback signals and re-emits them into the pipeline.
/// Acts as the re-entry point for the feedback loop, deliberately breaking GH's acyclic constraint.
/// </summary>
public class FeedbackCollector : PhyBase
{
    private string _lastData = string.Empty;
    private bool _pendingTrigger;

    /// <summary>
    /// Initializes a new instance of the <see cref="FeedbackCollector"/> class.
    /// </summary>
    public FeedbackCollector()
        : base("Feedback Collector", "FC", "Collects wireless Feedback signals and re-emits them into the pipeline.", "Regulators")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("97C158C6-EDE4-4521-A666-EB76D2E85FB6");

    /// <inheritdoc/>
    public override void CreateAttributes()
    {
        m_attributes = new FeedbackCollectorAttrib(this);
    }

    /// <inheritdoc/>
    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        // No physical inputs — data arrives wirelessly via Inject().
    }

    /// <inheritdoc/>
    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddTextParameter("Data", "D", "Last feedback value received from paired Feedback components.", GH_ParamAccess.item);
        pManager.AddBooleanParameter("Trigger", "T", "True momentarily when any paired Feedback component fires.", GH_ParamAccess.item);
    }

    /// <summary>
    /// Receives a wireless feedback injection from a paired <see cref="Feedback"/> component.
    /// Sets the pending trigger and schedules a re-solve so data propagates downstream.
    /// </summary>
    /// <param name="data">The feedback string to forward.</param>
    /// <param name="trigger">Whether this injection should fire the trigger output.</param>
    public void Inject(string data, bool trigger)
    {
        _lastData = data;

        if (trigger)
        {
            _pendingTrigger = true;
        }

        OnPingDocument()?.ScheduleSolution(1, _ => ExpireSolution(true));
    }

    /// <inheritdoc/>
    protected override void SolveInstance(IGH_DataAccess DA)
    {
        bool triggerOut = _pendingTrigger;
        _pendingTrigger = false;

        DA.SetData(0, _lastData);
        DA.SetData(1, triggerOut);

        // If we just fired a pulse, schedule an immediate re-solve so the output reverts to false.
        if (triggerOut)
        {
            OnPingDocument()?.ScheduleSolution(1, _ => ExpireSolution(true));
        }
    }
}
