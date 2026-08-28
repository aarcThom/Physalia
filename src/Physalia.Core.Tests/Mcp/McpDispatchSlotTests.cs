// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Physalia.Core.ConvoInstruct;
using Physalia.Core.Mcp;
using Physalia.Core.Tools;
using Xunit;

namespace Physalia.Core.Tests.Mcp;

/// <summary>
/// Dispatch when one Router output answers for MANY tool names — the MCP Server case. The
/// single-name behaviour every other tool node relies on is covered by ToolDispatchRoundTests.
/// </summary>
public class McpDispatchSlotTests
{
    private static ToolCallContent Call(string id, string name, string json = "{}") => new(id, name, json);

    [Fact]
    public void Plan_CallMatchesAnyNameInASlot_DispatchesToThatOutput()
    {
        ToolDispatchPlan plan = ToolDispatchRound.Plan(
            new[] { Call("1", "read_file") },
            new[] { new ToolOutputSlot("MCP", new[] { "read_file", "write_file", "list_dir" }) });

        ToolDispatchGroup group = Assert.Single(plan.Groups);
        Assert.Equal("MCP", group.OutputName);
        Assert.Equal("read_file", Assert.Single(group.Calls).Name);
    }

    [Fact]
    public void Plan_DifferentToolsOnOneSlot_RideAsOneDispatch()
    {
        // One output emits one latched signal, so everything routed to it must travel together;
        // the node itself fans the calls out by name.
        ToolDispatchPlan plan = ToolDispatchRound.Plan(
            new[] { Call("1", "read_file"), Call("2", "write_file") },
            new[] { new ToolOutputSlot("MCP", new[] { "read_file", "write_file" }) });

        ToolDispatchGroup group = Assert.Single(plan.Groups);
        Assert.Equal(new[] { "1", "2" }, group.Calls.Select(c => c.Id));
        Assert.Equal(new[] { "1", "2" }, plan.PendingToolUseIds);
    }

    [Fact]
    public void Plan_TwoServers_EachCallGoesToItsOwnOutput()
    {
        ToolDispatchPlan plan = ToolDispatchRound.Plan(
            new[] { Call("1", "notion__search"), Call("2", "files__read_file") },
            new[]
            {
                new ToolOutputSlot("notion", new[] { "notion__search", "notion__fetch" }),
                new ToolOutputSlot("files", new[] { "files__read_file" }),
            });

        Assert.Equal(2, plan.Groups.Count);
        Assert.Equal("notion", plan.Groups[0].OutputName);
        Assert.Equal("files", plan.Groups[1].OutputName);
        Assert.Empty(plan.SyntheticErrorResults);
    }

    [Fact]
    public void Plan_UnmatchedCall_NamesTheTOOLNamesNotTheOutputNames()
    {
        // Telling the model to call "MCP" would send it after something that does not exist.
        ToolDispatchPlan plan = ToolDispatchRound.Plan(
            new[] { Call("1", "invented_tool") },
            new[] { new ToolOutputSlot("MCP", new[] { "read_file", "write_file" }) });

        ToolResultContent error = Assert.Single(plan.SyntheticErrorResults);
        Assert.True(error.IsError);
        Assert.Contains("read_file", error.Content);
        Assert.Contains("write_file", error.Content);
        Assert.DoesNotContain("The available tools are: MCP", error.Content);
        Assert.Empty(plan.Groups);
    }

    [Fact]
    public void Plan_StringOverload_StillBehavesAsOneNamePerOutput()
    {
        // The pre-MCP call shape must keep working unchanged — it is what every other tool node uses.
        ToolDispatchPlan plan = ToolDispatchRound.Plan(
            new[] { Call("1", "search") },
            new[] { "search", "read_url" });

        Assert.Equal("search", Assert.Single(plan.Groups).OutputName);
    }

    [Theory]
    [InlineData("nonexistent-command-xyz-123")]
    [InlineData("")]
    [InlineData(null)]
    public void ResolveExecutable_NotFound_ReturnsNull(string? command)
    {
        Assert.Null(McpExecutable.Resolve(command));
    }

    [Fact]
    public void ResolveExecutable_ExplicitPath_IsReturnedUnchanged()
    {
        // The user named a specific file; second-guessing it would be wrong.
        const string Path = @"C:\tools\my-server.exe";
        Assert.Equal(Path, McpExecutable.Resolve(Path));
    }
}
