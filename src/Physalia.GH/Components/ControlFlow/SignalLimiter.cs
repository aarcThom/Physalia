// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using Grasshopper.Kernel;
using Physalia.Core.Signals;
using Physalia.GH.Parameters;

namespace Physalia.GH.Components;

/// <summary>
/// Counts how many new signals have arrived on its Signal input and routes each one by that
/// running count. While the count of distinct signals seen is at or below the Count limit, the
/// arriving signal passes through the first (Within Limit) output; once the count exceeds the
/// limit, further signals pass through the second (Over Limit) output. Counting uses the
/// consume-once intake, so a recompute or replayed solve never re-counts an already-seen signal.
/// The Reset Boolean (false→true) zeroes the count and clears both outputs, so routing starts over.
/// </summary>
public class SignalLimiter : StatefulComponentBase
{
    private const int InSignal = 0;
    private const int InCount = 1;
    private const int InReset = 2;

    private const int OutWithin = 0;
    private const int OutOver = 1;

    private int _count;
    private PhySignal? _withinSignal;
    private PhySignal? _overSignal;

    /// <summary>
    /// Initializes a new instance of the <see cref="SignalLimiter"/> class.
    /// </summary>
    public SignalLimiter()
        : base("Signal Limiter", "SigLim", "Counts signals and splits them at a limit: the first few leave one way, everything after leaves the other. This is how you cap the number of times a repair loop may go round.", "Control Flow")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("98AF02A3-89C2-4DB7-B12B-676A9CC0B9B8");

    /// <inheritdoc/>
    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddParameter(new Param_Signal(), "Signal", "S", "The signals to count. Each one counts once, however many times the canvas happens to re-solve.", GH_ParamAccess.list);
        pManager.AddIntegerParameter("Count", "N", "How many signals are allowed through the Within Limit output. Everything after that number leaves by Over Limit instead.", GH_ParamAccess.item, 1);
        pManager.AddBooleanParameter("Reset", "R", "A press puts the count back to zero and clears both outputs, so counting starts again.", GH_ParamAccess.item, false);
        pManager[InSignal].Optional = true;
    }

    /// <inheritdoc/>
    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddParameter(new Param_Signal(), "Within Limit", "W", "Each signal while the count is still inside the limit. This is the branch that keeps the loop turning.", GH_ParamAccess.item);
        pManager.AddParameter(new Param_Signal(), "Over Limit", "O", "Each signal once the count has passed the limit. Wire it to whatever should happen when the loop has gone on long enough — a note to the model, or nothing at all.", GH_ParamAccess.item);
    }

    /// <inheritdoc/>
    protected override void SolveInstance(IGH_DataAccess DA)
    {
        // Observe every solve (even mid-flight) so the consume-once baseline holds and latched
        // outputs are re-emitted for downstream consume-once.
        ObserveSignalInputs(DA, InSignal);

        int limit = 0;
        DA.GetData(InCount, ref limit);

        // A reset press zeroes the count and clears both outputs before this solve's signals are
        // counted, so a signal arriving with the reset is counted as the first of the new run.
        if (ObserveButtonPress(DA, InReset))
        {
            _count = 0;
            _withinSignal = null;
            _overSignal = null;
        }

        // Consume in global sequence order; each new signal increments the count and routes by it.
        foreach (ConsumedSignal item in ConsumeAllSignals(InSignal))
        {
            _count++;
            if (_count <= limit)
            {
                _withinSignal = item.Signal;
            }
            else
            {
                _overSignal = item.Signal;
            }
        }

        Message = $"{_count} / {limit}";
        OnDisplayExpired(true);

        EmitSignal(DA, OutWithin, _withinSignal);
        EmitSignal(DA, OutOver, _overSignal);
    }

    /// <inheritdoc/>
    protected override void OnCleared()
    {
        _count = 0;
        _withinSignal = null;
        _overSignal = null;
    }
}
