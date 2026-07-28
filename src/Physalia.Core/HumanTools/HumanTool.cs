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
/// Enables image attachments in the chat window's prompt box — paste, drag-and-drop, and the
/// file picker. Without this tool wired, image intake is fully disabled and prompts are
/// text-only. A marker record: the images themselves ride the submitted user turn as content
/// blocks, so there is nothing to configure here.
/// </summary>
public sealed record AddImageTool : HumanTool;
