// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Grasshopper.Kernel.Types;
using Physalia.Core.ConvoInstruct;

namespace Physalia.GH.Goo;

/// <summary>
/// Grasshopper goo wrapper for a single <see cref="ConversationMessage"/>.
/// </summary>
public class GH_ConversationMessage : GH_Goo<ConversationMessage>
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
    {
        Value = message;
    }

    /// <inheritdoc/>
    public override bool IsValid => Value is not null;

    /// <inheritdoc/>
    public override string TypeName => "ConversationMessage";

    /// <inheritdoc/>
    public override string TypeDescription => "A single turn in a conversation, carrying a role and one or more content blocks.";

    /// <inheritdoc/>
    public override IGH_Goo Duplicate() => new GH_ConversationMessage(Value);

    /// <inheritdoc/>
    public override string ToString() =>
        Value is null ? string.Empty : ConversationHelpers.ToDisplayString(Value);
}
