// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Text;

namespace Physalia.Core.Conversation;

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
            sb.Append('[');
            sb.Append(message.Role);
            sb.AppendLine("]");

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
                }
            }

            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }

    private static string FormatImageSource(ImageSource source) => source switch
    {
        InlineImage img => $"[Image: {img.MimeType}, {img.Data.Length} bytes]",
        UrlImage url => $"[Image: {url.Url}]",
        ManagedImage managed => $"[Image: file:{managed.FileHandle}]",
        _ => "[Image]",
    };
}
