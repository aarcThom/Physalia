// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Physalia.Core.Prompts;
using Physalia.GH.Attributes;
using Physalia.GH.ParamTypes;

namespace Physalia.GH.Components;

/// <summary>
/// The COMPOSER component. Manages the conversation history and provides the prompt UI.
/// </summary>
public class Composer : PhyBase
{
    // FIELDS ==========================================================================================
    private readonly Conversation _conversation = new();

    // PROPERTIES =======================================================================================

    /// <summary>
    /// Gets the conversation history managed by this component.
    /// </summary>
    public Conversation Conversation => _conversation;

    /// <summary>
    /// The text to be displayed in the prompt input box when the user is not currently entering text.
    /// </summary>
    public string UserPromptText = string.Empty;

    /// <summary>
    /// Gets the unique ID for this component. Do not change this ID after release.
    /// </summary>
    public override Guid ComponentGuid
    {
        get { return new Guid("2A8562D9-9866-461C-8A2D-2F7E4F40026B"); }
    }

    // CONSTRUCTOR =======================================================================================

    /// <summary>
    /// Initializes a new instance of the <see cref="Composer"/> class.
    /// </summary>
    public Composer()
        : base("Composer", "Comp", "Compose and manage the LLM conversation.", "Core")
    {
    }

    // GH COMPONENT OVERRIDES ============================================================================================

    /// <summary>
    /// Registers all the input parameters for this component.
    /// </summary>
    protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
    {
    }

    /// <summary>
    /// Registers all the output parameters for this component.
    /// </summary>
    protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
    {
        pManager.AddParameter(new ConversationGhParam(), "Conversation", "Conv", "The conversation history to be fed into Transmitter.", GH_ParamAccess.item);
    }

    /// <summary>
    /// This is the method that actually does the work.
    /// </summary>
    /// <param name="DA">The DA object is used to retrieve from inputs and store in connectedComponents.</param>
    protected override void SolveInstance(IGH_DataAccess DA)
    {
        DA.SetData(0, new ConversationGoo(_conversation));
    }

    /// <summary>
    /// Assigns the custom <see cref="ComposerAttrib"/> attribute class to this component.
    /// </summary>
    public override void CreateAttributes() => m_attributes = new ComposerAttrib(this);

    /// <summary>
    /// Appends a Reset Conversation item to the standard component context menu.
    /// </summary>
    /// <param name="menu">The context menu being built.</param>
    public override void AppendAdditionalMenuItems(System.Windows.Forms.ToolStripDropDown menu)
    {
        base.AppendAdditionalMenuItems(menu);

        var item = Menu_AppendItem(menu, "Reset Conversation", OnResetConversation, _conversation.LlmMessages.Count > 0);
        item.ToolTipText = "Clear all conversation history.";
    }

    /// <summary>
    /// Persists the prompt text and conversation history so they survive save and reload.
    /// </summary>
    /// <param name="writer">The GH_IWriter to write to.</param>
    /// <returns>true.</returns>
    public override bool Write(GH_IWriter writer)
    {
        writer.SetString("PromptText", UserPromptText ?? string.Empty); // the unentered text in the input box

        writer.SetInt32("MsgCount", _conversation.LlmMessages.Count);

        for (int i = 0; i < _conversation.LlmMessages.Count; i++)
        {
            // the LLM readable messages
            writer.SetString($"LLM_Role_{i}", _conversation.LlmMessages[i].Role);
            writer.SetString($"LLM_Content_{i}", _conversation.LlmMessages[i].Content);
            writer.SetString($"LLM_Status_{i}", _conversation.LlmMessages[i].StatusMessage ?? string.Empty);

            // the human readable messages
            writer.SetString($"human_msg_{i}", _conversation.HumanMessages[i]);
        }

        return base.Write(writer);
    }

    /// <summary>
    /// Restores the prompt text and conversation history on load.
    /// </summary>
    /// <param name="reader">The GH_IReader to read from.</param>
    /// <returns>true.</returns>
    public override bool Read(GH_IReader reader)
    {
        string promptText = string.Empty;
        reader.TryGetString("PromptText", ref promptText);
        UserPromptText = promptText;

        _conversation.Clear();

        int count = 0;
        if (reader.TryGetInt32("MsgCount", ref count))
        {
            for (int i = 0; i < count; i++)
            {
                string role = string.Empty;
                string llmContent = string.Empty;
                string humanMsg = string.Empty;
                reader.TryGetString($"LLM_Role_{i}", ref role);
                reader.TryGetString($"LLM_Content_{i}", ref llmContent);
                reader.TryGetString($"human_msg_{i}", ref humanMsg);

                if (role == "user")
                {
                    _conversation.AddUserMessage(llmContent, false, humanMsg);
                }
                else if (role == "assistant")
                {
                    string status = string.Empty;
                    reader.TryGetString($"Status_{i}", ref status);
                    _conversation.AddAssistantMessage(llmContent, string.IsNullOrEmpty(status) ? null : status);
                }
            }
        }

        return base.Read(reader);
    }

    // PUBLIC METHODS =======================================================================================

    /// <summary>
    /// Appends the current <see cref="UserPromptText"/> as a user turn in the conversation,
    /// clears the input box, and expires the solution to propagate the updated history.
    /// Does nothing if <see cref="UserPromptText"/> is null or whitespace.
    /// </summary>
    public void SubmitUserMessage()
    {
        if (string.IsNullOrWhiteSpace(UserPromptText))
        {
            return;
        }

        _conversation.AddUserMessage(UserPromptText, false);
        _conversation.Trigger = true;
        UserPromptText = string.Empty;
        ExpireSolution(true);
    }

    // PRIVATE METHODS =======================================================================================
    private void OnResetConversation(object sender, EventArgs e)
    {
        _conversation.Clear();
        OnPingDocument()?.ScheduleSolution(1, _ => ExpireSolution(true));
    }
}
