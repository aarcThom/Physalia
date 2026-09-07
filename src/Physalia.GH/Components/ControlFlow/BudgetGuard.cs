// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Grasshopper.Kernel;
using Physalia.Core.Budget;
using Physalia.Core.ConvoInstruct;
using Physalia.Core.Signals;
using Physalia.Core.Tools;

namespace Physalia.GH.Components;

/// <summary>
/// Caps what a pipeline may spend, and refuses the round that would go past it. Sits inline on the
/// hop from a Conversation Log to an LLM Call, exactly where a compaction component sits.
///
/// <para><b>It is the other half of the trigger tier, not an optional extra.</b> Signal Limiter caps
/// the rounds of one loop and Stall Guard catches a loop repeating itself, but neither bounds a
/// SESSION — and until a timer could start a round, a session was bounded by a person being present.
/// An armed trigger removes that, so something has to say how much an unattended pipeline may spend.
/// This is that thing, and a pipeline with any trigger armed and no guard on it has no upper bound on
/// its bill at all.</para>
///
/// <para><b>Refusing, then offering.</b> Over budget, the round is refused and the reason goes out on
/// Fail Signal — so a loop can say something about it rather than stopping silently. If a chat window
/// is open, the same moment puts a card up offering another slice; allowing it extends the cap and the
/// round goes through. With no window there is nobody to ask, so it simply stops. That is the
/// approval seam's own contract, reused rather than reimplemented: the question genuinely IS an
/// approval — may this pipeline spend more of your money — and every edge of it failing closed is
/// exactly what a budget wants.</para>
///
/// <para><b>The cap is checked before a call and against what is already spent</b>, so a pipeline can
/// overrun by up to one call. See <see cref="SpendPolicy"/> for why that is the right trade: the cost
/// of a call is not knowable until it has been made, and a runaway loop is stopped just as dead one
/// call late.</para>
///
/// <para><b>What it counts is what the LLM Calls in this same harness reported</b> — the two halves
/// find each other through the document rather than a wire (see <see cref="SpendLedger"/>), so this
/// needs no connection to the calls it is bounding, and one guard covers all of them. Session-only:
/// every reopen starts at nothing.</para>
/// </summary>
public class BudgetGuard : RoutingComponentBase<Instructions>
{
    private const int InMaxTokens = 0;
    private const int InMaxCalls = 1;
    private const int InExtension = 2;

    private SpendLimits _limits = SpendLimits.Unlimited;

    // Granted by the user answering the card, and added on top of the typed caps. Held here rather
    // than in the ledger because the caps are this node's business: the ledger only counts.
    private long _extraTokens;

    private long _extraCalls;

    private double _extensionFraction = 0.5;

    // Set by the asking task, read on the read pass it schedules.
    private bool _refused;

    private string _refusal = string.Empty;

    /// <summary>
    /// Initializes a new instance of the <see cref="BudgetGuard"/> class.
    /// </summary>
    public BudgetGuard()
        : base(
            "Budget Guard",
            "Budget",
            "Caps what this pipeline may spend and refuses the round that would go past it. Put one between a Conversation Log and an LLM Call on any pipeline with a trigger armed — nothing else bounds an unattended session.",
            "Control Flow")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("E2B7D410-6C93-4F58-A0E1-38547B9CD62A");

    /// <inheritdoc/>
    protected override string SignalInputDescription =>
        "The signal on its way to the LLM Call, carrying the conversation. Wire a Conversation Log — or a compaction component — in here, and this node's Success Signal on to the LLM Call.";

    /// <inheritdoc/>
    protected override string SignalOutputDescription =>
        "The signal, unchanged, when the round is within budget. Wire into the LLM Call.";

    /// <inheritdoc/>
    protected override string FailSignalDescription =>
        "Fires when the budget is spent and nobody extended it, carrying the reason. Wire it wherever a stopped pipeline should say so — into a Feedback, a Harness Out, or a panel.";

    /// <inheritdoc/>
    /// <remarks>
    /// The card is a human-speed wait, so the check runs off the solve and the read pass is asked for
    /// when it finishes. The same reason the LLM Call itself is async.
    /// </remarks>
    protected override bool AutoScheduleRead => false;

    /// <inheritdoc/>
    protected override void RegisterAdditionalInputs(GH_InputParamManager pManager)
    {
        pManager.AddIntegerParameter(
            "Max Tokens",
            "MT",
            "Total tokens this pipeline may spend in one session, prompt and reply together. Zero means no token cap.",
            GH_ParamAccess.item,
            0);
        pManager.AddIntegerParameter(
            "Max Calls",
            "MC",
            "How many inference calls this pipeline may make in one session. Zero means no call cap. This is the cap that works for a CLI provider on a subscription, which reports no token usage at all.",
            GH_ParamAccess.item,
            0);
        pManager.AddNumberParameter(
            "Extension",
            "E",
            "How much more to allow when you say yes to the card, as a fraction of the cap. 0.5 offers another half; zero offers no extension, so the card never appears and the budget is final.",
            GH_ParamAccess.item,
            0.5);
    }

    /// <inheritdoc/>
    protected override void RegisterAdditionalOutputs(GH_OutputParamManager pManager)
    {
        pManager.AddTextParameter(
            "Spent",
            "S",
            "What this pipeline has spent so far against its caps, as text. Wire into a panel to watch it.",
            GH_ParamAccess.item);
    }

    /// <inheritdoc/>
    protected override void OnSolveTick(IGH_DataAccess da)
    {
        int maxTokens = 0;
        int maxCalls = 0;
        double extension = 0.5;
        da.GetData(InMaxTokens, ref maxTokens);
        da.GetData(InMaxCalls, ref maxCalls);
        da.GetData(InExtension, ref extension);

        _limits = new SpendLimits(
            Math.Max(0, maxTokens) + _extraTokens,
            Math.Max(0, maxCalls) + _extraCalls);

        _extensionFraction = Math.Max(0.0, extension);

        if (_limits.IsUnlimited)
        {
            AddRuntimeMessage(
                GH_RuntimeMessageLevel.Remark,
                "No cap set, so nothing is bounded. Set Max Tokens or Max Calls — a pipeline with a trigger armed and no cap has no upper bound on its bill.");
        }

        // Read live off the ledger every solve, which is what keeps a panel wired here honest between
        // rounds rather than only just after one.
        da.SetData(FirstAdditionalOutputIndex, SpendPolicy.Describe(SpendLedger.Read(OnPingDocument()), _limits));
    }

    /// <inheritdoc/>
    protected override bool TryGetData(PhySignal signal, IGH_DataAccess da, out Instructions data)
    {
        data = default!;

        if (signal.Instructions is not Instructions instructions)
        {
            AddRuntimeMessage(
                GH_RuntimeMessageLevel.Warning,
                "Signal carried no Instructions — wire a Conversation Log (or a compaction component) into this input.");
            return false;
        }

        data = instructions;
        return true;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The whole decision happens here rather than in the read pass, because it may involve asking a
    /// person and that cannot be done from inside a solve.
    /// </remarks>
    protected override void PushSolve(Instructions data, IGH_DataAccess da)
    {
        Spend spent = SpendLedger.Read(OnPingDocument());
        SpendVerdict verdict = SpendPolicy.Check(spent, _limits);

        if (verdict.Allowed)
        {
            _refused = false;
            _refusal = string.Empty;
            RequestReadPass();
            return;
        }

        long extraTokens = _limits.MaxTokens > 0 ? (long)Math.Round(_limits.MaxTokens * _extensionFraction) : 0;
        long extraCalls = _limits.MaxCalls > 0 ? (long)Math.Round(_limits.MaxCalls * _extensionFraction) : 0;

        if (extraTokens <= 0 && extraCalls <= 0)
        {
            // No extension offered: the budget is final by the user's own setting, so there is nothing
            // to ask and asking anyway would be a card whose only useful answer is the one it cannot
            // act on.
            _refused = true;
            _refusal = verdict.Reason ?? "The budget is spent.";
            RequestReadPass();
            return;
        }

        AskForExtensionAsync(verdict, spent, extraTokens, extraCalls);
    }

    /// <inheritdoc/>
    protected override RoutingResult ReadSolve(Instructions data, IGH_DataAccess da)
    {
        if (_refused)
        {
            return RoutingResult.Fail(
                _refusal,
                _refusal,
                GH_RuntimeMessageLevel.Warning);
        }

        // Forwarded unchanged, Instructions and all — the point of sitting on this hop is to be
        // invisible when the answer is yes.
        return RoutingResult.Ok(
            "Within budget.",
            data);
    }

    /// <inheritdoc/>
    public override void AppendAdditionalMenuItems(ToolStripDropDown menu)
    {
        base.AppendAdditionalMenuItems(menu);
        Menu_AppendItem(menu, "Reset Budget", (_, _) =>
        {
            SpendLedger.Reset(OnPingDocument());
            _extraTokens = 0;
            _extraCalls = 0;
            ExpireSolution(true);
        });
    }

    /// <inheritdoc/>
    protected override void OnCleared()
    {
        base.OnCleared();
        _refused = false;
        _refusal = string.Empty;

        // Extensions go, the tally does not: clearing this node's outputs is not a claim about what
        // the pipeline has spent. "Reset Budget" is the verb for that, and it says so.
        _extraTokens = 0;
        _extraCalls = 0;
    }

    private void AskForExtensionAsync(SpendVerdict verdict, Spend spent, long extraTokens, long extraCalls)
    {
        string detail = SpendPolicy.Describe(spent, _limits)
            + Environment.NewLine
            + "Allow another "
            + (extraTokens > 0 ? SpendPolicy.Tokens(extraTokens) + " tokens" : string.Empty)
            + (extraTokens > 0 && extraCalls > 0 ? " and " : string.Empty)
            + (extraCalls > 0 ? extraCalls.ToString(System.Globalization.CultureInfo.InvariantCulture) + " calls" : string.Empty)
            + "?";

        Task.Run(async () =>
        {
            bool allowed = await ToolApprovalBroker
                .RequestAsync(
                    new ToolApprovalRequest(
                        "Budget Guard",
                        verdict.Reason ?? "This pipeline's budget is spent.",
                        detail),
                    Harness.PhyDocuments.Harness(this),
                    CancellationToken.None)
                .ConfigureAwait(false);

            if (allowed)
            {
                _extraTokens += extraTokens;
                _extraCalls += extraCalls;
                _refused = false;
                _refusal = string.Empty;
            }
            else
            {
                _refused = true;
                _refusal = (verdict.Reason ?? "The budget is spent.")
                    + " Nobody extended it, so this round did not run.";
            }

            // Safe from a background thread: ScheduleStateSolve marshals onto a scheduled solution.
            RequestReadPass();
        });
    }
}
