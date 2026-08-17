// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper.Kernel;
using Physalia.Core.ConvoInstruct;
using Physalia.Core.Signals;
using Physalia.GH.Attributes;
using Physalia.GH.Parameters;

namespace Physalia.GH.Components;

/// <summary>
/// Collects one or more wireless Feedback signals and re-emits them into the pipeline.
/// Acts as the re-entry point for the feedback loop, deliberately breaking GH's acyclic
/// constraint.
///
/// <para>Injected signals queue losslessly (a batch arriving in one solution aggregates;
/// injections during an active run wait their turn). The collector mints one fresh
/// outgoing signal per batch — its payload is the newline-joined feedback text and its
/// sequence is necessarily greater than every cause, preserving global causal order for
/// downstream consumers like Conversation Log.</para>
/// </summary>
public class FeedbackCollector : StatefulComponentBase
{
    private readonly object _lock = new();
    private readonly List<PhySignal> _injected = new();

    private List<PhySignal> _batch = new();
    private bool _doLatch;

    /// <summary>
    /// Initializes a new instance of the <see cref="FeedbackCollector"/> class.
    /// </summary>
    public FeedbackCollector()
        : base("Feedback Collector", "FC", "Collects wireless Feedback signals and re-emits them into the pipeline.", "Control Flow")
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
        // No physical inputs — signals arrive wirelessly via Inject().
    }

    /// <inheritdoc/>
    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddParameter(new Param_Signal(), "Signal", "S", "Latched signal minted per batch; its payload is all feedback from the batch, newline-joined. Downstream components consume it exactly once. Casts to text (the payload).", GH_ParamAccess.item);
    }

    /// <summary>
    /// Receives a wireless signal injection from a paired <see cref="Feedback"/> component.
    /// Injections queue losslessly: simultaneous injections aggregate into one batch, and
    /// signals arriving during an active run wait for the next one.
    /// </summary>
    /// <param name="signal">The signal to forward.</param>
    public void Inject(PhySignal signal)
    {
        lock (_lock)
        {
            _injected.Add(signal);
        }

        // Trace tap: the wireless hop bypasses EmitSignal/MarkConsumed, so record it here as a
        // "(wireless)" consumption by this collector.
        Diagnostics.SignalTraceLog.RecordWirelessInjection(signal, InstanceGuid, Name);

        ScheduleStateSolve(1, () => { });
    }

    /// <inheritdoc/>
    protected override void SolveInstance(IGH_DataAccess DA)
    {
        if (_doLatch)
        {
            // LATCH PASS — after the visible delay. Mint one outgoing signal for the batch;
            // its fresh sequence is greater than every injected cause, preserving order.
            _doLatch = false;

            SignalOutcome outcome = _batch.Any(s => s.Outcome == SignalOutcome.Failure)
                ? SignalOutcome.Failure
                : SignalOutcome.Success;

            // Preserve structured content across the batch: tool results ride ContentBlocks as
            // ToolResultContent, and each block's tool_use_id must survive aggregation — joining
            // payload strings alone would lose it. Equally, a text-only injection batched with a
            // block-carrying one must contribute its text AS a block, or the Conversation Log —
            // which reads non-empty blocks as the whole turn — drops it. See SignalAggregation.
            AggregatedContent combined = SignalAggregation.Combine(_batch, Environment.NewLine);
            IReadOnlyList<MessageContent> blocks = combined.ContentBlocks;

            LatchSuccess(combined.Payload, emitSignal: true, outcome: outcome, contentBlocks: blocks.Count > 0 ? blocks : null);
            _batch = new List<PhySignal>();

            bool more;
            lock (_lock)
            {
                more = _injected.Count > 0;
            }

            if (more)
            {
                // Injections arrived mid-run; nothing was dropped. One follow-up solve
                // starts the next batch.
                ScheduleStateSolve(1, () => { });
            }

            Emit(DA);
            return;
        }

        if (State != SolveState.Active)
        {
            List<PhySignal>? batch = null;
            lock (_lock)
            {
                if (_injected.Count > 0)
                {
                    batch = new List<PhySignal>(_injected);
                    _injected.Clear();
                }
            }

            if (batch is not null)
            {
                _batch = batch;
                EnterActive();
                ScheduleStateSolve(SolveDelayMs, () => _doLatch = true);
                Emit(DA);
                return;
            }
        }

        // Idle solve — re-emit the latched outputs.
        Emit(DA);
    }

    /// <inheritdoc/>
    protected override void OnCleared()
    {
        lock (_lock)
        {
            _injected.Clear();
        }

        _batch = new List<PhySignal>();
        _doLatch = false;
    }

    private void Emit(IGH_DataAccess da)
    {
        EmitSignal(da, 0, SuccessSignal);
    }
}
