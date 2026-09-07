// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using Grasshopper.Kernel;
using Physalia.GH.Parameters;

namespace Physalia.GH.Components;

/// <summary>
/// Where a delegated task ARRIVES inside a harness. Put one in a harness to make that harness
/// callable: a Delegate tool in another pipeline hands it a task, this mints a signal carrying it,
/// and the round runs as though a person had typed it.
///
/// <para><b>Active, where Harness In is passive, and the difference is the whole point.</b> Data
/// arriving at a Harness In mints nothing — it waits to be read, which is what stops the canvas
/// feeding a harness from closing into a loop. A task arriving here is an EVENT: somebody asked for
/// work to be done, and nothing else is going to start it. So this fires.</para>
///
/// <para><b>It is not a trigger, and has no Armed switch.</b> A trigger fires on its own and must
/// therefore be switched on deliberately; this fires only when another pipeline calls it, so the
/// caller's own budget and its own triggers are already the bound on how often that happens. An
/// arming switch here would only be a way to make a delegate mysteriously time out.</para>
///
/// <para>Standing on the user's canvas rather than inside a harness, nothing can call it — a
/// delegate links to a harness, and there is none. It says so rather than sitting quietly.</para>
/// </summary>
public class TaskIn : StatefulComponentBase
{
    private const int OutSignal = 0;
    private const int OutTask = 1;

    private string _task = string.Empty;

    private bool _doFire;

    private int _received;

    /// <summary>
    /// Initializes a new instance of the <see cref="TaskIn"/> class.
    /// </summary>
    public TaskIn()
        : base(
            "Task In",
            "TaskIn",
            "Where a task handed over by another pipeline's Delegate tool arrives. Putting one in a harness is what makes that harness callable. Wire its Signal into a Conversation Log's Prompt Signal input.",
            "I/O")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("B93C5027-4E18-4A76-BD52-0F61A8347CE9");

    /// <summary>
    /// Hands a task to this node. Called by <see cref="DelegationBroker"/> from the delegate's own
    /// thread, so it schedules rather than acting.
    /// </summary>
    /// <param name="task">The task text, which becomes the minted signal's payload.</param>
    internal void Deliver(string task)
    {
        _task = task ?? string.Empty;

        // Ready() re-enables a harness sub-document whose proxy has not solved recently — without it
        // the scheduled solution below is silently dropped and the delegate times out against a
        // pipeline that never woke. See PipelineWake.
        if (PipelineWake.Ready(this) is null)
        {
            return;
        }

        // Safe from a background thread: ScheduleStateSolve marshals onto a scheduled solution.
        ScheduleStateSolve(1, () => _doFire = true);
    }

    /// <inheritdoc/>
    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        // No inputs. The task comes from a Delegate tool in another pipeline, not from a wire — the
        // same reason Harness In has none.
    }

    /// <inheritdoc/>
    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddParameter(
            new Param_Signal(),
            "Signal",
            "S",
            "Fires when a task arrives, carrying the task text. Wire into a Conversation Log's Prompt Signal input — or anywhere else a signal starts work.",
            GH_ParamAccess.item);
        pManager.AddTextParameter(
            "Task",
            "T",
            "The task text on its own, so the definition can read it without deconstructing the signal.",
            GH_ParamAccess.item);
    }

    /// <inheritdoc/>
    protected override void SolveInstance(IGH_DataAccess DA)
    {
        if (Harness.PhyDocuments.Harness(this) is null)
        {
            AddRuntimeMessage(
                GH_RuntimeMessageLevel.Warning,
                "This node is on the canvas rather than inside a harness, so nothing can hand it a task. A Delegate tool links to a harness.");
        }

        if (_doFire)
        {
            // FIRE PASS — runs only from our own scheduled callback. A source, so there is no visible
            // Active delay: the latch happens on the solve the delivery scheduled.
            _doFire = false;
            _received++;
            LatchSuccess(_task);
        }

        EmitSignal(DA, OutSignal, SuccessSignal);
        DA.SetData(OutTask, _task);
    }

    /// <inheritdoc/>
    protected override string MessageForState(SolveState state) =>
        _received == 0 ? "waiting" : $"{_received} received";

    /// <inheritdoc/>
    protected override void OnCleared()
    {
        _task = string.Empty;
        _doFire = false;
        _received = 0;
    }
}
