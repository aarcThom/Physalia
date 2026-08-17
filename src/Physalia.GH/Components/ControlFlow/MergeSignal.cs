// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper.Kernel;
using Physalia.Core.Common;
using Physalia.Core.ConvoInstruct;
using Physalia.Core.Signals;
using Physalia.GH.Parameters;

namespace Physalia.GH.Components;

/// <summary>
/// Joins two or more signal branches into one. Each wired input holds the newest signal it has
/// received; once <em>every</em> wired input is holding one, the component mints a single merged
/// signal and starts the next round empty.
///
/// <para>It is a join, not a passthrough: parallel branches in a Physalia pipeline latch on their
/// own scheduled solves, so they usually arrive in different solutions. Emitting per solve would
/// therefore produce one signal per branch — and, downstream of a Conversation Log, one logged turn
/// per branch. Waiting for the whole set is what makes several reports (say a Runtime Health Check
/// and a Geometry Report) reach the model as ONE message.</para>
///
/// <para>The consequence is deliberate: a round in which a wired branch never fires leaves the
/// component parked, and its caption says so (<c>1 / 2</c>). Wire only branches that fire together,
/// and use <c>Clear Outputs</c> to abandon a parked round. Merging is by global sequence order —
/// causal order — never by arrival timing: payloads are joined oldest-first (blank line between,
/// blank payloads skipped), content blocks are concatenated in the same order, the newest
/// Instructions win, and the outcome is Failure if any merged signal failed.</para>
///
/// <para>Inputs are added and removed at the END only (the zoomable +/- icons on the last slot).
/// Consume-once bookkeeping and the per-input hold are keyed by parameter index, so allowing an
/// insertion in the middle would shift both and could replay or swallow an event.</para>
/// </summary>
public class MergeSignal : StatefulComponentBase, IGH_VariableParameterComponent
{
    /// <summary>Number of signal inputs a fresh component starts with, and the minimum it keeps.</summary>
    private const int MinInputs = 2;

    private const int OutSignal = 0;

    // Newest unconsumed signal per input index, held until the whole wired set is in. Session-only,
    // never serialised — like every other piece of signal state.
    private readonly Dictionary<int, PhySignal> _held = new();

    private int _mergedCount;

    /// <summary>
    /// Initializes a new instance of the <see cref="MergeSignal"/> class.
    /// </summary>
    public MergeSignal()
        : base("Merge Signal", "Merge", "Merges two or more signals into one. Waits until every wired input has a signal, then emits a single signal carrying all of their content.", "Control Flow")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("3E9B7C41-6A25-4F0D-8B7E-2C4A1D9F5B60");

    /// <inheritdoc/>
    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        for (int i = 0; i < MinInputs; i++)
        {
            pManager.AddParameter(NewSignalInput(i));
            pManager[i].Optional = true;
        }
    }

    /// <inheritdoc/>
    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddParameter(new Param_Signal(), "Signal", "S", "One signal carrying the merged content of the whole input set: payloads joined in sequence order, content blocks concatenated, newest Instructions kept. Latched until the next round.", GH_ParamAccess.item);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Only at the very end: the per-input hold and the base's consume-once marks are keyed by
    /// parameter index, and an insertion in the middle would shift every index above it.
    /// </remarks>
    public bool CanInsertParameter(GH_ParameterSide side, int index) =>
        side == GH_ParameterSide.Input && index == Params.Input.Count;

    /// <inheritdoc/>
    /// <remarks>Only the last input, and never below <see cref="MinInputs"/> — the same index-stability reason.</remarks>
    public bool CanRemoveParameter(GH_ParameterSide side, int index) =>
        side == GH_ParameterSide.Input && Params.Input.Count > MinInputs && index == Params.Input.Count - 1;

    /// <inheritdoc/>
    public IGH_Param CreateParameter(GH_ParameterSide side, int index) => NewSignalInput(index);

    /// <inheritdoc/>
    public bool DestroyParameter(GH_ParameterSide side, int index) => true;

    /// <inheritdoc/>
    public void VariableParameterMaintenance()
    {
        // Renumber every input so the names stay S1..Sn after an add or remove, and drop any hold
        // left behind by a removed input (its index no longer exists).
        for (int i = 0; i < Params.Input.Count; i++)
        {
            IGH_Param param = Params.Input[i];
            param.Name = InputName(i);
            param.NickName = InputNick(i);
            param.Description = InputDescription;
            param.Access = GH_ParamAccess.list;
            param.Optional = true;
        }

        foreach (int stale in _held.Keys.Where(i => i >= Params.Input.Count).ToList())
        {
            _held.Remove(stale);
        }
    }

    /// <inheritdoc/>
    protected override void SolveInstance(IGH_DataAccess DA)
    {
        int[] inputs = Enumerable.Range(0, Params.Input.Count).ToArray();

        // Observe every solve so nothing is lost while a round is part-collected.
        ObserveSignalInputs(DA, inputs);

        // Latest wins per input: two events from one branch inside a single round supersede.
        foreach (ConsumedSignal item in ConsumeAllSignals(inputs))
        {
            _held[item.ParamIndex] = item.Signal;
        }

        // Unwired inputs are not part of the set — a spare slot must not park the join forever.
        var wired = inputs.Where(i => Params.Input[i].SourceCount > 0).ToList();
        bool complete = wired.Count > 0 && wired.All(_held.ContainsKey);

        if (complete)
        {
            Merge(wired);
        }
        else if (_held.Count > 0)
        {
            // Part-collected: say how far along the round is, overriding the last round's caption.
            Message = $"{_held.Count} / {wired.Count}";
            OnDisplayExpired(true);
        }

        EmitSignal(DA, OutSignal, SuccessSignal);
    }

    /// <inheritdoc/>
    protected override string MessageForState(SolveState state) => state switch
    {
        SolveState.SolveSuccess => $"Merged {_mergedCount}",
        _ => base.MessageForState(state),
    };

    /// <inheritdoc/>
    protected override void OnCleared()
    {
        _held.Clear();
        _mergedCount = 0;
    }

    /// <summary>
    /// Mints one signal from the whole held set and opens the next round. Order is the global
    /// sequence — causal order — so the merged payload reads cause-before-consequence regardless
    /// of which branch happened to solve first.
    /// </summary>
    /// <param name="wired">Indices of the wired inputs making up this round.</param>
    private void Merge(IReadOnlyList<int> wired)
    {
        List<PhySignal> parts = wired.Select(i => _held[i]).OrderBy(s => s.Sequence).ToList();

        string payload = string.Join(
            Environment.NewLine + Environment.NewLine,
            parts.Select(s => s.Payload).Where(StringHelpers.IsNonBlank));

        // Content blocks survive the merge in the same order (a tool_result's id, an inline image).
        List<MessageContent> blocks = parts.SelectMany(s => s.ContentBlocks).ToList();

        // Instructions are a whole inference context, not something that concatenates: the newest
        // one wins, which is the one built from the most complete conversation.
        Instructions? instructions = parts.LastOrDefault(s => s.Instructions is not null)?.Instructions;

        SignalOutcome outcome = parts.Any(s => s.Outcome == SignalOutcome.Failure)
            ? SignalOutcome.Failure
            : SignalOutcome.Success;

        _mergedCount = parts.Count;
        _held.Clear();

        LatchSuccess(payload, emitSignal: true, outcome: outcome, contentBlocks: blocks.Count > 0 ? blocks : null, instructions: instructions);
    }

    private static string InputName(int index) => $"Signal {index + 1}";

    private static string InputNick(int index) => $"S{index + 1}";

    private static string InputDescription =>
        "One branch of the join. The merged signal is emitted once this and every other wired input is holding a signal; an unwired input is ignored.";

    private static Param_Signal NewSignalInput(int index) => new()
    {
        Name = InputName(index),
        NickName = InputNick(index),
        Description = InputDescription,
        Access = GH_ParamAccess.list,
        Optional = true,
    };
}
