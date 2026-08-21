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
    private const int InEstimator = 0;
    private const int InMaxTokens = 1;

    /// <summary>
    /// Initializes a new instance of the <see cref="TokenWindow"/> class.
    /// </summary>
    public TokenWindow()
        : base(
            "Token Window",
            "TokWin",
            "Shortens the conversation to as many recent turns as will fit a token budget. A Sliding Window counts turns; this one measures them, which is the honest way to hit a context limit.")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("82B8ED80-8433-490F-9037-9F338B4CD253");

    /// <inheritdoc/>
    protected override string SignalInputDescription =>
        "The conversation to measure and cut back, riding on a Conversation Log's signal. Usually reached from a Token Threshold's Over Limit output.";

    /// <inheritdoc/>
    protected override string SignalOutputDescription =>
        "As much of the recent conversation as fits the budget, ready for the LLM Call. If the measuring cannot be done, the conversation goes on in full rather than the turn being lost.";

    /// <inheritdoc/>
    protected override void RegisterCompactionInputs(GH_InputParamManager pManager)
    {
        pManager.AddParameter(
            new Param_ITokenEstimator(),
            "Tokenization Technique",
            "T",
            "How the turns are added up as they are kept. It has to be one of the local methods — Heuristic or Tiktoken — because a provider round trip cannot happen part-way through a solve.",
            GH_ParamAccess.item);
        pManager.AddIntegerParameter(
            "Max Tokens",
            "N",
            "The budget the kept turns must fit inside. The system prompt is counted against it but never dropped, so a very large prompt leaves less room for history.",
            GH_ParamAccess.item,
            8000);
    }

    /// <inheritdoc/>
    protected override CompactionResult? Compact(Instructions instructions, IGH_DataAccess da)
    {
        var estimatorGoo = new GH_ITokenEstimator();
        if (!da.GetData(InEstimator, ref estimatorGoo) || estimatorGoo.Value is null)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Connect a token estimator (a Tokenization Technique).");
            return null;
        }

        if (estimatorGoo.Value is not ISyncTokenEstimator estimator)
        {
            AddRuntimeMessage(
                GH_RuntimeMessageLevel.Warning,
                "Token Window needs a synchronous estimator (Heuristic or Tiktoken). The Anthropic, Gemini, and LlamaCpp estimators count asynchronously and cannot be used here.");
            return null;
        }

        int maxTokens = 8000;
        da.GetData(InMaxTokens, ref maxTokens);

        // The system prompt rides in on Instructions: always counted toward the budget, never compacted.
        return ConversationCompactor.KeepWithinTokenBudget(instructions.Conversation, instructions.SystemPrompt.Text, estimator, maxTokens);
    }
}
