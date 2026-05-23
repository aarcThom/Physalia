// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

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
/// </summary>
public class Recorder : PhyBase
{
    private Conversation _conversation = Conversation.Empty;
    private Conversation _recordedHistory = Conversation.Empty;
    private Conversation? _lastOverride;
    private string _lastPrompt = string.Empty;
    private bool _lastTrigger;

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
    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddTextParameter("System Prompt", "S", "System prompt from Composer.", GH_ParamAccess.item, string.Empty);
        pManager.AddTextParameter("Prompt", "P", "User prompt from Prompter.", GH_ParamAccess.item, string.Empty);
        pManager.AddTextParameter("LLM Response", "L", "LLM text response from Reasoner, recorded as an assistant message.", GH_ParamAccess.item, string.Empty);
        pManager.AddParameter(new Param_LlmToolCall(), "Tool Call", "TC", "Tool calls from Reasoner to record as an assistant message.", GH_ParamAccess.list);
        pManager.AddBooleanParameter("Trigger", "T", "Trigger from Prompter or Feedback. Initiates downstream solve.", GH_ParamAccess.item, false);
        pManager.AddParameter(new Param_Conversation(), "Conversation", "C", "Optional compacted conversation. Replaces the active conversation while all messages are preserved in Recorded History.", GH_ParamAccess.item);

        pManager[2].Optional = true;
        pManager[3].Optional = true;
        pManager[5].Optional = true;
    }

    /// <inheritdoc/>
    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddParameter(new Param_Instructions(), "Instructions", "I", "Conversation history and system prompt bundled for inference.", GH_ParamAccess.item);
        pManager.AddBooleanParameter("Trigger", "T", "Trigger passed through to Reasoner.", GH_ParamAccess.item);
        pManager.AddParameter(new Param_Conversation(), "Recorded History", "H", "Full conversation history including all messages before and after compaction.", GH_ParamAccess.item);
    }

    /// <inheritdoc/>
    public override void AppendAdditionalMenuItems(ToolStripDropDown menu)
    {
        base.AppendAdditionalMenuItems(menu);
        Menu_AppendSeparator(menu);
        Menu_AppendItem(menu, "Save Conversation", OnSaveConversation);
        Menu_AppendItem(menu, "Load Conversation", OnLoadConversation);
        Menu_AppendItem(menu, "Clear Conversation", OnClearConversation);
    }

    /// <inheritdoc/>
    protected override void SolveInstance(IGH_DataAccess DA)
    {
        string systemPrompt = string.Empty;
        string prompt = string.Empty;
        string llmResponse = string.Empty;
        var toolCallItems = new List<GH_LlmToolCall>();
        bool trigger = false;

        DA.GetData(0, ref systemPrompt);
        DA.GetData(1, ref prompt);
        DA.GetData(2, ref llmResponse);
        DA.GetDataList(3, toolCallItems);
        if (!DA.GetData(4, ref trigger)) return;

        // Apply compacted conversation override when a new one arrives.
        var overrideGoo = new GH_Conversation();
        bool hasOverride = DA.GetData(5, ref overrideGoo);
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

        bool appendedUserMessage = false;

        if (trigger && !_lastTrigger)
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

                    var message = new ConversationMessage(Role.Assistant, blocks);
                    try
                    {
                        _conversation = _conversation.Append(message);
                        _recordedHistory = _recordedHistory.Append(message);
                    }
                    catch (InvalidOperationException ex)
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, ex.Message);
                    }
                }
                else if (StringHelpers.IsNonBlank(llmResponse))
                {
                    var message = new ConversationMessage(Role.Assistant, llmResponse);
                    try
                    {
                        _conversation = _conversation.Append(message);
                        _recordedHistory = _recordedHistory.Append(message);
                    }
                    catch (InvalidOperationException ex)
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, ex.Message);
                    }
                }
                // Trigger is NOT forwarded when recording an assistant turn.
            }
            else
            {
                // User turn: prompt only — llmResponse is never a fallback here.
                if (StringHelpers.IsNonBlank(prompt) && !StringHelpers.AreEquivalent(prompt, _lastPrompt))
                {
                    var message = new ConversationMessage(Role.User, prompt);
                    try
                    {
                        _conversation = _conversation.Append(message);
                        _recordedHistory = _recordedHistory.Append(message);
                        _lastPrompt = prompt;
                        appendedUserMessage = true;
                    }
                    catch (InvalidOperationException ex)
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, ex.Message);
                    }
                }
            }
        }

        _lastTrigger = trigger;

        DA.SetData(0, new GH_Instructions(new Instructions(systemPrompt, _conversation)));
        DA.SetData(1, appendedUserMessage);
        DA.SetData(2, new GH_Conversation(_recordedHistory));
    }

    private void OnSaveConversation(object sender, EventArgs e)
    {
        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Save Conversation is not yet implemented.");
        ExpireSolution(true);
    }

    private void OnLoadConversation(object sender, EventArgs e)
    {
        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Load Conversation is not yet implemented.");
        ExpireSolution(true);
    }

    private void OnClearConversation(object sender, EventArgs e)
    {
        _conversation = Conversation.Empty;
        _recordedHistory = Conversation.Empty;
        _lastOverride = null;
        _lastPrompt = string.Empty;
        ExpireSolution(true);
    }
}
