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
            "Shortens the conversation to its most recent turns and drops the rest. The bluntest trim there is, and the cheapest — nothing is sent anywhere to do it.")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("D731E821-323F-47FB-90AF-F5D0D7B1099B");

    /// <inheritdoc/>
    protected override string SignalInputDescription =>
        "The conversation to cut back to its recent turns, riding on a Conversation Log's signal. Usually reached from a Token Threshold's Over Limit output.";

    /// <inheritdoc/>
    protected override string SignalOutputDescription =>
        "The shortened conversation, ready for the LLM Call. If the trim cannot be done, the conversation goes on in full rather than the turn being lost.";

    /// <inheritdoc/>
    protected override void RegisterCompactionInputs(GH_InputParamManager pManager)
    {
        pManager.AddIntegerParameter(
            "Max Messages",
            "N",
            "How many of the most recent turns to keep. Everything older is dropped.",
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
