// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Windows.Forms;

namespace Physalia.GH.Components;

/// <summary>
/// Base class for the two snapshot human tools — Geometry Snapshot and View Snapshot. Both arm a
/// button in the chat window that captures the Rhino viewport, and both let the human choose what
/// happens to that capture: send it immediately as its own message carrying the tool's default text,
/// or attach it to the prompt box like a pasted image and caption it themselves. That choice is the
/// only state a snapshot tool carries, so it lives here; what gets captured (and whether the button
/// needs arming) is the subclass's business.
/// </summary>
public abstract class SnapshotToolComponentBase : HumanToolComponentBase
{
    // Whether the captured snapshot is sent immediately with the tool's default message (true, the
    // default) or attached to the prompt box for the human to caption (false). A menu item, not an
    // input: HumanToolComponentBase seals RegisterInputParams — a human tool's whole contract is its
    // presence on the Conversation Log's Human Tools input. The chat window's snapshot page drives the
    // same field through SetSendWithMessage, so the two surfaces never disagree.
    private bool _sendWithMessage = true;

    /// <summary>
    /// Initializes a new instance of the <see cref="SnapshotToolComponentBase"/> class.
    /// </summary>
    /// <param name="name">The component display name.</param>
    /// <param name="nickname">The component nickname.</param>
    /// <param name="description">The component description.</param>
    protected SnapshotToolComponentBase(string name, string nickname, string description)
        : base(name, nickname, description)
    {
    }

    /// <summary>
    /// Gets a value indicating whether the capture is sent immediately as its own message carrying the
    /// tool's default text, rather than attached to the prompt box for the human to caption.
    /// </summary>
    protected bool SendWithMessage => _sendWithMessage;

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
    /// Unchecking this turns the snapshot button into an attach affordance: the capture lands in the
    /// prompt box like a pasted image and the human writes their own message, which is what you want
    /// when the snapshot is context for a specific question rather than a standing "look at this"
    /// nudge. The default message is unused in that mode, so the chat window disables its editor.
    /// Mirrored by the same switch on the chat window's page for this tool.
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
