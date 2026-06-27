// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Physalia.Core.ConvoInstruct;

namespace Physalia.Core.Compaction;

/// <summary>
/// The outcome of a compaction operation: the compacted conversation plus the message
/// counts before and after, so a component can report how much was dropped. Purely
/// informational — the compacted <see cref="Conversation"/> is the product.
/// </summary>
/// <param name="Conversation">The compacted conversation, valid for provider replay.</param>
/// <param name="OriginalMessageCount">Message count before compaction.</param>
/// <param name="RetainedMessageCount">Message count after compaction.</param>
public sealed record CompactionResult(
    Conversation Conversation,
    int OriginalMessageCount,
    int RetainedMessageCount)
{
    /// <summary>
    /// Gets the number of messages removed by the compaction. A summary replacing several
    /// turns with one counts as a net drop; it never goes negative.
    /// </summary>
    public int DroppedMessageCount => Math.Max(0, OriginalMessageCount - RetainedMessageCount);

    /// <summary>
    /// Builds a result from an original conversation and its compacted form.
    /// </summary>
    /// <param name="original">The conversation before compaction.</param>
    /// <param name="compacted">The conversation after compaction.</param>
    /// <returns>A populated <see cref="CompactionResult"/>.</returns>
    public static CompactionResult From(Conversation original, Conversation compacted)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(compacted);
        return new CompactionResult(compacted, original.Count, compacted.Count);
    }

    /// <summary>
    /// Builds a no-op result for a conversation that needed no compaction.
    /// </summary>
    /// <param name="conversation">The conversation left unchanged.</param>
    /// <returns>A result whose original and retained counts match.</returns>
    public static CompactionResult Unchanged(Conversation conversation)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        return new CompactionResult(conversation, conversation.Count, conversation.Count);
    }
}
