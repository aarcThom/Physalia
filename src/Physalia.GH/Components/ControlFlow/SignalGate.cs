// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using Grasshopper.Kernel;
using Physalia.Core.Signals;

namespace Physalia.GH.Components;

/// <summary>
/// Lets a signal through only while a condition holds, and sends it out of the other output when it
/// does not. The plain conditional the pipeline had no way to express: every branch in Physalia used
/// to be a specialist — Detect JSON knows only about JSON, Stall Guard only about repeated failures,
/// a guardrail's Success and Fail pair only about its own run.
///
/// <para><b>It decides NOW.</b> A shut gate turns a signal away rather than making it wait — which is
/// what makes it different from Hold Signal, the component to use when the answer is "not yet"
/// instead of "no". Both exist because both happen: refusing a round because the budget is spent is a
/// decision, and waiting for a survey to arrive is not.</para>
///
/// <para><b>Reading the signal's own outcome is a Deconstruct Signal away.</b> That node hands out a
/// Success boolean, so "carry on only if this succeeded" is that wired straight into Open — which is
/// worth knowing because a Merge Signal's combined outcome is otherwise unreachable, the two branches
/// having lost their own Success and Fail outputs by then.</para>
///
/// <para>The signal that comes out is the one that went in, sequence and all — see
/// <see cref="SignalRelayBase"/>. So a gate can sit on the hop between a Conversation Log and an LLM
/// Call without stripping the Instructions that hop exists to carry.</para>
/// </summary>
public class SignalGate : SignalRelayBase
{
    private const int InOpen = 1;

    private bool _open = true;

    private int _passedCount;

    private int _blockedCount;

    /// <summary>
    /// Initializes a new instance of the <see cref="SignalGate"/> class.
    /// </summary>
    public SignalGate()
        : base(
            "Signal Gate",
            "Gate",
            "Lets signals through while Open is true and sends them out of Blocked when it is false. For \"only carry on if…\" — use Hold Signal instead when the answer is \"not yet\" rather than \"no\".")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("3F82B5D6-04A7-49E1-8C6B-D2519A73E0F4");

    /// <inheritdoc/>
    protected override string SignalInputDescription =>
        "The signals to let through, or not. Each one is decided as it arrives.";

    /// <inheritdoc/>
    protected override string PassedOutputName => "Passed";

    /// <inheritdoc/>
    protected override string PassedOutputDescription =>
        "Each signal that arrived while the gate was open, exactly as it arrived — including the instructions and images it carries.";

    /// <inheritdoc/>
    protected override bool HasSecondaryOutput => true;

    /// <inheritdoc/>
    protected override string DivertedOutputName => "Blocked";

    /// <inheritdoc/>
    protected override string DivertedOutputDescription =>
        "Each signal that arrived while the gate was shut. Wire it to whatever should happen instead — a note to the model, a Feedback, or nothing at all.";

    /// <inheritdoc/>
    protected override string RelayCaption =>
        _passedCount == 0 && _blockedCount == 0
            ? (_open ? "open" : "shut")
            : $"{(_open ? "open" : "shut")} · {_passedCount}/{_passedCount + _blockedCount}";

    /// <inheritdoc/>
    protected override void RegisterAdditionalInputs(GH_InputParamManager pManager)
    {
        pManager.AddBooleanParameter(
            "Open",
            "O",
            "True lets signals through, false turns them away. Wire a Toggle, a comparison, or a Deconstruct Signal's Success output.",
            GH_ParamAccess.item,
            true);
    }

    /// <inheritdoc/>
    protected override void OnSolveTick(IGH_DataAccess da)
    {
        bool open = true;
        da.GetData(InOpen, ref open);
        _open = open;
    }

    /// <inheritdoc/>
    protected override RelayRoute Decide(PhySignal signal, bool held, IGH_DataAccess da)
    {
        if (_open)
        {
            _passedCount++;
            return RelayRoute.Pass;
        }

        _blockedCount++;
        return RelayRoute.Divert;
    }

    /// <inheritdoc/>
    protected override void OnCleared()
    {
        base.OnCleared();
        _passedCount = 0;
        _blockedCount = 0;
    }
}
