// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Physalia.Core.ConvoInstruct;
using Physalia.Core.Signals;
using Xunit;

namespace Physalia.Core.Tests.Signals;

public class SignalAggregationTests
{
    private const string Sep = "\n\n";

    private static PhySignal Text(string payload) =>
        PhySignal.Mint(SignalOutcome.Success, payload, Guid.NewGuid(), "test");

    private static PhySignal Blocks(string payload, params MessageContent[] blocks) =>
        PhySignal.Mint(SignalOutcome.Success, payload, Guid.NewGuid(), "test", contentBlocks: blocks);

    private static ImageContent Image() => new(new UrlImage("https://example.com/a.png"));

    [Fact]
    public void AllTextParts_StayTextOnly_NoBlocksInvented()
    {
        AggregatedContent result = SignalAggregation.Combine(new[] { Text("one"), Text("two") }, Sep);

        Assert.Equal("one" + Sep + "two", result.Payload);
        Assert.Empty(result.ContentBlocks);
    }

    [Fact]
    public void BlankPayloads_AreSkippedInTheJoin()
    {
        AggregatedContent result = SignalAggregation.Combine(new[] { Text("one"), Text("   "), Text("two") }, Sep);

        Assert.Equal("one" + Sep + "two", result.Payload);
    }

    [Fact]
    public void TextPartMergedWithImagePart_ContributesItsTextAsABlock()
    {
        // The Geometry Report + Geometry Observation case: the report carries text only, the
        // observation an image with a blank payload. Concatenating blocks alone would leave the
        // report in the payload and in no block, and the Conversation Log would drop it.
        AggregatedContent result = SignalAggregation.Combine(
            new[] { Text("REPORT"), Blocks(string.Empty, Image()) },
            Sep);

        Assert.Equal("REPORT", result.Payload);
        Assert.Collection(
            result.ContentBlocks,
            b => Assert.Equal("REPORT", Assert.IsType<TextContent>(b).Text),
            b => Assert.IsType<ImageContent>(b));
    }

    [Fact]
    public void PartsAreCombinedInTheOrderGiven()
    {
        AggregatedContent result = SignalAggregation.Combine(
            new[] { Blocks(string.Empty, Image()), Text("REPORT") },
            Sep);

        // Caller-decided order (Merge Signal sorts by sequence) is honoured, not reshuffled.
        Assert.Collection(
            result.ContentBlocks,
            b => Assert.IsType<ImageContent>(b),
            b => Assert.Equal("REPORT", Assert.IsType<TextContent>(b).Text));
    }

    [Fact]
    public void BlockCarryingPartsAreTakenVerbatim_ToolResultIdSurvives()
    {
        AggregatedContent result = SignalAggregation.Combine(
            new[] { Blocks("ran", new ToolResultContent("id1", "result")), Blocks(string.Empty, Image()) },
            Sep);

        // A part with blocks of its own contributes exactly those — its payload is only their trace,
        // so no duplicate text block is added.
        Assert.Collection(
            result.ContentBlocks,
            b => Assert.Equal("id1", Assert.IsType<ToolResultContent>(b).ToolCallId),
            b => Assert.IsType<ImageContent>(b));
    }

    [Fact]
    public void BlankTextPart_ContributesNothing()
    {
        AggregatedContent result = SignalAggregation.Combine(
            new[] { Text("   "), Blocks(string.Empty, Image()) },
            Sep);

        Assert.Equal(string.Empty, result.Payload);
        Assert.Single(result.ContentBlocks);
        Assert.IsType<ImageContent>(result.ContentBlocks[0]);
    }

    [Fact]
    public void EmptySet_YieldsEmptyContent()
    {
        AggregatedContent result = SignalAggregation.Combine(Array.Empty<PhySignal>(), Sep);

        Assert.Equal(string.Empty, result.Payload);
        Assert.Empty(result.ContentBlocks);
    }
}
