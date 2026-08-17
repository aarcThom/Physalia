// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Physalia.Core.Common;
using Physalia.Core.ConvoInstruct;

namespace Physalia.Core.Signals;

/// <summary>
/// The combined content of several signals: the joined payload and the combined content blocks.
/// </summary>
/// <param name="Payload">The joined text payload — the trace string for the aggregated signal.</param>
/// <param name="ContentBlocks">
/// The combined blocks, or empty when no part carried any (the ordinary text-only case, in which
/// the payload is the sole carrier).
/// </param>
public sealed record AggregatedContent(string Payload, IReadOnlyList<MessageContent> ContentBlocks);

/// <summary>
/// Pure policy for combining several signals into one, shared by every component that aggregates
/// (Merge Signal's join, the Feedback Collector's batch).
///
/// <para><b>The invariant it exists to keep.</b> A signal that carries content blocks carries them as
/// the WHOLE content of the turn — <see cref="PhySignal.Payload"/> is then only the text trace of
/// those blocks. Every producer obeys that: a Geometry Observation with no message mints an image
/// block and a blank payload, and one with a message mints a text block alongside it. Downstream,
/// <c>ConversationLogBuilder</c> therefore takes non-empty blocks as authoritative and uses the
/// payload for tracing alone.</para>
///
/// <para>Naive aggregation breaks that invariant, and silently: joining payload strings while merely
/// concatenating block lists leaves a text-only part's text in the payload and in no block, so the
/// Conversation Log records the blocks and drops the text. That is how a Geometry Report merged with
/// a Geometry Observation image reached the model as the image alone. So a part contributing no
/// blocks of its own contributes its payload AS a text block instead.</para>
///
/// <para>Blocks are only materialised when some part actually carried them: an all-text aggregation
/// stays text-only, keeping the payload the sole carrier exactly as before.</para>
/// </summary>
public static class SignalAggregation
{
    /// <summary>
    /// Combines signals into one payload plus one block list.
    /// </summary>
    /// <param name="parts">
    /// The signals to combine, already in causal (sequence) order — ordering is the caller's
    /// decision, and both the payload and the blocks are built in the order given.
    /// </param>
    /// <param name="separator">The string placed between two non-blank payloads.</param>
    /// <returns>The joined payload and the combined blocks.</returns>
    public static AggregatedContent Combine(IReadOnlyList<PhySignal> parts, string separator)
    {
        ArgumentNullException.ThrowIfNull(parts);
        ArgumentNullException.ThrowIfNull(separator);

        string payload = string.Join(
            separator,
            parts.Select(s => s.Payload).Where(StringHelpers.IsNonBlank));

        // No part carried blocks: leave the aggregate text-only rather than inventing a block list.
        if (!parts.Any(s => s.ContentBlocks.Count > 0))
        {
            return new AggregatedContent(payload, Array.Empty<MessageContent>());
        }

        var blocks = new List<MessageContent>();
        foreach (PhySignal part in parts)
        {
            if (part.ContentBlocks.Count > 0)
            {
                // Verbatim: a tool_result's tool_use_id and an inline image's bytes must survive.
                blocks.AddRange(part.ContentBlocks);
            }
            else if (StringHelpers.IsNonBlank(part.Payload))
            {
                // Text-only part joining a block-carrying set — promote its payload to a block, or
                // the whole aggregate's blocks would misrepresent it as having contributed nothing.
                blocks.Add(new TextContent(part.Payload));
            }
        }

        return new AggregatedContent(payload, blocks);
    }
}
