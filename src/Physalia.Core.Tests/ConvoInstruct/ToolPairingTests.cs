// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Physalia.Core.ConvoInstruct;
using Xunit;

namespace Physalia.Core.Tests.ConvoInstruct;

public class ToolPairingTests
{
    private static Conversation Convo(params ConversationMessage[] messages)
    {
        Conversation convo = Conversation.Empty;
        foreach (ConversationMessage message in messages)
        {
            convo = convo.Append(message);
        }

        return convo;
    }

    [Fact]
    public void FindProblems_ValidToolExchange_ReportsNothing()
    {
        Conversation convo = Convo(
            new ConversationMessage(Role.User, "question"),
            new ConversationMessage(Role.Assistant, new MessageContent[]
            {
                new TextContent("checking"),
                new ToolCallContent("id1", "search_components", "{}"),
            }),
            new ConversationMessage(Role.User, new MessageContent[] { new ToolResultContent("id1", "output") }),
            new ConversationMessage(Role.Assistant, "answer"));

        Assert.Empty(ToolPairing.FindProblems(convo));
    }

    [Fact]
    public void FindProblems_ConversationWithNoTools_ReportsNothing()
    {
        Conversation convo = Convo(
            new ConversationMessage(Role.User, "hello"),
            new ConversationMessage(Role.Assistant, "hi"));

        Assert.Empty(ToolPairing.FindProblems(convo));
    }

    [Fact]
    public void FindProblems_UnansweredCall_NamesTheTurnAndTheTool()
    {
        Conversation convo = Convo(
            new ConversationMessage(Role.User, "question"),
            new ConversationMessage(Role.Assistant, new MessageContent[]
            {
                new ToolCallContent("toolu_01BwQq", "search_components", "{}"),
            }),
            new ConversationMessage(Role.User, "unrelated follow-up"));

        IReadOnlyList<string> problems = ToolPairing.FindProblems(convo);

        string problem = Assert.Single(problems);
        Assert.Contains("turn 2", problem);
        Assert.Contains("search_components", problem);
        Assert.Contains("toolu_01BwQq", problem);
    }

    [Fact]
    public void FindProblems_ResultInALaterButNotAdjacentTurn_IsStillAProblem()
    {
        // Providers require the answer in the very next turn, so "somewhere later" is not enough.
        Conversation convo = Convo(
            new ConversationMessage(Role.User, "question"),
            new ConversationMessage(Role.Assistant, new MessageContent[] { new ToolCallContent("id1", "search", "{}") }),
            new ConversationMessage(Role.User, "chat"),
            new ConversationMessage(Role.Assistant, "chat"),
            new ConversationMessage(Role.User, new MessageContent[] { new ToolResultContent("id1", "output") }));

        Assert.NotEmpty(ToolPairing.FindProblems(convo));
    }

    [Fact]
    public void FindProblems_OrphanResult_IsReported()
    {
        Conversation convo = Convo(
            new ConversationMessage(Role.User, new MessageContent[] { new ToolResultContent("ghost", "output") }),
            new ConversationMessage(Role.Assistant, "answer"));

        string problem = Assert.Single(ToolPairing.FindProblems(convo));
        Assert.Contains("turn 1", problem);
        Assert.Contains("ghost", problem);
    }

    [Fact]
    public void FindProblems_PartiallyAnsweredMultiCallTurn_ReportsOnlyTheUnansweredOnes()
    {
        // The shape Anthropic rejected on 2026-07-29: four calls fused into one turn by a
        // compaction merge, with only one result following.
        Conversation convo = Convo(
            new ConversationMessage(Role.User, "question"),
            new ConversationMessage(Role.Assistant, new MessageContent[]
            {
                new ToolCallContent("a", "search_components", "{}"),
                new ToolCallContent("b", "search_components", "{}"),
                new ToolCallContent("c", "search_components", "{}"),
                new ToolCallContent("d", "search_components", "{}"),
            }),
            new ConversationMessage(Role.User, new MessageContent[] { new ToolResultContent("d", "output") }));

        IReadOnlyList<string> problems = ToolPairing.FindProblems(convo);

        Assert.Equal(3, problems.Count);
        Assert.DoesNotContain(problems, p => p.Contains("(d)"));
    }
}
