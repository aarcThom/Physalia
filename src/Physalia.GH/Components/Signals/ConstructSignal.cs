// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using Grasshopper.Kernel;
using Physalia.Core.Signals;
using Physalia.GH.Parameters;

namespace Physalia.GH.Components;

/// <summary>
/// Mints a signal carrying an arbitrary text payload, one per Button press. The manual
/// entry point into the signal pipeline: a Panel of text plus a Button lets any
/// signal-driven component (e.g. Schema Validator) be run standalone, without the upstream chain.
/// The Trigger is a native Boolean input (not a Signal) — it is the one sanctioned place a
/// Button drives the pipeline, since Signal inputs themselves accept only signals.
/// </summary>
public class ConstructSignal : StatefulComponentBase
{
    private const int InPayload = 0;
    private const int InFailure = 1;
    private const int InTrigger = 2;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConstructSignal"/> class.
    /// </summary>
    public ConstructSignal()
        : base("Construct Signal", "ConSig", "Makes a signal by hand, one per button press. This is the one place an ordinary Grasshopper button is allowed to drive a Physalia pipeline.", "Signals")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("A1B7E2C9-5D38-4F61-9E0A-7C42D8B5F316");

    /// <inheritdoc/>
    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddTextParameter("Payload", "P", "The text the signal carries — a fixed prompt, a canned instruction, whatever the receiving component expects to read.", GH_ParamAccess.item, string.Empty);
        pManager.AddBooleanParameter("Failure", "F", "Send it as a failure instead of a success, so a feedback path can be tried out without waiting for something to actually go wrong.", GH_ParamAccess.item, false);
        pManager.AddBooleanParameter(
            "Trigger",
            "T",
            "One press, one signal. Wire a Button here. Opening or pasting the file fires nothing — only a real press does.",
            GH_ParamAccess.item,
            false);
        pManager[InPayload].Optional = true;
    }

    /// <inheritdoc/>
    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddParameter(new Param_Signal(), "Signal", "S", "The signal, held on the wire until the next press. Anything expecting text reads it as the Payload.", GH_ParamAccess.item);
    }

    /// <inheritdoc/>
    protected override void SolveInstance(IGH_DataAccess DA)
    {
        // A source, not a processing hop: no visible Active delay — the latch happens on
        // the press solve. The output holds a single latched signal until the next press.
        if (ObserveButtonPress(DA, InTrigger))
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
