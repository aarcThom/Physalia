// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Linq;
using Physalia.Core.Compaction;
using Physalia.Core.ConvoInstruct;
using Xunit;

namespace Physalia.Core.Tests.Compaction;

public class StaleHistoryPruneTests
{
    private const string PlanBlock = "<plan>\ngoal: A tower\n1. Base\n2. Shaft\nnow: 1\n</plan>";

    private static string Document(int pad) =>
        "{\n  \"schema\": \"1.0\",\n  \"kind\": \"ghpatch\",\n  \"pad\": \"" + new string('x', pad) + "\"\n}";

    // Alternating user/assistant, where each assistant turn carries a plan block and a document —
    // the exact shape of a Physalia build loop.
    private static Conversation BuildLoop(int rounds)
    {
        Conversation convo = Conversation.Empty;
        for (int i = 0; i < rounds; i++)
        {
            convo = convo.Append(new ConversationMessage(
                Role.User, new MessageContent[] { new TextContent($"feedback {i}") }));
            convo = convo.Append(new ConversationMessage(
                Role.Assistant, new MessageContent[] { new TextContent(PlanBlock + "\n" + Document(600)) }));
        }

        return convo;
    }

    private static string TextOf(ConversationMessage message) =>
        string.Concat(message.Content.OfType<TextContent>().Select(t => t.Text));

    [Fact]
    public void StaleDocumentKeepLast_StubsOldDocumentsAndKeepsTheRecentOne()
    {
        Conversation convo = BuildLoop(4); // 8 messages; last assistant turn is at index 7

        CompactionResult result = ConversationCompactor.Prune(
            convo, new PruneOptions { StaleDocumentKeepLast = 2 });

        var messages = result.Conversation.Messages.ToList();

        // fromEnd == 1 for the final message, so a keep-last of 2 protects indices 6 and 7.
        Assert.Contains("\"schema\"", TextOf(messages[7]), StringComparison.Ordinal);

        foreach (int stale in new[] { 1, 3, 5 })
        {
            Assert.DoesNotContain("\"schema\"", TextOf(messages[stale]), StringComparison.Ordinal);
            Assert.Contains("elided from this transcript", TextOf(messages[stale]), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void StaleDocumentKeepLast_NeverTouchesUserTurns()
    {
        // A feedback turn can legitimately quote JSON back at the model; only the model's own
        // submissions are redundant with the canvas state.
        Conversation convo = Conversation.Empty
            .Append(new ConversationMessage(Role.User, new MessageContent[] { new TextContent(Document(600)) }))
            .Append(new ConversationMessage(Role.Assistant, new MessageContent[] { new TextContent("ok") }))
            .Append(new ConversationMessage(Role.User, new MessageContent[] { new TextContent("go on") }))
            .Append(new ConversationMessage(Role.Assistant, new MessageContent[] { new TextContent("done") }));

        CompactionResult result = ConversationCompactor.Prune(
            convo, new PruneOptions { StaleDocumentKeepLast = 1 });

        Assert.Contains("\"schema\"", TextOf(result.Conversation.Messages[0]), StringComparison.Ordinal);
    }

    [Fact]
    public void StalePlanBlockKeepLast_StripsOldBlocksAndKeepsTheRecentOne()
    {
        Conversation convo = BuildLoop(4);

        CompactionResult result = ConversationCompactor.Prune(
            convo, new PruneOptions { StalePlanBlockKeepLast = 2 });

        var messages = result.Conversation.Messages.ToList();

        Assert.Contains("<plan>", TextOf(messages[7]), StringComparison.Ordinal);
        foreach (int stale in new[] { 1, 3, 5 })
        {
            Assert.DoesNotContain("<plan>", TextOf(messages[stale]), StringComparison.Ordinal);

            // The document itself must survive plan stripping — this option touches only the block.
            Assert.Contains("\"schema\"", TextOf(messages[stale]), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void BothOptionsCompose()
    {
        Conversation convo = BuildLoop(4);

        CompactionResult result = ConversationCompactor.Prune(
            convo, new PruneOptions { StaleDocumentKeepLast = 2, StalePlanBlockKeepLast = 2 });

        string stale = TextOf(result.Conversation.Messages[1]);

        Assert.DoesNotContain("<plan>", stale, StringComparison.Ordinal);
        Assert.DoesNotContain("\"schema\"", stale, StringComparison.Ordinal);
        Assert.Contains("elided from this transcript", stale, StringComparison.Ordinal);
    }

    [Fact]
    public void PruningPreservesRoleAlternationAndTurnCount()
    {
        Conversation convo = BuildLoop(4);

        CompactionResult result = ConversationCompactor.Prune(
            convo, new PruneOptions { StaleDocumentKeepLast = 2, StalePlanBlockKeepLast = 2 });

        // Nothing is dropped — every turn keeps text — so the thread is structurally identical.
        Assert.Equal(convo.Count, result.Conversation.Count);
        Assert.Equal(
            convo.Messages.Select(m => m.Role),
            result.Conversation.Messages.Select(m => m.Role));
    }

    [Fact]
    public void NullOptionsAreANoOp()
    {
        Conversation convo = BuildLoop(3);

        CompactionResult result = ConversationCompactor.Prune(convo, new PruneOptions());

        Assert.Equal(
            convo.Messages.Select(TextOf),
            result.Conversation.Messages.Select(TextOf));
    }

    [Fact]
    public void PruningReclaimsTheBulkOfALongLoop()
    {
        Conversation convo = BuildLoop(10);
        int before = convo.Messages.Sum(m => TextOf(m).Length);

        CompactionResult result = ConversationCompactor.Prune(
            convo, new PruneOptions { StaleDocumentKeepLast = 2, StalePlanBlockKeepLast = 2 });
        int after = result.Conversation.Messages.Sum(m => TextOf(m).Length);

        Assert.True(after < before / 2, $"expected at least half reclaimed; {before} -> {after}");
    }
}
