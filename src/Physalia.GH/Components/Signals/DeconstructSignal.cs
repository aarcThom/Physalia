// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using Grasshopper.Kernel;
using Physalia.Core.Signals;
using Physalia.GH.Goo;
using Physalia.GH.Parameters;

namespace Physalia.GH.Components;

/// <summary>
/// Breaks a signal into its fields: sequence, outcome, payload, source, and mint time.
/// A passive tap — it never consumes, so wiring it anywhere on a signal wire inspects
/// the latched event without disturbing the consume-once bookkeeping of real receivers.
/// </summary>
public class DeconstructSignal : PhyBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DeconstructSignal"/> class.
    /// </summary>
    public DeconstructSignal()
        : base("Deconstruct Signal", "DeSig", "Opens a signal up so you can see what is inside it. Looking is free: unlike every other component, this one never uses the signal up, so you can tap it anywhere to see what is going on.", "Signals")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("B6D91A45-3E7F-4C28-8B5A-1F9E6D072C84");

    /// <inheritdoc/>
    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddParameter(new Param_Signal(), "Signal", "S", "The signal to look inside. It is left exactly as it was, still there for everything else on that wire.", GH_ParamAccess.item);
        pManager[0].Optional = true;
    }

    /// <inheritdoc/>
    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddIntegerParameter("Sequence", "#", "When it happened. Every signal takes the next number in line, so a higher number always means later.", GH_ParamAccess.item);
        pManager.AddBooleanParameter("Success", "OK", "True if the signal reports success, false if it reports failure.", GH_ParamAccess.item);
        pManager.AddTextParameter("Payload", "P", "The text it carries: the result if it succeeded, the complaint if it did not.", GH_ParamAccess.item);
        pManager.AddTextParameter("Source", "Sr", "The name of the component that sent it.", GH_ParamAccess.item);
        pManager.AddTextParameter("Time", "T", "The moment it was sent, to the millisecond.", GH_ParamAccess.item);
        pManager.AddParameter(new Param_Instructions(), "Instructions", "I", "The instructions and conversation it carries, if this is the signal on its way from a Conversation Log to an LLM Call. Empty for every other kind of signal.", GH_ParamAccess.item);
    }

    /// <inheritdoc/>
    protected override void SolveInstance(IGH_DataAccess DA)
    {
        GH_Signal? goo = null;
        if (!DA.GetData(0, ref goo) || goo?.Value is not PhySignal signal)
        {
            // Empty or invalid wire — nothing to show.
            return;
        }

        DA.SetData(0, (int)signal.Sequence);
        DA.SetData(1, signal.Outcome == SignalOutcome.Success);
        DA.SetData(2, signal.Payload);
        DA.SetData(3, signal.SourceName);
        DA.SetData(4, signal.Timestamp.ToLocalTime().ToString("HH:mm:ss.fff"));

        if (signal.Instructions is not null)
        {
            DA.SetData(5, new GH_Instructions(signal.Instructions));
        }
    }
}
