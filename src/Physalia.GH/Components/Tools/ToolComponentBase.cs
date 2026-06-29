// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Grasshopper.Kernel;
using Physalia.Core.Common;
using Physalia.Core.ConvoInstruct;
using Physalia.Core.Signals;
using Physalia.Core.Tools;
using Physalia.GH.Goo;
using Physalia.GH.Parameters;

namespace Physalia.GH.Components;

/// <summary>
/// Base for model-invoked tool nodes. Advertises a <see cref="ToolDefinition"/> on the Tool output
/// (wire into the Reasoner's Tools input), receives dispatched tool-call signals from a Router on the
/// Signal input, and emits a single tool-result signal on the Result output (wire through a Feedback
/// component into a Feedback Collector and back to the Router's Results input).
///
/// <para>The base owns the provider contract that every tool node must honour: a single dispatched
/// signal can carry <b>several</b> <see cref="ToolCallContent"/> blocks (the model calling this tool
/// more than once in one turn — parallel tool use). The base runs the call once per
/// <see cref="ToolCallContent"/> and emits <b>one</b> result signal whose content blocks hold a
/// <see cref="ToolResultContent"/> per call, each echoing that call's id. Answering only the first
/// call would strand the other ids as permanently pending at the Router and the round would never
/// complete. Subclasses implement only the tool's definition and its per-call logic.</para>
///
/// <para><b>Synchronous vs asynchronous.</b> Fast, CPU-only tools (e.g. a catalog search) leave
/// <see cref="RunsAsync"/> false and implement <see cref="ExecuteCall"/> — the result is computed and
/// emitted within the dispatch solve. I/O-bound tools (e.g. a web request) set
/// <see cref="RunsAsync"/> true and implement <see cref="ExecuteCallAsync"/>: the work runs off the
/// solve thread and the result signal latches on a later, self-scheduled solve, so the Grasshopper UI
/// never blocks on the network. Each dispatched signal is processed one at a time in the async path;
/// signals that arrive mid-run wait, latched on the wire, and are serviced after the current batch.</para>
/// </summary>
public abstract class ToolComponentBase : StatefulComponentBase
{
    /// <summary>Index of the base-owned Signal input (always first).</summary>
    protected const int InSignal = 0;

    private const int OutTool = 0;
    private const int OutResult = 1;

    private PhySignal? _resultSignal;

    // Async-path state (RunsAsync only): a batch is running; the completed result is staged by the
    // background task and emitted on the solve that _doEmit (set only by our own scheduled callback)
    // marshals back onto.
    private bool _busy;
    private bool _doEmit;
    private string _pendingPayload = string.Empty;
    private IReadOnlyList<MessageContent> _pendingResultBlocks = Array.Empty<MessageContent>();
    private CancellationTokenSource? _cts;

    /// <summary>
    /// Initializes a new instance of the <see cref="ToolComponentBase"/> class in the Tools sub-category.
    /// </summary>
    /// <param name="name">Component display name.</param>
    /// <param name="nickname">Component nickname.</param>
    /// <param name="description">Component description.</param>
    protected ToolComponentBase(string name, string nickname, string description)
        : base(name, nickname, description, "Tools")
    {
    }

    /// <summary>
    /// Gets the tool definition advertised to the model — its name, when-to-call description, and
    /// argument JSON Schema. The name is what the model emits and what a Router output is matched to.
    /// </summary>
    protected abstract ToolDefinition Definition { get; }

    /// <summary>
    /// Gets the tool definition this node advertises. Public so a Tools In Use scanner can collect
    /// it directly off the canvas without relying on the node having solved.
    /// </summary>
    public ToolDefinition AdvertisedDefinition => Definition;

    /// <summary>
    /// Gets a value indicating whether this tool runs its calls asynchronously off the solve thread.
    /// When false (default) the base calls <see cref="ExecuteCall"/> synchronously within the dispatch
    /// solve; when true it calls <see cref="ExecuteCallAsync"/> and latches the result on a later solve.
    /// </summary>
    protected virtual bool RunsAsync => false;

    /// <summary>
    /// Registers the tool's own inputs after the base-owned Signal input (index 1 onward).
    /// Default implementation adds nothing.
    /// </summary>
    /// <param name="pManager">The input parameter manager.</param>
    protected virtual void RegisterAdditionalInputs(GH_InputParamManager pManager)
    {
    }

    /// <summary>
    /// Hook called once per solve, before any dispatched calls run. Override to read per-solve
    /// context inputs (e.g. a wired catalog, or a resolved API key) into fields that the call logic
    /// then uses, so the context is read once rather than per call. Default implementation does nothing.
    /// </summary>
    /// <param name="da">The data access for the current solve.</param>
    protected virtual void OnSolveTick(IGH_DataAccess da)
    {
    }

    /// <summary>
    /// Executes a single tool call synchronously (for <see cref="RunsAsync"/> = false tools). Called
    /// once per <see cref="ToolCallContent"/> in the dispatched signal; read any context cached by
    /// <see cref="OnSolveTick"/>. Parse the call's <see cref="ToolCallContent.InputJson"/> for the
    /// tool's arguments. Asynchronous tools override <see cref="ExecuteCallAsync"/> instead.
    /// </summary>
    /// <param name="call">The tool call to execute.</param>
    /// <returns>The result body and whether it represents an error.</returns>
    protected virtual ToolCallResult ExecuteCall(ToolCallContent call) =>
        throw new NotSupportedException("This tool runs asynchronously; override ExecuteCallAsync instead.");

    /// <summary>
    /// Executes a single tool call asynchronously (for <see cref="RunsAsync"/> = true tools). The
    /// default wraps <see cref="ExecuteCall"/>. Apply any per-call timeout by linking
    /// <paramref name="ct"/> to a timeout token. Read context cached by <see cref="OnSolveTick"/>.
    /// </summary>
    /// <param name="call">The tool call to execute.</param>
    /// <param name="ct">Cancellation token; cancelled when a new batch starts or the component is removed.</param>
    /// <returns>The result body and whether it represents an error.</returns>
    protected virtual Task<ToolCallResult> ExecuteCallAsync(ToolCallContent call, CancellationToken ct) =>
        Task.FromResult(ExecuteCall(call));

    /// <inheritdoc/>
    protected sealed override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddParameter(new Param_Signal(), "Signal", "S", "Dispatched tool-call signal from a Router.", GH_ParamAccess.list);
        pManager[InSignal].Optional = true;
        RegisterAdditionalInputs(pManager);
    }

    /// <inheritdoc/>
    protected sealed override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddParameter(new Param_ToolDefinition(), "Tool", "T", "The tool definition advertised to the model. Wire into the Reasoner's Tools input.", GH_ParamAccess.item);
        pManager.AddParameter(new Param_Signal(), "Result", "R", "Tool result signal. Wire through a Feedback component into a Feedback Collector, then into the Router's Results input.", GH_ParamAccess.item);
    }

    /// <inheritdoc/>
    protected sealed override void SolveInstance(IGH_DataAccess DA)
    {
        // Always advertise the tool so the Reasoner sees it regardless of run state.
        DA.SetData(OutTool, new GH_ToolDefinition(Definition));

        OnSolveTick(DA);
        ObserveSignalInputs(DA, InSignal);

        if (RunsAsync)
        {
            SolveAsyncPath(DA);
            return;
        }

        // SYNCHRONOUS path: compute and emit within this solve.
        foreach (ConsumedSignal item in ConsumeAllSignals(InSignal))
        {
            ExecuteDispatched(item.Signal);
        }

        EmitSignal(DA, OutResult, _resultSignal);
    }

    /// <inheritdoc/>
    protected override void OnCleared()
    {
        _resultSignal = null;
        _busy = false;
        _doEmit = false;
        _pendingPayload = string.Empty;
        _pendingResultBlocks = Array.Empty<MessageContent>();
    }

    /// <inheritdoc/>
    /// <remarks>Cancels any in-flight asynchronous batch so its task does not outlive the component.</remarks>
    public override void RemovedFromDocument(GH_Document document)
    {
        _cts?.Cancel();
        base.RemovedFromDocument(document);
    }

    // Asynchronous dispatch: consume one signal, run its calls off-thread, latch the result on the
    // self-scheduled solve that sets _doEmit. Signals arriving mid-run wait and are serviced after.
    private void SolveAsyncPath(IGH_DataAccess da)
    {
        if (_doEmit)
        {
            // LATCH PASS — runs only from our own scheduled callback, once the batch has completed.
            _doEmit = false;
            _busy = false;
            _resultSignal = PhySignal.Mint(SignalOutcome.Success, _pendingPayload, InstanceGuid, Name, _pendingResultBlocks);

            if (HasUnconsumedSignals(InSignal))
            {
                // A dispatch arrived mid-run; nothing was dropped. One follow-up solve services it.
                ScheduleStateSolve(1, () => { });
            }

            EmitSignal(da, OutResult, _resultSignal);
            return;
        }

        if (!_busy && TryConsumeOldestSignal(InSignal, out PhySignal signal))
        {
            var calls = signal.ContentBlocks.OfType<ToolCallContent>().ToList();
            if (calls.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Dispatched signal carried no tool call.");
                EmitSignal(da, OutResult, _resultSignal);
                return;
            }

            _busy = true;
            StartAsyncBatch(calls);
        }

        // Re-emit the latched result (same sequence; downstream consume-once means no re-fire).
        EmitSignal(da, OutResult, _resultSignal);
    }

    private void StartAsyncBatch(IReadOnlyList<ToolCallContent> calls)
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        CancellationToken ct = _cts.Token;

        // The runner owns the one-result-per-call contract and the per-call/whole-batch error handling;
        // it returns null when cancelled so the latch never fires for an abandoned batch (which would
        // otherwise hang busy with the Router's id unanswered).
        Task.Run(async () =>
        {
            ToolBatchResult? batch = await ToolBatchRunner.RunAsync(
                calls,
                async (call, token) =>
                {
                    ToolCallResult result = await ExecuteCallAsync(call, token).ConfigureAwait(false);
                    return new ToolCallOutcome(result.Content, result.IsError);
                },
                ct).ConfigureAwait(false);

            if (batch is null)
            {
                return;
            }

            _pendingResultBlocks = batch.Blocks;
            _pendingPayload = batch.Payload;
            ScheduleStateSolve(1, () => _doEmit = true);
        }, ct);
    }

    private void ExecuteDispatched(PhySignal signal)
    {
        var calls = signal.ContentBlocks.OfType<ToolCallContent>().ToList();
        if (calls.Count == 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Dispatched signal carried no tool call.");
            return;
        }

        // The runner enforces one result block per call (keyed by call id) in a single result signal —
        // the Router needs every dispatched id answered to complete the round.
        ToolBatchResult batch = ToolBatchRunner.Run(
            calls,
            call =>
            {
                ToolCallResult result = ExecuteCall(call);
                return new ToolCallOutcome(result.Content, result.IsError);
            });

        _resultSignal = PhySignal.Mint(SignalOutcome.Success, batch.Payload, InstanceGuid, Name, batch.Blocks);
    }

    /// <summary>
    /// The outcome of a single tool call: the result body and whether it represents an error
    /// (mapped to the tool_result block's <c>is_error</c> flag so the model can self-correct).
    /// </summary>
    /// <param name="Content">The result body returned to the model.</param>
    /// <param name="IsError">True when the call failed.</param>
    protected readonly record struct ToolCallResult(string Content, bool IsError = false)
    {
        /// <summary>
        /// Creates a successful result.
        /// </summary>
        /// <param name="content">The result body.</param>
        /// <returns>A success <see cref="ToolCallResult"/>.</returns>
        public static ToolCallResult Ok(string content) => new(content, false);

        /// <summary>
        /// Creates an error result.
        /// </summary>
        /// <param name="content">The error body returned to the model.</param>
        /// <returns>An error <see cref="ToolCallResult"/>.</returns>
        public static ToolCallResult Error(string content) => new(content, true);
    }
}
