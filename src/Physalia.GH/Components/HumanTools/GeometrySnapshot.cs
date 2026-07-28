// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Windows.Forms;
using Physalia.Core.HumanTools;

namespace Physalia.GH.Components;

/// <summary>
/// Emits a <see cref="GeometrySnapshotTool"/> that arms the chat window's geometry button:
/// while it is wired into the Conversation Log's Human Tools input and a transmitter has generated
/// geometry (the script a Py Transmitter targets, or components a Component Transmitter placed),
/// the prompt box shows a geometry button. Pressing it captures a Rhino viewport snapshot of that
/// geometry — the same capture the Geometry Observation guardrail performs. By default the snapshot
/// is sent right away as its own user message carrying a built-in default message (editable from the
/// chat window's grounding panel); with "Send With Default Message" unchecked it is instead attached
/// to the prompt box like a pasted image, for the human to caption themselves. Has no inputs.
/// </summary>
public class GeometrySnapshot : HumanToolComponentBase
{
    // Whether the captured snapshot is sent immediately with the tool's default message (true, the
    // legacy behaviour) or attached to the prompt box for the human to caption (false). A menu item,
    // not an input: HumanToolComponentBase seals RegisterInputParams — a human tool's whole contract
    // is its presence on the Conversation Log's Human Tools input. The chat window's grounding panel
    // drives the same field through SetSendWithMessage, so the two surfaces never disagree.
    private bool _sendWithMessage = true;

    /// <summary>
    /// Initializes a new instance of the <see cref="GeometrySnapshot"/> class.
    /// </summary>
    public GeometrySnapshot()
        : base("Geometry Snapshot", "GeoSnap", "Adds a geometry button to the chat window that captures a viewport snapshot of the transmitter-generated geometry — sent as its own message, or attached to the prompt box for you to caption.")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("D5B8F2A6-7E31-4C94-A0D8-3F6E1B9C5A27");

    /// <inheritdoc/>
    protected override HumanTool Tool => new GeometrySnapshotTool(GeometrySnapshotTool.DefaultMessage, _sendWithMessage);

    /// <summary>
    /// Sets whether the snapshot is sent with the default message or attached to the prompt box, and
    /// re-solves so the re-emitted tool carries the change to the Conversation Log (and from there to
    /// the chat window). The counterpart of the context-menu toggle — same field, so the canvas
    /// checkmark and the chat window's switch are two views of one setting. Called from the chat
    /// window on the UI thread.
    /// </summary>
    /// <param name="on">True to send the snapshot as its own message with the default text; false to attach it to the prompt box.</param>
    public void SetSendWithMessage(bool on)
    {
        if (_sendWithMessage == on)
        {
            return;
        }

        _sendWithMessage = on;
        ExpireSolution(true);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Unchecking this turns the geometry button into an attach affordance: the snapshot lands in the
    /// prompt box like a pasted image and the human writes their own message, which is what you want
    /// when the snapshot is context for a specific question rather than a standing "look at what you
    /// built" nudge. The default message is unused in that mode, so the chat window disables its
    /// editor. Mirrored by the same switch on the chat window's Geometry Snapshot page.
    /// </remarks>
    public override void AppendAdditionalMenuItems(ToolStripDropDown menu)
    {
        base.AppendAdditionalMenuItems(menu);
        Menu_AppendItem(
            menu,
            "Send With Default Message",
            (_, _) => SetSendWithMessage(!_sendWithMessage),
            enabled: true,
            @checked: _sendWithMessage);
    }

    /// <inheritdoc/>
    public override bool Write(GH_IO.Serialization.GH_IWriter writer)
    {
        writer.SetBoolean("SendWithMessage", _sendWithMessage);
        return base.Write(writer);
    }

    /// <inheritdoc/>
    public override bool Read(GH_IO.Serialization.GH_IReader reader)
    {
        // Absent key = a file written before the toggle existed: keep the send-immediately behaviour.
        _sendWithMessage = !reader.ItemExists("SendWithMessage") || reader.GetBoolean("SendWithMessage");
        return base.Read(reader);
    }
}
