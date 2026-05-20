// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Physalia.Core.Tokens;

namespace Physalia.GH.Components;

/// <summary>
/// Estimates token count locally using a ~4 chars per token heuristic.
/// Calculated locally — no API call. Accurate to within ~10–20% for English prose.
/// Works for any provider. Use when exact counts are not required.
/// </summary>
public class HeuristicEstimator : PhyBase
{
    private static readonly HeuristicTokenEstimator _estimator = new HeuristicTokenEstimator();

    /// <summary>
    /// Initializes a new instance of the <see cref="HeuristicEstimator"/> class.
    /// </summary>
    public HeuristicEstimator()
        : base(
            "Heuristic Estimator",
            "HEst",
            "Estimates token count locally using a ~4 chars per token heuristic. No API call. Works for any provider.",
            "Tokens")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("601F9C93-9AF9-4FE4-9BFB-AB89199CEF5A");

    /// <inheritdoc/>
    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddGenericParameter("Data", "D", "Instructions, Conversation, or text string to estimate.", GH_ParamAccess.item);
    }

    /// <inheritdoc/>
    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddIntegerParameter("Tokens", "T", "Estimated token count.", GH_ParamAccess.item);
    }

    /// <inheritdoc/>
    protected override void SolveInstance(IGH_DataAccess DA)
    {
        IGH_Goo? goo = null;
        if (!DA.GetData(0, ref goo)) return;

        if (!TokenInputHelper.TryResolve(goo, out var instructions))
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Input could not be resolved to Instructions, Conversation, or text.");
            return;
        }

        DA.SetData(0, _estimator.Estimate(instructions));
    }
}
