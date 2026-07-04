// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using Grasshopper.Kernel;
using Physalia.Core.ConvoInstruct;
using Physalia.Core.Signals;
using Physalia.Core.Tokens;
using Physalia.GH.Goo;
using Physalia.GH.Parameters;

namespace Physalia.GH.Components;

/// <summary>
/// Token-budget gate for compaction: routes a Conversation Log's Signal — which carries the full
/// <see cref="Instructions"/> — by the estimated size of that context. A signal whose context is at or
/// under the Threshold passes through the <b>Under Limit</b> output; one that exceeds it passes through
/// the <b>Over Limit</b> output. The routed signal is the same event (the Instructions ride along), so
/// each branch can be wired onward without losing context.
///
/// <para>This is how compaction is gated on the forward path: wire the Conversation Log's Signal in, send
/// <b>Under Limit → LLM Call</b> (small context goes straight through) and
/// <b>Over Limit → Compactor → LLM Call</b> (large context is compacted first). Every turn reaches the
/// LLM Call exactly once; compaction only runs when the context is actually over budget — the
/// research-recommended "compact at ~70–95% of the window" behaviour.</para>
///
/// <para>Needs a synchronous token estimator (Heuristic or Tiktoken); the API-backed estimators count
/// asynchronously and are rejected. Routing uses the consume-once intake, so a recompute never
/// re-routes an already-seen signal.</para>
/// </summary>
public class TokenThreshold : StatefulComponentBase
{
    private const int InSignal = 0;
    private const int InEstimator = 1;
    private const int InThreshold = 2;

    private const int OutUnder = 0;
    private const int OutOver = 1;
    private const int OutTokenCount = 2;

    private PhySignal? _underSignal;
    private PhySignal? _overSignal;
    private int _lastTokens;

    /// <summary>
    /// Initializes a new instance of the <see cref="TokenThreshold"/> class.
    /// </summary>
    public TokenThreshold()
        : base(
            "Token Threshold",
            "TokGate",
            "Routes a Conversation Log's Signal by the estimated token size of the Instructions it carries: under the threshold passes through, over the threshold routes to a compactor.",
            "Tokens & Compaction")
    {
    }

    /// <inheritdoc/>
    public override GH_Exposure Exposure => GH_Exposure.tertiary;

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("02342020-637B-43CB-92A0-5A8DA63B025C");

    /// <inheritdoc/>
    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddParameter(new Param_Signal(), "Signal", "S", "The Conversation Log's Signal to route, carrying the Instructions to measure.", GH_ParamAccess.list);
        pManager.AddParameter(new Param_ITokenEstimator(), "Tokenization Technique", "T", "A synchronous token estimator (Heuristic or Tiktoken) used to measure the carried context.", GH_ParamAccess.item);
        pManager.AddIntegerParameter("Threshold", "N", "Token budget: a context at or under this passes Under Limit; over it routes to Over Limit (e.g. ~80% of the model's context limit).", GH_ParamAccess.item, 8000);
        pManager[InSignal].Optional = true;
    }

    /// <inheritdoc/>
    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddParameter(new Param_Signal(), "Under Limit", "U", "Carries the signal (with its Instructions) when its context is at or under the threshold. Wire to a LLM Call. Latched until the next routed signal.", GH_ParamAccess.item);
        pManager.AddParameter(new Param_Signal(), "Over Limit", "O", "Carries the signal (with its Instructions) when its context exceeds the threshold. Wire to a compaction component, then on to the LLM Call. Latched until the next routed signal.", GH_ParamAccess.item);
        pManager.AddIntegerParameter("Token Count", "N", "Estimated token count of the most recently routed context.", GH_ParamAccess.item);
    }

    /// <inheritdoc/>
    protected override void SolveInstance(IGH_DataAccess DA)
    {
        // Observe every solve so the consume-once baseline holds and latched outputs are re-emitted.
        ObserveSignalInputs(DA, InSignal);

        var estimatorGoo = new GH_ITokenEstimator();
        if (!DA.GetData(InEstimator, ref estimatorGoo) || estimatorGoo.Value is null)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Connect a token estimator (a Tokenization Technique).");
            EmitRouted(DA);
            return;
        }

        if (estimatorGoo.Value is not ISyncTokenEstimator estimator)
        {
            AddRuntimeMessage(
                GH_RuntimeMessageLevel.Warning,
                "Token Threshold needs a synchronous estimator (Heuristic or Tiktoken). The Anthropic, Gemini, and LlamaCpp estimators count asynchronously and cannot be used here.");
            EmitRouted(DA);
            return;
        }

        int threshold = 0;
        DA.GetData(InThreshold, ref threshold);

        // Route each newly-arrived signal by the token size of the Instructions it carries.
        foreach (ConsumedSignal item in ConsumeAllSignals(InSignal))
        {
            _lastTokens = item.Signal.Instructions is Instructions instructions
                ? estimator.Estimate(instructions)
                : 0;

            if (_lastTokens > threshold)
            {
                _overSignal = item.Signal;
            }
            else
            {
                _underSignal = item.Signal;
            }
        }

        Message = $"{_lastTokens} / {threshold}";
        OnDisplayExpired(true);

        EmitRouted(DA);
        DA.SetData(OutTokenCount, _lastTokens);
    }

    /// <inheritdoc/>
    protected override void OnCleared()
    {
        _underSignal = null;
        _overSignal = null;
        _lastTokens = 0;
    }

    private void EmitRouted(IGH_DataAccess da)
    {
        EmitSignal(da, OutUnder, _underSignal);
        EmitSignal(da, OutOver, _overSignal);
    }
}
