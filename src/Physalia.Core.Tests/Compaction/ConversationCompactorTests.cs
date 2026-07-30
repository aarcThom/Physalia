// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Physalia.Core.Compaction;
using Physalia.Core.ConvoInstruct;
using Physalia.Core.Tokens;
using Xunit;

namespace Physalia.Core.Tests.Compaction;

public class ConversationCompactorTests
{
    // 100 tokens per retained message — deterministic, independent of text content.
    private sealed class CountingEstimator : ISyncTokenEstimator
    {
        public int Estimate(Instructions instructions) => instructions.Conversation.Count * 100;
    }

    private static Conversation Build(int messageCount)
    {
        Conversation convo = Conversation.Empty;
        for (int i = 0; i < messageCount; i++)
        {
            Role role = i % 2 == 0 ? Role.User : Role.Assistant;
            convo = convo.Append(new ConversationMessage(role, new MessageContent[] { new TextContent($"m{i}") }));
        }

        return convo;
    }

    [Fact]
    public void KeepWithinTokenBudget_DropsUntilWithinBudget()
    {
        var estimator = new CountingEstimator();
        Conversation convo = Build(5); // 500 tokens

        CompactionResult result = ConversationCompactor.KeepWithinTokenBudget(convo, string.Empty, estimator, 250);

        int finalTokens = estimator.Estimate(new Instructions(string.Empty, result.Conversation));
        Assert.True(finalTokens <= 250, $"Expected <= 250 but was {finalTokens}");
        Assert.True(result.RetainedMessageCount < result.OriginalMessageCount);
    }

    [Fact]
    public void KeepWithinTokenBudget_WithinBudget_KeepsEverything()
    {
        var estimator = new CountingEstimator();
        Conversation convo = Build(3); // 300 tokens

        CompactionResult result = ConversationCompactor.KeepWithinTokenBudget(convo, string.Empty, estimator, 1000);

        Assert.Equal(3, result.RetainedMessageCount);
    }

    [Fact]
    public void KeepWithinTokenBudget_SingleOversizeMessage_IsKept()
    {
        var estimator = new CountingEstimator();
        Conversation convo = Build(1); // 100 tokens, but budget is smaller

        CompactionResult result = ConversationCompactor.KeepWithinTokenBudget(convo, string.Empty, estimator, 10);

        Assert.Equal(1, result.Conversation.Count);
    }

    /// <summary>
    /// Rebuilds the conversation from the 2026-07-29 staged-build session: a prompt, two tool
    /// exchanges, then alternating patch/report rounds. At 11 turns the Anchored Window fired for
    /// the first time and its head cut landed inside the first tool exchange, which produced an
    /// Anthropic 400 and killed the build at stage 3 of 5.
    /// </summary>
    private static Conversation BuildStagedSessionConversation()
    {
        Conversation convo = Conversation.Empty;

        convo = convo.Append(new ConversationMessage(Role.User, "create a 3d model of the whitehouse"));

        // Turn 2: three search_components calls in one assistant turn. Turn 3: their results.
        convo = convo.Append(new ConversationMessage(Role.Assistant, new MessageContent[]
        {
            new TextContent("<think>planning</think>"),
            new ToolCallContent("toolu_01BwQq", "search_components", "{\"query\":\"Domain Box\"}"),
            new ToolCallContent("toolu_014Be7", "search_components", "{\"query\":\"Construct Domain\"}"),
            new ToolCallContent("toolu_01SvRpt", "search_components", "{\"query\":\"XY Plane\"}"),
        }));
        convo = convo.Append(new ConversationMessage(Role.User, new MessageContent[]
        {
            new ToolResultContent("toolu_01BwQq", "Domain Box …"),
            new ToolResultContent("toolu_014Be7", "Construct Domain …"),
            new ToolResultContent("toolu_01SvRpt", "XY Plane …"),
        }));

        // Turns 4–5: a second, single-call exchange.
        convo = convo.Append(new ConversationMessage(Role.Assistant, new MessageContent[]
        {
            new TextContent("<think>more planning</think>"),
            new ToolCallContent("toolu_01Niuvw", "search_components", "{\"query\":\"Panel\"}"),
        }));
        convo = convo.Append(new ConversationMessage(Role.User, new MessageContent[]
        {
            new ToolResultContent("toolu_01Niuvw", "Panel …"),
        }));

        // Turn 6: stage 1. Turns 7–11: report, patch, report, patch, report.
        convo = convo.Append(new ConversationMessage(Role.Assistant, "stage 1 ghjson"));
        for (int round = 2; round <= 3; round++)
        {
            convo = convo.Append(new ConversationMessage(Role.User, $"geometry report {round - 1}") { IsFeedback = true });
            convo = convo.Append(new ConversationMessage(Role.Assistant, $"now: {round}"));
        }

        convo = convo.Append(new ConversationMessage(Role.User, "geometry report 3") { IsFeedback = true });

        return convo;
    }

    [Fact]
    public void KeepHeadAndTail_StagedSessionAtDefaults_ProducesASendableConversation()
    {
        Conversation convo = BuildStagedSessionConversation();
        Assert.Equal(11, convo.Count);

        // The Anchored Window's shipped defaults — the exact configuration that failed.
        CompactionResult result = ConversationCompactor.KeepHeadAndTail(convo, 2, 8);

        Assert.Empty(ToolPairing.FindProblems(result.Conversation));
    }

    [Fact]
    public void KeepHeadAndTail_ShrinksHeadOffAnAssistantTurnAskingForTools()
    {
        Conversation convo = BuildStagedSessionConversation();

        CompactionResult result = ConversationCompactor.KeepHeadAndTail(convo, 2, 8);

        // Head of 2 would have ended on turn 2, the three-call turn whose results sit in the
        // dropped middle. The head shrinks to the opening prompt so the exchange is dropped
        // WHOLE — not stripped down to a stump of a turn that asks for nothing. Reassemble would
        // make either version sendable, so the surviving text is what tells them apart.
        Assert.Equal(Role.User, result.Conversation.Messages[0].Role);
        Assert.DoesNotContain(
            result.Conversation.Messages.SelectMany(m => m.Content),
            b => b is ToolCallContent c && c.Id == "toolu_01BwQq");
        Assert.DoesNotContain(
            result.Conversation.Messages.SelectMany(m => m.Content),
            b => b is TextContent t && t.Text == "<think>planning</think>");
    }

    [Fact]
    public void KeepHeadAndTail_KeepsAToolExchangeThatFitsEntirelyInTheHead()
    {
        Conversation convo = BuildStagedSessionConversation();

        // A head of 3 covers the whole first exchange, so nothing needs shrinking.
        CompactionResult result = ConversationCompactor.KeepHeadAndTail(convo, 3, 4);

        Assert.Empty(ToolPairing.FindProblems(result.Conversation));
        Assert.Contains(
            result.Conversation.Messages.SelectMany(m => m.Content),
            b => b is ToolCallContent c && c.Id == "toolu_01BwQq");
    }

    [Fact]
    public void KeepRecentMessages_CuttingIntoAToolExchange_StaysSendable()
    {
        Conversation convo = BuildStagedSessionConversation();

        // Walk every window size: no cut may ever produce an unsendable conversation.
        for (int keep = 1; keep <= convo.Count; keep++)
        {
            CompactionResult result = ConversationCompactor.KeepRecentMessages(convo, keep);
            Assert.Empty(ToolPairing.FindProblems(result.Conversation));
        }
    }

    [Fact]
    public void KeepHeadAndTail_EveryHeadAndTailCombination_StaysSendable()
    {
        Conversation convo = BuildStagedSessionConversation();

        for (int head = 0; head <= convo.Count; head++)
        {
            for (int tail = 0; tail <= convo.Count; tail++)
            {
                CompactionResult result = ConversationCompactor.KeepHeadAndTail(convo, head, tail);
                Assert.Empty(ToolPairing.FindProblems(result.Conversation));
            }
        }
    }
}
