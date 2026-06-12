// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Physalia.Core.ConvoInstruct;

namespace Physalia.GH.Goo;

/// <summary>
/// Grasshopper goo wrapper for a single <see cref="ConversationMessage"/>.
/// </summary>
public class GH_ConversationMessage : PhyGoo<GH_ConversationMessage, ConversationMessage>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GH_ConversationMessage"/> class with no value.
    /// </summary>
    public GH_ConversationMessage()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="GH_ConversationMessage"/> class wrapping the given message.
    /// </summary>
    /// <param name="message">The conversation message to wrap.</param>
    public GH_ConversationMessage(ConversationMessage message)
        : base(message)
    {
    }

    /// <inheritdoc/>
    public override string TypeName => "ConversationMessage";

    /// <inheritdoc/>
    public override string TypeDescription => "A single turn in a conversation, carrying a role and one or more content blocks.";

    /// <inheritdoc/>
    public override string ToString() =>
        Value is null ? string.Empty : ConversationHelpers.ToDisplayString(Value);
}
