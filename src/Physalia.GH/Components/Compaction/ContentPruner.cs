// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using Grasshopper.Kernel;
using Physalia.Core.Compaction;
using Physalia.Core.ConvoInstruct;

namespace Physalia.GH.Components;

/// <summary>
/// Selective content pruning: removes or shortens the bulky, low-value parts of a conversation
/// (images, tool exchanges, auto-generated feedback, over-long tool output or text) while keeping
/// the conversational thread intact. Targets the biggest token sinks — stale tool results above
/// all — without forgetting whole turns. Deterministic; no LLM call. Tool pairing and role
/// alternation are repaired after pruning, so the output is always valid for replay.
/// </summary>
public class ContentPruner : CompactionComponentBase
{
    private const int InDropImages = 0;
    private const int InDropTools = 1;
    private const int InDropFeedback = 2;
    private const int InMaxToolResultChars = 3;
    private const int InMaxTextChars = 4;

    /// <summary>
    /// Initializes a new instance of the <see cref="ContentPruner"/> class.
    /// </summary>
    public ContentPruner()
        : base(
            "Content Pruner",
            "Prune",
            "Drops or truncates selected content (images, tool exchanges, feedback, over-long output) from a conversation. Deterministic; no LLM call.")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("EE741363-71D5-411A-AB19-51D58BF1D4FC");

    /// <inheritdoc/>
    protected override void RegisterCompactionInputs(GH_InputParamManager pManager)
    {
        pManager.AddBooleanParameter("Drop Images", "I", "Remove image content blocks.", GH_ParamAccess.item, false);
        pManager.AddBooleanParameter("Drop Tool Exchanges", "X", "Remove tool_use requests and their tool_result blocks.", GH_ParamAccess.item, false);
        pManager.AddBooleanParameter("Drop Feedback", "F", "Remove auto-generated feedback turns.", GH_ParamAccess.item, false);
        pManager.AddIntegerParameter("Max Tool Result Chars", "TR", "Truncate tool results longer than this many characters. 0 disables.", GH_ParamAccess.item, 0);
        pManager.AddIntegerParameter("Max Text Chars", "TX", "Truncate text blocks longer than this many characters. 0 disables.", GH_ParamAccess.item, 0);
    }

    /// <inheritdoc/>
    protected override CompactionResult Compact(Instructions instructions, IGH_DataAccess da)
    {
        bool dropImages = false;
        bool dropTools = false;
        bool dropFeedback = false;
        int maxToolResultChars = 0;
        int maxTextChars = 0;

        da.GetData(InDropImages, ref dropImages);
        da.GetData(InDropTools, ref dropTools);
        da.GetData(InDropFeedback, ref dropFeedback);
        da.GetData(InMaxToolResultChars, ref maxToolResultChars);
        da.GetData(InMaxTextChars, ref maxTextChars);

        var options = new PruneOptions
        {
            DropImages = dropImages,
            DropToolExchanges = dropTools,
            DropFeedbackTurns = dropFeedback,
            MaxToolResultChars = maxToolResultChars > 0 ? maxToolResultChars : null,
            MaxTextChars = maxTextChars > 0 ? maxTextChars : null,
        };

        return ConversationCompactor.Prune(instructions.Conversation, options);
    }
}
