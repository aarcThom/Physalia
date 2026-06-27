// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using Grasshopper.Kernel;
using Physalia.Core.ConvoInstruct;
using Physalia.Core.Tokens;
using Physalia.GH.Goo;
using Physalia.GH.Parameters;

namespace Physalia.GH.Components;

/// <summary>
/// Auto-trigger gate for compaction: estimates the token count of the active context and mints a
/// Signal once each time the count crosses a threshold upward. Wire its Signal into a compaction
/// component's Signal input so compaction fires automatically when the conversation grows past the
/// budget — the research-recommended "compact at ~70–95% of the window" behaviour — rather than on a
/// manual button.
///
/// <para>The fire is edge-triggered: it mints a fresh signal only on a below→at/above transition, so
/// a context that sits over the threshold does not re-fire every solve (and the compaction it
/// triggers, which brings the count back down, re-arms the gate for the next crossing). The first
/// solve only baselines the current state — a freshly loaded or pasted over-budget component does not
/// auto-fire. Needs a synchronous token estimator (Heuristic or Tiktoken); the API-backed estimators
/// count asynchronously and are rejected.</para>
/// </summary>
public class TokenThreshold : StatefulComponentBase
{
    private const int InData = 0;
    private const int InEstimator = 1;
    private const int InThreshold = 2;

    private const int OutSignal = 0;
    private const int OutTokenCount = 1;

    private bool _observed;
    private bool _wasOver;

    /// <summary>
    /// Initializes a new instance of the <see cref="TokenThreshold"/> class.
    /// </summary>
    public TokenThreshold()
        : base(
            "Token Threshold",
            "TokGate",
            "Fires a Signal each time the estimated token count crosses a threshold upward. Wire to a compaction component's Signal input to auto-trigger compaction.",
            "Compaction")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("02342020-637B-43CB-92A0-5A8DA63B025C");

    /// <inheritdoc/>
    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddParameter(new Param_Instructions(), "Instructions", "I", "The instructions to measure — typically a Recorder's Instructions (the live context sent to the model, system prompt included).", GH_ParamAccess.item);
        pManager.AddParameter(new Param_ITokenEstimator(), "Tokenization Technique", "T", "A synchronous token estimator (Heuristic or Tiktoken) used to measure the context.", GH_ParamAccess.item);
        pManager.AddIntegerParameter("Threshold", "N", "Fire when the estimated token count reaches this value (e.g. ~80% of the model's context limit).", GH_ParamAccess.item, 8000);
    }

    /// <inheritdoc/>
    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddParameter(new Param_Signal(), "Signal", "S", "Latched signal minted once each time the token count crosses the threshold upward. Downstream components consume it exactly once.", GH_ParamAccess.item);
        pManager.AddIntegerParameter("Token Count", "N", "The current estimated token count of the input.", GH_ParamAccess.item);
    }

    /// <inheritdoc/>
    protected override void SolveInstance(IGH_DataAccess DA)
    {
        var estimatorGoo = new GH_ITokenEstimator();
        if (!DA.GetData(InEstimator, ref estimatorGoo) || estimatorGoo.Value is not ITokenEstimator estimator)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Connect a token estimator (a Tokenization Technique).");
            return;
        }

        if (estimator is AsyncMarkerTokenEstimator)
        {
            AddRuntimeMessage(
                GH_RuntimeMessageLevel.Warning,
                "Token Threshold needs a synchronous estimator (Heuristic or Tiktoken). The Anthropic, Gemini, and LlamaCpp estimators count asynchronously and cannot be used here.");
            return;
        }

        var instrGoo = new GH_Instructions();
        if (!DA.GetData(InData, ref instrGoo) || instrGoo.Value is not Instructions instructions)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Connect a Recorder's Instructions output.");
            return;
        }

        int threshold = 0;
        DA.GetData(InThreshold, ref threshold);

        int tokens = estimator.Estimate(instructions);
        bool isOver = tokens >= threshold;

        if (!_observed)
        {
            // Baseline: never auto-fire on a fresh, pasted, or reloaded component, even if it
            // opens already over the threshold.
            _observed = true;
        }
        else if (isOver && !_wasOver)
        {
            // Rising edge across the threshold: mint a fresh signal so a downstream compactor fires
            // exactly once. The payload is a trace only — the compactor reads the conversation from
            // its own typed input.
            LatchSuccess($"Token threshold reached: {tokens} ≥ {threshold}");
        }

        _wasOver = isOver;

        Message = $"{tokens} / {threshold}";
        OnDisplayExpired(true);

        EmitSignal(DA, OutSignal, SuccessSignal);
        DA.SetData(OutTokenCount, tokens);
    }

    /// <inheritdoc/>
    protected override void OnCleared()
    {
        _observed = false;
        _wasOver = false;
    }
}
