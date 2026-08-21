// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using Physalia.Core.HumanTools;

namespace Physalia.GH.Components;

/// <summary>
/// Emits an <see cref="ExportConversationTool"/> that puts an export button at the top of the chat
/// window: pressing it saves the viewed conversation as a plain-text transcript — every turn
/// verbatim (assistant thinking and raw JSON replies included), each tool call with its input and
/// result. The raw material for a bug report. Wire it into the Conversation Log's Human Tools
/// input; without it the chat window offers no export. Has no inputs.
/// </summary>
public class ExportConversation : HumanToolComponentBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ExportConversation"/> class.
    /// </summary>
    public ExportConversation()
        : base("Export Conversation", "Export", "Adds a button to the chat window that saves the conversation you are looking at as a plain text file.")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("91C5514E-87C5-45FD-97A9-F3F0BFBDC136");

    /// <inheritdoc/>
    protected override string ToolOutputDescription =>
        "Puts the export button in the chat window. Wire into a Conversation Log's Human Tools input.";

    /// <inheritdoc/>
    protected override HumanTool Tool => new ExportConversationTool();
}
