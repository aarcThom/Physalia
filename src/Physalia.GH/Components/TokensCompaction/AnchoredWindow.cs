// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using Grasshopper.Kernel;
using Physalia.Core.Compaction;
using Physalia.Core.ConvoInstruct;

namespace Physalia.GH.Components;

/// <summary>
/// Anchored head+tail window: keeps the first K messages (the initial task and context the
/// model must not forget) and the last M messages (the live working set), dropping the middle.
/// Motivated by the "lost in the middle" effect — models attend most reliably to the start and
/// end of a context — so this preserves exactly the privileged positions. Deterministic; no LLM
/// call. When the kept head and tail meet at the same role they merge into one turn.
/// </summary>
public class AnchoredWindow : CompactionComponentBase
{
    private const int InKeepFirst = 0;
    private const int InKeepLast = 1;

    /// <summary>
    /// Initializes a new instance of the <see cref="AnchoredWindow"/> class.
    /// </summary>
    public AnchoredWindow()
        : base(
            "Anchored Window",
            "Anchor",
            "Keeps the first K and last M messages of a conversation and drops the middle. Deterministic; no LLM call.")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("ABA68278-2EA0-4D1A-B5D9-7E91CCC702D6");

    /// <inheritdoc/>
    protected override void RegisterCompactionInputs(GH_InputParamManager pManager)
    {
        pManager.AddIntegerParameter(
            "Keep First",
            "K",
            "How many leading messages to keep (the initial task / context).",
            GH_ParamAccess.item,
            2);
        pManager.AddIntegerParameter(
            "Keep Last",
            "M",
            "How many trailing messages to keep (the recent working set).",
            GH_ParamAccess.item,
            8);
    }

    /// <inheritdoc/>
    protected override CompactionResult Compact(Instructions instructions, IGH_DataAccess da)
    {
        int keepFirst = 2;
        int keepLast = 8;
        da.GetData(InKeepFirst, ref keepFirst);
        da.GetData(InKeepLast, ref keepLast);
        return ConversationCompactor.KeepHeadAndTail(instructions.Conversation, keepFirst, keepLast);
    }
}
