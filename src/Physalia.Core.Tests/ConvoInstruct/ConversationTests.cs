// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Physalia.Core.ConvoInstruct;
using Xunit;

namespace Physalia.Core.Tests.ConvoInstruct;

public class ConversationTests
{
    private static ConversationMessage User(string text) =>
        new(Role.User, new MessageContent[] { new TextContent(text) });

    private static ConversationMessage Assistant(string text) =>
        new(Role.Assistant, new MessageContent[] { new TextContent(text) });

    [Fact]
    public void Empty_HasNoMessages()
    {
        Assert.Equal(0, Conversation.Empty.Count);
        Assert.Empty(Conversation.Empty.Messages);
    }

    [Fact]
    public void Append_AlternatingRoles_BuildsOrderedHistory()
    {
        Conversation convo = Conversation.Empty
            .Append(User("hi"))
            .Append(Assistant("hello"))
            .Append(User("more"));

        Assert.Equal(3, convo.Count);
        Assert.Equal(Role.User, convo.Messages[0].Role);
        Assert.Equal(Role.Assistant, convo.Messages[1].Role);
        Assert.Equal(Role.User, convo.Messages[2].Role);
    }

    [Fact]
    public void Append_DoesNotMutateSource()
    {
        Conversation first = Conversation.Empty.Append(User("hi"));
        Conversation second = first.Append(Assistant("hello"));

        Assert.Equal(1, first.Count);
        Assert.Equal(2, second.Count);
    }

    [Fact]
    public void Append_ConsecutiveSameRole_Throws()
    {
        Conversation convo = Conversation.Empty.Append(User("hi"));

        Assert.Throws<InvalidOperationException>(() => convo.Append(User("again")));
    }

    [Fact]
    public void Append_Null_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => Conversation.Empty.Append(null!));
    }

    [Fact]
    public void MergeIntoLastUserMessage_Text_AppendsTextBlock()
    {
        Conversation convo = Conversation.Empty.Append(User("first"));

        Conversation merged = convo.MergeIntoLastUserMessage("second");

        Assert.Equal(1, merged.Count);
        ConversationMessage last = merged.Messages[^1];
        Assert.Equal(Role.User, last.Role);
        Assert.Equal(2, last.Content.Count);
        Assert.Equal("first", Assert.IsType<TextContent>(last.Content[0]).Text);
        Assert.Equal("second", Assert.IsType<TextContent>(last.Content[1]).Text);
    }

    [Fact]
    public void MergeIntoLastUserMessage_Blocks_PreservesExistingAndAppendsImages()
    {
        var image = new ImageContent(new UrlImage("https://example.com/a.png"));
        Conversation convo = Conversation.Empty.Append(User("first"));

        Conversation merged = convo.MergeIntoLastUserMessage(new MessageContent[] { image });

        ConversationMessage last = merged.Messages[^1];
        Assert.Equal(2, last.Content.Count);
        Assert.IsType<TextContent>(last.Content[0]);
        Assert.Same(image, last.Content[1]);
    }

    [Fact]
    public void MergeIntoLastUserMessage_BlankText_Throws()
    {
        Conversation convo = Conversation.Empty.Append(User("first"));

        Assert.Throws<ArgumentException>(() => convo.MergeIntoLastUserMessage("   "));
    }

    [Fact]
    public void MergeIntoLastUserMessage_EmptyConversation_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => Conversation.Empty.MergeIntoLastUserMessage("x"));
    }

    [Fact]
    public void MergeIntoLastUserMessage_LastTurnNotUser_Throws()
    {
        Conversation convo = Conversation.Empty
            .Append(User("hi"))
            .Append(Assistant("hello"));

        Assert.Throws<InvalidOperationException>(() => convo.MergeIntoLastUserMessage("x"));
    }

    [Fact]
    public void MergeIntoLastUserMessage_EmptyBlocks_Throws()
    {
        Conversation convo = Conversation.Empty.Append(User("first"));

        Assert.Throws<ArgumentException>(() =>
            convo.MergeIntoLastUserMessage(Array.Empty<MessageContent>()));
    }
}
