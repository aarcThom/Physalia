// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Physalia.Core.ConvoInstruct;

/// <summary>
/// Checks that a conversation's tool exchanges are paired the way providers require, and says
/// in plain language what is wrong when they are not.
///
/// <para>Every provider enforces the same two rules: a <see cref="ToolResultContent"/> must echo
/// a <see cref="ToolCallContent"/> that came before it, and every <see cref="ToolCallContent"/>
/// must be answered by the turn IMMEDIATELY after the one that asked. Breaking either is a hard
/// request rejection, not a degraded answer — and the rejection names opaque provider ids, so
/// this reports the offending turn and tool name too.</para>
///
/// <para>This is a read-only diagnosis. Repairing a broken conversation is
/// <c>CompactionInvariants.Reassemble</c>'s job; use the two together at any boundary where a
/// conversation assembled elsewhere is about to be sent.</para>
/// </summary>
public static class ToolPairing
{
    /// <summary>
    /// Finds every tool-pairing defect in a conversation.
    /// </summary>
    /// <param name="conversation">The conversation about to be sent.</param>
    /// <returns>One human-readable sentence per defect, in turn order; empty when the conversation is valid.</returns>
    public static IReadOnlyList<string> FindProblems(Conversation conversation)
    {
        ArgumentNullException.ThrowIfNull(conversation);

        var problems = new List<string>();
        var seenToolUseIds = new HashSet<string>();

        for (int i = 0; i < conversation.Count; i++)
        {
            ConversationMessage message = conversation.Messages[i];

            // Turn numbers are 1-based so they line up with what the Signal Trace and the chat
            // window show a human.
            int turn = i + 1;

            var answeredNext = new HashSet<string>();
            if (i + 1 < conversation.Count)
            {
                foreach (MessageContent block in conversation.Messages[i + 1].Content)
                {
                    if (block is ToolResultContent next)
                    {
                        answeredNext.Add(next.ToolCallId);
                    }
                }
            }

            foreach (MessageContent block in message.Content)
            {
                switch (block)
                {
                    case ToolCallContent call:
                        seenToolUseIds.Add(call.Id);

                        if (!answeredNext.Contains(call.Id))
                        {
                            problems.Add(
                                $"turn {turn} asks for the tool \"{call.Name}\" ({Abbreviate(call.Id)}) "
                                + "but the turn after it carries no matching tool result");
                        }

                        break;

                    case ToolResultContent result when !seenToolUseIds.Contains(result.ToolCallId):
                        problems.Add(
                            $"turn {turn} carries a tool result for {Abbreviate(result.ToolCallId)}, "
                            + "which no earlier turn asked for");
                        break;

                    default:
                        break;
                }
            }
        }

        return problems;
    }

    /// <summary>
    /// Shortens a provider tool id so a canvas message stays readable.
    /// </summary>
    /// <param name="id">The provider-assigned tool id.</param>
    /// <returns>The id, truncated with an ellipsis when it is long.</returns>
    private static string Abbreviate(string id) =>
        string.IsNullOrEmpty(id) || id.Length <= 14 ? id : id.Substring(0, 14) + "…";
}
