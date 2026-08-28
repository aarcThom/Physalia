// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Windows.Forms;

namespace Physalia.GH.Components;

/// <summary>
/// Base class for the two snapshot human tools — Geometry Snapshot and View Snapshot. Both arm a
/// button in the chat window that captures the Rhino viewport, and both let the human choose what
/// happens to that capture: attach it to the prompt box like a pasted image and caption it themselves
/// (the default), or send it immediately as its own message carrying the tool's default text. That
/// choice, and the wording that rides with the capture, are the whole of what a snapshot tool carries,
/// so both live here; what gets captured (and whether the button needs arming) is the subclass's
/// business. Every future snapshot tool inherits the default from this field, not from its own
/// constructor.
///
/// <para>Both are settings, so both are serialized on the component rather than held by the
/// Conversation Log: a configured snapshot tool is meant to travel — copied into another harness, or
/// shipped inside a preset — with its wording intact.</para>
/// </summary>
public abstract class SnapshotToolComponentBase : HumanToolComponentBase
{
    // Whether the captured snapshot is sent immediately with the tool's default message (true) or
    // attached to the prompt box for the human to caption (false, the default). Attaching is the
    // default because a snapshot is nearly always context for a particular question: sending it on
    // its own with standing wording spends a turn saying nothing the human meant to say, and the
    // human has to wait for that turn before asking what they were actually looking at. A menu item,
    // not an input: HumanToolComponentBase seals RegisterInputParams — a human tool's whole contract
    // is its presence on the Conversation Log's Human Tools input. The chat window's snapshot page
    // drives the same field through SetSendWithMessage, so the two surfaces never disagree.
    private bool _sendWithMessage;

    // The text that rides with the capture instead of the tool's built-in default. Null = use the
    // default (the common case). Edited from this tool's page in the chat window, which reaches it
    // through the Conversation Log; it lives HERE, beside the send-or-attach flag it belongs with, so
    // a configured snapshot tool carries its wording into another harness and into a preset.
    private string? _messageOverride;

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
    /// Gets the text that rides with the capture in place of the tool's default message, or null when
    /// the default is used.
    /// </summary>
    public string? MessageOverride => _messageOverride;

    /// <summary>
    /// Sets the text that rides with the capture, or clears it back to the tool's default message.
    /// Called from the chat window (through the Conversation Log) on the UI thread; does not re-solve,
    /// because the tool on the wire still advertises its default — the substitution happens where the
    /// message is composed, and the Conversation Log reads this on its own next solve.
    /// </summary>
    /// <param name="message">The override text, or null (or blank) to use the tool's default message.</param>
    public void SetMessageOverride(string? message) =>
        _messageOverride = string.IsNullOrWhiteSpace(message) ? null : message;

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
        SettingArchive.WriteOptionalString(writer, "MessageOverride", _messageOverride);
        return base.Write(writer);
    }

    /// <inheritdoc/>
    public override bool Read(GH_IO.Serialization.GH_IReader reader)
    {
        // Absent key = a file written before the toggle existed, so it never expressed a preference:
        // it gets the current default (attach) like a freshly placed tool, not the behaviour that
        // happened to be hard-wired at the time.
        _sendWithMessage = reader.ItemExists("SendWithMessage") && reader.GetBoolean("SendWithMessage");
        _messageOverride = SettingArchive.ReadOptionalString(reader, "MessageOverride");
        return base.Read(reader);
    }
}
