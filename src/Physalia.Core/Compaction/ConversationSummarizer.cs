// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Text;
using Physalia.Core.Common;
using Physalia.Core.ConvoInstruct;
using Physalia.Core.Models;
using Physalia.Core.Providers;

namespace Physalia.Core.Compaction;

/// <summary>
/// LLM-backed compaction: replaces the older portion of a conversation with a single
/// model-written summary turn, keeping the most recent turns verbatim (the summary-buffer
/// pattern). Unlike <see cref="ConversationCompactor"/> this invents new, denser text, so it
/// preserves meaning where a deterministic window would simply forget it — at the cost of one
/// inference call. The orchestration (split → summarize → splice) lives here in Core; a GH
/// component supplies the provider and marshals the async call.
/// </summary>
public static class ConversationSummarizer
{
    /// <summary>
    /// The default system prompt used when the caller supplies none. Frames the model as a
    /// faithful compactor rather than a conversational assistant.
    /// </summary>
    public const string DefaultInstruction =
        "You are a conversation compactor. Summarize the conversation provided into a concise but " +
        "complete briefing that preserves: the user's goals and constraints, key decisions made, " +
        "important facts, names, and values, and the current state of the work so it can be continued. " +
        "Write in plain prose. Do not add commentary, headings, or questions — output only the summary.";

    /// <summary>
    /// The label prefixed to the synthesized summary turn so the model (and the UI) can tell it
    /// apart from genuine user input.
    /// </summary>
    public const string SummaryHeader = "[Summary of earlier conversation]";

    /// <summary>
    /// Summarizes the older portion of a conversation and splices the summary in front of the
    /// retained recent turns.
    /// </summary>
    /// <param name="conversation">The conversation to compact.</param>
    /// <param name="provider">The LLM provider used to write the summary.</param>
    /// <param name="config">The model configuration for the summarization call.</param>
    /// <param name="instruction">The summarization system prompt, or null/blank for <see cref="DefaultInstruction"/>.</param>
    /// <param name="keepRecentMessages">How many of the most recent messages to keep verbatim (clamped to ≥ 0).</param>
    /// <param name="ct">Cancellation token for the inference call.</param>
    /// <returns>
    /// An <see cref="CompactionResult"/> on success, or an <see cref="LlmError"/> when the
    /// summarization call fails. A conversation with nothing old enough to summarize is returned
    /// unchanged.
    /// </returns>
    public static async Task<Result<CompactionResult, LlmError>> SummarizeAsync(
        Conversation conversation,
        ILlmProvider provider,
        ModelConfig config,
        string? instruction,
        int keepRecentMessages,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(config);

        if (keepRecentMessages < 0)
        {
            keepRecentMessages = 0;
        }

        int splitAt = Math.Max(0, conversation.Count - keepRecentMessages);
        var toSummarize = conversation.Messages.Take(splitAt).ToList();
        var recent = conversation.Messages.Skip(splitAt).ToList();

        if (toSummarize.Count == 0)
        {
            // Everything is within the keep-recent window; nothing to summarize.
            return new Result<CompactionResult, LlmError>.Ok(CompactionResult.Unchanged(conversation));
        }

        // Render the older portion as a single user turn for the summarizer. Reassemble first so
        // the rendered transcript reads as a valid alternating dialogue.
        string transcript = ConversationHelpers.ToDisplayString(CompactionInvariants.Reassemble(toSummarize));
        Conversation summarizationInput = Conversation.Empty.Append(
            new ConversationMessage(Role.User, "Conversation to summarize:\n\n" + transcript));

        string systemPrompt = string.IsNullOrWhiteSpace(instruction) ? DefaultInstruction : instruction!;

        var builder = new StringBuilder();

        await foreach (Result<LlmResponseChunk, LlmError> chunk in
            provider.StreamAsync(summarizationInput, systemPrompt, config, null, ct))
        {
            if (chunk.IsOk(out LlmResponseChunk? value, out LlmError? error))
            {
                if (value.ContentDelta != null)
                {
                    builder.Append(value.ContentDelta);
                }
            }
            else
            {
                return new Result<CompactionResult, LlmError>.Err(error);
            }
        }

        string summary = builder.ToString().Trim();
        if (summary.Length == 0)
        {
            return new Result<CompactionResult, LlmError>.Err(
                new LlmError(LlmErrorKind.InvalidRequest, "The summarizer returned an empty summary."));
        }

        // Splice: one user turn carrying the summary, then the recent turns verbatim.
        // Marked IsFeedback so the UI styles it as machine-generated, not human-typed.
        var rebuilt = new List<ConversationMessage>(recent.Count + 1)
        {
            new ConversationMessage(Role.User, SummaryHeader + "\n\n" + summary) { IsFeedback = true },
        };
        rebuilt.AddRange(recent);

        Conversation compacted = CompactionInvariants.Reassemble(rebuilt);
        return new Result<CompactionResult, LlmError>.Ok(CompactionResult.From(conversation, compacted));
    }
}
