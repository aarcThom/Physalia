// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Physalia.Core.ConvoInstruct;

namespace Physalia.Core.Tokens;

/// <summary>
/// Token estimator that uses a simple character-count heuristic (~4 chars per token).
/// No dependencies, no async initialisation, accurate to within ~10–20% for English prose.
/// Safe to use for any provider when exact counts are not required.
/// Overhead constants are documented on <see cref="TokenEstimationHelpers"/>.
/// </summary>
public sealed class HeuristicTokenEstimator : ISyncTokenEstimator
{
    private const int CharsPerToken = 4;

    /// <inheritdoc/>
    public int Estimate(Instructions instructions)
    {
        int count = 0;

        // System prompt is transmitted as a {"role": "system", ...} message —
        // apply the same per-message framing overhead as conversation turns.
        if (!string.IsNullOrEmpty(instructions.SystemPrompt))
        {
            count += TokenEstimationHelpers.OverheadPerMessage;
            count += instructions.SystemPrompt.Length / CharsPerToken;
        }

        foreach (var message in instructions.Conversation.Messages)
        {
            count += TokenEstimationHelpers.OverheadPerMessage;

            foreach (var block in message.Content)
            {
                count += TokenEstimationHelpers.ExtractText(block).Length / CharsPerToken;
            }
        }

        // Reply priming appended to every request regardless of conversation length.
        count += TokenEstimationHelpers.ReplyPriming;

        return count;
    }
}
