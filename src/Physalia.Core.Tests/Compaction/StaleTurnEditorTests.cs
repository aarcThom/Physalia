// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using Physalia.Core.Compaction;
using Xunit;

namespace Physalia.Core.Tests.Compaction;

public class StaleTurnEditorTests
{
    private static string Document(string kind, int pad) =>
        "{\n  \"schema\": \"1.0\",\n  \"kind\": \"" + kind + "\",\n  \"pad\": \""
        + new string('x', pad) + "\"\n}";

    [Fact]
    public void StubsTheDocument_AndKeepsThePreamble()
    {
        string turn = "now: 3\n" + Document("ghpatch", 500);

        string result = StaleTurnEditor.StubTrailingDocument(turn);

        Assert.StartsWith("now: 3", result, StringComparison.Ordinal);
        Assert.DoesNotContain("\"schema\"", result, StringComparison.Ordinal);
        Assert.Contains("elided from this transcript", result, StringComparison.Ordinal);
        Assert.Contains("ghpatch", result, StringComparison.Ordinal);
    }

    [Fact]
    public void StubRecordsTheOriginalSize()
    {
        string doc = Document("ghpatch", 500);
        string result = StaleTurnEditor.StubTrailingDocument("prose\n" + doc);

        Assert.Contains(doc.Length.ToString("N0", System.Globalization.CultureInfo.InvariantCulture), result, StringComparison.Ordinal);
    }

    [Fact]
    public void NamesTheDocumentKindFromItsOwnField()
    {
        string result = StaleTurnEditor.StubTrailingDocument("p\n" + Document("ghpatch", 400));
        Assert.Contains("ghpatch document", result, StringComparison.Ordinal);
    }

    [Fact]
    public void FallsBackToGhJsonWhenNoKindIsDeclared()
    {
        string doc = "{\n  \"schema\": \"1.0\",\n  \"components\": \"" + new string('x', 400) + "\"\n}";
        string result = StaleTurnEditor.StubTrailingDocument("p\n" + doc);

        Assert.Contains("GhJSON document", result, StringComparison.Ordinal);
    }

    [Fact]
    public void LeavesProseOnlyTurnsAlone()
    {
        const string prose = "The pavilion is built as described; both surfaces meet along the ridge.";

        Assert.Equal(prose, StaleTurnEditor.StubTrailingDocument(prose));
    }

    [Fact]
    public void LeavesShortDocumentsAlone()
    {
        // Below the threshold the stub is as long as what it would replace.
        const string turn = "prose\n{\"a\":1}";

        Assert.Equal(turn, StaleTurnEditor.StubTrailingDocument(turn));
    }

    [Fact]
    public void IgnoresBracesInsideASentence()
    {
        string turn = "I considered using {a} here but chose otherwise. " + new string('y', 400);

        Assert.Equal(turn, StaleTurnEditor.StubTrailingDocument(turn));
    }

    [Fact]
    public void ProducesAStubEvenWithNoPrecedingProse()
    {
        string result = StaleTurnEditor.StubTrailingDocument(Document("ghpatch", 500));

        Assert.StartsWith("[a ", result, StringComparison.Ordinal);
        Assert.DoesNotContain("\"schema\"", result, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void HandlesEmptyInput(string? input)
    {
        Assert.Equal(input ?? string.Empty, StaleTurnEditor.StubTrailingDocument(input));
    }

    [Fact]
    public void StubIsDramaticallySmallerThanTheDocument()
    {
        string turn = "now: 4\n" + Document("ghpatch", 12000);
        string result = StaleTurnEditor.StubTrailingDocument(turn);

        Assert.True(result.Length < turn.Length / 20, $"stub was {result.Length} chars against {turn.Length}");
    }
}
