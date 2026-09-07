// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Physalia.Core.Common;
using Physalia.Core.ConvoInstruct;

namespace Physalia.Core.Recording;

/// <summary>
/// One image lifted out of a conversation on its way to disk, or waiting to be put back.
/// </summary>
/// <param name="Key">The name it is filed under — a file name, not a path.</param>
/// <param name="MediaType">Its MIME type.</param>
/// <param name="Bytes">The image itself.</param>
public sealed record ArchivedImage(string Key, string MediaType, byte[] Bytes);

/// <summary>
/// A conversation ready to be written: the transcript as JSON, and the images that go beside it.
/// </summary>
/// <param name="Json">The transcript.</param>
/// <param name="Images">The images the transcript refers to by key.</param>
public sealed record ConversationArchive(string Json, IReadOnlyList<ArchivedImage> Images);

/// <summary>
/// Turns a conversation into something that can be written to a project folder and read back.
///
/// <para><b>Why the transcript has to persist at all.</b> Nothing in the Physalia lifecycle does, on
/// purpose — signals are session events and every component reopens Empty. That is right for solve
/// state and wrong for the conversation: a project runs over weeks, a `.phy` carries a pipeline and
/// its files but not what was said, and reopening a file to find the model has forgotten the entire
/// brief is the single most expensive thing about the plug-in's memory model. So the conversation is
/// the one thing that is written down.</para>
///
/// <para><b>Images go BESIDE the transcript, not inside it.</b> Base64 in JSON triples the bytes and
/// makes the file unreadable by eye, which matters because a transcript is exactly the kind of thing
/// someone will want to open and search. So an image becomes a key plus a file, and the caller owns
/// the writing — passed in as a sink, the same way <c>ModelApiResolver</c> takes an injected
/// environment lookup, and for the same reason: file system in a pure function makes the interesting
/// cases untestable.</para>
///
/// <para><b>A URL image and a managed handle stay as they are.</b> Neither is bytes we hold: one is
/// somebody else's server and the other is a provider's file handle. A handle will usually have
/// expired by the time a conversation is resumed — a Gemini file lives 48 hours — so it is kept as
/// what it was rather than repaired, and the model sees a reference it cannot fetch instead of an
/// image that silently became something else.</para>
///
/// <para><b>Reading is forgiving in one direction only.</b> An unknown block type is DROPPED with the
/// rest of the turn kept, because a transcript written by a newer version should still mostly open;
/// but a turn whose role cannot be read is refused outright, since role alternation is the one thing
/// every provider enforces and a guessed role produces a request that fails at the far end.</para>
/// </summary>
public static class ConversationArchiver
{
    /// <summary>
    /// The format version written into every archive.
    ///
    /// <para>One of the few places a version field earns its keep, for the same reason it does in a
    /// <c>.phy</c> manifest and not in the MCP or API stores: those are written and read by the same
    /// machine within one install, while a transcript sits in a project folder that gets copied,
    /// shared, and opened next year.</para>
    /// </summary>
    public const int FormatVersion = 1;

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
    };

    /// <summary>
    /// Prepares a conversation for writing.
    /// </summary>
    /// <param name="conversation">The conversation to archive.</param>
    /// <returns>The transcript JSON and the images it refers to.</returns>
    public static ConversationArchive Write(Conversation? conversation)
    {
        var images = new List<ArchivedImage>();
        var turns = new List<Dictionary<string, object?>>();

        IReadOnlyList<ConversationMessage> messages =
            conversation?.Messages ?? Array.Empty<ConversationMessage>();

        for (int t = 0; t < messages.Count; t++)
        {
            ConversationMessage message = messages[t];
            var blocks = new List<Dictionary<string, object?>>();

            for (int b = 0; b < message.Content.Count; b++)
            {
                Dictionary<string, object?>? block = WriteBlock(message.Content[b], t, b, images);
                if (block is not null)
                {
                    blocks.Add(block);
                }
            }

            var turn = new Dictionary<string, object?>
            {
                ["role"] = message.Role == Role.Assistant ? "assistant" : "user",
                ["content"] = blocks,
            };

            if (message.IsFeedback)
            {
                turn["isFeedback"] = true;
            }

            if (message.Sources.Count > 0)
            {
                // Kept because it is what badges a feedback turn with the node that produced it in the
                // chat window. A resumed conversation that lost its attribution would show every
                // automated turn as though a person had typed it.
                turn["sources"] = message.Sources
                    .Select(s => new Dictionary<string, object?>
                    {
                        ["id"] = s.Id.ToString(),
                        ["name"] = s.Name,
                    })
                    .ToList();
            }

            turns.Add(turn);
        }

        var document = new Dictionary<string, object?>
        {
            ["version"] = FormatVersion,
            ["saved"] = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
            ["turns"] = turns,
        };

        return new ConversationArchive(JsonSerializer.Serialize(document, WriteOptions), images);
    }

    /// <summary>
    /// Reads a conversation back.
    /// </summary>
    /// <param name="json">The transcript JSON.</param>
    /// <param name="loadImage">
    /// Fetches an image previously written beside the transcript, by key. Return null for one that is
    /// missing — the block is dropped and the rest of the turn kept, because a lost thumbnail is not
    /// a reason to lose the brief.
    /// </param>
    /// <returns>The conversation, or why it could not be read.</returns>
    public static Result<Conversation, string> Read(
        string? json,
        Func<string, ArchivedImage?>? loadImage = null)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new Result<Conversation, string>.Err("The transcript file is empty.");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            return new Result<Conversation, string>.Err($"The transcript is not valid JSON: {ex.Message}");
        }

        using (document)
        {
            JsonElement root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return new Result<Conversation, string>.Err("The transcript is not a JSON object.");
            }

            if (root.TryGetProperty("version", out JsonElement version)
                && version.TryGetInt32(out int number)
                && number > FormatVersion)
            {
                // Refused rather than guessed at, exactly as a .phy from the future is. A transcript
                // read wrongly is worse than one not read: the model would carry on from a history
                // that is subtly not what was said.
                return new Result<Conversation, string>.Err(
                    $"This transcript was written by a newer version of Physalia (format {number}, this one reads {FormatVersion}).");
            }

            if (!root.TryGetProperty("turns", out JsonElement turns) || turns.ValueKind != JsonValueKind.Array)
            {
                return new Result<Conversation, string>.Err("The transcript has no turns.");
            }

            Conversation conversation = Conversation.Empty;

            foreach (JsonElement turn in turns.EnumerateArray())
            {
                if (!TryReadRole(turn, out Role role))
                {
                    return new Result<Conversation, string>.Err(
                        "A turn in the transcript has no readable role. Roles must alternate, so this cannot be guessed.");
                }

                List<MessageContent> blocks = ReadBlocks(turn, loadImage);

                if (blocks.Count == 0)
                {
                    // Every block dropped — an all-image turn whose files have gone. Skipped rather
                    // than appended empty: a provider rejects a turn with no content, so keeping it
                    // would break the very next call.
                    continue;
                }

                var message = new ConversationMessage(role, blocks)
                {
                    IsFeedback = turn.TryGetProperty("isFeedback", out JsonElement flag)
                        && flag.ValueKind == JsonValueKind.True,
                    Sources = ReadSources(turn),
                };

                try
                {
                    conversation = conversation.Append(message);
                }
                catch (InvalidOperationException)
                {
                    // Two same-role turns in a row, which Conversation refuses and every provider
                    // would too. Merge rather than fail: the transcript is somebody's work, and a
                    // history that reads back as one longer turn is far better than none.
                    conversation = Merge(conversation, message);
                }
            }

            return new Result<Conversation, string>.Ok(conversation);
        }
    }

    private static Dictionary<string, object?>? WriteBlock(
        MessageContent content,
        int turnIndex,
        int blockIndex,
        List<ArchivedImage> images)
    {
        switch (content)
        {
            case TextContent text:
                return new Dictionary<string, object?> { ["type"] = "text", ["text"] = text.Text };

            case ToolCallContent call:
                return new Dictionary<string, object?>
                {
                    ["type"] = "toolCall",
                    ["id"] = call.Id,
                    ["name"] = call.Name,
                    ["input"] = call.InputJson,
                };

            case ToolResultContent result:
                return new Dictionary<string, object?>
                {
                    ["type"] = "toolResult",
                    ["id"] = result.ToolCallId,
                    ["content"] = result.Content,
                    ["isError"] = result.IsError,
                };

            case ImageContent image:
                return WriteImage(image.Source, turnIndex, blockIndex, images);

            default:
                // A block type this version does not know how to write. Dropped, and the turn kept.
                return null;
        }
    }

    private static Dictionary<string, object?>? WriteImage(
        ImageSource source,
        int turnIndex,
        int blockIndex,
        List<ArchivedImage> images)
    {
        switch (source)
        {
            case InlineImage inline:
                string key = $"turn-{turnIndex.ToString("000", CultureInfo.InvariantCulture)}-{blockIndex.ToString("00", CultureInfo.InvariantCulture)}{Extension(inline.MimeType)}";
                images.Add(new ArchivedImage(key, inline.MimeType, inline.Data));

                return new Dictionary<string, object?>
                {
                    ["type"] = "image",
                    ["key"] = key,
                    ["mediaType"] = inline.MimeType,
                };

            case UrlImage url:
                return new Dictionary<string, object?> { ["type"] = "image", ["url"] = url.Url };

            case ManagedImage managed:
                // Kept as the handle it was, not resolved: it belongs to a provider's file store and
                // will usually have expired. See the class remarks.
                return new Dictionary<string, object?> { ["type"] = "image", ["handle"] = managed.FileHandle };

            default:
                return null;
        }
    }

    private static List<MessageContent> ReadBlocks(JsonElement turn, Func<string, ArchivedImage?>? loadImage)
    {
        var blocks = new List<MessageContent>();

        if (!turn.TryGetProperty("content", out JsonElement content) || content.ValueKind != JsonValueKind.Array)
        {
            return blocks;
        }

        foreach (JsonElement block in content.EnumerateArray())
        {
            string type = block.TryGetProperty("type", out JsonElement t) ? t.GetString() ?? string.Empty : string.Empty;

            switch (type)
            {
                case "text":
                    string text = Str(block, "text");
                    if (text.Length > 0)
                    {
                        blocks.Add(new TextContent(text));
                    }

                    break;

                case "toolCall":
                    blocks.Add(new ToolCallContent(Str(block, "id"), Str(block, "name"), Str(block, "input")));
                    break;

                case "toolResult":
                    blocks.Add(new ToolResultContent(
                        Str(block, "id"),
                        Str(block, "content"),
                        block.TryGetProperty("isError", out JsonElement e) && e.ValueKind == JsonValueKind.True));
                    break;

                case "image":
                    MessageContent? image = ReadImage(block, loadImage);
                    if (image is not null)
                    {
                        blocks.Add(image);
                    }

                    break;

                default:
                    // Unknown block type from a newer writer. Dropped; the turn survives.
                    break;
            }
        }

        return blocks;
    }

    private static MessageContent? ReadImage(JsonElement block, Func<string, ArchivedImage?>? loadImage)
    {
        if (block.TryGetProperty("url", out JsonElement url) && url.GetString() is { Length: > 0 } address)
        {
            return new ImageContent(new UrlImage(address));
        }

        if (block.TryGetProperty("handle", out JsonElement handle) && handle.GetString() is { Length: > 0 } file)
        {
            return new ImageContent(new ManagedImage(file));
        }

        if (loadImage is null
            || !block.TryGetProperty("key", out JsonElement keyElement)
            || keyElement.GetString() is not { Length: > 0 } key)
        {
            return null;
        }

        ArchivedImage? loaded = loadImage(key);
        return loaded is null ? null : new ImageContent(new InlineImage(loaded.Bytes, loaded.MediaType));
    }

    private static IReadOnlyList<ComponentOrigin> ReadSources(JsonElement turn)
    {
        if (!turn.TryGetProperty("sources", out JsonElement sources) || sources.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<ComponentOrigin>();
        }

        var origins = new List<ComponentOrigin>();

        foreach (JsonElement source in sources.EnumerateArray())
        {
            Guid id = Guid.TryParse(Str(source, "id"), out Guid parsed) ? parsed : Guid.Empty;
            origins.Add(new ComponentOrigin(id, Str(source, "name")));
        }

        return origins;
    }

    private static bool TryReadRole(JsonElement turn, out Role role)
    {
        role = Role.User;

        if (!turn.TryGetProperty("role", out JsonElement element) || element.GetString() is not { } text)
        {
            return false;
        }

        if (string.Equals(text, "assistant", StringComparison.OrdinalIgnoreCase))
        {
            role = Role.Assistant;
            return true;
        }

        if (string.Equals(text, "user", StringComparison.OrdinalIgnoreCase))
        {
            role = Role.User;
            return true;
        }

        return false;
    }

    // Folds a same-role turn into the previous one. Text joins with a blank line; every other block
    // is carried across as it stands, so an image or a tool result is never lost to the merge.
    private static Conversation Merge(Conversation conversation, ConversationMessage message)
    {
        if (conversation.Count == 0)
        {
            return conversation;
        }

        ConversationMessage last = conversation.Messages[^1];
        var merged = new List<MessageContent>(last.Content);

        foreach (MessageContent block in message.Content)
        {
            if (block is TextContent text
                && merged.Count > 0
                && merged[^1] is TextContent existing)
            {
                merged[^1] = new TextContent(existing.Text + Environment.NewLine + Environment.NewLine + text.Text);
                continue;
            }

            merged.Add(block);
        }

        var rebuilt = Conversation.Empty;
        for (int i = 0; i < conversation.Count - 1; i++)
        {
            rebuilt = rebuilt.Append(conversation.Messages[i]);
        }

        return rebuilt.Append(new ConversationMessage(last.Role, merged)
        {
            IsFeedback = last.IsFeedback,
            Sources = last.Sources,
        });
    }

    private static string Str(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) ? value.GetString() ?? string.Empty : string.Empty;

    private static string Extension(string mimeType) => mimeType?.ToLowerInvariant() switch
    {
        "image/png" => ".png",
        "image/jpeg" => ".jpg",
        "image/jpg" => ".jpg",
        "image/gif" => ".gif",
        "image/webp" => ".webp",
        _ => ".bin",
    };
}
