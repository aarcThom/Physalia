// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using System.Collections.Generic;
using System.Windows.Forms;
using Grasshopper.Kernel;
using Physalia.Core.Common;
using Physalia.Core.ConvoInstruct;
using Physalia.Core.Signals;
using Physalia.GH.Goo;
using Physalia.GH.Parameters;
using System.Linq;

namespace Physalia.GH.Components;

/// <summary>
/// Maintains the full conversation history as an append-only log.
/// An optional Conversation override replaces the active conversation (for compaction)
/// while the Recorded History output preserves every message ever seen.
///
/// <para>Events arrive on three dedicated Signal inputs — Prompt, Response, Feedback —
/// so the turn type comes from event identity, never from conversation parity. Signals
/// are consumed in global sequence order (causal order), which guarantees a response is
/// recorded before the feedback it provoked even when both arrive in the same solve.
/// User-side text arriving while the last turn is already a user message merges into
/// that message, preserving the role alternation providers require. The outgoing Signal
/// is minted only when a user turn was recorded — assistant turns latch quietly so a
/// Reasoner wired off this output cannot re-fire itself in an infinite loop.</para>
/// </summary>
public class Recorder : StatefulComponentBase
{
    private const int InSystemPrompt = 0;
    private const int InPromptSignal = 1;
    private const int InResponseSignal = 2;
    private const int InFeedbackSignal = 3;
    private const int InToolCalls = 4;
    private const int InConversation = 5;

    private Conversation _conversation = Conversation.Empty;
    private Conversation _recordedHistory = Conversation.Empty;
    private Conversation? _lastOverride;

    // Latched outputs; null while Empty or Active so the wires are genuinely blank.
    private Instructions? _latchedInstructions;
    private Conversation? _latchedHistory;

    // Set ONLY by our own scheduled callback so the latch runs after the visible delay.
    private bool _doLatch;
    private AppendOutcome _pendingOutcome;
    private string _pendingUserText = string.Empty;

    private enum AppendOutcome
    {
        Nothing,
        UserTurn,
        AssistantTurn,
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Recorder"/> class.
    /// </summary>
    public Recorder()
        : base("Recorder", "Rec", "Maintains the full conversation history as an append-only log.", "Core")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("43A02F6D-D97D-4241-B4DD-067D7AE0D75E");

    /// <inheritdoc/>
    protected override string ClearMenuText => "Clear Conversation";

    /// <inheritdoc/>
    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddTextParameter("System Prompt", "S", "System prompt from Composer.", GH_ParamAccess.item, string.Empty);
        pManager.AddParameter(new Param_Signal(), "Prompt Signal", "PS", "Records a user turn; the signal payload is the prompt text. Use Construct Signal to combine a text payload with a manual trigger.", GH_ParamAccess.list);
        pManager.AddParameter(new Param_Signal(), "Response Signal", "RS", "Records an assistant turn from the Reasoner's Success Signal. Tool calls take priority over the payload text.", GH_ParamAccess.list);
        pManager.AddParameter(new Param_Signal(), "Feedback Signal", "FS", "Records feedback as a user turn. Wire one or more Feedback Collectors directly — no OR gate needed.", GH_ParamAccess.list);
        pManager.AddParameter(new Param_LlmToolCall(), "Tool Calls", "TC", "Tool calls from Reasoner to record as an assistant message.", GH_ParamAccess.list);
        pManager.AddParameter(new Param_Conversation(), "Conversation", "C", "Optional compacted conversation. Replaces the active conversation while all messages are preserved in Recorded History.", GH_ParamAccess.item);

        pManager[InPromptSignal].Optional = true;
        pManager[InResponseSignal].Optional = true;
        pManager[InFeedbackSignal].Optional = true;
        pManager[InToolCalls].Optional = true;
        pManager[InConversation].Optional = true;
    }

    /// <inheritdoc/>
    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddParameter(new Param_Instructions(), "Instructions", "I", "Conversation history and system prompt bundled for inference. Latched after each run.", GH_ParamAccess.item);
        pManager.AddParameter(new Param_Signal(), "Signal", "Sig", "Latched signal minted when a user turn was recorded. Assistant turns latch quietly.", GH_ParamAccess.item);
        pManager.AddParameter(new Param_Conversation(), "Recorded History", "H", "Full conversation history including all messages before and after compaction.", GH_ParamAccess.item);
    }

    /// <inheritdoc/>
    public override void AppendAdditionalMenuItems(ToolStripDropDown menu)
    {
        base.AppendAdditionalMenuItems(menu);
        Menu_AppendItem(menu, "Save Conversation", OnSaveConversation);
        Menu_AppendItem(menu, "Load Conversation", OnLoadConversation);
    }

    /// <inheritdoc/>
    protected override void SolveInstance(IGH_DataAccess DA)
    {
        string systemPrompt = string.Empty;
        var toolCallItems = new List<GH_LlmToolCall>();

        DA.GetData(InSystemPrompt, ref systemPrompt);
        DA.GetDataList(InToolCalls, toolCallItems);

        // Apply compacted conversation override when a new one arrives. This lives outside
        // the state machine — compaction is a data transformation, not a run outcome.
        var overrideGoo = new GH_Conversation();
        bool hasOverride = DA.GetData(InConversation, ref overrideGoo);
        Conversation? overrideConversation = hasOverride ? overrideGoo?.Value : null;

        if (overrideConversation is not null && !ReferenceEquals(overrideConversation, _lastOverride))
        {
            _lastOverride = overrideConversation;
            _conversation = overrideConversation;

            // Absorb the compacted conversation into the recorded history so no messages are lost.
            foreach (var msg in overrideConversation.Messages)
            {
                try
                {
                    _recordedHistory = _recordedHistory.Append(msg);
                }
                catch (InvalidOperationException ex)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Recorded history skipped a message during compaction: {ex.Message}");
                }
            }
        }

        // Observe every solve, even mid-run: events arriving while busy wait, latched on
        // their wires, and are serviced after the latch.
        ObserveSignalInputs(DA, InPromptSignal, InResponseSignal, InFeedbackSignal);

        if (_doLatch)
        {
            // LATCH PASS — runs only from our own scheduled callback, after the visible delay.
            _doLatch = false;
            _latchedInstructions = new Instructions(systemPrompt, _conversation);
            _latchedHistory = _recordedHistory;

            switch (_pendingOutcome)
            {
                case AppendOutcome.UserTurn:
                    LatchSuccess(_pendingUserText);
                    break;
                case AppendOutcome.AssistantTurn:
                    // Quiet success: a Reasoner wired off the outgoing signal must not
                    // re-fire after its own response is recorded.
                    LatchSuccess(string.Empty, emitSignal: false);
                    break;
                default:
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Signal received but there was nothing new to record.");
                    LatchFailure(string.Empty, emitSignal: false);
                    break;
            }

            if (HasUnconsumedSignals(InPromptSignal, InResponseSignal, InFeedbackSignal))
            {
                // Events arrived mid-run; nothing was dropped. One follow-up solve
                // services them in sequence order.
                ScheduleStateSolve(1, () => { });
            }

            EmitOutputs(DA);
            return;
        }

        if (State != SolveState.Active)
        {
            // Consuming in global sequence order is the ordering guarantee: feedback is
            // always minted after the response that provoked it, so even when both land
            // in this one solve the assistant turn is recorded first.
            IReadOnlyList<ConsumedSignal> consumed =
                ConsumeAllSignals(InPromptSignal, InResponseSignal, InFeedbackSignal);

            if (consumed.Count > 0)
            {
                EnterActive();
                _pendingOutcome = AppendOutcome.Nothing;
                _pendingUserText = string.Empty;

                foreach (ConsumedSignal item in consumed)
                {
                    ApplySignal(item, toolCallItems);
                }

                ScheduleStateSolve(SolveDelayMs, () => _doLatch = true);
                EmitOutputs(DA);
                return;
            }
        }

        // Idle solve — re-emit the latched outputs.
        EmitOutputs(DA);
    }

    /// <inheritdoc/>
    protected override void ClearStateOutputs()
    {
        _latchedInstructions = null;
        _latchedHistory = null;
    }

    /// <inheritdoc/>
    protected override void OnCleared()
    {
        _conversation = Conversation.Empty;
        _recordedHistory = Conversation.Empty;
        _lastOverride = null;
        _doLatch = false;
        _pendingOutcome = AppendOutcome.Nothing;
        _pendingUserText = string.Empty;
    }

    /// <summary>
    /// Records one consumed signal into the conversation. Turn type comes from the input
    /// the signal arrived on — never from conversation parity.
    /// </summary>
    private void ApplySignal(ConsumedSignal item, List<GH_LlmToolCall> toolCallItems)
    {
        switch (item.ParamIndex)
        {
            case InResponseSignal:
                RecordAssistantTurn(item.Signal, toolCallItems);
                break;

            case InPromptSignal:
                // The payload IS the prompt. A bare Button press mints an empty payload —
                // Construct Signal is the manual path for attaching text to a trigger.
                if (!StringHelpers.IsNonBlank(item.Signal.Payload))
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Prompt signal carried no text — use Construct Signal to attach a payload to a manual trigger.");
                    break;
                }

                RecordUserText(item.Signal.Payload);
                break;

            case InFeedbackSignal:
                if (!StringHelpers.IsNonBlank(item.Signal.Payload))
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Feedback signal received with an empty payload.");
                    break;
                }

                RecordUserText(item.Signal.Payload);
                break;
        }
    }

    private void RecordAssistantTurn(PhySignal signal, List<GH_LlmToolCall> toolCallItems)
    {
        // Tool calls take priority over the payload text.
        var validToolCalls = toolCallItems
            .Where(g => g?.Value != null)
            .Select(g => g.Value)
            .ToList();

        ConversationMessage? message = null;
        if (validToolCalls.Count > 0)
        {
            var blocks = validToolCalls
                .Select(tc => (MessageContent)new ToolCallContent(tc.Id, tc.Name, tc.InputJson))
                .ToList();
            message = new ConversationMessage(Role.Assistant, blocks);
        }
        else if (StringHelpers.IsNonBlank(signal.Payload))
        {
            message = new ConversationMessage(Role.Assistant, signal.Payload);
        }

        if (message is null)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Response signal received but it carried no text or tool calls.");
            return;
        }

        try
        {
            // Guard only: under sequence ordering an assistant turn cannot legally follow
            // another assistant turn, so a throw here indicates a wiring mistake.
            _conversation = _conversation.Append(message);
            _recordedHistory = _recordedHistory.Append(message);

            if (_pendingOutcome == AppendOutcome.Nothing)
            {
                _pendingOutcome = AppendOutcome.AssistantTurn;
            }
        }
        catch (InvalidOperationException ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, ex.Message);
        }
    }

    private void RecordUserText(string text)
    {
        try
        {
            // Applied per conversation: each merges or appends based on its own last role
            // (they can diverge around compaction absorbs). Merging preserves the strict
            // role alternation providers require when two user-side events arrive in a row.
            _conversation = RecordUserInto(_conversation, text);
            _recordedHistory = RecordUserInto(_recordedHistory, text);
            _pendingOutcome = AppendOutcome.UserTurn;
            _pendingUserText = text;
        }
        catch (InvalidOperationException ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, ex.Message);
        }
    }

    private static Conversation RecordUserInto(Conversation conversation, string text) =>
        conversation.Count > 0 && conversation.Messages[^1].Role == Role.User
            ? conversation.MergeIntoLastUserMessage(text)
            : conversation.Append(new ConversationMessage(Role.User, text));

    private void EmitOutputs(IGH_DataAccess da)
    {
        // Skip SetData while unlatched so the outputs are truly empty, not null items.
        if (_latchedInstructions is not null)
        {
            da.SetData(0, new GH_Instructions(_latchedInstructions));
        }

        EmitSignal(da, 1, SuccessSignal);

        if (_latchedHistory is not null)
        {
            da.SetData(2, new GH_Conversation(_latchedHistory));
        }
    }

    private void OnSaveConversation(object? sender, EventArgs e)
    {
        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Save Conversation is not yet implemented.");
        ExpireSolution(true);
    }

    private void OnLoadConversation(object? sender, EventArgs e)
    {
        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Load Conversation is not yet implemented.");
        ExpireSolution(true);
    }
}
