// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Physalia.Core.Common;

namespace Physalia.Core.ConvoInstruct;

/// <summary>
/// An immutable, append-only conversation history.
/// GH components replace their reference on each append; Core never mutates in place.
/// The system prompt is NOT stored here — it is passed at inference call time.
/// </summary>
public sealed class Conversation
{
    private readonly IReadOnlyList<ConversationMessage> _messages;

    private Conversation(IReadOnlyList<ConversationMessage> messages)
    {
        _messages = messages;
    }

    /// <summary>
    /// Gets an empty conversation with no turns.
    /// </summary>
    public static Conversation Empty { get; } = new Conversation(Array.Empty<ConversationMessage>());

    /// <summary>
    /// Gets the ordered list of turns in this conversation.
    /// </summary>
    public IReadOnlyList<ConversationMessage> Messages => _messages;

    /// <summary>
    /// Gets the number of turns in this conversation.
    /// </summary>
    public int Count => _messages.Count;

    /// <summary>
    /// Returns a new <see cref="Conversation"/> with the given message appended.
    /// </summary>
    /// <param name="message">The message to append.</param>
    /// <returns>A new conversation with the message appended.</returns>
    /// <exception cref="ArgumentNullException">Thrown when message is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the message role is the same as the last message role,
    /// which would produce consecutive same-role turns.
    /// </exception>
    public Conversation Append(ConversationMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (_messages.Count > 0 && _messages[^1].Role == message.Role)
        {
            throw new InvalidOperationException(
                $"Cannot append a {message.Role} message after another {message.Role} message. " +
                "Consecutive same-role turns are not permitted.");
        }

        var next = new List<ConversationMessage>(_messages.Count + 1);
        next.AddRange(_messages);
        next.Add(message);
        return new Conversation(next);
    }

    /// <summary>
    /// Returns a new <see cref="Conversation"/> whose last message — which must be a
    /// <see cref="Role.User"/> turn — gains an appended <see cref="TextContent"/> block.
    /// Used when a second user-side event (e.g. feedback) arrives before an assistant
    /// turn: providers require alternating roles, so the texts merge into one turn.
    /// </summary>
    /// <param name="text">The text to append to the last user message.</param>
    /// <param name="incomingIsFeedback">Whether the arriving turn is auto-generated feedback.</param>
    /// <param name="incomingSources">The components that produced the arriving turn, if any.</param>
    /// <returns>A new conversation with the merged last turn.</returns>
    /// <exception cref="ArgumentException">Thrown when text is null or blank.</exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the conversation is empty or the last message is not a user turn.
    /// </exception>
    public Conversation MergeIntoLastUserMessage(
        string text,
        bool incomingIsFeedback = false,
        IReadOnlyList<ComponentOrigin>? incomingSources = null)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("Cannot merge blank text into the conversation.", nameof(text));
        }

        return MergeIntoLastUserMessage(new MessageContent[] { new TextContent(text) }, incomingIsFeedback, incomingSources);
    }

    /// <summary>
    /// Returns a new <see cref="Conversation"/> whose last message — which must be a
    /// <see cref="Role.User"/> turn — gains the given content blocks appended. Used when a
    /// second user-side event arrives before an assistant turn (e.g. an image-bearing prompt
    /// after feedback): providers require alternating roles, so the blocks merge into one turn.
    /// </summary>
    /// <param name="blocks">The content blocks to append to the last user message.</param>
    /// <param name="incomingIsFeedback">
    /// Whether the arriving turn is auto-generated feedback. The merged turn counts as feedback only
    /// when BOTH sides were (matching the compaction merge rule): a human prompt merged onto a
    /// feedback turn is not machine-generated, and must not be presented as if it were.
    /// </param>
    /// <param name="incomingSources">
    /// The components that produced the arriving turn; unioned with the last turn's, so a merged turn
    /// names every node behind it.
    /// </param>
    /// <returns>A new conversation with the merged last turn.</returns>
    /// <exception cref="ArgumentException">Thrown when blocks is null or empty.</exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the conversation is empty or the last message is not a user turn.
    /// </exception>
    public Conversation MergeIntoLastUserMessage(
        IReadOnlyList<MessageContent> blocks,
        bool incomingIsFeedback = false,
        IReadOnlyList<ComponentOrigin>? incomingSources = null)
    {
        if (blocks is null || blocks.Count == 0)
        {
            throw new ArgumentException("Cannot merge an empty content list into the conversation.", nameof(blocks));
        }

        if (_messages.Count == 0 || _messages[^1].Role != Role.User)
        {
            throw new InvalidOperationException(
                "Cannot merge into the last user message: the conversation is empty or the last turn is not a user turn.");
        }

        ConversationMessage last = _messages[^1];
        var mergedContent = new List<MessageContent>(last.Content.Count + blocks.Count);
        mergedContent.AddRange(last.Content);
        mergedContent.AddRange(blocks);

        var next = new List<ConversationMessage>(_messages.Count);
        next.AddRange(_messages.Take(_messages.Count - 1));
        next.Add(new ConversationMessage(Role.User, mergedContent)
        {
            IsFeedback = last.IsFeedback && incomingIsFeedback,
            Sources = UnionSources(last.Sources, incomingSources),
        });
        return new Conversation(next);
    }

    // Deduplicated by guid, order preserved: the same component feeding a turn twice is one badge.
    private static IReadOnlyList<ComponentOrigin> UnionSources(
        IReadOnlyList<ComponentOrigin> existing,
        IReadOnlyList<ComponentOrigin>? incoming)
    {
        if (incoming is null || incoming.Count == 0)
        {
            return existing;
        }

        var union = new List<ComponentOrigin>(existing);
        var seen = new HashSet<Guid>(existing.Select(o => o.Id));
        foreach (ComponentOrigin origin in incoming)
        {
            if (seen.Add(origin.Id))
            {
                union.Add(origin);
            }
        }

        return union;
    }
}
