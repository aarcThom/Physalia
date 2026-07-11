// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Physalia.Core.Common;
using Physalia.Core.ConvoInstruct;
using Xunit;

namespace Physalia.Core.Tests.Common;

public class ThinkingTagsTests
{
    [Fact]
    public void Strip_ClosedBlock_ReturnsAnswer()
    {
        Assert.Equal("answer", ThinkingTags.Strip("<think>reasoning</think>\n\nanswer"));
    }

    [Fact]
    public void Strip_ThinkingVariantAndMixedCase_Removed()
    {
        Assert.Equal("answer", ThinkingTags.Strip("<THINKING>reasoning</thinking>answer"));
    }

    [Fact]
    public void Strip_UnclosedTrailingBlock_ReturnsPrecedingText()
    {
        Assert.Equal("answer", ThinkingTags.Strip("answer <think>partial reasoning"));
    }

    [Fact]
    public void Strip_MultipleBlocks_AllRemoved()
    {
        Assert.Equal("a b", ThinkingTags.Strip("<think>one</think>a <think>two</think>b"));
    }

    [Fact]
    public void Strip_NoTags_ReturnsTrimmedInput()
    {
        Assert.Equal("plain text", ThinkingTags.Strip("  plain text  "));
    }

    [Fact]
    public void Strip_NullOrEmpty_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, ThinkingTags.Strip(null));
        Assert.Equal(string.Empty, ThinkingTags.Strip(string.Empty));
    }

    [Fact]
    public void Strip_ThinkingOnly_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, ThinkingTags.Strip("<think>only reasoning</think>"));
        Assert.Equal(string.Empty, ThinkingTags.Strip("<think>truncated reasoning"));
    }

    [Fact]
    public void StripAssistantMessage_MixedBlocks_PreservesNonText()
    {
        var message = new ConversationMessage(Role.Assistant, new MessageContent[]
        {
            new TextContent("<think>reasoning</think>\n\nanswer"),
            new ToolCallContent("id_1", "alpha", "{}"),
        });

        ConversationMessage stripped = ThinkingTags.StripAssistantMessage(message);

        Assert.Equal(2, stripped.Content.Count);
        Assert.Equal("answer", Assert.IsType<TextContent>(stripped.Content[0]).Text);
        Assert.IsType<ToolCallContent>(stripped.Content[1]);
    }

    [Fact]
    public void StripAssistantMessage_ThinkingOnly_SubstitutesPlaceholder()
    {
        var message = new ConversationMessage(Role.Assistant, "<think>only reasoning</think>");

        ConversationMessage stripped = ThinkingTags.StripAssistantMessage(message);

        var text = Assert.IsType<TextContent>(Assert.Single(stripped.Content));
        Assert.False(string.IsNullOrWhiteSpace(text.Text));
        Assert.DoesNotContain("<think", text.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StripAssistantMessage_NoTags_ReturnsSameInstance()
    {
        var message = new ConversationMessage(Role.Assistant, "plain answer");

        Assert.Same(message, ThinkingTags.StripAssistantMessage(message));
    }
}

public class StopReasonsTests
{
    [Theory]
    [InlineData("max_tokens")]
    [InlineData("MAX_TOKENS")]
    [InlineData("length")]
    public void IsTruncation_TokenLimitReasons_True(string reason)
    {
        Assert.True(StopReasons.IsTruncation(reason));
    }

    [Theory]
    [InlineData("end_turn")]
    [InlineData("stop")]
    [InlineData("STOP")]
    [InlineData(null)]
    public void IsTruncation_NormalOrMissingReasons_False(string? reason)
    {
        Assert.False(StopReasons.IsTruncation(reason));
    }
}
