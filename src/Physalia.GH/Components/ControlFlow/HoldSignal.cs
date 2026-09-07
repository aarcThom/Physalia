// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using System.Globalization;
using Grasshopper.Kernel;
using Physalia.Core.Signals;

namespace Physalia.GH.Components;

/// <summary>
/// Keeps a signal waiting until a condition becomes true, then lets it go. The "not yet" that Signal
/// Gate deliberately does not do: a gate decides now and turns a signal away, this one waits.
///
/// <para><b>What it is for.</b> Sequencing work that a wire cannot order — hold the round until the
/// survey has arrived, until the canvas is free of errors, until a long import has finished. It is
/// also the missing half of the trigger tier: a trigger says something happened, and this says wait
/// until something is true, which between them is most of what "and then" means.</para>
///
/// <para><b>Read this before wiring the condition.</b> Grasshopper only recomputes what it has
/// expired, and asking for another solve here expires THIS node, not the graph feeding Release. So a
/// condition computed from data that never changes on its own — whether a file exists, say — would be
/// re-read forever and answer the same thing forever. Recheck therefore expires the components wired
/// straight into Release as well, which is what makes that case work at all; it is also why the
/// interval matters, since anything expensive in there is recomputed at that rate. A condition
/// downstream of a trigger or a live grounder needs none of this and can leave Recheck at zero.</para>
///
/// <para>A timeout is there so a wait cannot become a leak. Zero means wait indefinitely, which is
/// the right default for a hold inside a pipeline someone is watching and the wrong one for anything
/// unattended.</para>
/// </summary>
public class HoldSignal : SignalRelayBase
{
    private const int InRelease = 1;
    private const int InTimeout = 2;
    private const int InRecheck = 3;

    private bool _release;

    private double _timeoutSeconds;

    private double _recheckSeconds = 1.0;

    private DateTime _heldSince = DateTime.MinValue;

    private int _released;

    private int _timedOut;

    /// <summary>
    /// Initializes a new instance of the <see cref="HoldSignal"/> class.
    /// </summary>
    public HoldSignal()
        : base(
            "Hold Signal",
            "Hold",
            "Keeps a signal waiting until Release becomes true, then sends it on. Use Signal Gate instead when the answer is \"no\" rather than \"not yet\".")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("C61A47F2-8D35-4B90-A7E6-1F58204C9BD3");

    /// <inheritdoc/>
    protected override string SignalInputDescription =>
        "The signal to hold. One waits at a time; anything else stays latched on the wire and takes its turn afterwards, in the order it happened.";

    /// <inheritdoc/>
    protected override string PassedOutputName => "Released";

    /// <inheritdoc/>
    protected override string PassedOutputDescription =>
        "The signal once the condition came true, exactly as it arrived — including the instructions and images it carries.";

    /// <inheritdoc/>
    protected override bool HasSecondaryOutput => true;

    /// <inheritdoc/>
    protected override string DivertedOutputName => "Timed Out";

    /// <inheritdoc/>
    protected override string DivertedOutputDescription =>
        "The signal if the timeout ran out before the condition came true. Wire it to whatever should happen when the thing being waited for never arrives.";

    /// <inheritdoc/>
    protected override HoldPolicy Holding => HoldPolicy.KeepOldest;

    /// <inheritdoc/>
    protected override int PollMs =>
        _recheckSeconds > 0
            ? (int)Math.Round(Math.Max(0.05, _recheckSeconds) * 1000.0)
            : (_timeoutSeconds > 0 ? 1000 : 0);

    /// <inheritdoc/>
    protected override string RelayCaption
    {
        get
        {
            if (HeldSignal is not null)
            {
                double waited = (DateTime.UtcNow - _heldSince).TotalSeconds;
                return _timeoutSeconds > 0
                    ? $"waiting {waited.ToString("0", CultureInfo.InvariantCulture)}s / {_timeoutSeconds.ToString("0", CultureInfo.InvariantCulture)}s"
                    : $"waiting {waited.ToString("0", CultureInfo.InvariantCulture)}s";
            }

            return _released == 0 && _timedOut == 0
                ? string.Empty
                : $"{_released} through, {_timedOut} timed out";
        }
    }

    /// <inheritdoc/>
    protected override void RegisterAdditionalInputs(GH_InputParamManager pManager)
    {
        pManager.AddBooleanParameter(
            "Release",
            "R",
            "The signal waits until this is true. Wire whatever says the thing being waited for has happened.",
            GH_ParamAccess.item,
            false);
        pManager.AddNumberParameter(
            "Timeout",
            "T",
            "Seconds to wait before giving up and sending the signal out of Timed Out instead. Zero waits indefinitely — fine when somebody is watching, a leak when nobody is.",
            GH_ParamAccess.item,
            0.0);
        pManager.AddNumberParameter(
            "Recheck",
            "P",
            "Seconds between re-reading the condition. Also re-runs whatever is wired into Release, which is what lets it notice something outside Grasshopper. Zero only re-reads when the graph re-solves on its own.",
            GH_ParamAccess.item,
            0.0);
    }

    /// <inheritdoc/>
    protected override void OnSolveTick(IGH_DataAccess da)
    {
        bool release = false;
        double timeout = 0.0;
        double recheck = 0.0;
        da.GetData(InRelease, ref release);
        da.GetData(InTimeout, ref timeout);
        da.GetData(InRecheck, ref recheck);

        _release = release;
        _timeoutSeconds = Math.Max(0.0, timeout);
        _recheckSeconds = Math.Max(0.0, recheck);
    }

    /// <inheritdoc/>
    protected override RelayRoute Decide(PhySignal signal, bool held, IGH_DataAccess da)
    {
        if (!held)
        {
            _heldSince = DateTime.UtcNow;
        }

        if (_release)
        {
            _released++;
            return RelayRoute.Pass;
        }

        if (_timeoutSeconds > 0 && (DateTime.UtcNow - _heldSince).TotalSeconds >= _timeoutSeconds)
        {
            _timedOut++;
            return RelayRoute.Divert;
        }

        if (_recheckSeconds > 0)
        {
            // See the class remarks: expiring this node re-reads nothing, because Grasshopper
            // recomputes only what it expired. The components feeding Release are marked here so the
            // condition is actually re-evaluated — which is the whole mechanism by which a wait on
            // something outside the data graph can ever end.
            ExpireReleaseSources();
        }

        return RelayRoute.Hold;
    }

    /// <inheritdoc/>
    protected override void OnCleared()
    {
        base.OnCleared();
        _released = 0;
        _timedOut = 0;
        _heldSince = DateTime.MinValue;
    }

    private void ExpireReleaseSources()
    {
        if (Params.Input.Count <= InRelease)
        {
            return;
        }

        foreach (IGH_Param source in Params.Input[InRelease].Sources)
        {
            // recompute:false — the poll's own scheduled solution is the one that will run, and asking
            // for a solution from inside a solve is re-entrant.
            source.ExpireSolution(false);
        }
    }
}
