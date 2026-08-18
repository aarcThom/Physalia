// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Physalia.Core.ConvoInstruct;
using Physalia.Core.Tools;
using Xunit;

namespace Physalia.Core.Tests.Tools;

public class ToolBatchRunnerTests
{
    private static ToolCallContent Call(string id, string name = "tool") => new(id, name, "{}");

    [Fact]
    public void Run_ProducesOneResultBlockPerCall_EchoingIds()
    {
        ToolBatchResult batch = ToolBatchRunner.Run(
            new[] { Call("1"), Call("2") },
            call => new ToolCallOutcome($"out-{call.Id}", false));

        Assert.Equal(2, batch.Blocks.Count);
        var ids = batch.Blocks.Cast<ToolResultContent>().Select(b => b.ToolCallId).ToArray();
        Assert.Equal(new[] { "1", "2" }, ids);
        Assert.Equal(string.Join(Environment.NewLine, "out-1", "out-2"), batch.Payload);
    }

    [Fact]
    public void Run_Attachments_LandAfterEveryResult()
    {
        // Anthropic requires the tool_result blocks to lead the answering user turn, so an attachment
        // from the FIRST call must still sort behind the LAST result.
        var image = new ImageContent(new InlineImage(new byte[] { 9 }, "image/png"));

        ToolBatchResult batch = ToolBatchRunner.Run(
            new[] { Call("1"), Call("2") },
            call => call.Id == "1"
                ? new ToolCallOutcome("out-1", false, new MessageContent[] { image })
                : new ToolCallOutcome("out-2", false));

        Assert.Equal(3, batch.Blocks.Count);
        Assert.IsType<ToolResultContent>(batch.Blocks[0]);
        Assert.IsType<ToolResultContent>(batch.Blocks[1]);
        Assert.Same(image, batch.Blocks[2]);

        // The payload stays the result text only — an image has no text form.
        Assert.Equal(string.Join(Environment.NewLine, "out-1", "out-2"), batch.Payload);
    }

    [Fact]
    public async Task RunAsync_Attachments_LandAfterEveryResult()
    {
        var image = new ImageContent(new InlineImage(new byte[] { 9 }, "image/png"));

        ToolBatchResult? batch = await ToolBatchRunner.RunAsync(
            new[] { Call("1"), Call("2") },
            (call, _) => Task.FromResult(call.Id == "1"
                ? new ToolCallOutcome("out-1", false, new MessageContent[] { image })
                : new ToolCallOutcome("out-2", false)),
            CancellationToken.None);

        Assert.NotNull(batch);
        Assert.Equal(3, batch!.Blocks.Count);
        Assert.IsType<ToolResultContent>(batch.Blocks[0]);
        Assert.IsType<ToolResultContent>(batch.Blocks[1]);
        Assert.Same(image, batch.Blocks[2]);
    }

    [Fact]
    public void Run_NoAttachments_ProducesResultsOnly()
    {
        // Every ordinary tool: the turn must be byte-for-byte what it was before attachments existed.
        ToolBatchResult batch = ToolBatchRunner.Run(
            new[] { Call("1") },
            _ => new ToolCallOutcome("out", false));

        Assert.Single(batch.Blocks);
        Assert.IsType<ToolResultContent>(batch.Blocks[0]);
    }

    [Fact]
    public void Run_EmptyCalls_ProducesEmptyResult()
    {
        ToolBatchResult batch = ToolBatchRunner.Run(Array.Empty<ToolCallContent>(), _ => new ToolCallOutcome("x", false));

        Assert.Empty(batch.Blocks);
        Assert.Equal(string.Empty, batch.Payload);
    }

    [Fact]
    public async Task RunAsync_AllSucceed_ProducesOneBlockPerCall()
    {
        ToolBatchResult? batch = await ToolBatchRunner.RunAsync(
            new[] { Call("1"), Call("2") },
            (call, _) => Task.FromResult(new ToolCallOutcome($"out-{call.Id}", false)),
            CancellationToken.None);

        Assert.NotNull(batch);
        Assert.Equal(2, batch!.Blocks.Count);
    }

    [Fact]
    public async Task RunAsync_PerCallException_BecomesErrorResult_OthersStillComplete()
    {
        ToolBatchResult? batch = await ToolBatchRunner.RunAsync(
            new[] { Call("1"), Call("2") },
            (call, _) => call.Id == "1"
                ? throw new InvalidOperationException("boom")
                : Task.FromResult(new ToolCallOutcome("ok", false)),
            CancellationToken.None);

        Assert.NotNull(batch);
        Assert.Equal(2, batch!.Blocks.Count);

        var blocks = batch.Blocks.Cast<ToolResultContent>().ToArray();
        ToolResultContent failed = Assert.Single(blocks, b => b.ToolCallId == "1");
        Assert.True(failed.IsError);
        Assert.Equal("boom", failed.Content);
        ToolResultContent ok = Assert.Single(blocks, b => b.ToolCallId == "2");
        Assert.False(ok.IsError);
    }

    [Fact]
    public async Task RunAsync_Cancelled_ReturnsNull()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        ToolBatchResult? batch = await ToolBatchRunner.RunAsync(
            new[] { Call("1") },
            (call, _) => Task.FromResult(new ToolCallOutcome("ok", false)),
            cts.Token);

        Assert.Null(batch);
    }
}
