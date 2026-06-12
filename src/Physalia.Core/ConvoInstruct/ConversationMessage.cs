// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Physalia.Core.ConvoInstruct;

/// <summary>
/// A single turn in a conversation, carrying one or more content blocks.
/// </summary>
/// <param name="Role">Who produced this turn.</param>
/// <param name="Content">The content blocks for this turn.</param>
public record ConversationMessage(Role Role, IReadOnlyList<MessageContent> Content)
{
    /// <summary>
    /// Convenience constructor for a single text block.
    /// </summary>
    /// <param name="role">Who produced this turn.</param>
    /// <param name="text">The text of the turn.</param>
    public ConversationMessage(Role role, string text)
        : this(role, new[] { new TextContent(text) })
    {
    }
}
