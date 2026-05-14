// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Physalia.Core.Conversation;

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
}
