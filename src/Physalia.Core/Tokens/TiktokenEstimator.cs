// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using SharpToken;
using Physalia.Core.ConvoInstruct;

namespace Physalia.Core.Tokens;

/// <summary>
/// Token estimator backed by tiktoken via <c>SharpToken</c>.
/// Accurate for OpenAI-family models (GPT-3.5/4: cl100k_base; GPT-4o: o200k_base).
/// Reasonable for other providers that use similar BPE vocabularies.
/// Overhead constants are documented on <see cref="TokenEstimationHelpers"/>.
/// </summary>
/// <remarks>
/// Construct via <see cref="CreateForModel"/> or <see cref="CreateForEncoding"/>.
/// The underlying <see cref="GptEncoding"/> instance is thread-safe and should be
/// cached and reused rather than recreated per call.
/// </remarks>
public sealed class TiktokenEstimator : ISyncTokenEstimator
{
    private readonly GptEncoding _encoding;

    private TiktokenEstimator(GptEncoding encoding)
    {
        _encoding = encoding;
    }

    /// <summary>
    /// Creates a <see cref="TiktokenEstimator"/> for the given model name.
    /// Common values: <c>"gpt-4o"</c> (o200k_base), <c>"gpt-4"</c> (cl100k_base).
    /// </summary>
    /// <param name="modelName">The OpenAI model name used to select the vocabulary.</param>
    /// <returns>A ready-to-use estimator instance.</returns>
    public static TiktokenEstimator CreateForModel(string modelName = "gpt-4o")
    {
        var encoding = GptEncoding.GetEncodingForModel(modelName);
        return new TiktokenEstimator(encoding);
    }

    /// <summary>
    /// Creates a <see cref="TiktokenEstimator"/> for an explicit encoding name.
    /// Common values: <c>"o200k_base"</c>, <c>"cl100k_base"</c>, <c>"p50k_base"</c>.
    /// </summary>
    /// <param name="encodingName">The tiktoken encoding name.</param>
    /// <returns>A ready-to-use estimator instance.</returns>
    public static TiktokenEstimator CreateForEncoding(string encodingName)
    {
        var encoding = GptEncoding.GetEncoding(encodingName);
        return new TiktokenEstimator(encoding);
    }

    /// <inheritdoc/>
    public int Estimate(Instructions instructions)
    {
        int count = 0;

        // System prompt is transmitted as a {"role": "system", ...} message —
        // apply the same per-message framing overhead as conversation turns.
        if (!instructions.SystemPrompt.IsEmpty)
        {
            count += TokenEstimationHelpers.OverheadPerMessage;
            count += _encoding.Encode(instructions.SystemPrompt.Text).Count;
        }

        foreach (var message in instructions.Conversation.Messages)
        {
            count += TokenEstimationHelpers.OverheadPerMessage;

            foreach (var block in message.Content)
            {
                count += _encoding.Encode(TokenEstimationHelpers.ExtractText(block)).Count;
            }
        }

        // Reply priming appended to every request regardless of conversation length.
        count += TokenEstimationHelpers.ReplyPriming;

        return count;
    }
}
