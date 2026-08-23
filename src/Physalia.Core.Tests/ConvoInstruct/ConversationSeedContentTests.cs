// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Collections.Generic;
using System.Linq;
using Physalia.Core.ConvoInstruct;
using Xunit;

namespace Physalia.Core.Tests.ConvoInstruct;

/// <summary>
/// Tests for <see cref="ConversationHelpers.ToSeedContent"/> — the local-CLI providers' history
/// serialisation. The rule these pin down is that an image survives a seed AS AN IMAGE: rendering the
/// history as text alone reduced a picture to "[Image: image/png, N bytes]", so a snapshot was
/// invisible to the model on every turn that reseeded.
/// </summary>
public class ConversationSeedContentTests
{
    private const string Instruction = "Continue from the conversation above.";

    private static readonly byte[] Pixels = { 1, 2, 3, 4 };

    [Fact]
    public void ToSeedContent_KeepsInlineImageAsAnImageBlock()
    {
        Conversation conversation = TwoTurnsWithImage();

        IReadOnlyList<MessageContent> blocks = ConversationHelpers.ToSeedContent(conversation, Instruction);

        ImageContent image = Assert.Single(blocks.OfType<ImageContent>());
        InlineImage inline = Assert.IsType<InlineImage>(image.Source);
        Assert.Equal(Pixels, inline.Data);
        Assert.Equal("image/png", inline.MimeType);
    }

    [Fact]
    public void ToSeedContent_KeepsTheImageInPlace()
    {
        Conversation conversation = TwoTurnsWithImage();

        IReadOnlyList<MessageContent> blocks = ConversationHelpers.ToSeedContent(conversation, Instruction);

        // Text before the picture, the picture, then the text that followed it: the transcript still
        // reads in conversation order, which is what tells the model whose turn the picture was in.
        Assert.Collection(
            blocks,
            first => Assert.Contains("look at this", Assert.IsType<TextContent>(first).Text),
            second => Assert.IsType<ImageContent>(second),
            third => Assert.Contains("I see a roof", Assert.IsType<TextContent>(third).Text));
    }

    [Fact]
    public void ToSeedContent_PutsTheInstructionLast()
    {
        Conversation conversation = TwoTurnsWithImage();

        IReadOnlyList<MessageContent> blocks = ConversationHelpers.ToSeedContent(conversation, Instruction);

        Assert.EndsWith(Instruction, Assert.IsType<TextContent>(blocks[^1]).Text);
    }

    [Fact]
    public void ToSeedContent_TextOnlyHistoryIsOneBlock()
    {
        var conversation = Conversation.Empty
            .Append(new ConversationMessage(Role.User, new MessageContent[] { new TextContent("hello") }))
            .Append(new ConversationMessage(Role.Assistant, new MessageContent[] { new TextContent("hi") }));

        IReadOnlyList<MessageContent> blocks = ConversationHelpers.ToSeedContent(conversation, Instruction);

        // No images means no reason to split: the seed stays the single text block it always was.
        TextContent only = Assert.IsType<TextContent>(Assert.Single(blocks));
        Assert.Equal(
            $"{ConversationHelpers.ToDisplayString(conversation)}\n\n{Instruction}",
            only.Text);
    }

    [Fact]
    public void ToSeedContent_EmptyConversationIsTheInstructionAlone()
    {
        IReadOnlyList<MessageContent> blocks = ConversationHelpers.ToSeedContent(Conversation.Empty, Instruction);

        Assert.Equal(Instruction, Assert.IsType<TextContent>(Assert.Single(blocks)).Text);
    }

    [Fact]
    public void ToSeedContent_ManagedImageStaysATextLabel()
    {
        var conversation = Conversation.Empty
            .Append(new ConversationMessage(
                Role.User,
                new MessageContent[]
                {
                    new TextContent("see attached"),
                    new ImageContent(new ManagedImage("file-abc")),
                }))
            .Append(new ConversationMessage(Role.Assistant, new MessageContent[] { new TextContent("ok") }));

        IReadOnlyList<MessageContent> blocks = ConversationHelpers.ToSeedContent(conversation, Instruction);

        // A managed handle names a file in one provider's own store, which a CLI cannot fetch. Saying
        // so in the transcript beats handing over a block the session would drop on the floor.
        Assert.Empty(blocks.OfType<ImageContent>());
        Assert.Contains("file:file-abc", Assert.IsType<TextContent>(Assert.Single(blocks)).Text);
    }

    [Fact]
    public void ToSeedContent_KeepsEveryImageAcrossTurns()
    {
        var conversation = Conversation.Empty
            .Append(new ConversationMessage(
                Role.User,
                new MessageContent[] { new TextContent("first"), new ImageContent(new InlineImage(Pixels, "image/png")) }))
            .Append(new ConversationMessage(Role.Assistant, new MessageContent[] { new TextContent("noted") }))
            .Append(new ConversationMessage(
                Role.User,
                new MessageContent[] { new TextContent("second"), new ImageContent(new InlineImage(Pixels, "image/png")) }));

        IReadOnlyList<MessageContent> blocks = ConversationHelpers.ToSeedContent(conversation, Instruction);

        Assert.Equal(2, blocks.OfType<ImageContent>().Count());
    }

    private static Conversation TwoTurnsWithImage() =>
        Conversation.Empty
            .Append(new ConversationMessage(
                Role.User,
                new MessageContent[]
                {
                    new TextContent("look at this"),
                    new ImageContent(new InlineImage(Pixels, "image/png")),
                }))
            .Append(new ConversationMessage(Role.Assistant, new MessageContent[] { new TextContent("I see a roof") }));
}
