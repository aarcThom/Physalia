// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using Grasshopper.Kernel;
using Physalia.Core.Compaction;
using Physalia.Core.ConvoInstruct;
using Physalia.Core.Signals;
using Physalia.GH.Goo;
using Physalia.GH.Parameters;

namespace Physalia.GH.Components;

/// <summary>
/// Base for the deterministic compaction components. Each routes through
/// <see cref="RoutingComponentBase{TData}"/>: a trigger <c>Signal</c> starts a run, the source
/// <see cref="Conversation"/> is read from a typed input, and the compacted conversation is
/// emitted on the <b>Success Signal</b> (carried on the signal itself, not the payload). Routing —
/// rather than a plain dataflow output — is what lets the result travel back to a Recorder's
/// Conversation input through a <see cref="Feedback"/> → <see cref="FeedbackCollector"/> link,
/// which deliberately breaks Grasshopper's acyclic constraint. A direct wire would be an illegal
/// cycle (the Recorder feeds the compactor, which feeds the Recorder).
///
/// <para>Subclasses implement the pure transform in <see cref="Compact"/>; the base owns the
/// Conversation input, the trigger contract, error routing, and minting the carrying signal.</para>
/// </summary>
public abstract class CompactionComponentBase : RoutingComponentBase<Conversation>
{
    /// <summary>Input index of the source conversation (the first additional input).</summary>
    protected const int InSourceConversation = 0;

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
        pManager.AddParameter(
            new Param_Conversation(),
            "Conversation",
            "C",
            "The conversation history to compact — typically a Recorder's Recorded History output.",
            GH_ParamAccess.item);
        RegisterCompactionInputs(pManager);
    }

    /// <summary>
    /// Registers the subclass's own inputs, appended after the Conversation input and before the
    /// base-owned Signal trigger. Default implementation adds nothing.
    /// </summary>
    /// <param name="pManager">The input parameter manager.</param>
    protected virtual void RegisterCompactionInputs(GH_InputParamManager pManager)
    {
    }

    /// <summary>
    /// Performs the pure compaction. Read the subclass's own inputs from <paramref name="da"/>.
    /// Return null to fail the run with a warning (e.g. a missing input).
    /// </summary>
    /// <param name="conversation">The source conversation from the typed input.</param>
    /// <param name="da">The data access for the current solve.</param>
    /// <returns>The compaction result, or null to route a failure.</returns>
    protected abstract CompactionResult? Compact(Conversation conversation, IGH_DataAccess da);

    /// <inheritdoc/>
    /// <remarks>
    /// The trigger signal just says "go"; the conversation to compact comes from the typed input,
    /// exactly like the Reasoner reading its Instructions input rather than the signal payload.
    /// </remarks>
    protected sealed override bool TryGetData(PhySignal signal, IGH_DataAccess da, out Conversation data)
    {
        data = default!;
        var goo = new GH_Conversation();
        if (!da.GetData(InSourceConversation, ref goo) || goo.Value is not Conversation conversation)
        {
            return false;
        }

        data = conversation;
        return true;
    }

    /// <inheritdoc/>
    /// <remarks>Deterministic and synchronous — there is nothing to push before the read pass.</remarks>
    protected override void PushSolve(Conversation data, IGH_DataAccess da)
    {
    }

    /// <inheritdoc/>
    protected sealed override RoutingResult ReadSolve(Conversation data, IGH_DataAccess da)
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

        return Succeed(result);
    }

    /// <summary>
    /// Builds the success routing result that carries the compacted conversation on the minted
    /// Success Signal. Shared so the asynchronous <see cref="Summarizer"/> reports identically.
    /// </summary>
    /// <param name="result">The compaction result.</param>
    /// <returns>A success <see cref="RoutingResult"/> carrying the compacted conversation.</returns>
    protected RoutingResult Succeed(CompactionResult result)
    {
        string trace = $"Compacted {result.OriginalMessageCount} → {result.RetainedMessageCount} messages";
        return RoutingResult.Ok(
            trace,
            conversation: result.Conversation,
            message: $"{trace} ({result.DroppedMessageCount} dropped).",
            level: GH_RuntimeMessageLevel.Remark);
    }
}
