// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Physalia.Core.Providers;
using Physalia.GH.Attributes;
using Physalia.GH.ParamTypes;

namespace Physalia.GH.Components;

/// <summary>
/// The prompt class.
/// </summary>
public class Prompt : PhyBase
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
    /// Initializes a new instance of the <see cref="Prompt"/> class.
    /// </summary>
    public Prompt()
        : base("Prompt", "Prmpt", "Prompt the LLM", "Core")
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
        pManager.AddParameter(new ConversationGhParam(), "Conversation", "Conv", "The conversation history to be fed into Crest.", GH_ParamAccess.item);
    }

    /// <summary>
    /// This is the method that actually does the work.
    /// </summary>
    /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
    protected override void SolveInstance(IGH_DataAccess DA)
    {
        DA.SetData(0, new ConversationGoo(_conversation));
    }

    /// <summary>
    /// Assigns the custom <see cref="PromptAttrib"/> attribute class to this component.
    /// </summary>
    public override void CreateAttributes() => m_attributes = new PromptAttrib(this);

    /// <summary>
    /// Appends a Reset Conversation item to the standard component context menu.
    /// </summary>
    /// <param name="menu">The context menu being built.</param>
    public override void AppendAdditionalMenuItems(System.Windows.Forms.ToolStripDropDown menu)
    {
        base.AppendAdditionalMenuItems(menu);

        var item = Menu_AppendItem(menu, "Reset Conversation", OnResetConversation, _conversation.Messages.Count > 0);
        item.ToolTipText = "Clear all conversation history.";
    }

    /// <summary>
    /// Persists the prompt text and conversation history so they survive save and reload.
    /// </summary>
    /// <param name="writer">The GH_IWriter to write to.</param>
    /// <returns>true.</returns>
    public override bool Write(GH_IWriter writer)
    {
        writer.SetString("PromptText", UserPromptText ?? string.Empty);
        writer.SetInt32("MsgCount", _conversation.Messages.Count);
        for (int i = 0; i < _conversation.Messages.Count; i++)
        {
            writer.SetString($"Role_{i}", _conversation.Messages[i].Role);
            writer.SetString($"Content_{i}", _conversation.Messages[i].Content);
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
                string content = string.Empty;
                reader.TryGetString($"Role_{i}", ref role);
                reader.TryGetString($"Content_{i}", ref content);

                if (role == "user")
                    _conversation.AddUserMessage(content);
                else if (role == "assistant")
                    _conversation.AddAssistantMessage(content);
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
            return;

        _conversation.AddUserMessage(UserPromptText);
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
