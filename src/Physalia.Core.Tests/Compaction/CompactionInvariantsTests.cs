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

    /// <summary>
    /// Asserts the invariant providers actually enforce: every tool call is answered by the turn
    /// immediately after the one that asked, not merely somewhere later.
    /// </summary>
    private static void AssertEveryCallAnsweredByNextTurn(Conversation conversation)
    {
        for (int i = 0; i < conversation.Count; i++)
        {
            var calls = conversation.Messages[i].Content.OfType<ToolCallContent>().ToList();
            if (calls.Count == 0)
            {
                continue;
            }

            Assert.True(i + 1 < conversation.Count, $"Turn {i + 1} asks for tools with no turn after it.");

            var answered = conversation.Messages[i + 1].Content
                .OfType<ToolResultContent>()
                .Select(r => r.ToolCallId)
                .ToHashSet();

            foreach (ToolCallContent call in calls)
            {
                Assert.Contains(call.Id, answered);
            }
        }
    }

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
    public void Reassemble_StripsDanglingToolCall_WhenItsResultWasCut()
    {
        // The tool_result turn was compacted away, leaving the call unanswered — the mirror of
        // the orphan-result case, and a hard provider error in its own right.
        Conversation result = CompactionInvariants.Reassemble(new[]
        {
            UserText("question"),
            Msg(Role.Assistant, new TextContent("looking that up"), new ToolCallContent("id1", "search", "{}")),
            UserText("next question"),
        });

        Assert.DoesNotContain(
            result.Messages.SelectMany(m => m.Content),
            b => b is ToolCallContent);

        // The reasoning around the call is not collateral damage.
        Assert.Contains(result.Messages[1].Content, b => b is TextContent t && t.Text == "looking that up");
    }

    [Fact]
    public void Reassemble_DropsTurnLeftWithNothingButUnanswerableCalls()
    {
        Conversation result = CompactionInvariants.Reassemble(new[]
        {
            UserText("question"),
            Msg(Role.Assistant, new ToolCallContent("id1", "search", "{}")),
            UserText("unrelated follow-up"),
        });

        // The assistant turn held only the call, so it goes entirely — and the two user turns
        // that are now adjacent merge, leaving a single valid opening turn.
        Assert.Equal(1, result.Count);
        Assert.Equal(Role.User, result.Messages[0].Role);
    }

    [Fact]
    public void Reassemble_NeverLeavesACallUnansweredByTheVeryNextTurn()
    {
        // Two assistant turns, each with its own answered call, but the tool_result between them
        // was cut. Merging them (step 4) would fuse both calls into one turn that the single
        // surviving result cannot fully answer — the exact shape Anthropic rejected on
        // 2026-07-29.
        Conversation result = CompactionInvariants.Reassemble(new[]
        {
            UserText("question"),
            Msg(Role.Assistant, new ToolCallContent("id1", "search", "{}")),
            Msg(Role.Assistant, new ToolCallContent("id2", "search", "{}")),
            Msg(Role.User, new ToolResultContent("id2", "output")),
        });

        AssertEveryCallAnsweredByNextTurn(result);
    }

    [Fact]
    public void Reassemble_StripsTrailingCallWithNoTurnAfterIt()
    {
        Conversation result = CompactionInvariants.Reassemble(new[]
        {
            UserText("question"),
            Msg(Role.Assistant, new TextContent("on it"), new ToolCallContent("id1", "search", "{}")),
        });

        AssertEveryCallAnsweredByNextTurn(result);
    }

    [Fact]
    public void Reassemble_ChainOfHalfAnsweredExchanges_StillProducesAValidConversation()
    {
        // Adversarial: consecutive assistant turns each asking for a tool, with results present
        // only for some. Every repair here re-merges and re-strips, so this is the shape that
        // exercises the pass loop rather than settling on the first pass. It must never throw
        // (Append rejects consecutive same-role turns) and must come out sendable.
        var messages = new List<ConversationMessage> { UserText("start") };
        for (int i = 0; i < 12; i++)
        {
            messages.Add(Msg(Role.Assistant, new ToolCallContent($"id{i}", "search", "{}")));

            // Answer only every third call, and never adjacently — the rest dangle.
            if (i % 3 == 0)
            {
                messages.Add(Msg(Role.User, new ToolResultContent($"id{i}", "output")));
            }
        }

        Conversation result = CompactionInvariants.Reassemble(messages);

        AssertEveryCallAnsweredByNextTurn(result);
        for (int i = 1; i < result.Count; i++)
        {
            Assert.NotEqual(result.Messages[i - 1].Role, result.Messages[i].Role);
        }

        Assert.True(result.Count == 0 || result.Messages[0].Role == Role.User);
    }

    [Fact]
    public void Reassemble_IsIdempotentOnAValidToolExchange()
    {
        var messages = new[]
        {
            UserText("question"),
            Msg(Role.Assistant, new TextContent("checking"), new ToolCallContent("id1", "search", "{}")),
            Msg(Role.User, new ToolResultContent("id1", "output")),
            AssistantText("answer"),
        };

        Conversation once = CompactionInvariants.Reassemble(messages);
        Conversation twice = CompactionInvariants.Reassemble(once.Messages);

        Assert.Equal(4, once.Count);
        Assert.Equal(once.Count, twice.Count);
        Assert.Contains(once.Messages[1].Content, b => b is ToolCallContent c && c.Id == "id1");
        Assert.Contains(twice.Messages[1].Content, b => b is ToolCallContent c && c.Id == "id1");
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
