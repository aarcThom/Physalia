// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Physalia.Core.ConvoInstruct;

namespace Physalia.Core.Tokens;

/// <summary>
/// Token estimator that uses a simple character-count heuristic (~4 chars per token).
/// No dependencies, no async initialisation, accurate to within ~10–20% for English prose.
/// Safe to use for any provider when exact counts are not required.
/// </summary>
public sealed class HeuristicTokenEstimator : ITokenEstimator
{
    private const int CharsPerToken = 4;

    /// <summary>
    /// Per-message overhead in tokens to account for role and formatting framing.
    /// </summary>
    private const int OverheadPerMessage = 4;

    /// <inheritdoc/>
    public int Estimate(Instructions instructions)
    {
        int count = 0;

        if (!string.IsNullOrEmpty(instructions.SystemPrompt))
        {
            count += instructions.SystemPrompt.Length / CharsPerToken;
        }

        foreach (var message in instructions.Conversation.Messages)
        {
            count += OverheadPerMessage;

            foreach (var block in message.Content)
            {
                count += ExtractText(block).Length / CharsPerToken;
            }
        }

        return count;
    }

    private static string ExtractText(MessageContent block) => block switch
    {
        TextContent text => text.Text,
        ToolCallContent call => call.Name + " " + call.InputJson,
        ToolResultContent result => result.Content,
        ImageContent => "[image]",
        _ => string.Empty,
    };
}
