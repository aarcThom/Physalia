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
/// <item>Tool exchanges are paired in BOTH directions. A <see cref="ToolResultContent"/> whose
/// matching <see cref="ToolCallContent"/> was dropped is removed, AND a
/// <see cref="ToolCallContent"/> whose result did not survive the cut is removed. Either
/// orphan is a hard API error.</item>
/// <item>Every surviving <see cref="ToolCallContent"/> is answered in the IMMEDIATELY following
/// message, which is what providers actually require — merging two assistant turns can break
/// that even when both halves of each exchange survive.</item>
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

        var working = messages.ToList();

        // Repairing one invariant can expose another: stripping an unanswered tool_use can empty a
        // message, dropping that message can expose a leading assistant turn or make two same-role
        // turns adjacent, and merging those can leave a tool_use whose result is no longer in the
        // very next message — which strips again. Re-run until a pass changes nothing.
        //
        // The chain terminates: a pass only re-merges because the pass before it dropped a message,
        // so it can propagate at most once per message. Two passes settle every cut we have seen;
        // the bound is a backstop, not an expectation.
        int maxPasses = working.Count + 2;
        for (int pass = 0; pass < maxPasses; pass++)
        {
            List<ConversationMessage> next = Normalize(working);
            bool stable = Unchanged(working, next);
            working = next;

            if (stable)
            {
                break;
            }
        }

        // Belt and braces, and deliberately not covered by a test: if the loop above exited
        // because it converged, this is a no-op, and the termination argument says it always
        // converges. It is here so that a flaw in that argument costs a slightly longer prompt
        // instead of an exception out of Append (which rejects consecutive same-role turns) on
        // the inference path.
        working = MergeConsecutiveSameRole(working, DropLeadingAssistantTurns(working));

        Conversation conversation = Conversation.Empty;
        foreach (ConversationMessage message in working)
        {
            conversation = conversation.Append(message);
        }

        return conversation;
    }

    /// <summary>
    /// Runs one full repair pass over the sequence.
    /// </summary>
    /// <param name="list">The messages to repair, in order.</param>
    /// <returns>The repaired sequence; never longer, and never carrying more blocks, than the input.</returns>
    private static List<ConversationMessage> Normalize(List<ConversationMessage> list)
    {
        // 1. Drop leading assistant turns: a conversation must open with a user turn.
        int start = DropLeadingAssistantTurns(list);

        // 2. Every tool id answered anywhere in the retained span. A tool_use whose result did
        //    not survive the cut has nothing to pair with, so it cannot stay.
        var answeredIds = new HashSet<string>();
        for (int i = start; i < list.Count; i++)
        {
            foreach (MessageContent block in list[i].Content)
            {
                if (block is ToolResultContent result)
                {
                    answeredIds.Add(result.ToolCallId);
                }
            }
        }

        // 3. Strip both kinds of orphan, tracking tool_use ids in causal order so a result can
        //    only pair with a call that precedes it. A message left with no content blocks is
        //    dropped entirely.
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
                    case ToolCallContent orphanCall when !answeredIds.Contains(orphanCall.Id):
                        // Orphaned: the matching tool_result was compacted away. Drop it.
                        break;

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

        // 4. Dropping emptied messages in step 3 can expose a new leading assistant turn.
        // 5. Merge consecutive same-role messages so role alternation holds.
        List<ConversationMessage> merged =
            MergeConsecutiveSameRole(cleaned, DropLeadingAssistantTurns(cleaned));

        // 6. Providers require every tool_use in a turn to be answered by the turn immediately
        //    after it. Step 5's merge can fuse two assistant turns whose results were answered
        //    in separate following turns, and a trailing tool_use has no following turn at all —
        //    both leave calls the next message does not cover. Strip those; the next pass drops
        //    any message they emptied.
        return StripUnansweredByNextMessage(merged);
    }

    /// <summary>
    /// Finds the first message that is not an assistant turn — a conversation must open with a
    /// user turn.
    /// </summary>
    /// <param name="messages">The messages to scan, in order.</param>
    /// <returns>The index of the first user turn, or the count when there is none.</returns>
    private static int DropLeadingAssistantTurns(List<ConversationMessage> messages)
    {
        int start = 0;
        while (start < messages.Count && messages[start].Role == Role.Assistant)
        {
            start++;
        }

        return start;
    }

    /// <summary>
    /// Merges consecutive same-role messages — which a cut can produce when the dropped span sat
    /// between two turns of the same role — so role alternation holds. Content blocks concatenate
    /// in order; IsFeedback survives only when both merged turns were feedback.
    /// </summary>
    /// <param name="messages">The messages to merge, in order.</param>
    /// <param name="start">The index to start from, skipping any leading assistant turns.</param>
    /// <returns>A strictly role-alternating sequence.</returns>
    private static List<ConversationMessage> MergeConsecutiveSameRole(List<ConversationMessage> messages, int start)
    {
        var merged = new List<ConversationMessage>(messages.Count);

        for (int i = start; i < messages.Count; i++)
        {
            ConversationMessage message = messages[i];

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

        return merged;
    }

    /// <summary>
    /// Removes every tool call that the immediately following message does not answer.
    /// </summary>
    /// <param name="messages">The role-alternating messages to check, in order.</param>
    /// <returns>The sequence with unanswerable calls removed.</returns>
    private static List<ConversationMessage> StripUnansweredByNextMessage(List<ConversationMessage> messages)
    {
        var output = new List<ConversationMessage>(messages.Count);

        for (int i = 0; i < messages.Count; i++)
        {
            ConversationMessage message = messages[i];

            if (!message.Content.Any(b => b is ToolCallContent))
            {
                output.Add(message);
                continue;
            }

            var answeredNext = new HashSet<string>();
            if (i + 1 < messages.Count)
            {
                foreach (MessageContent block in messages[i + 1].Content)
                {
                    if (block is ToolResultContent toolResult)
                    {
                        answeredNext.Add(toolResult.ToolCallId);
                    }
                }
            }

            var kept = message.Content
                .Where(b => b is not ToolCallContent call || answeredNext.Contains(call.Id))
                .ToList();

            if (kept.Count == message.Content.Count)
            {
                output.Add(message);
            }
            else if (kept.Count > 0)
            {
                output.Add(message with { Content = kept });
            }

            // kept.Count == 0: the turn was nothing but unanswerable calls — drop it. The next
            // pass re-merges and re-trims around the hole.
        }

        return output;
    }

    /// <summary>
    /// Tests whether a repair pass changed anything. A pass only ever removes messages or blocks,
    /// so equal message and block totals mean nothing was stripped, dropped, or merged.
    /// </summary>
    /// <param name="before">The sequence handed to the pass.</param>
    /// <param name="after">The sequence the pass returned.</param>
    /// <returns>True when the pass was a no-op.</returns>
    private static bool Unchanged(List<ConversationMessage> before, List<ConversationMessage> after)
    {
        if (before.Count != after.Count)
        {
            return false;
        }

        int blocksBefore = before.Sum(m => m.Content.Count);
        int blocksAfter = after.Sum(m => m.Content.Count);
        return blocksBefore == blocksAfter;
    }
}
