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
/// <see cref="RoutingComponentBase{TData}"/>: it consumes a Conversation Log's Signal — which <b>carries the
/// full Instructions</b> — compacts the conversation, and re-emits a Signal carrying the compacted
/// Instructions on its single <b>Signal</b> output, wired straight to the LLM Call
/// (<c>Conversation Log → Compactor → LLM Call</c>). No loop-back: the Conversation Log stays the uncompacted source
/// of truth; the compactor only transforms the copy on the signal.
///
/// <para>The <see cref="Instructions"/> carry the system prompt + conversation: the system prompt is
/// <b>always included</b> when measuring a token budget but is <b>never compacted</b> — only the
/// conversation is — and the compacted Instructions re-attach the original system prompt.</para>
///
/// <para>Subclasses implement the pure transform in <see cref="Compact"/>; the base owns reading the
/// Instructions off the consumed signal, the trigger contract, the fail-open policy, and minting the
/// carrying signal.</para>
///
/// <para><b>Compaction fails open</b>, on a single Signal output. It is an optimisation: the whole
/// conversation is already on the signal, so a compactor that cannot run forwards it UNCOMPACTED with
/// a warning, and the cost is a longer prompt rather than a lost turn. A Fail route could not have
/// been wired anywhere useful — a failure signal carries a feedback string and no Instructions, so
/// forward to the LLM Call it is dropped as "Signal carried no Instructions" (misleading, since one
/// IS wired), back through Feedback it lands "Compaction could not run" in front of the model as a
/// user turn it cannot act on, and unwired — the realistic case — the loop simply stalls with nothing
/// but a component warning to explain it. Every reachable failure here is a canvas setup mistake (an
/// unwired token estimator, an async estimator where a sync one is required) that the component
/// already reports on itself, so there is no runtime condition for a signal to carry.</para>
/// </summary>
public abstract class CompactionComponentBase : RoutingComponentBase<Instructions>
{
    /// <inheritdoc/>
    /// <remarks>
    /// Sealed: the fail-open contract is uniform across compactors, and it stays right even for a
    /// future compactor that calls an LLM to summarise — a network failure there should still cost
    /// a longer prompt, not the turn.
    /// </remarks>
    protected sealed override bool HasFailOutput => false;

    /// <summary>
    /// Initializes a new instance of the <see cref="CompactionComponentBase"/> class in the
    /// Compaction sub-category.
    /// </summary>
    /// <param name="name">Component display name.</param>
    /// <param name="nickname">Component nickname.</param>
    /// <param name="description">Component description.</param>
    protected CompactionComponentBase(string name, string nickname, string description)
        : base(name, nickname, description, "Tokens & Compaction")
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
    /// Return null when the compaction cannot run (a missing or unusable input); surface the reason
    /// as a runtime message first, because the base then forwards the conversation UNCOMPACTED
    /// rather than failing the turn.
    /// </summary>
    /// <param name="instructions">The source instructions (system prompt + conversation) from the consumed signal.</param>
    /// <param name="da">The data access for the current solve.</param>
    /// <returns>The compaction result, or null to forward the conversation uncompacted.</returns>
    protected abstract CompactionResult? Compact(Instructions instructions, IGH_DataAccess da);

    /// <inheritdoc/>
    /// <remarks>
    /// The Instructions to compact ride on the consumed signal itself (the Conversation Log mints a signal
    /// carrying them) — the trigger IS the data, exactly as the LLM Call now reads it.
    /// </remarks>
    protected sealed override bool TryGetData(PhySignal signal, IGH_DataAccess da, out Instructions data)
    {
        data = default!;
        if (signal.Instructions is not Instructions instructions)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Signal carried no Instructions — wire a Conversation Log into this input.");
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
            return PassThrough(data, ex.Message, GH_RuntimeMessageLevel.Error);
        }

        return result is null
            ? PassThrough(data, "the component is not configured to run; see its warning above", GH_RuntimeMessageLevel.Warning)
            : Succeed(data, result);
    }

    /// <summary>
    /// The fail-open result: the ORIGINAL Instructions, forwarded intact on the Signal output so the
    /// LLM Call still gets its inference context. The reason rides as a runtime message on the
    /// component, where a canvas setup mistake belongs — never as signal payload aimed at the model,
    /// which cannot wire a token estimator.
    /// </summary>
    /// <param name="source">The instructions that arrived, forwarded unchanged.</param>
    /// <param name="reason">Why compaction did not run.</param>
    /// <param name="level">The level for the runtime message.</param>
    /// <returns>A success <see cref="RoutingResult"/> carrying the uncompacted Instructions.</returns>
    private static RoutingResult PassThrough(Instructions source, string reason, GH_RuntimeMessageLevel level)
    {
        int count = source.Conversation.Count;
        return RoutingResult.Ok(
            $"Not compacted ({reason}); forwarded {count} message(s) unchanged",
            instructions: source,
            message: $"Compaction did not run — {reason}. The conversation was forwarded UNCOMPACTED ({count} message(s)), so the prompt is longer than intended but the turn still runs.",
            level: level);
    }

    /// <summary>
    /// Builds the success routing result that carries the compacted Instructions (original system
    /// prompt + compacted conversation) on the minted Signal, forwarded to the LLM Call.
    /// </summary>
    /// <param name="source">The source instructions, for the preserved system prompt.</param>
    /// <param name="result">The compaction result.</param>
    /// <returns>A success <see cref="RoutingResult"/> carrying the compacted Instructions.</returns>
    private static RoutingResult Succeed(Instructions source, CompactionResult result)
    {
        string trace = $"Compacted {result.OriginalMessageCount} → {result.RetainedMessageCount} messages";
        return RoutingResult.Ok(
            trace,
            // Carry the source tools forward unchanged — compaction shrinks the conversation, not the
            // set of tools advertised to the model, so the LLM Call still sees them past a compactor.
            instructions: new Instructions(source.SystemPrompt, result.Conversation) { Tools = source.Tools },
            message: $"{trace} ({result.DroppedMessageCount} dropped).",
            level: GH_RuntimeMessageLevel.Remark);
    }
}
