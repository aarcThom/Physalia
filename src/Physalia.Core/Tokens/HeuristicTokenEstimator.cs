// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

// Overhead constants derived from the OpenAI token-counting cookbook:
//   https://github.com/openai/openai-cookbook/blob/main/examples/How_to_count_tokens_with_tiktoken.ipynb
//
// Key rules applied here:
//   Concept 3 — Per-message overhead: every message in the API payload carries
//               3 structural framing tokens (<|start|>{role}\n{content}<|end|>\n)
//               plus ~1 token for the role value ("user"/"assistant"/"system").
//               Combined constant: OverheadPerMessage = 4.
//               This applies equally to the system prompt, which is transmitted
//               as a {"role": "system", "content": "..."} message.
//
//   Concept 4 — Reply priming: every request appends 3 tokens to prime the
//               assistant response (<|start|>assistant<|message|>).
//               Constant: ReplyPriming = 3.
//
// Reference: https://help.openai.com/en/articles/4936856-what-are-tokens-and-how-to-count-them

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

    // 3 structural framing tokens + ~1 token for the role value.
    private const int OverheadPerMessage = 4;

    // Every request is primed with <|start|>assistant<|message|> before the model replies.
    private const int ReplyPriming = 3;

    /// <inheritdoc/>
    public int Estimate(Instructions instructions)
    {
        int count = 0;

        // System prompt is transmitted as a {"role": "system", ...} message —
        // apply the same per-message framing overhead as conversation turns.
        if (!string.IsNullOrEmpty(instructions.SystemPrompt))
        {
            count += OverheadPerMessage;
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

        // Reply priming appended to every request regardless of conversation length.
        count += ReplyPriming;

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
