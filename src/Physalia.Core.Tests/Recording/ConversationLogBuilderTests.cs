// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Physalia.Core.ConvoInstruct;
using Physalia.Core.Recording;
using Physalia.Core.Signals;
using Xunit;

namespace Physalia.Core.Tests.Recording;

public class ConversationLogBuilderTests
{
    private static PhySignal Text(string payload) =>
        PhySignal.Mint(SignalOutcome.Success, payload, Guid.NewGuid(), "test");

    private static PhySignal Blocks(string payload, params MessageContent[] blocks) =>
        PhySignal.Mint(SignalOutcome.Success, payload, Guid.NewGuid(), "test", contentBlocks: blocks);

    private static RecordResult Record(Conversation start, params RecordEvent[] events) =>
        ConversationLogBuilder.Record(start, events);

    [Fact]
    public void Prompt_StartsUserTurn_AndFiresInference()
    {
        RecordResult result = Record(Conversation.Empty, new RecordEvent(RecordedTurnKind.Prompt, Text("hello")));

        Assert.Equal(RecordOutcome.UserTurn, result.Outcome);
        Assert.Equal(1, result.Conversation.Count);
        Assert.Equal(Role.User, result.Conversation.Messages[0].Role);
        Assert.Equal("hello", result.UserTraceText);
    }

    [Fact]
    public void Response_AfterUser_AppendsAssistant_AndLatchesQuietly()
    {
        Conversation start = Conversation.Empty.Append(new ConversationMessage(Role.User, "q"));

        RecordResult result = Record(start, new RecordEvent(RecordedTurnKind.Response, Text("answer")));

        Assert.Equal(RecordOutcome.AssistantTurn, result.Outcome);
        Assert.Equal(2, result.Conversation.Count);
        Assert.Equal(Role.Assistant, result.Conversation.Messages[^1].Role);
    }

    [Fact]
    public void ResponseThenFeedback_InOneBatch_RecordsAssistantFirst_OutcomeIsUserTurn()
    {
        // Sequence order: the response that provoked the feedback is recorded before the feedback,
        // and the final outcome is a user turn (which fires the LLM Call again).
        Conversation start = Conversation.Empty.Append(new ConversationMessage(Role.User, "q"));

        RecordResult result = Record(
            start,
            new RecordEvent(RecordedTurnKind.Response, Text("answer")),
            new RecordEvent(RecordedTurnKind.Feedback, Text("try again")));

        Assert.Equal(RecordOutcome.UserTurn, result.Outcome);
        Assert.Equal(3, result.Conversation.Count);
        Assert.Equal(Role.Assistant, result.Conversation.Messages[1].Role);
        Assert.Equal(Role.User, result.Conversation.Messages[2].Role);
        Assert.True(result.Conversation.Messages[2].IsFeedback);
    }

    [Fact]
    public void TwoUserSideEvents_MergeIntoOneTurn()
    {
        // Prompt then feedback before any assistant turn: providers require alternation, so they merge.
        RecordResult result = Record(
            Conversation.Empty,
            new RecordEvent(RecordedTurnKind.Prompt, Text("first")),
            new RecordEvent(RecordedTurnKind.Feedback, Text("second")));

        Assert.Equal(1, result.Conversation.Count);
        Assert.Equal(2, result.Conversation.Messages[0].Content.Count);
        Assert.Equal(RecordOutcome.UserTurn, result.Outcome);
    }

    [Fact]
    public void ToolSignal_WithToolUse_RecordsAssistantTurn_Quietly()
    {
        Conversation start = Conversation.Empty.Append(new ConversationMessage(Role.User, "q"));
        PhySignal toolUse = Blocks(string.Empty, new ToolCallContent("id1", "search", "{}"));

        RecordResult result = Record(start, new RecordEvent(RecordedTurnKind.Tool, toolUse));

        Assert.Equal(RecordOutcome.AssistantTurn, result.Outcome);
        Assert.Equal(Role.Assistant, result.Conversation.Messages[^1].Role);
        Assert.Contains(result.Conversation.Messages[^1].Content, b => b is ToolCallContent);
    }

    [Fact]
    public void ToolSignal_WithToolUseAndResult_RecordsAssistantThenUser()
    {
        Conversation start = Conversation.Empty.Append(new ConversationMessage(Role.User, "q"));
        PhySignal mixed = Blocks(
            "tool ran",
            new ToolCallContent("id1", "search", "{}"),
            new ToolResultContent("id1", "result"));

        RecordResult result = Record(start, new RecordEvent(RecordedTurnKind.Tool, mixed));

        // Assistant request recorded first, then the tool_result user turn — never dropped.
        Assert.Equal(3, result.Conversation.Count);
        Assert.Equal(Role.Assistant, result.Conversation.Messages[1].Role);
        Assert.Equal(Role.User, result.Conversation.Messages[2].Role);
        Assert.Equal(RecordOutcome.UserTurn, result.Outcome);
    }

    [Fact]
    public void BlankPromptWithNoBlocks_RecordsNothing_WithWarning()
    {
        RecordResult result = Record(Conversation.Empty, new RecordEvent(RecordedTurnKind.Prompt, Text("   ")));

        Assert.Equal(RecordOutcome.Nothing, result.Outcome);
        Assert.Equal(0, result.Conversation.Count);
        Assert.NotEmpty(result.Warnings);
    }

    [Fact]
    public void ImagesOnlyPrompt_BlankPayload_RecordsUserTurn()
    {
        PhySignal imageOnly = Blocks(string.Empty, new ImageContent(new UrlImage("https://example.com/a.png")));

        RecordResult result = Record(Conversation.Empty, new RecordEvent(RecordedTurnKind.Prompt, imageOnly));

        Assert.Equal(RecordOutcome.UserTurn, result.Outcome);
        Assert.Equal(1, result.Conversation.Count);
        Assert.Contains(result.Conversation.Messages[0].Content, b => b is ImageContent);
    }

    [Fact]
    public void FeedbackWithImageBlock_AndUnrepresentedPayloadText_KeepsTheText()
    {
        // Last-line guard: an aggregate that carried a text report in its payload but only an image
        // in its blocks must not lose the report. SignalAggregation prevents that shape at the
        // source; this proves the Conversation Log no longer drops it if one ever reaches it.
        Conversation start = Conversation.Empty.Append(new ConversationMessage(Role.User, "q"))
            .Append(new ConversationMessage(Role.Assistant, "a"));
        PhySignal aggregate = Blocks("GEOMETRY REPORT", new ImageContent(new UrlImage("https://example.com/a.png")));

        RecordResult result = Record(start, new RecordEvent(RecordedTurnKind.Feedback, aggregate));

        IReadOnlyList<MessageContent> recorded = result.Conversation.Messages[^1].Content;
        Assert.Collection(
            recorded,
            b => Assert.Equal("GEOMETRY REPORT", Assert.IsType<TextContent>(b).Text),
            b => Assert.IsType<ImageContent>(b));
    }

    [Fact]
    public void FeedbackWithTextAndImageBlocks_IsNotDuplicated()
    {
        // The well-formed shape (a Geometry Observation with a Message): the payload is the trace of
        // the text block already present, so nothing is prepended.
        Conversation start = Conversation.Empty.Append(new ConversationMessage(Role.User, "q"))
            .Append(new ConversationMessage(Role.Assistant, "a"));
        PhySignal wellFormed = Blocks(
            "look at this",
            new TextContent("look at this"),
            new ImageContent(new UrlImage("https://example.com/a.png")));

        RecordResult result = Record(start, new RecordEvent(RecordedTurnKind.Feedback, wellFormed));

        Assert.Equal(2, result.Conversation.Messages[^1].Content.Count);
    }

    [Fact]
    public void ToolResultTurn_IsNeverPrefixedWithPayloadText()
    {
        // A tool_result block must lead its user message, so the payload-text guard must not apply
        // to tool turns even though their payload is non-blank.
        Conversation start = Conversation.Empty.Append(new ConversationMessage(Role.User, "q"))
            .Append(new ConversationMessage(Role.Assistant, "a"));
        PhySignal toolResult = Blocks("tool ran", new ToolResultContent("id1", "result"));

        RecordResult result = Record(start, new RecordEvent(RecordedTurnKind.Tool, toolResult));

        Assert.IsType<ToolResultContent>(result.Conversation.Messages[^1].Content[0]);
    }

    [Fact]
    public void EmptyBatch_RecordsNothing()
    {
        RecordResult result = Record(Conversation.Empty);

        Assert.Equal(RecordOutcome.Nothing, result.Outcome);
        Assert.Equal(0, result.Conversation.Count);
    }
}
