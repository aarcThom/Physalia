// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Grasshopper.Kernel;
using Physalia.Core.Signals;
using Physalia.GH.Parameters;

namespace Physalia.GH.Components;

/// <summary>
/// Gates a signal hop against a non-converging feedback loop. Success signals always pass
/// through untouched. Failure signals are fingerprinted by their normalized payload text and
/// compared to the previous failure: while the payloads differ the loop is making progress and
/// each signal passes through; when the same failure text arrives for the Stall Limit-th
/// consecutive time the signal still passes but with an escalation preamble prepended, telling
/// the model to stop patching and explain the blocker to the human in prose; any identical
/// failure beyond the limit is not re-emitted at all — the loop parks with a STALLED caption
/// until something actually changes.
///
/// <para>Wire it between the Feedback Collector and the Conversation Log's Feedback Signal
/// input to cap identical feedback rounds (the Signal Limiter remains the cap on <em>total</em>
/// rounds). The comparison is exact identity, not similarity: the fingerprint strips the
/// "Current base checksum" line (a checksum legitimately changes when a patch applies even
/// though the reported problems are identical), so "same problems, same values" counts as a
/// stall even when the model shuffled structure. Any different failure, any success signal,
/// or a menu Clear resets the streak — the guard self-heals the moment the loop moves.</para>
/// </summary>
public class StallGuard : StatefulComponentBase
{
    private const int InSignal = 0;
    private const int InStallLimit = 1;

    private const int OutPass = 0;
    private const int OutStalled = 1;

    private const int DefaultStallLimit = 3;

    private string? _lastFingerprint;
    private int _repeatCount;
    private PhySignal? _passSignal;
    private PhySignal? _stalledSignal;

    /// <summary>
    /// Initializes a new instance of the <see cref="StallGuard"/> class.
    /// </summary>
    public StallGuard()
        : base("Stall Guard", "Stall", "Passes signals through until the same failure payload arrives N consecutive times: the Nth repeat is escalated (the model is told to stop patching and explain the blocker to the human), and further identical repeats are not re-emitted. Any different signal resets the streak.", "Guardrails")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("6E3F91B4-2C8A-4D57-9B0E-A47D5F1C83E2");

    /// <inheritdoc/>
    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddParameter(new Param_Signal(), "Signal", "S", "Signals to gate. Success signals always pass; consecutive identical failure payloads are counted against the Stall Limit.", GH_ParamAccess.list);
        pManager.AddIntegerParameter("Stall Limit", "SL", "Consecutive identical failure payloads tolerated. The repeat that reaches the limit passes with an escalation preamble telling the model to stop and explain the blocker to the human; repeats beyond the limit are not re-emitted (the loop parks until something changes). 0 disables the guard.", GH_ParamAccess.item, DefaultStallLimit);
        pManager[InSignal].Optional = true;
    }

    /// <inheritdoc/>
    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddParameter(new Param_Signal(), "Pass", "P", "Carries each gated signal onward (unchanged, or with the escalation preamble at the limit). Latched until the next signal.", GH_ParamAccess.item);
        pManager.AddParameter(new Param_Signal(), "Stalled", "X", "Carries the suppressed failure signal once the loop is parked, for optional human-facing wiring (a panel, a notification). Latched until the streak resets.", GH_ParamAccess.item);
    }

    /// <inheritdoc/>
    protected override void SolveInstance(IGH_DataAccess DA)
    {
        // Observe every solve so the consume-once baseline holds and latched outputs are
        // re-emitted for downstream consume-once.
        ObserveSignalInputs(DA, InSignal);

        int limit = DefaultStallLimit;
        DA.GetData(InStallLimit, ref limit);

        foreach (ConsumedSignal item in ConsumeAllSignals(InSignal))
        {
            PhySignal signal = item.Signal;

            if (limit <= 0 || signal.Outcome != SignalOutcome.Failure)
            {
                // Not a failure (or the guard is disabled): the loop is moving. Pass the
                // original signal through — same sequence, payload, blocks, and outcome —
                // and reset the streak.
                _lastFingerprint = null;
                _repeatCount = 0;
                _stalledSignal = null;
                _passSignal = signal;
                continue;
            }

            string fingerprint = Fingerprint(signal.Payload);
            _repeatCount = string.Equals(fingerprint, _lastFingerprint, StringComparison.Ordinal) ? _repeatCount + 1 : 1;
            _lastFingerprint = fingerprint;

            if (_repeatCount < limit)
            {
                _stalledSignal = null;
                _passSignal = signal;
            }
            else if (_repeatCount == limit)
            {
                // The model has now seen this exact failure limit-1 times without moving the
                // loop. Pass it once more, but framed as a dead end: the preamble instructs a
                // prose reply to the human, which the Detect JSON gate then parks naturally.
                _passSignal = PhySignal.Mint(
                    SignalOutcome.Failure,
                    BuildEscalation(limit) + Environment.NewLine + Environment.NewLine + signal.Payload,
                    InstanceGuid,
                    Name,
                    signal.ContentBlocks.Count > 0 ? signal.ContentBlocks : null);
            }
            else
            {
                // The model ignored the escalation. Park the loop: nothing is re-emitted on
                // Pass, so no inference fires. The suppressed signal latches on Stalled for
                // human-facing wiring. The next different signal resumes the loop.
                _stalledSignal = signal;
                AddRuntimeMessage(
                    GH_RuntimeMessageLevel.Warning,
                    $"Feedback loop stalled: {_repeatCount} consecutive identical failure payloads; re-emission halted. Fix the canvas manually or Clear this component to resume.");
            }
        }

        Message = _stalledSignal is not null
            ? $"STALLED ({_repeatCount}x)"
            : _repeatCount > 0 ? $"{_repeatCount} / {limit}" : null;
        OnDisplayExpired(true);

        EmitSignal(DA, OutPass, _passSignal);
        EmitSignal(DA, OutStalled, _stalledSignal);
    }

    /// <inheritdoc/>
    protected override void OnCleared()
    {
        _lastFingerprint = null;
        _repeatCount = 0;
        _passSignal = null;
        _stalledSignal = null;
    }

    /// <summary>
    /// Hashes the payload with volatile lines removed, so "the same problems" fingerprints
    /// identically across rounds. The "Current base checksum" line is stripped because the
    /// checksum legitimately changes when a patch applies, even when the reported problems —
    /// the thing the streak measures — have not.
    /// </summary>
    /// <param name="payload">The failure payload to fingerprint.</param>
    /// <returns>The hex fingerprint.</returns>
    private static string Fingerprint(string payload)
    {
        string normalized = string.Join(
            '\n',
            payload.Split('\n')
                .Select(line => line.TrimEnd('\r', ' ', '\t'))
                .Where(line => !line.TrimStart().StartsWith("Current base checksum", StringComparison.Ordinal)))
            .Trim();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
    }

    private static string BuildEscalation(int limit)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"[LOOP STALLED — you are receiving this IDENTICAL feedback for the {Ordinal(limit)} consecutive time. Your recent attempts changed nothing.]");
        sb.AppendLine("STOP submitting graphs or patches now. Do not reply with JSON. Reply in plain language addressed to the human operator:");
        sb.AppendLine("1. What you were trying to achieve on the canvas.");
        sb.AppendLine("2. The exact recurring problem (quote the warning or error).");
        sb.AppendLine("3. What you already tried and why you believe it did not work.");
        sb.AppendLine("4. The specific information, decision, or manual fix you need from the human.");
        sb.Append("The repeated feedback follows for reference only — do not attempt another fix.");
        return sb.ToString();
    }

    private static string Ordinal(int n)
    {
        return (n % 100 is 11 or 12 or 13 ? 0 : n % 10) switch
        {
            1 => $"{n}st",
            2 => $"{n}nd",
            3 => $"{n}rd",
            _ => $"{n}th",
        };
    }
}
