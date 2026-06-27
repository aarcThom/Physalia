// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using Grasshopper.Kernel;
using Physalia.Core.Compaction;
using Physalia.Core.ConvoInstruct;
using Physalia.Core.Tokens;
using Physalia.GH.Goo;
using Physalia.GH.Parameters;

namespace Physalia.GH.Components;

/// <summary>
/// Token-budget window: drops the oldest messages until the estimated token count of the
/// remaining conversation (plus the system prompt) fits within a budget, keeping the recent
/// tail. This is the sliding window expressed in the unit that actually matters — tokens — so a
/// few long turns and many short ones are bounded the same way. Deterministic; no LLM call, but
/// it needs a synchronous token estimator (Heuristic or Tiktoken). The API-backed estimators
/// (Anthropic, Gemini, LlamaCpp) count asynchronously and are rejected here.
/// </summary>
public class TokenWindow : CompactionComponentBase
{
    private const int InEstimator = 1;
    private const int InSystemPrompt = 2;
    private const int InMaxTokens = 3;

    /// <summary>
    /// Initializes a new instance of the <see cref="TokenWindow"/> class.
    /// </summary>
    public TokenWindow()
        : base(
            "Token Window",
            "TokWin",
            "Keeps the most recent messages of a conversation that fit within a token budget. Deterministic; needs a synchronous token estimator.")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("82B8ED80-8433-490F-9037-9F338B4CD253");

    /// <inheritdoc/>
    protected override void RegisterCompactionInputs(GH_InputParamManager pManager)
    {
        pManager.AddParameter(
            new Param_ITokenEstimator(),
            "Tokenization Technique",
            "T",
            "A synchronous token estimator (Heuristic or Tiktoken) used to measure the budget.",
            GH_ParamAccess.item);
        pManager.AddTextParameter(
            "System Prompt",
            "S",
            "The system prompt counted against the budget. Optional.",
            GH_ParamAccess.item,
            string.Empty);
        pManager.AddIntegerParameter(
            "Max Tokens",
            "N",
            "The token budget the kept conversation (plus system prompt) must fit within.",
            GH_ParamAccess.item,
            8000);
        pManager[InSystemPrompt].Optional = true;
    }

    /// <inheritdoc/>
    protected override CompactionResult? Compact(Conversation conversation, IGH_DataAccess da)
    {
        var estimatorGoo = new GH_ITokenEstimator();
        if (!da.GetData(InEstimator, ref estimatorGoo) || estimatorGoo.Value is not ITokenEstimator estimator)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Connect a token estimator (a Tokenization Technique).");
            return null;
        }

        if (estimator is AsyncMarkerTokenEstimator)
        {
            AddRuntimeMessage(
                GH_RuntimeMessageLevel.Warning,
                "Token Window needs a synchronous estimator (Heuristic or Tiktoken). The Anthropic, Gemini, and LlamaCpp estimators count asynchronously and cannot be used here.");
            return null;
        }

        string systemPrompt = string.Empty;
        da.GetData(InSystemPrompt, ref systemPrompt);

        int maxTokens = 8000;
        da.GetData(InMaxTokens, ref maxTokens);

        return ConversationCompactor.KeepWithinTokenBudget(conversation, systemPrompt ?? string.Empty, estimator, maxTokens);
    }
}
