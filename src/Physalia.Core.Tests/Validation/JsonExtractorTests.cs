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

    [Fact]
    public void LooksTruncated_DocumentCutMidObject_True()
    {
        // The shape of a max_tokens-truncated response: complete inner objects, then the cut.
        string raw = "<think>reasoning</think>\n\n{\n  \"schema\": \"1.0\",\n  \"components\": [\n"
            + "    { \"name\": \"Number Slider\", \"id\": 4 },\n    { \"name\": \"Division\", \"id\": 14, \"";

        Assert.True(JsonExtractor.LooksTruncated(raw));
    }

    [Fact]
    public void LooksTruncated_DocumentCutInsideStringLiteral_True()
    {
        string raw = "{ \"components\": [ { \"name\": \"Pi";

        Assert.True(JsonExtractor.LooksTruncated(raw));
    }

    [Fact]
    public void LooksTruncated_CompleteDocument_False()
    {
        Assert.False(JsonExtractor.LooksTruncated("prose {\"a\":1,\"b\":[2,3]} more prose"));
    }

    [Fact]
    public void LooksTruncated_StrayBraceInProse_False()
    {
        // An unclosed brace with no string literal inside is prose, not a truncated document.
        Assert.False(JsonExtractor.LooksTruncated("set the {width and height to taste"));
    }

    [Fact]
    public void LooksTruncated_BlankInput_False()
    {
        Assert.False(JsonExtractor.LooksTruncated("   "));
    }

    // ---- Malformed documents (one dropped closer), as opposed to truncated ones ----------------

    // The shape that cost two identical retries in the 2026-07-27 session: a plan block, then a
    // complete-looking document one closing brace short. The scan hits a mismatch rather than
    // running off the end, so the truncation guard never saw it, and the extractor walked INTO the
    // document and returned the "components" array — which the validator reported as "the root is
    // an array", a defect the model had not made and could not act on.
    private const string MissingCloser =
        "<plan>\ngoal: a tower\n1. Mass\nnow: 1\n</plan>\n"
        + "{\"schema\":\"1.0\",\"components\":["
        + "{\"name\":\"Number Slider\",\"id\":1,\"pivot\":\"0,0\","
        + "\"componentState\":{\"extensions\":{\"gh.numberslider\":{\"value\":\"5<0~10>\"}}}"  // one '}' short
        + "],\"connections\":[]}";

    [Fact]
    public void LooksMalformed_DocumentMissingOneCloser_True()
    {
        Assert.True(JsonExtractor.LooksMalformed(MissingCloser));
    }

    [Fact]
    public void LooksTruncated_DocumentMissingOneCloser_False()
    {
        // It reached its end; it is wrong, not cut off. The two need different feedback.
        Assert.False(JsonExtractor.LooksTruncated(MissingCloser));
    }

    [Fact]
    public void ExtractJson_DocumentMissingOneCloser_DoesNotReturnAnInnerArray()
    {
        string extracted = JsonExtractor.ExtractJson(MissingCloser);

        Assert.False(
            extracted.TrimStart().StartsWith("["),
            "a nested array must never be recovered as the document — that is what produced "
            + "\"Value is array but should be object\" for a document whose root is an object");
        Assert.StartsWith("{", extracted.TrimStart());
    }

    [Fact]
    public void LooksMalformed_CompleteDocument_False()
    {
        Assert.False(JsonExtractor.LooksMalformed("prose {\"a\":1,\"b\":[2,3]} more prose"));
    }

    [Fact]
    public void LooksMalformed_TruncatedDocument_False()
    {
        Assert.False(JsonExtractor.LooksMalformed("{ \"components\": [ { \"name\": \"Pi"));
    }

    [Fact]
    public void LooksMalformed_MismatchedBracketsInProse_False()
    {
        // Not document-shaped, so it is prose with a typo, not a broken document.
        Assert.False(JsonExtractor.LooksMalformed("pick a value [between 1 and 10} inclusive"));
    }

    // A broken attempt that DOES close, followed by a good one: the revision must still win.
    [Fact]
    public void ExtractJson_BrokenAttemptThenValidDocument_TakesTheValidOne()
    {
        string raw = "{\"schema\":\"1.0\",\"components\":[{\"a\":1]}\n"
            + "on reflection:\n"
            + "{\"schema\":\"1.0\",\"components\":[{\"name\":\"Circle\",\"id\":1,\"pivot\":\"0,0\"}]}";

        Assert.Contains("Circle", JsonExtractor.ExtractJson(raw));
    }
}
