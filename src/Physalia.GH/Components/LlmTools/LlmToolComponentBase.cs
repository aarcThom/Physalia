// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Grasshopper.Kernel;
using Physalia.Core.Common;
using Physalia.Core.ConvoInstruct;
using Physalia.Core.Signals;
using Physalia.Core.Tools;
using Physalia.GH.Goo;
using Physalia.GH.Parameters;

namespace Physalia.GH.Components;

/// <summary>
/// Base for model-invoked tool nodes. Advertises a <see cref="LlmToolDefinition"/> on the Tool output
/// (wire into the LLM Call's Tools input), receives dispatched tool-call signals from a Router on the
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
public abstract class LlmToolComponentBase : StatefulComponentBase
{
    /// <summary>Index of the base-owned Signal input (always first).</summary>
    protected const int InSignal = 0;

    private const int OutTool = 0;
    private const int OutResult = 1;

    /// <summary>
    /// Gets the index of the first output registered by <see cref="RegisterAdditionalOutputs"/> —
    /// 2, after the fixed Tool(0)/Result(1) pair. Compute a subclass's own output indices from this
    /// rather than hard-coding them.
    /// </summary>
    protected static int FirstAdditionalOutputIndex => 2;

    private PhySignal? _resultSignal;

    // Whether this node is advertised to the model. True (the default) means the wired Tools Present
    // grounder lists it, so the model may call it; false leaves the node wired and working but unseen
    // — the way to switch a tool off without unwiring it. A menu item AND the chat window's tools
    // page drive this same field, and it is serialized here rather than as a name-keyed selection on
    // the Conversation Log: the setting belongs to the node, so it survives a copy into another
    // harness, ships inside a preset, and cannot be confused with a second node of the same tool.
    private bool _advertise = true;

    // Async-path state (RunsAsync only): a batch is running; the completed result is staged by the
    // background task and emitted on the solve that _doEmit (set only by our own scheduled callback)
    // marshals back onto.
    private bool _busy;
    private bool _doEmit;
    private string _pendingPayload = string.Empty;
    private IReadOnlyList<MessageContent> _pendingResultBlocks = Array.Empty<MessageContent>();
    private CancellationTokenSource? _cts;

    /// <summary>
    /// Initializes a new instance of the <see cref="LlmToolComponentBase"/> class in the Tools sub-category.
    /// </summary>
    /// <param name="name">Component display name.</param>
    /// <param name="nickname">Component nickname.</param>
    /// <param name="description">Component description.</param>
    protected LlmToolComponentBase(string name, string nickname, string description)
        : base(name, nickname, description, "LLM Tools")
    {
    }

    /// <summary>
    /// Gets the tool definition advertised to the model — its name, when-to-call description, and
    /// argument JSON Schema. The name is what the model emits and what a Router output is matched to.
    /// </summary>
    /// <remarks>
    /// Virtual rather than abstract only so a node advertising a whole SET of tools can override
    /// <see cref="Definitions"/> instead. Every node advertising exactly one tool — which is all of
    /// them but the MCP Server — overrides this and nothing else changes for it.
    /// </remarks>
    protected virtual LlmToolDefinition Definition =>
        throw new NotSupportedException(
            $"{GetType().Name} advertises a set of tools; read {nameof(Definitions)} instead of {nameof(Definition)}.");

    /// <summary>
    /// Gets every tool definition this node advertises. Defaults to the single
    /// <see cref="Definition"/>; override instead of <see cref="Definition"/> when the set is
    /// discovered at runtime, as an MCP server's is.
    /// </summary>
    protected virtual IReadOnlyList<LlmToolDefinition> Definitions => new[] { Definition };

    /// <summary>
    /// Gets the tool definitions this node advertises. Public so a Tools In Use scanner can collect
    /// them directly off the canvas without relying on the node having solved.
    /// </summary>
    public IReadOnlyList<LlmToolDefinition> AdvertisedDefinitions => Definitions;

    /// <summary>
    /// Gets a value indicating whether this node is advertised to the model. False keeps it wired and
    /// able to answer, but leaves it out of what the model is told exists — so it is never called.
    /// </summary>
    public bool Advertise => _advertise;

    /// <summary>
    /// Gets a value indicating whether this tool runs its calls asynchronously off the solve thread.
    /// When false (default) the base calls <see cref="ExecuteCall"/> synchronously within the dispatch
    /// solve; when true it calls <see cref="ExecuteCallAsync"/> and latches the result on a later solve.
    /// </summary>
    protected virtual bool RunsAsync => false;

    /// <summary>
    /// Gets the tooltip for the Signal input: what this particular tool is being asked to do
    /// when the Router dispatches a call to it. Every tool writes its own — a shared default
    /// would tell the reader nothing about the tool they are hovering over.
    /// </summary>
    protected abstract string SignalInputDescription { get; }

    /// <summary>
    /// Gets the tooltip for the Tool output: the advertisement the model reads, in this tool's
    /// own terms.
    /// </summary>
    protected abstract string ToolOutputDescription { get; }

    /// <summary>
    /// Gets the tooltip for the Result output: what this tool hands back after a call.
    /// </summary>
    protected abstract string ResultOutputDescription { get; }

    /// <summary>
    /// Registers the tool's own inputs after the base-owned Signal input (index 1 onward).
    /// Default implementation adds nothing.
    /// </summary>
    /// <param name="pManager">The input parameter manager.</param>
    protected virtual void RegisterAdditionalInputs(GH_InputParamManager pManager)
    {
    }

    /// <summary>
    /// Registers the tool's own outputs after the base-owned Tool and Result outputs
    /// (<see cref="FirstAdditionalOutputIndex"/> onward). Default implementation adds nothing.
    /// </summary>
    /// <param name="pManager">The output parameter manager.</param>
    protected virtual void RegisterAdditionalOutputs(GH_OutputParamManager pManager)
    {
    }

    /// <summary>
    /// Hook called once at the very end of every solve, after any dispatched calls have run.
    /// Override to publish outputs registered by <see cref="RegisterAdditionalOutputs"/> that the
    /// calls themselves mutate — <see cref="OnSolveTick"/> runs too early for those and would leave
    /// the wire one solve behind. Re-publish unconditionally, including on idle solves, so the value
    /// stays on the wire rather than blanking between solves. Default implementation does nothing.
    /// </summary>
    /// <param name="da">The data access for the current solve.</param>
    protected virtual void OnSolveEnd(IGH_DataAccess da)
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
        pManager.AddParameter(new Param_Signal(), "Signal", "S", SignalInputDescription, GH_ParamAccess.list);
        pManager[InSignal].Optional = true;
        RegisterAdditionalInputs(pManager);
    }

    /// <inheritdoc/>
    protected sealed override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddParameter(new Param_LlmToolDefinition(), "Tool", "T", ToolOutputDescription, GH_ParamAccess.list);
        pManager.AddParameter(new Param_Signal(), "Result", "R", ResultOutputDescription, GH_ParamAccess.item);
        RegisterAdditionalOutputs(pManager);
    }

    /// <inheritdoc/>
    protected sealed override void SolveInstance(IGH_DataAccess DA)
    {
        // Always advertise the tools so the LLM Call sees them regardless of run state. A list, not
        // an item, because an MCP Server node stands for its server's whole tool set.
        DA.SetDataList(OutTool, Definitions.Select(d => new GH_LlmToolDefinition(d)));

        OnSolveTick(DA);
        ObserveSignalInputs(DA, InSignal);

        if (RunsAsync)
        {
            SolveAsyncPath(DA);
        }
        else
        {
            // SYNCHRONOUS path: compute and emit within this solve.
            foreach (ConsumedSignal item in ConsumeAllSignals(InSignal))
            {
                ExecuteDispatched(item.Signal);
            }

            EmitSignal(DA, OutResult, _resultSignal);
        }

        // After the calls, so a subclass output the calls mutate is published in the same solve.
        OnSolveEnd(DA);
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
                    return new ToolCallOutcome(result.Content, result.IsError, result.Attachments);
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
                return new ToolCallOutcome(result.Content, result.IsError, result.Attachments);
            });

        _resultSignal = PhySignal.Mint(SignalOutcome.Success, batch.Payload, InstanceGuid, Name, batch.Blocks);
    }

    /// <summary>
    /// Sets whether this node is advertised to the model, and re-solves.
    ///
    /// <para>Re-solving this node is what makes the change land: a Tools Present grounder is not
    /// wired to the tool nodes it reports, it SCANS for them, and it re-reads the canvas at the end of
    /// any solution whose result differs from what it last emitted. So expiring this node is enough to
    /// get the new advertised set to the Conversation Log — see <c>ToolsInUse</c>.</para>
    /// </summary>
    /// <param name="on">True to advertise this tool to the model; false to keep it wired but unseen.</param>
    public void SetAdvertise(bool on)
    {
        if (_advertise == on)
        {
            return;
        }

        _advertise = on;
        ExpireSolution(true);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Unchecking "Advertise To The Model" is how you park a tool: the node stays wired to its Router
    /// and would still answer a call, but the model is never told it exists, so no call comes. The same
    /// switch is on the chat window's tools page.
    /// </remarks>
    public override void AppendAdditionalMenuItems(ToolStripDropDown menu)
    {
        base.AppendAdditionalMenuItems(menu);
        Menu_AppendItem(
            menu,
            "Advertise To The Model",
            (_, _) => SetAdvertise(!_advertise),
            enabled: true,
            @checked: _advertise);
    }

    /// <summary>
    /// Restores the advertise flag WITHOUT re-solving — for a setting being migrated out of an older
    /// file, where a solution is already on its way and starting one is not allowed. The change still
    /// reaches the model, because a Tools Present grounder folds this flag into the signature it
    /// compares at the end of every solution, and re-emits when it differs.
    /// </summary>
    /// <param name="on">True to advertise this tool to the model.</param>
    internal void RestoreAdvertise(bool on) => _advertise = on;

    /// <inheritdoc/>
    public override bool Write(GH_IO.Serialization.GH_IWriter writer)
    {
        writer.SetBoolean("Advertise", _advertise);
        return base.Write(writer);
    }

    /// <inheritdoc/>
    public override bool Read(GH_IO.Serialization.GH_IReader reader)
    {
        // Absent key = a file written before the toggle existed: a wired tool was always advertised.
        _advertise = !reader.ItemExists("Advertise") || reader.GetBoolean("Advertise");
        return base.Read(reader);
    }

    /// <summary>
    /// The outcome of a single tool call: the result body, whether it represents an error
    /// (mapped to the tool_result block's <c>is_error</c> flag so the model can self-correct), and
    /// any blocks the result body itself cannot hold.
    /// </summary>
    /// <param name="Content">The result body returned to the model.</param>
    /// <param name="IsError">True when the call failed.</param>
    /// <param name="Attachments">
    /// Blocks this call answers WITH but cannot answer THROUGH — an image, in practice, since a
    /// tool_result is text on every provider. They ride the same user turn as sibling blocks, placed
    /// after every result. Null for every ordinary tool.
    /// </param>
    protected readonly record struct ToolCallResult(
        string Content,
        bool IsError = false,
        IReadOnlyList<MessageContent>? Attachments = null)
    {
        /// <summary>
        /// Creates a successful result.
        /// </summary>
        /// <param name="content">The result body.</param>
        /// <returns>A success <see cref="ToolCallResult"/>.</returns>
        public static ToolCallResult Ok(string content) => new(content, false);

        /// <summary>
        /// Creates a successful result that also carries blocks the result body cannot hold, such as
        /// a captured image.
        /// </summary>
        /// <param name="content">The result body.</param>
        /// <param name="attachments">The blocks to send in the same user turn, after the results.</param>
        /// <returns>A success <see cref="ToolCallResult"/> carrying the attachments.</returns>
        public static ToolCallResult OkWith(string content, IReadOnlyList<MessageContent> attachments) =>
            new(content, false, attachments);

        /// <summary>
        /// Creates an error result.
        /// </summary>
        /// <param name="content">The error body returned to the model.</param>
        /// <returns>An error <see cref="ToolCallResult"/>.</returns>
        public static ToolCallResult Error(string content) => new(content, true);
    }
}
