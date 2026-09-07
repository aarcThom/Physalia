// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;

namespace Physalia.GH.Components;

/// <summary>
/// Fires when the data wired into it changes, while armed. The active counterpart of Harness In, and
/// the way a slider, a Rhino reference or an upstream calculation starts a round instead of merely
/// being available to one.
///
/// <para><b>Harness In is passive on purpose, and this is the deliberate exception.</b> Data arriving
/// at a Harness In mints nothing, which is what keeps "Harness Out writes the canvas, the canvas feeds
/// Harness In" from closing into a loop Grasshopper's own cycle detector cannot see — the two ends are
/// in different documents. Arming one of these re-opens exactly that possibility: if what this watches
/// is downstream of anything the pipeline WRITES, every round produces the change that starts the
/// next. Nothing here can detect that, because the cycle runs through the user's canvas and a
/// deliberate re-run looks identical to a runaway. What bounds it is a Signal Limiter on the way out,
/// and the Budget Guard as the backstop; what makes it survivable is that a trigger is never armed
/// when a file opens.</para>
///
/// <para><b>What counts as a change.</b> The tree's shape plus the reference identity of every item on
/// it — see <see cref="TreeIdentity"/>. Exact in the direction that matters: it can report a change
/// for data that recomputed to the same values, and it can never miss data that changed. A dragged
/// slider therefore produces a burst, which the settle window folds into one round when the dragging
/// stops.</para>
///
/// <para>The first observation is a baseline and never fires, so arming this — or a solve that simply
/// happens after arming — does not report data that was already sitting there.</para>
/// </summary>
public class DataTrigger : SignalSourceBase<string>
{
    private const int InData = 0;
    private const int InPayload = 1;
    private const int InSettle = 2;

    private string _key = string.Empty;

    private bool _baselined;

    private string _payload = string.Empty;

    private double _settleSeconds = 1.0;

    private int _items;

    private int _branches;

    /// <summary>
    /// Initializes a new instance of the <see cref="DataTrigger"/> class.
    /// </summary>
    public DataTrigger()
        : base(
            "Data Changed",
            "DataWatch",
            "Starts a round when the data wired into it changes. Right-click and tick Armed to switch it on. Take care what you wire in: if the pipeline itself affects this data, every round will start the next one.")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("A5C93E17-6B84-42F0-9D3A-8E107C25B4D9");

    /// <inheritdoc/>
    protected override string SignalOutputDescription =>
        "Fires when the wired data changes, carrying the Payload text (or a note of what arrived). Wire into a Conversation Log's Prompt Signal input, or into anything else a signal drives.";

    /// <inheritdoc/>
    protected override string ArmedCaption => "on change";

    /// <inheritdoc/>
    /// <remarks>
    /// A dragged slider raises a change per frame, so the window is what turns a drag into one round.
    /// Default a full second because the thing on the other end of this signal costs money.
    /// </remarks>
    protected override int SettleMs => (int)Math.Round(Math.Max(0.05, _settleSeconds) * 1000.0);

    /// <inheritdoc/>
    protected override void RegisterSourceInputs(GH_InputParamManager pManager)
    {
        pManager.AddGenericParameter(
            "Data",
            "D",
            "Whatever should start a round when it changes — a slider, a curve, a number from somewhere upstream. Branch structure is watched as well as contents.",
            GH_ParamAccess.tree);
        pManager.AddTextParameter(
            "Payload",
            "P",
            "The text the signal carries — the standing prompt to send when this changes. Blank sends a plain note saying what arrived.",
            GH_ParamAccess.item,
            string.Empty);
        pManager.AddNumberParameter(
            "Settle",
            "T",
            "Seconds of quiet before firing. Long enough that dragging a slider is one round rather than one per frame.",
            GH_ParamAccess.item,
            1.0);
        pManager[InData].Optional = true;
        pManager[InPayload].Optional = true;
    }

    /// <inheritdoc/>
    protected override void OnSolveTick(IGH_DataAccess da)
    {
        string payload = string.Empty;
        double settle = 1.0;
        da.GetData(InPayload, ref payload);
        da.GetData(InSettle, ref settle);
        _payload = payload ?? string.Empty;
        _settleSeconds = settle;

        var tree = new GH_Structure<IGH_Goo>();
        da.GetDataTree(InData, out GH_Structure<IGH_Goo> incoming);
        if (incoming is not null)
        {
            tree = incoming;
        }

        string key = TreeIdentity.Of(tree);

        // The identity is tracked whether or not the trigger is armed, so arming does not immediately
        // report data that was already on the wire. Only the FIRING is gated on being armed.
        if (!_baselined)
        {
            _baselined = true;
            _key = key;
            Measure(tree);
            return;
        }

        if (key == _key)
        {
            return;
        }

        _key = key;
        Measure(tree);
        ReportEvent(key);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Nothing to subscribe to: the listener is Grasshopper's own solve, which reaches
    /// <see cref="OnSolveTick"/> whenever anything upstream recomputes.
    /// </remarks>
    protected override void StartListening()
    {
    }

    /// <inheritdoc/>
    protected override void StopListening()
    {
    }

    /// <inheritdoc/>
    protected override string ComposePayload(IReadOnlyList<string> events)
    {
        if (!string.IsNullOrWhiteSpace(_payload))
        {
            return _payload;
        }

        string shape = _branches == 1
            ? $"{_items.ToString(CultureInfo.InvariantCulture)} {(_items == 1 ? "item" : "items")}"
            : $"{_items.ToString(CultureInfo.InvariantCulture)} items in {_branches.ToString(CultureInfo.InvariantCulture)} branches";

        return _items == 0
            ? "The wired data changed and is now empty."
            : $"The wired data changed: {shape}.";
    }

    /// <inheritdoc/>
    protected override void OnCleared()
    {
        base.OnCleared();

        // Re-baseline, so a cleared trigger behaves like a freshly placed one rather than firing on
        // whatever is on the wire at the next solve.
        _baselined = false;
        _key = string.Empty;
    }

    private void Measure(GH_Structure<IGH_Goo> tree)
    {
        _branches = tree.PathCount;
        _items = tree.DataCount;
    }
}
