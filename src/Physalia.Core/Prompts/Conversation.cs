// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Physalia.Core.Prompts;

/// <summary>
/// Maintains the ordered history of a multi-turn LLM conversation.
/// Shared by reference through the Grasshopper graph so that CREST can
/// append assistant responses without requiring a back-reference to the Prompt component.
/// </summary>
public class Conversation
{
    // FIELDS ==========================================================================================

    private readonly List<ConversationMessage> _messages = new();

    // PROPERTIES =======================================================================================

    /// <summary>
    /// Gets the ordered list of messages in this conversation.
    /// </summary>
    public IReadOnlyList<ConversationMessage> Messages => _messages;

    /// <summary>
    /// Gets the content of the most recent user message, or <see langword="null"/> if none exists.
    /// </summary>
    public string? LastUserMessage => LastOf("user");

    /// <summary>
    /// Gets the content of the most recent assistant message, or <see langword="null"/> if none exists.
    /// </summary>
    public string? LastAssistantMessage => LastOf("assistant");

    // PUBLIC METHODS =======================================================================================

    /// <summary>
    /// Appends a user turn to the conversation.
    /// </summary>
    /// <param name="content">The text content of the user message.</param>
    public void AddUserMessage(string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        _messages.Add(new ConversationMessage("user", content));
    }

    /// <summary>
    /// Appends an assistant turn to the conversation.
    /// </summary>
    /// <param name="content">The raw response string returned by the LLM.</param>
    /// <param name="statusMessage">An optional short human-readable summary displayed in the conversation history UI.</param>
    public void AddAssistantMessage(string content, string? statusMessage = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        _messages.Add(new ConversationMessage("assistant", content, statusMessage));
    }

    /// <summary>
    /// Removes all messages from the conversation.
    /// </summary>
    public void Clear() => _messages.Clear();

    // PRIVATE METHODS =======================================================================================

    private string? LastOf(string role) =>
        _messages.LastOrDefault(m => m.Role == role)?.Content;
}
