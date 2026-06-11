// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using Grasshopper.Kernel;
using Physalia.Core.Signals;
using Physalia.GH.Parameters;

namespace Physalia.GH.Components;

/// <summary>
/// Mints a signal carrying an arbitrary text payload, one per consumed trigger. The
/// manual entry point into the signal pipeline: a Panel of text plus a Button lets any
/// signal-driven component (e.g. Auditor) be run standalone, without the upstream chain.
/// The incoming trigger's own payload is ignored — the Payload input is authoritative.
/// </summary>
public class ConstructSignal : StatefulComponentBase
{
    private const int InPayload = 0;
    private const int InFailure = 1;
    private const int InSignal = 2;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConstructSignal"/> class.
    /// </summary>
    public ConstructSignal()
        : base("Construct Signal", "ConSig", "Mints a signal carrying the given payload, once per incoming trigger.", "Signals")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("A1B7E2C9-5D38-4F61-9E0A-7C42D8B5F316");

    /// <inheritdoc/>
    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddTextParameter("Payload", "P", "Text payload carried by the minted signal.", GH_ParamAccess.item, string.Empty);
        pManager.AddBooleanParameter("Failure", "F", "When true the minted signal carries a Failure outcome (for hand-testing feedback paths).", GH_ParamAccess.item, false);
        pManager.AddParameter(
            new Param_Signal(),
            "Signal",
            "S",
            "Trigger. Each incoming signal mints the output once; native Buttons/Toggles also work (one mint per press).",
            GH_ParamAccess.list);
        pManager[InPayload].Optional = true;
        pManager[InSignal].Optional = true;
    }

    /// <inheritdoc/>
    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddParameter(new Param_Signal(), "Signal", "S", "The minted signal, latched until the next trigger or a clear. Casts to text (the payload).", GH_ParamAccess.item);
    }

    /// <inheritdoc/>
    protected override void SolveInstance(IGH_DataAccess DA)
    {
        ObserveSignalInputs(DA, InSignal);

        // A source, not a processing hop: no visible Active delay — the latch happens on
        // the consume solve. Several queued triggers collapse into one mint (the output
        // holds a single latched signal anyway).
        if (ConsumeAllSignals(InSignal).Count > 0)
        {
            string payload = string.Empty;
            bool failure = false;
            DA.GetData(InPayload, ref payload);
            DA.GetData(InFailure, ref failure);

            LatchSuccess(payload, emitSignal: true, outcome: failure ? SignalOutcome.Failure : SignalOutcome.Success);
        }

        EmitSignal(DA, 0, SuccessSignal);
    }
}
