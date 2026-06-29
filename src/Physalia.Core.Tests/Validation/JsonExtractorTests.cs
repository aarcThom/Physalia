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
