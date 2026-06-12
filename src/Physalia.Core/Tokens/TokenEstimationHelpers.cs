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
/// Constants and text extraction shared by the synchronous token estimators.
/// </summary>
public static class TokenEstimationHelpers
{
    /// <summary>
    /// Per-message framing overhead: 3 structural tokens + ~1 token for the role value.
    /// </summary>
    public const int OverheadPerMessage = 4;

    /// <summary>
    /// Reply priming appended to every request (<c>&lt;|start|&gt;assistant&lt;|message|&gt;</c>).
    /// </summary>
    public const int ReplyPriming = 3;

    /// <summary>
    /// Renders a content block as the text an estimator should count.
    /// </summary>
    /// <param name="block">The content block to render.</param>
    /// <returns>The countable text; images become a placeholder marker.</returns>
    public static string ExtractText(MessageContent block) => block switch
    {
        TextContent text => text.Text,
        ToolCallContent call => call.Name + " " + call.InputJson,
        ToolResultContent result => result.Content,
        ImageContent => "[image]",
        _ => string.Empty,
    };
}
