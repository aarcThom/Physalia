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
        : base("Deconstruct Signal", "DeSig", "Breaks a signal into sequence, outcome, payload, source, and time. Passive — never consumes the signal.", "Signals")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("B6D91A45-3E7F-4C28-8B5A-1F9E6D072C84");

    /// <inheritdoc/>
    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddParameter(new Param_Signal(), "Signal", "S", "The signal to inspect.", GH_ParamAccess.item);
        pManager[0].Optional = true;
    }

    /// <inheritdoc/>
    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddIntegerParameter("Sequence", "#", "Process-wide monotonic sequence number. Higher = happened later; sequence order is causal order.", GH_ParamAccess.item);
        pManager.AddBooleanParameter("Success", "OK", "True for a Success outcome, false for Failure.", GH_ParamAccess.item);
        pManager.AddTextParameter("Payload", "P", "The carried payload: result text on success, feedback text on failure.", GH_ParamAccess.item);
        pManager.AddTextParameter("Source", "Sr", "Name of the component that minted the signal.", GH_ParamAccess.item);
        pManager.AddTextParameter("Time", "T", "Local mint time (HH:mm:ss.fff).", GH_ParamAccess.item);
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
    }
}
