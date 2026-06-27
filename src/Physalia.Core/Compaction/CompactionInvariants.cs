// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Physalia.Core.ConvoInstruct;

namespace Physalia.Core.Compaction;

/// <summary>
/// Reassembles an arbitrarily-cut sequence of messages back into a provider-valid
/// <see cref="Conversation"/>. Every compaction strategy that drops or edits messages
/// funnels its result through <see cref="Reassemble"/> so the output always satisfies the
/// invariants providers require — even when the cut lands in the middle of a tool exchange
/// or a multi-turn run.
///
/// <para>The invariants enforced:</para>
/// <list type="bullet">
/// <item>The conversation begins with a <see cref="Role.User"/> turn (most providers reject
/// a leading assistant turn).</item>
/// <item>No <see cref="ToolResultContent"/> is left orphaned — a tool result whose matching
/// <see cref="ToolCallContent"/> was dropped is removed (an unmatched tool result is a hard
/// API error).</item>
/// <item>Roles strictly alternate — consecutive same-role messages (which a cut can produce
/// when the dropped span sat between two turns of the same role) are merged into one.</item>
/// </list>
/// </summary>
public static class CompactionInvariants
{
    /// <summary>
    /// Rebuilds a valid conversation from an ordered, possibly-invalid message sequence.
    /// </summary>
    /// <param name="messages">The retained messages, in original order.</param>
    /// <returns>A conversation satisfying the provider invariants; empty when nothing survives.</returns>
    public static Conversation Reassemble(IEnumerable<ConversationMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);

        var list = messages.ToList();

        // 1. Drop leading assistant turns: a conversation must open with a user turn.
        int start = 0;
        while (start < list.Count && list[start].Role == Role.Assistant)
        {
            start++;
        }

        // 2. Strip orphan tool results (their tool_use was dropped), tracking tool_use ids in
        //    causal order. A message left with no content blocks is dropped entirely.
        var seenToolUseIds = new HashSet<string>();
        var cleaned = new List<ConversationMessage>();

        for (int i = start; i < list.Count; i++)
        {
            ConversationMessage message = list[i];
            var kept = new List<MessageContent>(message.Content.Count);

            foreach (MessageContent block in message.Content)
            {
                switch (block)
                {
                    case ToolCallContent call:
                        seenToolUseIds.Add(call.Id);
                        kept.Add(block);
                        break;

                    case ToolResultContent result when !seenToolUseIds.Contains(result.ToolCallId):
                        // Orphaned: the matching tool_use was compacted away. Drop it.
                        break;

                    default:
                        kept.Add(block);
                        break;
                }
            }

            if (kept.Count > 0)
            {
                cleaned.Add(message with { Content = kept });
            }
        }

        // 3. Dropping emptied messages in step 2 can expose a new leading assistant turn.
        int s2 = 0;
        while (s2 < cleaned.Count && cleaned[s2].Role == Role.Assistant)
        {
            s2++;
        }

        // 4. Merge consecutive same-role messages so role alternation holds. Content blocks
        //    concatenate in order; IsFeedback survives only when both merged turns were feedback.
        var merged = new List<ConversationMessage>();
        for (int i = s2; i < cleaned.Count; i++)
        {
            ConversationMessage message = cleaned[i];

            if (merged.Count > 0 && merged[^1].Role == message.Role)
            {
                ConversationMessage prev = merged[^1];
                var blocks = new List<MessageContent>(prev.Content.Count + message.Content.Count);
                blocks.AddRange(prev.Content);
                blocks.AddRange(message.Content);
                merged[^1] = new ConversationMessage(prev.Role, blocks)
                {
                    IsFeedback = prev.IsFeedback && message.IsFeedback,
                };
            }
            else
            {
                merged.Add(message);
            }
        }

        // 5. Build via Append, which re-validates alternation (now guaranteed).
        Conversation conversation = Conversation.Empty;
        foreach (ConversationMessage message in merged)
        {
            conversation = conversation.Append(message);
        }

        return conversation;
    }
}
