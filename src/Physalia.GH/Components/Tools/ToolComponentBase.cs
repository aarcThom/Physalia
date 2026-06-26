// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper.Kernel;
using Physalia.Core.Common;
using Physalia.Core.ConvoInstruct;
using Physalia.Core.Signals;
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
/// more than once in one turn — parallel tool use). The base runs <see cref="ExecuteCall"/> once per
/// call and emits <b>one</b> result signal whose content blocks hold a <see cref="ToolResultContent"/>
/// per call, each echoing that call's id. Answering only the first call would strand the other ids as
/// permanently pending at the Router and the round would never complete. Subclasses implement only the
/// tool's definition and its per-call logic.</para>
/// </summary>
public abstract class ToolComponentBase : StatefulComponentBase
{
    /// <summary>Index of the base-owned Signal input (always first).</summary>
    protected const int InSignal = 0;

    private const int OutTool = 0;
    private const int OutResult = 1;

    private PhySignal? _resultSignal;

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
    /// Registers the tool's own inputs after the base-owned Signal input (index 1 onward).
    /// Default implementation adds nothing.
    /// </summary>
    /// <param name="pManager">The input parameter manager.</param>
    protected virtual void RegisterAdditionalInputs(GH_InputParamManager pManager)
    {
    }

    /// <summary>
    /// Hook called once per solve, before any dispatched calls run. Override to read per-solve
    /// context inputs (e.g. a wired catalog) into fields that <see cref="ExecuteCall"/> then uses,
    /// so the context is read once rather than per call. Default implementation does nothing.
    /// </summary>
    /// <param name="da">The data access for the current solve.</param>
    protected virtual void OnSolveTick(IGH_DataAccess da)
    {
    }

    /// <summary>
    /// Executes a single tool call and returns its result. Called once per <see cref="ToolCallContent"/>
    /// in the dispatched signal; read any context cached by <see cref="OnSolveTick"/>. Parse the call's
    /// <see cref="ToolCallContent.InputJson"/> for the tool's arguments.
    /// </summary>
    /// <param name="call">The tool call to execute.</param>
    /// <returns>The result body and whether it represents an error.</returns>
    protected abstract ToolCallResult ExecuteCall(ToolCallContent call);

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
    }

    private void ExecuteDispatched(PhySignal signal)
    {
        var calls = signal.ContentBlocks.OfType<ToolCallContent>().ToList();
        if (calls.Count == 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Dispatched signal carried no tool call.");
            return;
        }

        // One result block per call (keyed by call id) in a single result signal — the Router needs
        // every dispatched id answered to complete the round.
        var resultBlocks = new List<MessageContent>();
        var resultTexts = new List<string>();
        foreach (ToolCallContent call in calls)
        {
            ToolCallResult result = ExecuteCall(call);
            resultBlocks.Add(new ToolResultContent(call.Id, result.Content, result.IsError));
            resultTexts.Add(result.Content);
        }

        string payload = string.Join(Environment.NewLine, resultTexts);
        _resultSignal = PhySignal.Mint(SignalOutcome.Success, payload, InstanceGuid, Name, resultBlocks);
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
