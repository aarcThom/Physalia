// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

namespace Physalia.Core.HumanTools;

/// <summary>
/// An affordance the human uses from the chat window — the counterpart of an LLM tool. A
/// discriminated union (closed set of records): each case describes one capability the chat
/// window offers the user when the matching component is wired into the Conversation Log's
/// Human Tools input. Human tools never contribute to the system prompt and are never
/// advertised to the model — they shape what the human can send, not what the model can call.
/// </summary>
public abstract record HumanTool;

/// <summary>
/// Enables the chat window's geometry button — the analog of the Geometry Observation
/// guardrail, sent on demand by the human instead of routed as a feedback signal. When this
/// tool is wired (and transmitter-generated geometry exists on the canvas) the prompt box shows
/// a geometry button; pressing it captures the Rhino viewport (framed on the generated
/// geometry).
/// <para>
/// What happens next depends on <see cref="SendWithMessage"/>: when true (the default) the
/// snapshot is sent immediately as its own user message carrying <see cref="Message"/> — a
/// snapshot is never attached to a typed prompt automatically. When false the snapshot is
/// instead attached to the prompt box like a pasted image and waits for the human to type
/// their own message; <see cref="Message"/> is then unused (and the chat window hides its
/// editor).
/// </para>
/// </summary>
/// <param name="Message">The text sent alongside the snapshot image. Unused when SendWithMessage is false.</param>
/// <param name="SendWithMessage">True to send the snapshot immediately as its own message carrying Message; false to attach it to the prompt box for the human to caption.</param>
public sealed record GeometrySnapshotTool(string Message, bool SendWithMessage = true) : HumanTool
{
    /// <summary>
    /// The message sent with the snapshot unless the user edits it in the chat window's
    /// grounding panel — the same text a Geometry Observation component would carry.
    /// </summary>
    public const string DefaultMessage =
        "Attached is a snapshot of the Rhino viewport showing the geometry currently generated "
        + "on the canvas. Ground your response in what has actually been built.";
}

/// <summary>
/// Enables the chat window's view button — the geometry-free sibling of
/// <see cref="GeometrySnapshotTool"/>. Where the geometry snapshot hunts down transmitter-generated
/// geometry and frames the camera on it, this captures the active Rhino viewport exactly as the human
/// is looking at it: no geometry scan, no zoom, no arming condition. Wired is armed — the button works
/// on an empty document, on referenced geometry Physalia never placed, and on a view the human has
/// composed by hand.
/// <para>
/// <see cref="SendWithMessage"/> works exactly as it does on the geometry snapshot: true (the
/// default) sends the capture immediately as its own user message carrying <see cref="Message"/>;
/// false attaches it to the prompt box like a pasted image and waits for the human's own caption
/// (<see cref="Message"/> is then unused and the chat window hides its editor).
/// </para>
/// </summary>
/// <param name="Message">The text sent alongside the view capture. Unused when SendWithMessage is false.</param>
/// <param name="SendWithMessage">True to send the capture immediately as its own message carrying Message; false to attach it to the prompt box for the human to caption.</param>
public sealed record ViewSnapshotTool(string Message, bool SendWithMessage = true) : HumanTool
{
    /// <summary>
    /// The message sent with the view capture unless the user edits it in the chat window's
    /// grounding panel. Deliberately says nothing about "generated" geometry — this snapshot makes
    /// no claim about where what you can see came from.
    /// </summary>
    public const string DefaultMessage =
        "Attached is a snapshot of the Rhino viewport exactly as I am looking at it right now. "
        + "Ground your response in what you can see.";
}

/// <summary>
/// Enables image attachments in the chat window's prompt box — paste, drag-and-drop, and the
/// file picker. Without this tool wired, image intake is fully disabled and prompts are
/// text-only. A marker record: the images themselves ride the submitted user turn as content
/// blocks, so there is nothing to configure here.
/// </summary>
public sealed record AddImageTool : HumanTool;

/// <summary>
/// Enables the chat window's export button — writes the viewed conversation to a plain-text
/// transcript (every turn verbatim, each tool call with its input and result), the raw material
/// for a bug report. A marker record: the transcript is built by the chat window from the
/// conversation it is already displaying, so there is nothing to configure here.
/// </summary>
public sealed record ExportConversationTool : HumanTool;

/// <summary>
/// Enables the chat window's signal-trace button, which opens the Physalia signal-trace window:
/// every signal that reached a wire this session, with its payload, carried content, and
/// consumption timeline. A marker record — the trace is a process-wide session log, so there is
/// nothing to configure here.
/// </summary>
public sealed record SignalTraceTool : HumanTool;

/// <summary>
/// Enables the chat window's image editor: a mark-up surface — freehand pen, text notes, arrows,
/// an eraser for the mark-up alone — laid over an image before it leaves for the model. Every
/// image the human can send passes through it while this tool is wired:
/// <list type="bullet">
/// <item><description>
/// A capture from any snapshot tool (<see cref="GeometrySnapshotTool"/>, <see cref="ViewSnapshotTool"/>,
/// and any future one) opens in the editor rather than going straight out. In attach mode, cancelling
/// still attaches the plain capture — only the mark-up is discarded. In send-with-default-message mode
/// there is nothing to fall back to, so cancelling abandons the capture entirely.
/// </description></item>
/// <item><description>
/// An image already in the prompt box (pasted, dropped, or picked, so an <see cref="AddImageTool"/>
/// is wired too) grows an edit button on its thumbnail, which reopens it in the editor.
/// </description></item>
/// </list>
/// A marker record: the mark-up is flattened into the image the human sends, so nothing about it
/// survives to configure. Without this tool wired, images travel exactly as captured.
/// </summary>
public sealed record ImageMarkUpTool : HumanTool;

/// <summary>
/// Puts the live token count in the corner of the chat window. A marker record: the count itself
/// is read off a Token Estimator on the canvas, and WHICH estimator is a canvas fact — the Token
/// Count component grip-links to one — so there is nothing carried here.
/// <para>
/// The counter exists only while this tool is wired AND its component is linked to an estimator.
/// The two halves are deliberately separate concerns: the Token Estimator counts, this says the
/// human wants to see the number. Without the tool the estimator still counts for everything
/// downstream of it (a Token Threshold, a compactor) and the window shows nothing.
/// </para>
/// </summary>
public sealed record TokenCountTool : HumanTool;
