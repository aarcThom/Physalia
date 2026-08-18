// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Physalia.Core.ConvoInstruct;
using Physalia.Core.Signals;
using Physalia.Core.Tools;
using Physalia.GH.Goo;
using Physalia.GH.Parameters;

namespace Physalia.GH.Components;

/// <summary>
/// Dispatches a LLM Call's tool calls to tool nodes and forwards both the model's request and the
/// returned results back toward the Conversation Log. Each user-added output receives the calls whose name
/// matches it; an output's name updates automatically to the tool it is wired into (the tool node's
/// advertised <c>Tool Definition</c>), so dispatch matches without a manual rename. A fixed Feedback
/// output (always last) carries the assistant tool-call request and, later, the collected tool
/// results — wire it through a Feedback component into a Feedback Collector and on to the Conversation Log's
/// Tool input.
///
/// <para>Tool results return wirelessly: each tool node's result goes through a Feedback component
/// into a Feedback Collector whose Signal output is wired into this Router's Results input. Because
/// every return hop is the wireless Feedback transport, the loop closes without a GH cycle.</para>
///
/// <para>When the model calls several tools at once, each result returns independently. The Router
/// holds them until every dispatched <c>tool_use</c> id has a matching result, then forwards one
/// combined signal so the Conversation Log logs a single user turn carrying all <c>tool_result</c> blocks —
/// which is what the provider requires after a multi-tool assistant turn. A call with no matching
/// output is answered with an error result so that round can still complete.</para>
/// </summary>
public class Router : StatefulComponentBase, IGH_VariableParameterComponent
{
    private const int InToolCalls = 0;
    private const int InResults = 1;

    // Latched dispatch signals keyed by the tool-output nickname they were sent to, plus the
    // current feedback signal. Keyed by nickname (not index) so they survive output add/remove.
    private readonly Dictionary<string, PhySignal> _dispatched = new(StringComparer.OrdinalIgnoreCase);
    private PhySignal? _feedbackSignal;

    // Per-round tool-result aggregation. The provider requires every tool_use block in the
    // assistant turn to have a matching tool_result in the SINGLE user turn that follows, so the
    // Router holds results until the whole dispatched set is in, then forwards one combined signal.
    // Correctness is by the dispatched id set, not by the timing of independent tool nodes.
    private readonly HashSet<string> _pendingToolUseIds = new(StringComparer.Ordinal);
    private readonly List<ToolResultContent> _collectedResults = new();

    // Blocks a tool sent back ALONGSIDE its tool_result — an image, in practice. A tool result is
    // text on every provider, so a tool answering with a picture (Take Snapshot) returns both, and
    // the picture rides the same user turn as a sibling block. Kept separate from the results so it
    // lands AFTER them: Anthropic requires the tool_result blocks to lead that turn.
    private readonly List<MessageContent> _collectedAttachments = new();
    private bool _awaitingResults;

    /// <summary>
    /// Initializes a new instance of the <see cref="Router"/> class.
    /// </summary>
    public Router()
        : base("Router", "Rtr", "Dispatches tool calls to named outputs and routes the request and results back to the Conversation Log.", "LLM Tools")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("B4E1C7A2-3D5F-4068-9A1C-7E2D5B9F36A4");

    /// <inheritdoc/>
    /// <remarks>
    /// Subscribes to the document so a tool output's name can refresh the moment it is wired into a
    /// tool node — wiring an output does not re-solve this (the source) component, so the rename is
    /// driven off the end of the solution that solved the newly wired tool node.
    /// </remarks>
    public override void AddedToDocument(GH_Document document)
    {
        base.AddedToDocument(document);
        document.SolutionEnd += OnDocumentSolutionEnd;
    }

    /// <inheritdoc/>
    public override void RemovedFromDocument(GH_Document document)
    {
        document.SolutionEnd -= OnDocumentSolutionEnd;
        base.RemovedFromDocument(document);
    }

    /// <summary>Gets the output index of the trailing fixed Feedback output.</summary>
    private int FeedbackIndex => Params.Output.Count - 1;

    /// <inheritdoc/>
    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddParameter(new Param_Signal(), "Tool Calls", "TC", "Tool-call signal from a LLM Call's Tool Calls output. Each call is dispatched to the output whose nickname matches the tool's name.", GH_ParamAccess.list);
        pManager.AddParameter(new Param_Signal(), "Results", "R", "Tool results returning through a Feedback Collector. Forwarded to the Conversation Log via the Feedback output.", GH_ParamAccess.list);
        pManager[InToolCalls].Optional = true;
        pManager[InResults].Optional = true;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Registers only the fixed Feedback output (always last). The user adds one signal output per
    /// tool above it via the zoom +/- icons; each output's name then tracks the tool node it is
    /// wired into, so no manual rename is required.
    /// </remarks>
    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddParameter(new Param_Signal(), "Feedback", "F", "Carries the assistant tool-call request and the collected results back to the Conversation Log. Wire through a Feedback component into a Feedback Collector, then into the Conversation Log's Tool input.", GH_ParamAccess.item);
    }

    /// <inheritdoc/>
    public bool CanInsertParameter(GH_ParameterSide side, int index) =>
        side == GH_ParameterSide.Output && index < Params.Output.Count;

    /// <inheritdoc/>
    public bool CanRemoveParameter(GH_ParameterSide side, int index) =>
        side == GH_ParameterSide.Output && index >= 0 && index < Params.Output.Count - 1;

    /// <inheritdoc/>
    public IGH_Param CreateParameter(GH_ParameterSide side, int index)
    {
        string nick = NextAvailableNick("T", Params.Output.Select(p => p.NickName));
        return new Param_Signal
        {
            Name = nick,
            NickName = nick,
            Description = "Dispatches tool calls whose name matches this output. Wire it into a tool node's Signal input and its name updates to that tool automatically.",
            Access = GH_ParamAccess.item,
        };
    }

    /// <inheritdoc/>
    public bool DestroyParameter(GH_ParameterSide side, int index) => true;

    /// <inheritdoc/>
    public void VariableParameterMaintenance()
    {
        // Keep every tool output (all but the trailing Feedback) as an item-access signal with a
        // non-blank nickname; never touch a nickname the user has set.
        for (int i = 0; i < FeedbackIndex; i++)
        {
            IGH_Param param = Params.Output[i];
            param.Access = GH_ParamAccess.item;
            if (string.IsNullOrWhiteSpace(param.NickName))
            {
                param.NickName = $"T{i + 1}";
            }

            if (string.IsNullOrWhiteSpace(param.Name))
            {
                param.Name = param.NickName;
            }
        }
    }

    /// <inheritdoc/>
    protected override void SolveInstance(IGH_DataAccess DA)
    {
        // Keep tool-output names in step with what each is wired into before dispatching, so
        // FindToolOutput matches on the live tool name even if a wire changed since last solve.
        if (SyncToolOutputNames())
        {
            Attributes?.ExpireLayout();
        }

        ObserveSignalInputs(DA, InToolCalls, InResults);

        // Consume in global sequence order: a tool-call request is always minted before the
        // results it provokes, so the request reaches the Conversation Log before the results.
        bool dispatched = false;
        foreach (ConsumedSignal item in ConsumeAllSignals(InToolCalls, InResults))
        {
            if (item.ParamIndex == InToolCalls)
            {
                DispatchToolCalls(item.Signal);
                dispatched = true;
            }
            else
            {
                CollectResults(item.Signal);
            }
        }

        if (dispatched)
        {
            // Emit the assistant request alone this solve. If the whole set is already satisfied
            // (e.g. every call was dropped and answered with a synthetic error), forward the
            // combined results on a follow-up solve so the request is consumed downstream first.
            if (ResultsReady())
            {
                ScheduleStateSolve(1, () => { });
            }
        }
        else if (ResultsReady())
        {
            ForwardCollectedResults();
        }

        Emit(DA);
    }

    /// <inheritdoc/>
    protected override void OnCleared()
    {
        _dispatched.Clear();
        _feedbackSignal = null;
        _pendingToolUseIds.Clear();
        _collectedResults.Clear();
        _collectedAttachments.Clear();
        _awaitingResults = false;
    }

    private void DispatchToolCalls(PhySignal signal)
    {
        var calls = signal.ContentBlocks.OfType<ToolCallContent>().ToList();
        if (calls.Count == 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Tool-call signal carried no tool_use blocks.");
            return;
        }

        // Start a fresh round: the dispatched ids are the set whose results must all be gathered
        // before the combined tool_result turn is forwarded.
        _pendingToolUseIds.Clear();
        _collectedResults.Clear();
        _collectedAttachments.Clear();
        _awaitingResults = true;

        // Forward the whole assistant turn (text + tool_use blocks) to the Conversation Log so the model's
        // request is logged before any result.
        _feedbackSignal = PhySignal.Mint(SignalOutcome.Success, signal.Payload, InstanceGuid, Name, signal.ContentBlocks);

        // The pure policy decides grouping (parallel calls to one tool ride together as one dispatch),
        // synthetic errors for unmatched calls, and the awaited id set. Names exclude the Feedback output.
        var availableNames = new List<string>(FeedbackIndex);
        for (int i = 0; i < FeedbackIndex; i++)
        {
            availableNames.Add(Params.Output[i].NickName);
        }

        ToolDispatchPlan plan = ToolDispatchRound.Plan(calls, availableNames);

        foreach (string warning in plan.Warnings)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, warning);
        }

        foreach (string id in plan.PendingToolUseIds)
        {
            _pendingToolUseIds.Add(id);
        }

        _collectedResults.AddRange(plan.SyntheticErrorResults);

        foreach (ToolDispatchGroup group in plan.Groups)
        {
            var blocks = group.Calls.Cast<MessageContent>().ToList();
            _dispatched[group.OutputName] = PhySignal.Mint(SignalOutcome.Success, group.Payload, InstanceGuid, Name, blocks);
        }
    }

    private void CollectResults(PhySignal signal)
    {
        // Accumulate (never forward per-result): each independent tool node returns its own
        // tool_result, but they must arrive at the Conversation Log as ONE user turn. Match by tool_use_id
        // so the dispatched set drains regardless of arrival order.
        foreach (MessageContent block in signal.ContentBlocks)
        {
            if (block is ToolResultContent result)
            {
                _collectedResults.Add(result);
                _pendingToolUseIds.Remove(result.ToolCallId);
            }
            else if (block is not ToolCallContent)
            {
                // Anything that is not the answer itself and not a re-echoed call: forward it rather
                // than drop it, or a tool that answers with an image would silently lose the image.
                _collectedAttachments.Add(block);
            }
        }
    }

    private bool ResultsReady() =>
        _awaitingResults && _pendingToolUseIds.Count == 0 && _collectedResults.Count > 0;

    private void ForwardCollectedResults()
    {
        // One combined signal carrying every tool_result — recorded as the single user turn the
        // provider requires after the assistant tool_use turn, firing the LLM Call exactly once.
        _awaitingResults = false;
        (IReadOnlyList<MessageContent> blocks, string payload) =
            ToolDispatchRound.CombineResults(_collectedResults, _collectedAttachments);

        _feedbackSignal = PhySignal.Mint(SignalOutcome.Success, payload, InstanceGuid, Name, blocks);
        _collectedResults.Clear();
        _collectedAttachments.Clear();
    }

    private void Emit(IGH_DataAccess da)
    {
        int feedbackIndex = FeedbackIndex;

        for (int i = 0; i < feedbackIndex; i++)
        {
            if (_dispatched.TryGetValue(Params.Output[i].NickName, out PhySignal? sig))
            {
                EmitSignal(da, i, sig);
            }
        }

        EmitSignal(da, feedbackIndex, _feedbackSignal);
    }

    private void OnDocumentSolutionEnd(object sender, GH_SolutionEventArgs e)
    {
        // A wire may have been added/removed to a tool node during this solution. Refresh names
        // off the canvas (renaming is display-only — no re-solve, so no solution loop).
        if (SyncToolOutputNames())
        {
            Attributes?.ExpireLayout();
            Grasshopper.Instances.RedrawCanvas();
        }
    }

    /// <summary>
    /// Aligns every tool output's name with the tool node it is wired into. An output wired into a
    /// tool node takes that node's advertised tool name; one wired to nothing tool-related reverts to
    /// its default <c>T{n}</c>; one wired to a tool node whose definition has not solved yet is left
    /// untouched (it resolves on a later pass). Returns true when any name changed.
    /// </summary>
    private bool SyncToolOutputNames()
    {
        bool changed = false;
        for (int i = 0; i < FeedbackIndex; i++)
        {
            IGH_Param output = Params.Output[i];
            ToolConnection conn = InspectConnection(output);

            string? desired = conn switch
            {
                { ToolName: { } name } => name,        // resolved to a live tool name
                { ConnectedToTool: true } => null,     // a tool node, not yet solved — leave as is
                _ => $"T{i + 1}",                       // not wired to a tool — default name
            };

            if (desired is not null && !string.Equals(output.NickName, desired, StringComparison.Ordinal))
            {
                output.Name = desired;
                output.NickName = desired;
                changed = true;
            }
        }

        return changed;
    }

    /// <summary>
    /// Inspects an output's recipients for a tool node, reporting whether it feeds one and, when
    /// available, the name that node advertises through its Tool Definition output.
    /// </summary>
    private static ToolConnection InspectConnection(IGH_Param output)
    {
        bool connectedToTool = false;

        foreach (IGH_Param recipient in output.Recipients)
        {
            if (recipient.Attributes?.GetTopLevel?.DocObject is not IGH_Component component)
            {
                continue;
            }

            foreach (IGH_Param toolParam in component.Params.Output)
            {
                if (toolParam is not Param_LlmToolDefinition)
                {
                    continue;
                }

                connectedToTool = true;
                foreach (IGH_Goo goo in toolParam.VolatileData.AllData(true))
                {
                    if (goo is GH_LlmToolDefinition def && def.Value is { } definition &&
                        !string.IsNullOrWhiteSpace(definition.Name))
                    {
                        return new ToolConnection(true, definition.Name);
                    }
                }
            }
        }

        return new ToolConnection(connectedToTool, null);
    }

    private static string NextAvailableNick(string prefix, IEnumerable<string> existing)
    {
        var names = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);
        for (int n = 1; ; n++)
        {
            string candidate = $"{prefix}{n}";
            if (!names.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    /// <summary>
    /// What an output's wiring tells us about the tool it serves.
    /// </summary>
    /// <param name="ConnectedToTool">True when the output feeds a tool node (a component with a Tool Definition output).</param>
    /// <param name="ToolName">The advertised tool name when it has solved and is available; otherwise null.</param>
    private readonly record struct ToolConnection(bool ConnectedToTool, string? ToolName);
}
