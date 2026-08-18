// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Physalia.Core.Common;

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

    /// <summary>
    /// Gets a value indicating whether this user turn is auto-generated feedback (e.g. validation
    /// errors routed back for correction) rather than text the human typed. Presentation-only: it
    /// is ignored by provider adapters (they read only <see cref="Role"/> and <see cref="Content"/>)
    /// and is session-only. Defaults to false.
    /// </summary>
    public bool IsFeedback { get; init; }

    /// <summary>
    /// Gets the components that produced this turn — the origin trail of the signal(s) recorded into
    /// it, so the chat window can name and badge the node a feedback turn came from. Usually one
    /// entry; several when an aggregated or merged turn combined branches. Presentation-only in the
    /// same way as <see cref="IsFeedback"/>: provider adapters never see it and it is session-only.
    /// Empty for a human-typed prompt and for anything recorded without a signal behind it.
    /// </summary>
    public IReadOnlyList<ComponentOrigin> Sources { get; init; } = Array.Empty<ComponentOrigin>();
}
