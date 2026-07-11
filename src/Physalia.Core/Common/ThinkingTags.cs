// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Text.RegularExpressions;
using Physalia.Core.ConvoInstruct;

namespace Physalia.Core.Common;

/// <summary>
/// The inline tag convention used to carry model thinking/reasoning through the pipeline.
/// Providers wrap thinking deltas as <c>&lt;think&gt;…&lt;/think&gt;</c> inside the streamed
/// text; the chat UI renders those blocks as a collapsible Reasoning section, and request
/// builders strip them from assistant history before resending.
/// </summary>
/// <remarks>
/// Known limitation: Anthropic extended thinking combined with tool use officially requires
/// echoing thinking blocks with their signatures on the follow-up request. Inline tags cannot
/// carry signatures and are stripped on resend, so Anthropic tool-call rounds with thinking
/// enabled may degrade (the API tolerates the missing blocks but the model loses its chain
/// of thought between rounds).
/// </remarks>
public static class ThinkingTags
{
    /// <summary>Opening tag emitted by providers when a thinking block starts.</summary>
    public const string Open = "<think>";

    /// <summary>Bare closing tag, used to close a block left open on the final chunk.</summary>
    public const string Close = "</think>";

    /// <summary>Closing tag plus separator emitted when a thinking block ends mid-stream.</summary>
    public const string CloseAndSeparate = "</think>\n\n";

    private const string ThinkingOnlyPlaceholder = "[no visible reply — the response was thinking only]";

    // Mirrors the chat UI's THINK_BLOCK regex — tolerates <think>/<thinking> and mismatched pairs.
    private static readonly Regex ClosedBlock = new(
        @"<think(?:ing)?>.*?</think(?:ing)?>",
        RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Mirrors the chat UI's OPEN_THINK regex — an unclosed trailing block (mid-stream/truncated).
    private static readonly Regex OpenTrailing = new(
        @"<think(?:ing)?>.*\z",
        RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Removes all closed thinking blocks and any unclosed trailing block, returning the
    /// visible answer text, trimmed.
    /// </summary>
    /// <param name="text">The tagged text, or null.</param>
    /// <returns>The text with all thinking blocks removed, trimmed; empty when nothing remains.</returns>
    public static string Strip(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        string result = ClosedBlock.Replace(text, string.Empty);
        result = OpenTrailing.Replace(result, string.Empty);
        return result.Trim();
    }

    /// <summary>
    /// Returns an assistant message with thinking blocks stripped from its text content,
    /// for provider request serialization. Text blocks left blank by the strip are dropped;
    /// non-text blocks pass through untouched. A message left with no content at all keeps
    /// a single placeholder text block, because providers reject empty assistant turns and
    /// dropping the whole message would create consecutive user turns.
    /// </summary>
    /// <param name="message">The assistant message to strip.</param>
    /// <returns>The stripped message, or the same instance when it carries no thinking tags.</returns>
    public static ConversationMessage StripAssistantMessage(ConversationMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        bool hasTags = message.Content.Any(block =>
            block is TextContent text &&
            text.Text.Contains("<think", StringComparison.OrdinalIgnoreCase));

        if (!hasTags)
        {
            return message;
        }

        var stripped = new List<MessageContent>();
        foreach (MessageContent block in message.Content)
        {
            if (block is TextContent text)
            {
                string visible = Strip(text.Text);
                if (StringHelpers.IsNonBlank(visible))
                {
                    stripped.Add(new TextContent(visible));
                }
            }
            else
            {
                stripped.Add(block);
            }
        }

        if (stripped.Count == 0)
        {
            stripped.Add(new TextContent(ThinkingOnlyPlaceholder));
        }

        return message with { Content = stripped };
    }
}
