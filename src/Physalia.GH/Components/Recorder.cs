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

namespace Physalia.GH.Components;

/// <summary>
/// Maintains the full conversation history as an append-only log.
/// Arbitrates between forward data flow (prompt) and returning Feedback signals.
/// </summary>
public class Recorder : PhyBase
{
    private Conversation _conversation = Conversation.Empty;
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
        pManager.AddTextParameter("system prompt", "S", "System prompt from Composer.", GH_ParamAccess.item, string.Empty);
        pManager.AddTextParameter("prompt", "P", "User prompt from Prompter.", GH_ParamAccess.item, string.Empty);
        pManager.AddTextParameter("feedback", "F", "Feedback strings from paired Feedback components.", GH_ParamAccess.list);
        pManager.AddBooleanParameter("trigger", "T", "Trigger from Prompter or Feedback. Initiates downstream solve.", GH_ParamAccess.item, false);

        pManager[2].Optional = true;
    }

    /// <inheritdoc/>
    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddParameter(new Param_Instructions(), "instructions", "I", "Conversation history and system prompt bundled for inference.", GH_ParamAccess.item);
        pManager.AddBooleanParameter("trigger", "T", "Trigger passed through to Reasoner.", GH_ParamAccess.item);
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
        var feedbackItems = new List<string>();
        bool trigger = false;

        DA.GetData(0, ref systemPrompt);
        DA.GetData(1, ref prompt);
        DA.GetDataList(2, feedbackItems);
        if (!DA.GetData(3, ref trigger)) return;

        if (trigger && !_lastTrigger)
        {
            // Feedback takes priority — when feedback is present, the forward prompt is blocked.
            if (feedbackItems.Count > 0)
            {
                foreach (string fb in feedbackItems)
                {
                    if (!StringHelpers.IsNonBlank(fb)) continue;

                    Role nextRole = _conversation.Count == 0 || _conversation.Messages[_conversation.Count - 1].Role == Role.Assistant
                        ? Role.User
                        : Role.Assistant;

                    try
                    {
                        _conversation = _conversation.Append(new ConversationMessage(nextRole, fb));
                    }
                    catch (InvalidOperationException ex)
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, ex.Message);
                    }
                }
            }
            else if (StringHelpers.IsNonBlank(prompt) && !StringHelpers.AreEquivalent(prompt, _lastPrompt))
            {
                try
                {
                    _conversation = _conversation.Append(new ConversationMessage(Role.User, prompt));
                    _lastPrompt = prompt;
                }
                catch (InvalidOperationException ex)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, ex.Message);
                }
            }
        }

        _lastTrigger = trigger;

        DA.SetData(0, new GH_Instructions(new Instructions(systemPrompt, _conversation)));
        DA.SetData(1, trigger);
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
        ExpireSolution(true);
    }
}
