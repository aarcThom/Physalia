// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Text;

namespace Physalia.Core.ConvoInstruct;

/// <summary>
/// Utility methods for working with <see cref="Conversation"/> instances.
/// </summary>
public static class ConversationHelpers
{
    /// <summary>
    /// Returns a human-readable representation of the conversation suitable for display
    /// (e.g. the Conversation Log component's canvas output). Not intended for API serialisation.
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

    /// <summary>
    /// Serialises a whole conversation into the content blocks of ONE user message: the transcript
    /// as text, with every image kept as a real image block in the place it appeared. This is what a
    /// local-CLI provider seeds a fresh process with — the CLI holds no prior context, so the history
    /// has to travel inside a single turn.
    /// <para>
    /// The images are the point. Rendering the whole history as text turns a picture into
    /// "[Image: image/png, 152092 bytes]", which tells the model an image exists and then shows it
    /// nothing — so a snapshot, or a marked-up snapshot, silently stopped being visible on any turn
    /// that reseeded. A reseed is not the rare case: it happens whenever the conversation did not grow
    /// by exactly one user message, which a tool round, a feedback turn or a compaction all do.
    /// </para>
    /// <para>
    /// Inline and URL images pass through as blocks because every provider can send those. A
    /// <see cref="ManagedImage"/> keeps its text label: it names a file handle uploaded to one
    /// provider's own store, which a CLI cannot resolve, and saying so beats dropping it.
    /// </para>
    /// </summary>
    /// <param name="conversation">The conversation to serialise.</param>
    /// <param name="trailingInstruction">
    /// Text appended after the transcript — the caller's instruction to carry on from it. Joined onto
    /// the final text block, so a history with no images produces exactly one block.
    /// </param>
    /// <returns>The content blocks of the seed message, in conversation order.</returns>
    public static IReadOnlyList<MessageContent> ToSeedContent(
        Conversation conversation,
        string trailingInstruction)
    {
        ArgumentNullException.ThrowIfNull(conversation);

        var blocks = new List<MessageContent>();
        var sb = new StringBuilder();

        foreach (ConversationMessage message in conversation.Messages)
        {
            sb.Append('[');
            sb.Append(message.Role);
            sb.AppendLine("]");

            foreach (MessageContent block in message.Content)
            {
                // An image interrupts the text run: flush what has been written, hand over the real
                // block, and carry on writing after it. Order is preserved, so the transcript still
                // reads as the conversation did and each picture sits in the turn that carried it.
                if (block is ImageContent { Source: InlineImage or UrlImage })
                {
                    FlushText(blocks, sb);
                    blocks.Add(block);
                    continue;
                }

                AppendBlock(sb, block);
            }

            sb.AppendLine();
        }

        string tail = sb.ToString().TrimEnd();
        string closing = string.IsNullOrEmpty(trailingInstruction)
            ? tail
            : tail.Length > 0 ? $"{tail}\n\n{trailingInstruction}" : trailingInstruction;
        if (closing.Length > 0)
        {
            blocks.Add(new TextContent(closing));
        }

        return blocks;
    }

    private static void FlushText(List<MessageContent> blocks, StringBuilder sb)
    {
        string text = sb.ToString().TrimEnd();
        sb.Clear();
        if (text.Length > 0)
        {
            blocks.Add(new TextContent(text));
        }
    }

    private static void AppendContentBlocks(StringBuilder sb, ConversationMessage message)
    {
        foreach (MessageContent block in message.Content)
        {
            AppendBlock(sb, block);
        }
    }

    // The one text rendering of a content block. Shared by the display strings and by the seed
    // transcript on purpose: the Codex session hands a tool result back worded exactly like this, so
    // a result has to read the same whichever path it arrived by.
    private static void AppendBlock(StringBuilder sb, MessageContent block)
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

    private static string FormatImageSource(ImageSource source) => source switch
    {
        InlineImage img => $"[Image: {img.MimeType}, {img.Data.Length} bytes]",
        UrlImage url => $"[Image: {url.Url}]",
        ManagedImage managed => $"[Image: file:{managed.FileHandle}]",
        _ => "[Image]",
    };
}
