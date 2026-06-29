// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Physalia.Core.ConvoInstruct;
using Physalia.Core.Tools;
using Xunit;

namespace Physalia.Core.Tests.Tools;

public class ToolDispatchRoundTests
{
    private static ToolCallContent Call(string id, string name, string json = "{}") =>
        new(id, name, json);

    [Fact]
    public void Plan_MultipleCallsToSameTool_GroupedIntoOneDispatch()
    {
        ToolDispatchPlan plan = ToolDispatchRound.Plan(
            new[] { Call("1", "search", "{\"a\":1}"), Call("2", "search", "{\"b\":2}") },
            new[] { "search" });

        ToolDispatchGroup group = Assert.Single(plan.Groups);
        Assert.Equal("search", group.OutputName);
        Assert.Equal(2, group.Calls.Count);
        Assert.Equal(string.Join(Environment.NewLine, "{\"a\":1}", "{\"b\":2}"), group.Payload);
        Assert.Equal(new[] { "1", "2" }, plan.PendingToolUseIds);
        Assert.Empty(plan.SyntheticErrorResults);
        Assert.Empty(plan.Warnings);
    }

    [Fact]
    public void Plan_UnknownTool_ProducesSyntheticErrorAndWarning()
    {
        ToolDispatchPlan plan = ToolDispatchRound.Plan(
            new[] { Call("1", "missing") },
            new[] { "search" });

        Assert.Empty(plan.Groups);
        Assert.Empty(plan.PendingToolUseIds);
        ToolResultContent error = Assert.Single(plan.SyntheticErrorResults);
        Assert.Equal("1", error.ToolCallId);
        Assert.True(error.IsError);
        Assert.Contains("missing", error.Content);
        Assert.NotEmpty(plan.Warnings);
    }

    [Fact]
    public void Plan_MixedMatchedAndUnknown_DispatchesMatchedAndErrorsUnknown()
    {
        ToolDispatchPlan plan = ToolDispatchRound.Plan(
            new[] { Call("1", "search"), Call("2", "ghost") },
            new[] { "search" });

        Assert.Single(plan.Groups);
        Assert.Equal(new[] { "1" }, plan.PendingToolUseIds);
        Assert.Equal("2", Assert.Single(plan.SyntheticErrorResults).ToolCallId);
    }

    [Fact]
    public void Plan_MatchesOutputNameCaseInsensitively_KeepsOutputCasing()
    {
        ToolDispatchPlan plan = ToolDispatchRound.Plan(
            new[] { Call("1", "Search") },
            new[] { "search" });

        Assert.Equal("search", Assert.Single(plan.Groups).OutputName);
    }

    [Fact]
    public void Plan_EmptyCalls_ProducesEmptyPlan()
    {
        ToolDispatchPlan plan = ToolDispatchRound.Plan(Array.Empty<ToolCallContent>(), new[] { "search" });

        Assert.Empty(plan.Groups);
        Assert.Empty(plan.PendingToolUseIds);
        Assert.Empty(plan.SyntheticErrorResults);
        Assert.Empty(plan.Warnings);
    }

    [Fact]
    public void CombineResults_JoinsNonBlankContent_AndKeepsAllBlocks()
    {
        var results = new[]
        {
            new ToolResultContent("1", "ok"),
            new ToolResultContent("2", "   "),
            new ToolResultContent("3", "done"),
        };

        (IReadOnlyList<MessageContent> blocks, string payload) = ToolDispatchRound.CombineResults(results);

        Assert.Equal(3, blocks.Count); // every result becomes a tool_result block
        Assert.Equal(string.Join(Environment.NewLine, "ok", "done"), payload); // blank content dropped from trace
    }
}
