// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using Grasshopper.Kernel;
using Physalia.Core.Compaction;
using Physalia.Core.ConvoInstruct;

namespace Physalia.GH.Components;

/// <summary>
/// Sliding recency window: keeps the most recent N messages and drops the rest. The cheapest,
/// fully deterministic compaction — no LLM call. Tool pairs and role alternation in the kept
/// tail are repaired automatically, so the output is always valid for replay.
/// </summary>
public class SlidingWindow : CompactionComponentBase
{
    private const int InMaxMessages = 0;

    /// <summary>
    /// Initializes a new instance of the <see cref="SlidingWindow"/> class.
    /// </summary>
    public SlidingWindow()
        : base(
            "Sliding Window",
            "Window",
            "Keeps the most recent N messages of a conversation and drops older ones. Deterministic; no LLM call.")
    {
    }

    /// <inheritdoc/>
    public override GH_Exposure Exposure => GH_Exposure.quinary;

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("D731E821-323F-47FB-90AF-F5D0D7B1099B");

    /// <inheritdoc/>
    protected override void RegisterCompactionInputs(GH_InputParamManager pManager)
    {
        pManager.AddIntegerParameter(
            "Max Messages",
            "N",
            "How many of the most recent messages to keep.",
            GH_ParamAccess.item,
            10);
    }

    /// <inheritdoc/>
    protected override CompactionResult Compact(Instructions instructions, IGH_DataAccess da)
    {
        int maxMessages = 10;
        da.GetData(InMaxMessages, ref maxMessages);
        return ConversationCompactor.KeepRecentMessages(instructions.Conversation, maxMessages);
    }
}
