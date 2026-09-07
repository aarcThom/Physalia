// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using System.Globalization;
using Grasshopper.Kernel;
using Physalia.Core.Signals;

namespace Physalia.GH.Components;

/// <summary>
/// Lets at most one signal through per interval, holding the newest of anything that arrives in
/// between and sending it on when the interval is up.
///
/// <para><b>Why it earns its place next to the triggers.</b> Each trigger folds its own bursts — a
/// copied folder is one wake-up, a dragged slider is one round — but a burst is not the only way to
/// get too many rounds. Three armed triggers on one Conversation Log, a Rhino edit that lands a second
/// after a file appears, a user typing while a timer fires: each event is legitimate, none is a burst,
/// and the rounds still pile up. This is where a rate is stated once for the whole pipeline, and it is
/// the place to put it rather than tuning three settle windows against each other.</para>
///
/// <para><b>Superseded, not queued</b> — trailing edge. A signal held here is replaced by the next one
/// to arrive, so what comes out at the end of the window is the most recent state of the world rather
/// than the oldest. Queueing would be the wrong answer twice over: it would forward every event after
/// all (only later), and the early ones describe a situation that has since moved on.</para>
///
/// <para>It has one output, deliberately. A superseded signal has not been refused — it has been
/// overtaken — so there is nothing meaningful to route it to, and a Dropped output would invite
/// wiring up something to react to events that have already been replaced by the one that did come
/// through. The count is on the caption instead.</para>
/// </summary>
public class SignalThrottle : SignalRelayBase
{
    private const int InInterval = 1;

    private double _intervalSeconds = 5.0;

    private DateTime _lastPassed = DateTime.MinValue;

    private int _passed;

    private int _superseded;

    /// <summary>
    /// Initializes a new instance of the <see cref="SignalThrottle"/> class.
    /// </summary>
    public SignalThrottle()
        : base(
            "Signal Throttle",
            "Throttle",
            "Lets one signal through per interval and holds the newest of the rest until the interval is up. Put it where several triggers meet, so the rate is stated once for the whole pipeline.")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("94D0F63A-2C71-4E85-B1A9-70C3E846F2D5");

    /// <inheritdoc/>
    protected override string SignalInputDescription =>
        "The signals to rate-limit. Wire every trigger and prompt source that shares one pipeline in here.";

    /// <inheritdoc/>
    protected override string PassedOutputName => "Signal";

    /// <inheritdoc/>
    protected override string PassedOutputDescription =>
        "One signal per interval, exactly as it arrived — the newest one waiting when the interval came up.";

    /// <inheritdoc/>
    protected override bool HasSecondaryOutput => false;

    /// <inheritdoc/>
    protected override HoldPolicy Holding => HoldPolicy.KeepNewest;

    /// <inheritdoc/>
    /// <remarks>
    /// Whatever is left of the current window, so the held signal goes out when the interval is
    /// genuinely up rather than at the next unrelated solve. Floored at a tenth of a second: a poll
    /// due immediately is still a scheduled solution, and asking for it every millisecond would be a
    /// busy loop.
    /// </remarks>
    protected override int PollMs
    {
        get
        {
            double remaining = _intervalSeconds - (DateTime.UtcNow - _lastPassed).TotalSeconds;
            return (int)Math.Round(Math.Max(0.1, remaining) * 1000.0);
        }
    }

    /// <inheritdoc/>
    protected override string RelayCaption
    {
        get
        {
            string rate = $"1 / {_intervalSeconds.ToString("0.##", CultureInfo.InvariantCulture)}s";

            if (HeldSignal is not null)
            {
                double remaining = Math.Max(0.0, _intervalSeconds - (DateTime.UtcNow - _lastPassed).TotalSeconds);
                return $"{rate} · {remaining.ToString("0.#", CultureInfo.InvariantCulture)}s to go";
            }

            return _superseded > 0 ? $"{rate} · {_passed} through, {_superseded} overtaken" : rate;
        }
    }

    /// <inheritdoc/>
    protected override void RegisterAdditionalInputs(GH_InputParamManager pManager)
    {
        pManager.AddNumberParameter(
            "Interval",
            "T",
            "Shortest time between signals leaving here, in seconds. Anything arriving inside that waits, and is replaced if something newer turns up before the interval is up.",
            GH_ParamAccess.item,
            5.0);
    }

    /// <inheritdoc/>
    protected override void OnSolveTick(IGH_DataAccess da)
    {
        double interval = 5.0;
        da.GetData(InInterval, ref interval);
        _intervalSeconds = Math.Max(0.0, interval);
    }

    /// <inheritdoc/>
    protected override RelayRoute Decide(PhySignal signal, bool held, IGH_DataAccess da)
    {
        // Counted on the way in rather than on the way out, so a signal replaced while waiting is
        // counted once as overtaken and never as having passed.
        if (!held && HeldSignal is not null)
        {
            _superseded++;
        }

        if ((DateTime.UtcNow - _lastPassed).TotalSeconds >= _intervalSeconds)
        {
            _lastPassed = DateTime.UtcNow;
            _passed++;
            return RelayRoute.Pass;
        }

        return RelayRoute.Hold;
    }

    /// <inheritdoc/>
    protected override void OnCleared()
    {
        base.OnCleared();
        _passed = 0;
        _superseded = 0;
        _lastPassed = DateTime.MinValue;
    }
}
