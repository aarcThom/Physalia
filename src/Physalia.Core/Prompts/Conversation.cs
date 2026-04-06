// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;

namespace Physalia.Core.Prompts;

/// <summary>
/// Maintains the ordered history of a multi-turn LLM conversation.
/// Shared by reference through the Grasshopper graph so that CREST can
/// append assistant responses without requiring a back-reference to the Prompt component.
/// </summary>
public class Conversation
{
    // FIELDS ==========================================================================================
    private readonly List<ConversationMessage> _llmMessages = new ();

    // need to keep track of what is an autofix error message. It's really weird to have an autofix message
    // submitted under 'you'.
    private readonly List<string> _humanMessages = new ();

    // PROPERTIES =======================================================================================

    /// <summary>
    /// Gets the ordered list of messages in this conversation. Formatted for the LLM.
    /// </summary>
    public IReadOnlyList<ConversationMessage> LlmMessages => _llmMessages;

    /// <summary>
    /// Gets the ordered list of messages formatted for the user.
    /// </summary>
    public IReadOnlyList<string> HumanMessages => _humanMessages;

    /// <summary>
    /// Gets or sets a value indicating whether the next solve should trigger an API call in CREST.
    /// Set by the Prompt component when the user submits via Shift+Enter; consumed and reset by CREST.
    /// </summary>
    public bool Trigger { get; set; }

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
    /// <param name="isAutoFixError">Set False is user input, True if from Autofix.</param>
    /// <param name="restoreHuman">Use when deserializing when opening saved document. If not null, isAutoFixError is ignored, and string is set.</param>
    public void AddUserMessage(string content, bool isAutoFixError, string? restoreHuman = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        _llmMessages.Add(new ConversationMessage("user", content));

        if (restoreHuman != null)
        {
            _humanMessages.Add(restoreHuman);
        }
        else
        {
            var humanRole = isAutoFixError ? "Physalia" : "You";
            _humanMessages.Add(FormatHumanMessage(humanRole, content));
        }
    }

    /// <summary>
    /// Appends an assistant turn to the conversation.
    /// </summary>
    /// <param name="content">The raw response string returned by the LLM.</param>
    /// <param name="statusMessage">An optional short human-readable summary displayed in the conversation history UI.</param>
    public void AddAssistantMessage(string content, string? statusMessage = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        _llmMessages.Add(new ConversationMessage("assistant", content, statusMessage));

        var llmStatus = (statusMessage == null) ? "thinking" : statusMessage;
        _humanMessages.Add(FormatHumanMessage("LLM", llmStatus));
    }

    /// <summary>
    /// Removes all messages and corresponding error bool from the conversation.
    /// </summary>
    public void Clear()
    {
        _llmMessages.Clear();
        _humanMessages.Clear();
    }

    // PRIVATE METHODS =======================================================================================
    private string? LastOf(string role) =>
        _llmMessages.LastOrDefault(m => m.Role == role)?.Content;

    private static string FormatHumanMessage(string role, string content)
    {
        return role + "================" + Environment.NewLine + content + Environment.NewLine;
    }
}
