// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Text;

namespace Physalia.Core.ConvoInstruct;

/// <summary>
/// Utility methods for working with <see cref="Conversation"/> instances.
/// </summary>
public static class ConversationHelpers
{
    /// <summary>
    /// Returns a human-readable representation of the conversation suitable for display
    /// (e.g. the Recorder component's canvas output). Not intended for API serialisation.
    /// </summary>
    /// <param name="conversation">The conversation to format.</param>
    /// <returns>A formatted string with each turn labelled by role.</returns>
    public static string ToDisplayString(Conversation conversation)
    {
        ArgumentNullException.ThrowIfNull(conversation);

        if (conversation.Count == 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();

        foreach (ConversationMessage message in conversation.Messages)
        {
            AppendMessage(sb, message);
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Returns a human-readable representation of a single <see cref="ConversationMessage"/>.
    /// Not intended for API serialisation.
    /// </summary>
    /// <param name="message">The message to format.</param>
    /// <returns>A formatted string labelled by role.</returns>
    public static string ToDisplayString(ConversationMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        var sb = new StringBuilder();
        AppendMessage(sb, message);
        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Returns a human-readable string of only the content blocks in a <see cref="ConversationMessage"/>,
    /// without the role label.
    /// </summary>
    /// <param name="message">The message whose content to format.</param>
    /// <returns>A formatted string of the content blocks only.</returns>
    public static string ToContentString(ConversationMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        var sb = new StringBuilder();
        AppendContentBlocks(sb, message);
        return sb.ToString().TrimEnd();
    }

    private static void AppendMessage(StringBuilder sb, ConversationMessage message)
    {
        sb.Append('[');
        sb.Append(message.Role);
        sb.AppendLine("]");
        AppendContentBlocks(sb, message);
    }

    private static void AppendContentBlocks(StringBuilder sb, ConversationMessage message)
    {
        foreach (MessageContent block in message.Content)
        {
            switch (block)
            {
                case TextContent text:
                    sb.AppendLine(text.Text);
                    break;
                case ImageContent image:
                    sb.AppendLine(FormatImageSource(image.Source));
                    break;
                case ToolCallContent call:
                    sb.AppendLine($"[Tool call: {call.Name} (id:{call.Id})]");
                    sb.AppendLine(call.InputJson);
                    break;
                case ToolResultContent result:
                    string label = result.IsError ? "Tool error" : "Tool result";
                    sb.AppendLine($"[{label}: id:{result.ToolCallId}]");
                    sb.AppendLine(result.Content);
                    break;
            }
        }
    }

    private static string FormatImageSource(ImageSource source) => source switch
    {
        InlineImage img => $"[Image: {img.MimeType}, {img.Data.Length} bytes]",
        UrlImage url => $"[Image: {url.Url}]",
        ManagedImage managed => $"[Image: file:{managed.FileHandle}]",
        _ => "[Image]",
    };
}
