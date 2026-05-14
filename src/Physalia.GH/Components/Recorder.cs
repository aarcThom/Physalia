// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using Grasshopper.Kernel;
using Physalia.Core.Conversation;

namespace Physalia.GH.Components;

/// <summary>
/// Maintains an append-only conversation history and outputs it as a formatted display string.
/// </summary>
public class Recorder : PhyBase
{
    private Conversation _conversation = Conversation.Empty;
    private bool _lastAppend;
    private bool _lastClear;

    /// <summary>
    /// Initializes a new instance of the <see cref="Recorder"/> class.
    /// </summary>
    public Recorder()
        : base("Recorder", "Rec", "Maintains conversation history as an append-only log.", "Core")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("43A02F6D-D97D-4241-B4DD-067D7AE0D75E");

    /// <inheritdoc/>
    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddTextParameter("text", "t", "Text to append to the conversation.", GH_ParamAccess.item, string.Empty);
        pManager.AddBooleanParameter("append", "a", "Appends text to the conversation on rising edge. Alternates between User and Assistant roles.", GH_ParamAccess.item, false);
        pManager.AddBooleanParameter("clear", "c", "Clears the conversation on rising edge.", GH_ParamAccess.item, false);
    }

    /// <inheritdoc/>
    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddTextParameter("text out", "t", "Conversation formatted for display.", GH_ParamAccess.item);
    }

    /// <inheritdoc/>
    protected override void SolveInstance(IGH_DataAccess DA)
    {
        string text = string.Empty;
        bool append = false;
        bool clear = false;

        if (!DA.GetData(0, ref text)) return;
        if (!DA.GetData(1, ref append)) return;
        if (!DA.GetData(2, ref clear)) return;

        if (clear && !_lastClear)
        {
            _conversation = Conversation.Empty;
        }

        if (append && !_lastAppend && !string.IsNullOrEmpty(text))
        {
            Role nextRole = _conversation.Count == 0 || _conversation.Messages[_conversation.Count - 1].Role == Role.Assistant
                ? Role.User
                : Role.Assistant;

            _conversation = _conversation.Append(new ConversationMessage(nextRole, text));
        }

        _lastAppend = append;
        _lastClear = clear;

        DA.SetData(0, ConversationHelpers.ToDisplayString(_conversation));
    }
}
