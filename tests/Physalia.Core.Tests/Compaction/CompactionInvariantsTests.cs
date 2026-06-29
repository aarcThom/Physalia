// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Physalia.Core.Compaction;
using Physalia.Core.ConvoInstruct;
using Xunit;

namespace Physalia.Core.Tests.Compaction;

public class CompactionInvariantsTests
{
    private static ConversationMessage Msg(Role role, params MessageContent[] blocks) =>
        new(role, blocks);

    private static ConversationMessage UserText(string text) =>
        Msg(Role.User, new TextContent(text));

    private static ConversationMessage AssistantText(string text) =>
        Msg(Role.Assistant, new TextContent(text));

    [Fact]
    public void Reassemble_Empty_ReturnsEmptyConversation()
    {
        Conversation result = CompactionInvariants.Reassemble(Array.Empty<ConversationMessage>());

        Assert.Equal(0, result.Count);
    }

    [Fact]
    public void Reassemble_DropsLeadingAssistantTurn()
    {
        Conversation result = CompactionInvariants.Reassemble(new[]
        {
            AssistantText("leading"),
            UserText("u"),
            AssistantText("a"),
        });

        Assert.Equal(2, result.Count);
        Assert.Equal(Role.User, result.Messages[0].Role);
        Assert.Equal(Role.Assistant, result.Messages[1].Role);
    }

    [Fact]
    public void Reassemble_MergesConsecutiveSameRoleTurns()
    {
        // A cut removed the assistant turn that sat between two user turns.
        Conversation result = CompactionInvariants.Reassemble(new[]
        {
            UserText("a"),
            UserText("b"),
        });

        Assert.Equal(1, result.Count);
        Assert.Equal(2, result.Messages[0].Content.Count);
    }

    [Fact]
    public void Reassemble_StripsOrphanToolResult()
    {
        // The tool_use for "ghost" was compacted away, leaving an orphan tool_result.
        Conversation result = CompactionInvariants.Reassemble(new[]
        {
            UserText("question"),
            Msg(Role.User, new ToolResultContent("ghost", "output")),
        });

        Assert.Equal(1, result.Count);
        Assert.DoesNotContain(result.Messages[0].Content, b => b is ToolResultContent);
    }

    [Fact]
    public void Reassemble_KeepsToolResultWhenMatchingCallSurvives()
    {
        Conversation result = CompactionInvariants.Reassemble(new[]
        {
            UserText("question"),
            Msg(Role.Assistant, new ToolCallContent("id1", "tool", "{}")),
            Msg(Role.User, new ToolResultContent("id1", "output")),
        });

        Assert.Equal(3, result.Count);
        Assert.Contains(result.Messages[2].Content, b => b is ToolResultContent r && r.ToolCallId == "id1");
    }

    [Fact]
    public void Reassemble_NeverProducesConsecutiveSameRole()
    {
        Conversation result = CompactionInvariants.Reassemble(new[]
        {
            UserText("a"),
            UserText("b"),
            AssistantText("c"),
            AssistantText("d"),
        });

        for (int i = 1; i < result.Count; i++)
        {
            Assert.NotEqual(result.Messages[i - 1].Role, result.Messages[i].Role);
        }
    }
}
