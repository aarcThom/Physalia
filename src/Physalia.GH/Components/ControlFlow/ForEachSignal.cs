// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using Grasshopper.Kernel;
using Physalia.Core.Signals;
using Physalia.GH.Parameters;

namespace Physalia.GH.Components;

/// <summary>
/// Walks a list one item at a time, waiting for each round to finish before starting the next. "Do
/// this for each of these" — which a Physalia pipeline could not express at all before: a signal
/// starts one round, and a list of twelve rooms had no way of becoming twelve rounds.
///
/// <para><b>Strictly sequential, and it is a rule rather than a limitation.</b> The pipeline
/// downstream has one Conversation Log, one solve state and one latched signal per wire, so twelve
/// items running at once would interleave into a single conversation and produce twelve answers about
/// each other. The loop therefore advances only when told the previous item is finished, which is
/// what the Next input is: wire the END of the per-item work back into it. For work that genuinely
/// should be independent, give each item a Delegate call — a sub-pipeline has a conversation of its
/// own, which is the actual fix for a shared context.</para>
///
/// <para><b>Index is the output that makes this useful with anything but text.</b> A signal carries
/// text, so the items are text — but the index comes out beside it, and a List Item on the far end
/// picks the matching curve, room, or panel out of a parallel list. That is a smaller idea than a
/// generic per-item wire and it composes with the whole of Grasshopper.</para>
///
/// <para><b>The list is snapshotted when the loop starts.</b> A pipeline that edits the canvas can
/// easily change the very list it is iterating — placing components changes a canvas-state grounding,
/// which changes what is upstream of this — and a loop whose list grows under it never ends. So the
/// items are taken once, at Start, and the loop finishes the list it began with.</para>
/// </summary>
public class ForEachSignal : StatefulComponentBase
{
    private const int InItems = 0;
    private const int InStart = 1;
    private const int InNext = 2;
    private const int InReset = 3;

    private const int OutItemSignal = 0;
    private const int OutDoneSignal = 1;
    private const int OutItem = 2;
    private const int OutIndex = 3;

    private readonly List<string> _snapshot = new();

    private PhySignal? _itemSignal;

    private PhySignal? _doneSignal;

    // −1 means "not running". The index of the item currently out for work otherwise.
    private int _index = -1;

    private bool _running;

    /// <summary>
    /// Initializes a new instance of the <see cref="ForEachSignal"/> class.
    /// </summary>
    public ForEachSignal()
        : base(
            "For Each",
            "ForEach",
            "Sends one signal per item in a list, waiting for each round to finish before the next. Wire the end of the per-item work back into Next, and Done Signal to whatever happens after the list.",
            "Control Flow")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("8E3A0C51-B79D-4267-A4F8-C0125B63E9A7");

    /// <inheritdoc/>
    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddTextParameter(
            "Items",
            "I",
            "The list to walk. Each item becomes one signal's payload. Snapshotted when the loop starts, so a list that changes mid-loop does not extend it.",
            GH_ParamAccess.list);
        pManager.AddParameter(
            new Param_Signal(),
            "Start",
            "S",
            "Starts the loop from the first item. Anything arriving here while a loop is running restarts it from the top.",
            GH_ParamAccess.list);
        pManager.AddParameter(
            new Param_Signal(),
            "Next",
            "N",
            "Says the current item is finished, so the next one may go. Wire the END of the per-item work back in here — that is what keeps the loop sequential.",
            GH_ParamAccess.list);
        pManager.AddBooleanParameter(
            "Reset",
            "R",
            "A press abandons the loop and clears both outputs.",
            GH_ParamAccess.item,
            false);
        pManager[InItems].Optional = true;
        pManager[InStart].Optional = true;
        pManager[InNext].Optional = true;
    }

    /// <inheritdoc/>
    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddParameter(
            new Param_Signal(),
            "Item Signal",
            "IS",
            "Fires once per item, carrying that item's text. Wire into whatever does the per-item work.",
            GH_ParamAccess.item);
        pManager.AddParameter(
            new Param_Signal(),
            "Done Signal",
            "DS",
            "Fires once, after the last item has been reported finished. Wire into whatever happens when the whole list is through.",
            GH_ParamAccess.item);
        pManager.AddTextParameter(
            "Item",
            "I",
            "The item currently out for work, on its own.",
            GH_ParamAccess.item);
        pManager.AddIntegerParameter(
            "Index",
            "#",
            "Which item is out for work, counting from zero. Wire into a List Item to pick the matching geometry out of a parallel list — this is how the loop carries anything that is not text.",
            GH_ParamAccess.item);
    }

    /// <inheritdoc/>
    protected override void SolveInstance(IGH_DataAccess DA)
    {
        var items = new List<string>();
        DA.GetDataList(InItems, items);

        // Observe every solve, including mid-loop: a Next arriving while this is busy waits on the
        // wire and is serviced in causal order.
        ObserveSignalInputs(DA, InStart, InNext);

        if (ObserveButtonPress(DA, InReset))
        {
            Abandon();
        }

        // Global sequence order, which is causal order — so a Start and a Next landing in the same
        // solve are applied in the order they actually happened, and a restart is never overtaken by
        // the completion of the item it replaced.
        foreach (ConsumedSignal consumed in ConsumeAllSignals(InStart, InNext))
        {
            if (consumed.ParamIndex == InStart)
            {
                Begin(items);
            }
            else
            {
                Advance();
            }
        }

        Message = Caption();
        OnDisplayExpired(true);

        EmitSignal(DA, OutItemSignal, _itemSignal);
        EmitSignal(DA, OutDoneSignal, _doneSignal);
        DA.SetData(OutItem, _index >= 0 && _index < _snapshot.Count ? _snapshot[_index] : string.Empty);
        DA.SetData(OutIndex, _index);
    }

    /// <inheritdoc/>
    protected override void OnCleared()
    {
        Abandon();
    }

    private void Begin(List<string> items)
    {
        _snapshot.Clear();
        _snapshot.AddRange(items);
        _doneSignal = null;
        _index = -1;
        _running = true;

        if (_snapshot.Count == 0)
        {
            // An empty list is DONE, not broken. A pipeline that found nothing to work on should carry
            // on to whatever comes after the loop, and stalling here would look like a hung round.
            _running = false;
            _itemSignal = null;
            _doneSignal = PhySignal.Mint(SignalOutcome.Success, "Nothing to do: the list was empty.", InstanceGuid, Name);
            return;
        }

        Advance();
    }

    private void Advance()
    {
        if (!_running)
        {
            // A Next with no loop running — the per-item work fired again, or a stray signal arrived.
            // Ignored rather than treated as a start: starting a loop is the Start input's job, and
            // guessing here would run a list nobody asked for.
            return;
        }

        _index++;

        if (_index >= _snapshot.Count)
        {
            _running = false;
            _itemSignal = null;
            _doneSignal = PhySignal.Mint(
                SignalOutcome.Success,
                $"All {_snapshot.Count.ToString(CultureInfo.InvariantCulture)} items are through.",
                InstanceGuid,
                Name);
            return;
        }

        _itemSignal = PhySignal.Mint(SignalOutcome.Success, _snapshot[_index], InstanceGuid, Name);
    }

    private void Abandon()
    {
        _snapshot.Clear();
        _index = -1;
        _running = false;
        _itemSignal = null;
        _doneSignal = null;
    }

    private string Caption()
    {
        if (_running && _index >= 0)
        {
            return $"{(_index + 1).ToString(CultureInfo.InvariantCulture)} / {_snapshot.Count.ToString(CultureInfo.InvariantCulture)}";
        }

        return _doneSignal is not null && _snapshot.Count > 0
            ? $"done · {_snapshot.Count.ToString(CultureInfo.InvariantCulture)}"
            : string.Empty;
    }
}
