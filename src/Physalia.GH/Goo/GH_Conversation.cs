// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Grasshopper.Kernel.Types;
using Physalia.Core.ConvoInstruct;

namespace Physalia.GH.Goo;

/// <summary>
/// Grasshopper goo wrapper for a <see cref="Conversation"/>.
/// </summary>
public class GH_Conversation : GH_Goo<Conversation>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GH_Conversation"/> class with no value.
    /// </summary>
    public GH_Conversation()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="GH_Conversation"/> class wrapping the given conversation.
    /// </summary>
    /// <param name="conversation">The conversation to wrap.</param>
    public GH_Conversation(Conversation conversation)
    {
        Value = conversation;
    }

    /// <inheritdoc/>
    public override bool IsValid => Value is not null;

    /// <inheritdoc/>
    public override string TypeName => "Conversation";

    /// <inheritdoc/>
    public override string TypeDescription => "An immutable, append-only conversation history.";

    /// <inheritdoc/>
    public override IGH_Goo Duplicate() => new GH_Conversation(Value);

    /// <inheritdoc/>
    public override string ToString() =>
        Value is null ? string.Empty : ConversationHelpers.ToDisplayString(Value);
}
