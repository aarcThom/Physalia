// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel.Attributes;
using Physalia.GH.Components;

namespace Physalia.GH.Attributes;

/// <summary>
/// Attributes for the Chatbox component. Plain component drawing — the chat UI lives in a
/// standalone window — with a double-click to open that window.
/// </summary>
public class ChatboxAttrib : GH_ComponentAttributes
{
    private readonly Chatbox _chatbox;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChatboxAttrib"/> class.
    /// </summary>
    /// <param name="chatbox">The Chatbox component that owns these attributes.</param>
    public ChatboxAttrib(Chatbox chatbox)
        : base(chatbox)
    {
        _chatbox = chatbox;
    }

    /// <summary>
    /// Opens the chat window on double-click.
    /// </summary>
    /// <param name="sender">The Grasshopper canvas that raised the event.</param>
    /// <param name="e">The mouse event data.</param>
    /// <returns>Handled — the double-click is consumed to open the window.</returns>
    public override GH_ObjectResponse RespondToMouseDoubleClick(GH_Canvas sender, GH_CanvasMouseEvent e)
    {
        _chatbox.OpenWindow();
        return GH_ObjectResponse.Handled;
    }
}
