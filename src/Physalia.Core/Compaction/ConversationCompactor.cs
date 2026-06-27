// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Physalia.Core.ConvoInstruct;
using Physalia.Core.Tokens;

namespace Physalia.Core.Compaction;

/// <summary>
/// Pure, deterministic conversation-compaction strategies. None of these call an LLM — they
/// drop, slice, or edit existing messages and never invent new content (for that, see
/// <see cref="ConversationSummarizer"/>). Every method funnels its retained messages through
/// <see cref="CompactionInvariants.Reassemble"/>, so the returned conversation is always valid
/// for provider replay regardless of where the cut fell.
/// </summary>
public static class ConversationCompactor
{
    /// <summary>
    /// Sliding window by message count: keeps the most recent <paramref name="maxMessages"/>
    /// messages and drops the rest. The classic recency window — cheapest possible compaction,
    /// but it forgets the oldest context entirely.
    /// </summary>
    /// <param name="conversation">The conversation to compact.</param>
    /// <param name="maxMessages">How many of the most recent messages to keep (clamped to ≥ 0).</param>
    /// <returns>The compacted conversation and its statistics.</returns>
    public static CompactionResult KeepRecentMessages(Conversation conversation, int maxMessages)
    {
        ArgumentNullException.ThrowIfNull(conversation);

        if (maxMessages < 0)
        {
            maxMessages = 0;
        }

        if (conversation.Count <= maxMessages)
        {
            return CompactionResult.Unchanged(conversation);
        }

        IEnumerable<ConversationMessage> tail = conversation.Messages.Skip(conversation.Count - maxMessages);
        Conversation compacted = CompactionInvariants.Reassemble(tail);
        return CompactionResult.From(conversation, compacted);
    }

    /// <summary>
    /// Token-budget window: drops the oldest messages one at a time until the estimated token
    /// count of the remaining conversation (including the system prompt) fits within
    /// <paramref name="maxTokens"/>. Keeps the recent tail. A single message larger than the
    /// budget is kept on its own — deterministic compaction cannot shrink one message
    /// (use <see cref="Prune"/> or <see cref="ConversationSummarizer"/> for that).
    /// </summary>
    /// <param name="conversation">The conversation to compact.</param>
    /// <param name="systemPrompt">The system prompt counted against the budget.</param>
    /// <param name="estimator">The token estimator (a synchronous one such as the heuristic or tiktoken).</param>
    /// <param name="maxTokens">The token budget the result must fit within.</param>
    /// <returns>The compacted conversation and its statistics.</returns>
    public static CompactionResult KeepWithinTokenBudget(
        Conversation conversation,
        string systemPrompt,
        ITokenEstimator estimator,
        int maxTokens)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        ArgumentNullException.ThrowIfNull(estimator);

        if (conversation.Count == 0)
        {
            return CompactionResult.Unchanged(conversation);
        }

        var working = conversation.Messages.ToList();

        while (true)
        {
            Conversation candidate = CompactionInvariants.Reassemble(working);
            int tokens = estimator.Estimate(new Instructions(systemPrompt, candidate));

            if (tokens <= maxTokens || working.Count <= 1)
            {
                return CompactionResult.From(conversation, candidate);
            }

            // Drop the oldest message and re-measure the recent tail.
            working.RemoveAt(0);
        }
    }

    /// <summary>
    /// Anchored head+tail window: keeps the first <paramref name="headCount"/> messages (the
    /// initial task/context the model should never forget) and the last
    /// <paramref name="tailCount"/> messages (the live working set), dropping the middle. When
    /// the kept head and tail abut at the same role they merge into one turn — the elided span
    /// simply vanishes from the thread.
    /// </summary>
    /// <param name="conversation">The conversation to compact.</param>
    /// <param name="headCount">How many leading messages to keep (clamped to ≥ 0).</param>
    /// <param name="tailCount">How many trailing messages to keep (clamped to ≥ 0).</param>
    /// <returns>The compacted conversation and its statistics.</returns>
    public static CompactionResult KeepHeadAndTail(Conversation conversation, int headCount, int tailCount)
    {
        ArgumentNullException.ThrowIfNull(conversation);

        if (headCount < 0)
        {
            headCount = 0;
        }

        if (tailCount < 0)
        {
            tailCount = 0;
        }

        if (conversation.Count <= headCount + tailCount)
        {
            return CompactionResult.Unchanged(conversation);
        }

        var kept = new List<ConversationMessage>(headCount + tailCount);
        kept.AddRange(conversation.Messages.Take(headCount));
        kept.AddRange(conversation.Messages.Skip(conversation.Count - tailCount));

        Conversation compacted = CompactionInvariants.Reassemble(kept);
        return CompactionResult.From(conversation, compacted);
    }

    /// <summary>
    /// Selective content pruning: walks every turn and removes or shortens the parts selected by
    /// <paramref name="options"/> (images, tool exchanges, feedback turns, over-long tool results
    /// or text), keeping the conversational thread otherwise intact. A turn left empty by pruning
    /// is dropped and the result reassembled to keep tool pairing and role alternation valid.
    /// </summary>
    /// <param name="conversation">The conversation to compact.</param>
    /// <param name="options">Which content to drop or truncate.</param>
    /// <returns>The compacted conversation and its statistics.</returns>
    public static CompactionResult Prune(Conversation conversation, PruneOptions options)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        ArgumentNullException.ThrowIfNull(options);

        var result = new List<ConversationMessage>(conversation.Count);

        foreach (ConversationMessage message in conversation.Messages)
        {
            if (options.DropFeedbackTurns && message.IsFeedback)
            {
                continue;
            }

            var kept = new List<MessageContent>(message.Content.Count);

            foreach (MessageContent block in message.Content)
            {
                switch (block)
                {
                    case ImageContent when options.DropImages:
                        break;

                    case ToolCallContent when options.DropToolExchanges:
                        break;

                    case ToolResultContent when options.DropToolExchanges:
                        break;

                    case ToolResultContent toolResult when options.MaxToolResultChars is int max && toolResult.Content.Length > max:
                        kept.Add(new ToolResultContent(toolResult.ToolCallId, Truncate(toolResult.Content, max), toolResult.IsError));
                        break;

                    case TextContent text when options.MaxTextChars is int maxText && text.Text.Length > maxText:
                        kept.Add(new TextContent(Truncate(text.Text, maxText)));
                        break;

                    default:
                        kept.Add(block);
                        break;
                }
            }

            if (kept.Count > 0)
            {
                result.Add(message with { Content = kept });
            }
        }

        Conversation compacted = CompactionInvariants.Reassemble(result);
        return CompactionResult.From(conversation, compacted);
    }

    private static string Truncate(string value, int max)
    {
        if (value.Length <= max)
        {
            return value;
        }

        int dropped = value.Length - max;
        return value.Substring(0, max) + $"\n… [truncated {dropped} characters]";
    }
}
