// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using Grasshopper.Kernel;

namespace Physalia.GH.Components;

/// <summary>
/// Fires on a clock, while armed. The trigger that makes an unattended pipeline possible at all: it
/// is the only source that needs nothing to happen anywhere.
///
/// <para><b>What it is for.</b> Polling something the plug-in cannot be told about — an API whose
/// data changes, a job queue, a shared drive on a network share where file-system notifications do not
/// arrive. For anything on this machine's disk, use the Folder Watcher instead: it hears the change
/// rather than looking for it, so it reacts at once and costs nothing in between.</para>
///
/// <para><b>It reopens off, always</b> — see the base class. A file that opened armed would start
/// spending money on whatever machine happened to open it, including a colleague's.</para>
/// </summary>
public class TimerTrigger : SignalSourceBase<DateTime>
{
    private const int InInterval = 0;
    private const int InPayload = 1;

    /// <summary>
    /// Shortest interval accepted. Not a performance limit — a round takes seconds and costs money,
    /// so anything under this is a typo rather than an intention, and honouring it would hammer a
    /// provider on the user's account.
    /// </summary>
    private const double MinIntervalSeconds = 1.0;

    private System.Threading.Timer? _clock;

    private double _interval = 60.0;

    private string _payload = string.Empty;

    /// <summary>
    /// Initializes a new instance of the <see cref="TimerTrigger"/> class.
    /// </summary>
    public TimerTrigger()
        : base(
            "Timer",
            "Timer",
            "Starts a round on a clock, for as long as it is armed. Right-click and tick Armed to switch it on — it is always off when a file opens, so nothing runs on a machine nobody is watching.")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("0C4F71A8-3E92-4D5B-8A17-2B6E9D40F5C3");

    /// <inheritdoc/>
    protected override string SignalOutputDescription =>
        "Fires every interval while armed, carrying the Payload text. Wire into a Conversation Log's Prompt Signal input to send a standing prompt, or into anything else a signal drives.";

    /// <inheritdoc/>
    protected override string ArmedCaption =>
        $"every {Describe(_interval)}";

    /// <inheritdoc/>
    /// <remarks>
    /// One tick, one signal — there is no burst to fold. The window is short rather than zero only so
    /// that ticks piling up behind a long round collapse into one instead of queueing.
    /// </remarks>
    protected override int SettleMs => 50;

    /// <inheritdoc/>
    protected override void RegisterSourceInputs(GH_InputParamManager pManager)
    {
        pManager.AddNumberParameter(
            "Interval",
            "T",
            "Seconds between rounds. Changing it while armed takes effect at once.",
            GH_ParamAccess.item,
            60.0);
        pManager.AddTextParameter(
            "Payload",
            "P",
            "The text each signal carries — the standing prompt, or whatever the receiving component reads. Blank sends a plain note that the timer fired.",
            GH_ParamAccess.item,
            string.Empty);
        pManager[InPayload].Optional = true;
    }

    /// <inheritdoc/>
    protected override void OnSolveTick(IGH_DataAccess da)
    {
        double interval = 60.0;
        string payload = string.Empty;
        da.GetData(InInterval, ref interval);
        da.GetData(InPayload, ref payload);

        _payload = payload ?? string.Empty;

        if (interval < MinIntervalSeconds)
        {
            AddRuntimeMessage(
                GH_RuntimeMessageLevel.Warning,
                $"An interval of {interval.ToString("0.##", CultureInfo.InvariantCulture)}s would start rounds faster than they can finish. Using {MinIntervalSeconds.ToString("0.##", CultureInfo.InvariantCulture)}s.");
            interval = MinIntervalSeconds;
        }

        if (Math.Abs(interval - _interval) > 0.0001)
        {
            _interval = interval;

            // Restart rather than adjust: a change to the interval should be measured from now, not
            // from whenever the last tick happened to be.
            RestartListening();
            UpdateStateDisplay();
        }
    }

    /// <inheritdoc/>
    protected override void StartListening()
    {
        int period = (int)Math.Round(Math.Max(MinIntervalSeconds, _interval) * 1000.0);

        // Deliberately does NOT fire immediately on arming. Arming is a switch, not a run button, and
        // Construct Signal is right there for "go now" — a trigger that fired on arming would make
        // every accidental tick of the menu item a round.
        _clock = new System.Threading.Timer(_ => ReportEvent(DateTime.UtcNow), null, period, period);
    }

    /// <inheritdoc/>
    protected override void StopListening()
    {
        _clock?.Dispose();
        _clock = null;
    }

    /// <inheritdoc/>
    protected override string ComposePayload(IReadOnlyList<DateTime> events)
    {
        if (!string.IsNullOrWhiteSpace(_payload))
        {
            return _payload;
        }

        // No standing prompt typed. Say the time rather than something fixed: a bare "Timer fired"
        // recorded three times over is indistinguishable in a transcript, and the time is the one
        // thing about a tick that differs.
        DateTime when = events.Count > 0 ? events[^1].ToLocalTime() : DateTime.Now;
        string skipped = events.Count > 1
            ? $" ({events.Count} ticks elapsed while the pipeline was busy)"
            : string.Empty;

        return $"Timer fired at {when.ToString("HH:mm:ss", CultureInfo.InvariantCulture)}.{skipped}";
    }

    private static string Describe(double seconds) =>
        seconds >= 3600
            ? $"{(seconds / 3600.0).ToString("0.##", CultureInfo.InvariantCulture)}h"
            : seconds >= 60
                ? $"{(seconds / 60.0).ToString("0.##", CultureInfo.InvariantCulture)}m"
                : $"{seconds.ToString("0.##", CultureInfo.InvariantCulture)}s";
}
