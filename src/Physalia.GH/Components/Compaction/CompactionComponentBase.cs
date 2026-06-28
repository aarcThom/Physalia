// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using Grasshopper.Kernel;
using Physalia.Core.Compaction;
using Physalia.Core.ConvoInstruct;
using Physalia.Core.Signals;

namespace Physalia.GH.Components;

/// <summary>
/// Base for the deterministic compaction components. Each is an inline forward-path
/// <see cref="RoutingComponentBase{TData}"/>: it consumes a Recorder's Signal — which <b>carries the
/// full Instructions</b> — compacts the conversation, and re-emits a Signal carrying the compacted
/// Instructions on its <b>Success Signal</b>, wired straight to the Reasoner
/// (<c>Recorder → Compactor → Reasoner</c>). No loop-back: the Recorder stays the uncompacted source
/// of truth; the compactor only transforms the copy on the signal.
///
/// <para>The <see cref="Instructions"/> carry the system prompt + conversation: the system prompt is
/// <b>always included</b> when measuring a token budget but is <b>never compacted</b> — only the
/// conversation is — and the compacted Instructions re-attach the original system prompt.</para>
///
/// <para>Subclasses implement the pure transform in <see cref="Compact"/>; the base owns reading the
/// Instructions off the consumed signal, the trigger contract, error routing, and minting the carrying
/// signal.</para>
/// </summary>
public abstract class CompactionComponentBase : RoutingComponentBase<Instructions>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CompactionComponentBase"/> class in the
    /// Compaction sub-category.
    /// </summary>
    /// <param name="name">Component display name.</param>
    /// <param name="nickname">Component nickname.</param>
    /// <param name="description">Component description.</param>
    protected CompactionComponentBase(string name, string nickname, string description)
        : base(name, nickname, description, "Compaction")
    {
    }

    /// <inheritdoc/>
    protected sealed override void RegisterAdditionalInputs(GH_InputParamManager pManager)
    {
        // No typed Instructions input: the Instructions ride on the consumed signal. Subclass params
        // start at index 0; the base-owned Signal trigger is appended last by RoutingComponentBase.
        RegisterCompactionInputs(pManager);
    }

    /// <summary>
    /// Registers the subclass's own inputs (starting at index 0), before the base-owned Signal
    /// trigger. Default implementation adds nothing.
    /// </summary>
    /// <param name="pManager">The input parameter manager.</param>
    protected virtual void RegisterCompactionInputs(GH_InputParamManager pManager)
    {
    }

    /// <summary>
    /// Performs the pure compaction on the conversation inside <paramref name="instructions"/>,
    /// leaving the system prompt untouched. Read the subclass's own inputs from <paramref name="da"/>.
    /// Return null to fail the run with a warning (e.g. a missing input).
    /// </summary>
    /// <param name="instructions">The source instructions (system prompt + conversation) from the consumed signal.</param>
    /// <param name="da">The data access for the current solve.</param>
    /// <returns>The compaction result, or null to route a failure.</returns>
    protected abstract CompactionResult? Compact(Instructions instructions, IGH_DataAccess da);

    /// <inheritdoc/>
    /// <remarks>
    /// The Instructions to compact ride on the consumed signal itself (the Recorder mints a signal
    /// carrying them) — the trigger IS the data, exactly as the Reasoner now reads it.
    /// </remarks>
    protected sealed override bool TryGetData(PhySignal signal, IGH_DataAccess da, out Instructions data)
    {
        data = default!;
        if (signal.Instructions is not Instructions instructions)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Signal carried no Instructions — wire a Recorder into this input.");
            return false;
        }

        data = instructions;
        return true;
    }

    /// <inheritdoc/>
    /// <remarks>Deterministic and synchronous — there is nothing to push before the read pass.</remarks>
    protected override void PushSolve(Instructions data, IGH_DataAccess da)
    {
    }

    /// <inheritdoc/>
    protected sealed override RoutingResult ReadSolve(Instructions data, IGH_DataAccess da)
    {
        CompactionResult? result;
        try
        {
            result = Compact(data, da);
        }
        catch (Exception ex)
        {
            return RoutingResult.Fail(ex.Message, ex.Message, GH_RuntimeMessageLevel.Error);
        }

        if (result is null)
        {
            return RoutingResult.Fail("Compaction could not run; see the component warning.");
        }

        return Succeed(data, result);
    }

    /// <summary>
    /// Builds the success routing result that carries the compacted Instructions (original system
    /// prompt + compacted conversation) on the minted Success Signal, forwarded to the Reasoner.
    /// </summary>
    /// <param name="source">The source instructions, for the preserved system prompt.</param>
    /// <param name="result">The compaction result.</param>
    /// <returns>A success <see cref="RoutingResult"/> carrying the compacted Instructions.</returns>
    private static RoutingResult Succeed(Instructions source, CompactionResult result)
    {
        string trace = $"Compacted {result.OriginalMessageCount} → {result.RetainedMessageCount} messages";
        return RoutingResult.Ok(
            trace,
            instructions: new Instructions(source.SystemPrompt, result.Conversation),
            message: $"{trace} ({result.DroppedMessageCount} dropped).",
            level: GH_RuntimeMessageLevel.Remark);
    }
}
