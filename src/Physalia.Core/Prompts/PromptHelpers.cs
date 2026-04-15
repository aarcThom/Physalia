// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Collections.Generic;

namespace Physalia.Core.Prompts;

/// <summary>
/// Utility methods for constructing and manipulating conversation histories
/// before they are sent to an LLM provider.
/// </summary>
public static class PromptHelpers
{
    /// <summary>
    /// Returns a copy of <paramref name="messages"/> with <paramref name="paramPrompt"/>
    /// appended to the content of the last message, without mutating the original list.
    /// Returns the original list unchanged if it is empty or <paramref name="paramPrompt"/>
    /// is null or whitespace.
    /// </summary>
    /// <param name="messages">The conversation history to augment.</param>
    /// <param name="paramPrompt">The parameter context to append to the last message.</param>
    /// <returns>The augmented message list, or the original list if no augmentation was needed.</returns>
    public static IReadOnlyList<ConversationMessage> AppendParamsToLastMessage(
        IReadOnlyList<ConversationMessage> messages,
        string paramPrompt)
    {
        if (string.IsNullOrEmpty(paramPrompt) || messages.Count == 0)
            return messages;

        var list = new List<ConversationMessage>(messages);
        var last = list[list.Count - 1];
        list[list.Count - 1] = new ConversationMessage(last.Role, last.Content + paramPrompt);
        return list;
    }
}
