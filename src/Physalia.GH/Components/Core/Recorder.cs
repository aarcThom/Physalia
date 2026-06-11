// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using System.Collections.Generic;
using System.Windows.Forms;
using Grasshopper.Kernel;
using Physalia.Core.Common;
using Physalia.Core.ConvoInstruct;
using Physalia.GH.Goo;
using Physalia.GH.Parameters;
using System.Linq;

namespace Physalia.GH.Components;

/// <summary>
/// Maintains the full conversation history as an append-only log.
/// Arbitrates between forward data flow (prompt) and returning Feedback signals.
/// An optional Conversation override replaces the active conversation (for compaction)
/// while the Recorded History output preserves every message ever seen.
///
/// <para>Participates in the lifecycle state machine: a trigger rising edge appends the
/// next turn and enters Active; after the visible delay the Instructions and Recorded
/// History outputs latch. The Trigger output pulses only when a user message was
/// appended — assistant turns latch quietly so a Reasoner wired off the pulse cannot
/// re-fire itself in an infinite loop.</para>
/// </summary>
public class Recorder : StatefulComponentBase
{
    private Conversation _conversation = Conversation.Empty;
    private Conversation _recordedHistory = Conversation.Empty;
    private Conversation? _lastOverride;
    private string _lastPrompt = string.Empty;
    private string _lastPhysaliaPrompt = string.Empty;

    // Latched outputs; null while Empty or Active so the wires are genuinely blank.
    private Instructions? _latchedInstructions;
    private Conversation? _latchedHistory;

    // Set ONLY by our own scheduled callback so the latch runs after the visible delay.
    private bool _doLatch;
    private AppendOutcome _pendingOutcome;

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
    protected override bool RestoreLatchedStateOnLoad => false;

    /// <inheritdoc/>
    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddTextParameter("System Prompt", "S", "System prompt from Composer.", GH_ParamAccess.item, string.Empty);
        pManager.AddTextParameter("Prompt", "P", "User prompt from Prompter.", GH_ParamAccess.item, string.Empty);
        pManager.AddTextParameter("LLM Response", "L", "LLM text response from Reasoner, recorded as an assistant message.", GH_ParamAccess.item, string.Empty);
        pManager.AddTextParameter("Physalia Prompt", "PP", "Feedback from linters, GH errors, or visual inspection. Appended as a User message after an Assistant turn.", GH_ParamAccess.item, string.Empty);
        pManager.AddParameter(new Param_LlmToolCall(), "Tool Call", "TC", "Tool calls from Reasoner to record as an assistant message.", GH_ParamAccess.list);
        pManager.AddBooleanParameter("Trigger", "T", "Trigger from Prompter or Feedback. Initiates downstream solve.", GH_ParamAccess.item, false);
        pManager.AddParameter(new Param_Conversation(), "Conversation", "C", "Optional compacted conversation. Replaces the active conversation while all messages are preserved in Recorded History.", GH_ParamAccess.item);

        pManager[2].Optional = true;
        pManager[3].Optional = true;
        pManager[4].Optional = true;
        pManager[6].Optional = true;
    }

    /// <inheritdoc/>
    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddParameter(new Param_Instructions(), "Instructions", "I", "Conversation history and system prompt bundled for inference. Latched after each run.", GH_ParamAccess.item);
        pManager.AddBooleanParameter("Trigger", "T", "Pulses true for one solve when a user message was appended.", GH_ParamAccess.item);
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
        string prompt = string.Empty;
        string llmResponse = string.Empty;
        string physaliaPrompt = string.Empty;
        var toolCallItems = new List<GH_LlmToolCall>();
        bool trigger = false;

        DA.GetData(0, ref systemPrompt);
        DA.GetData(1, ref prompt);
        DA.GetData(2, ref llmResponse);
        DA.GetData(3, ref physaliaPrompt);
        DA.GetDataList(4, toolCallItems);
        if (!DA.GetData(5, ref trigger)) return;

        // Apply compacted conversation override when a new one arrives. This lives outside
        // the state machine — compaction is a data transformation, not a run outcome.
        var overrideGoo = new GH_Conversation();
        bool hasOverride = DA.GetData(6, ref overrideGoo);
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

        if (_doLatch)
        {
            // LATCH PASS — runs only from our own scheduled signal, after the visible delay.
            _doLatch = false;
            _latchedInstructions = new Instructions(systemPrompt, _conversation);
            _latchedHistory = _recordedHistory;

            switch (_pendingOutcome)
            {
                case AppendOutcome.UserTurn:
                    LatchSuccess(pulse: true);
                    break;
                case AppendOutcome.AssistantTurn:
                    // Quiet success: a Reasoner wired off the pulse must not re-fire
                    // after its own response is recorded.
                    LatchSuccess(pulse: false);
                    break;
                default:
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Trigger fired but there was nothing new to record.");
                    LatchFailure(pulse: false);
                    break;
            }

            EmitOutputs(DA);
            return;
        }

        if (DetectRisingEdge(trigger) && State != SolveState.Active)
        {
            // Appends must happen in this solve: pulse-borne inputs (e.g. a Feedback
            // Collector's Physalia Prompt) only exist during the triggering solution.
            // Only the latch and pulse are deferred by the visible delay.
            EnterActive();
            _pendingOutcome = AppendNextTurn(prompt, llmResponse, physaliaPrompt, toolCallItems);
            ScheduleStateSolve(SolveDelayMs, () => _doLatch = true);
            EmitOutputs(DA);
            return;
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
        _lastPrompt = string.Empty;
        _lastPhysaliaPrompt = string.Empty;
        _doLatch = false;
        _pendingOutcome = AppendOutcome.Nothing;
    }

    /// <summary>
    /// Appends the next conversation turn from the current inputs and reports what,
    /// if anything, was recorded.
    /// </summary>
    private AppendOutcome AppendNextTurn(string prompt, string llmResponse, string physaliaPrompt, List<GH_LlmToolCall> toolCallItems)
    {
        // Determine whether the next turn should be User or Assistant.
        Role nextRole = _conversation.Count == 0 || _conversation.Messages[_conversation.Count - 1].Role == Role.Assistant
            ? Role.User
            : Role.Assistant;

        if (nextRole == Role.Assistant)
        {
            // Assistant turn: tool calls take priority over plain text response.
            var validToolCalls = toolCallItems
                .Where(g => g?.Value != null)
                .Select(g => g.Value)
                .ToList();

            if (validToolCalls.Count > 0)
            {
                var blocks = validToolCalls
                    .Select(tc => (MessageContent)new ToolCallContent(tc.Id, tc.Name, tc.InputJson))
                    .ToList();

                return TryAppend(new ConversationMessage(Role.Assistant, blocks))
                    ? AppendOutcome.AssistantTurn
                    : AppendOutcome.Nothing;
            }

            if (StringHelpers.IsNonBlank(llmResponse))
            {
                return TryAppend(new ConversationMessage(Role.Assistant, llmResponse))
                    ? AppendOutcome.AssistantTurn
                    : AppendOutcome.Nothing;
            }

            return AppendOutcome.Nothing;
        }

        // User turn: Physalia Prompt takes priority; Prompt is the fallback.
        bool physaliaIsNew = StringHelpers.IsNonBlank(physaliaPrompt)
            && !StringHelpers.AreEquivalent(physaliaPrompt, _lastPhysaliaPrompt);
        bool promptIsNew = StringHelpers.IsNonBlank(prompt)
            && !StringHelpers.AreEquivalent(prompt, _lastPrompt);

        string? userText = physaliaIsNew ? physaliaPrompt
            : promptIsNew ? prompt
            : null;

        if (userText is null)
        {
            return AppendOutcome.Nothing;
        }

        if (!TryAppend(new ConversationMessage(Role.User, userText)))
        {
            return AppendOutcome.Nothing;
        }

        if (physaliaIsNew)
            _lastPhysaliaPrompt = physaliaPrompt;
        else
            _lastPrompt = prompt;

        return AppendOutcome.UserTurn;
    }

    private bool TryAppend(ConversationMessage message)
    {
        try
        {
            _conversation = _conversation.Append(message);
            _recordedHistory = _recordedHistory.Append(message);
            return true;
        }
        catch (InvalidOperationException ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, ex.Message);
            return false;
        }
    }

    private void EmitOutputs(IGH_DataAccess da)
    {
        // Skip SetData while unlatched so the outputs are truly empty, not null items.
        if (_latchedInstructions is not null)
        {
            da.SetData(0, new GH_Instructions(_latchedInstructions));
        }

        da.SetData(1, SuccessPulse);

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
