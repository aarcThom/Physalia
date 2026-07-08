// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Physalia.Core.Validation;
using Xunit;

namespace Physalia.Core.Tests.Validation;

public class JsonExtractorTests
{
    [Fact]
    public void ExtractJson_StripsJsonFence()
    {
        string raw = "Here you go:\n```json\n{\"a\":1}\n```\nThanks.";

        Assert.Equal("{\"a\":1}", JsonExtractor.ExtractJson(raw));
    }

    [Fact]
    public void ExtractJson_StripsGenericFence()
    {
        string raw = "```\n{\"a\":1}\n```";

        Assert.Equal("{\"a\":1}", JsonExtractor.ExtractJson(raw));
    }

    [Fact]
    public void ExtractJson_ExtractsBareObjectFromProse()
    {
        string raw = "Sure, here is the result {\"a\":1,\"b\":2} and that's it.";

        Assert.Equal("{\"a\":1,\"b\":2}", JsonExtractor.ExtractJson(raw));
    }

    [Fact]
    public void ExtractJson_ExtractsBareArray()
    {
        string raw = "prefix [1, 2, 3] suffix";

        Assert.Equal("[1, 2, 3]", JsonExtractor.ExtractJson(raw));
    }

    [Fact]
    public void ExtractJson_NoJson_ReturnsTrimmedInput()
    {
        Assert.Equal("just prose", JsonExtractor.ExtractJson("  just prose  "));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ExtractJson_BlankInput_ReturnedUnchanged(string raw)
    {
        Assert.Equal(raw, JsonExtractor.ExtractJson(raw));
    }

    [Fact]
    public void ExtractJson_MultipleJsonFences_ReturnsLastBlock()
    {
        string raw = "First try:\n```json\n{\"attempt\":1}\n```\nThat was wrong, corrected:\n```json\n{\"attempt\":2}\n```\nDone.";

        Assert.Equal("{\"attempt\":2}", JsonExtractor.ExtractJson(raw));
    }

    [Fact]
    public void ExtractJson_LastBlockTruncated_FallsBackToEarlierParseableBlock()
    {
        string raw = "```json\n{\"attempt\":1}\n```\nRevising:\n```json\n{\"attempt\":2,\"components\":[\n```";

        Assert.Equal("{\"attempt\":1}", JsonExtractor.ExtractJson(raw));
    }

    [Fact]
    public void ExtractJson_NoBlockParses_ReturnsLastBlock()
    {
        string raw = "```json\nnot json at all\n```\n```json\nstill { not json\n```";

        Assert.Equal("still { not json", JsonExtractor.ExtractJson(raw));
    }

    [Fact]
    public void ExtractJson_MultipleGenericFences_ReturnsLastBlock()
    {
        string raw = "```\n{\"a\":1}\n```\nprose\n```\n{\"a\":2}\n```";

        Assert.Equal("{\"a\":2}", JsonExtractor.ExtractJson(raw));
    }

    [Fact]
    public void ExtractJson_JsonFencePreferredOverLaterGenericFence()
    {
        string raw = "```json\n{\"tagged\":true}\n```\nprose\n```\n{\"generic\":true}\n```";

        Assert.Equal("{\"tagged\":true}", JsonExtractor.ExtractJson(raw));
    }

    [Fact]
    public void ExtractJson_MultipleBareObjects_ReturnsLastParseable()
    {
        string raw = "First idea: {\"attempt\":1} — no, better: {\"attempt\":2} done.";

        Assert.Equal("{\"attempt\":2}", JsonExtractor.ExtractJson(raw));
    }

    [Fact]
    public void ExtractJson_StrayBracesInProse_SkippedForParseableObject()
    {
        string raw = "Use the {width} placeholder, then emit {\"a\":1}.";

        Assert.Equal("{\"a\":1}", JsonExtractor.ExtractJson(raw));
    }

    [Fact]
    public void ExtractJson_UnclosedOpenerInProse_StillFindsRealObject()
    {
        string raw = "A stray { opener in prose, then the real thing: {\"a\":1} end.";

        Assert.Equal("{\"a\":1}", JsonExtractor.ExtractJson(raw));
    }

    [Fact]
    public void ExtractJson_BracesInsideStringLiterals_DoNotSplitTheObject()
    {
        string raw = "result: {\"text\":\"a } b { c\",\"n\":1} trailing prose";

        Assert.Equal("{\"text\":\"a } b { c\",\"n\":1}", JsonExtractor.ExtractJson(raw));
    }

    [Fact]
    public void ExtractJson_NoCandidateParses_FallsBackToOutermostSpan()
    {
        string raw = "prefix {\"a\":1 suffix} tail";

        Assert.Equal("{\"a\":1 suffix}", JsonExtractor.ExtractJson(raw));
    }

    [Fact]
    public void PrettyPrint_IndentsValidJson()
    {
        string result = JsonExtractor.PrettyPrint("{\"a\":1}");

        Assert.Contains("\n", result);
        Assert.Contains("\"a\": 1", result);
    }

    [Fact]
    public void PrettyPrint_InvalidJson_ReturnedUnchanged()
    {
        Assert.Equal("not json", JsonExtractor.PrettyPrint("not json"));
    }
}
