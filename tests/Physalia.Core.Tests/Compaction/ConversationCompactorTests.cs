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
}
