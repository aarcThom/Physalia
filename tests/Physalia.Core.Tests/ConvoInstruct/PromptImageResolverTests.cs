// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Physalia.Core.ConvoInstruct;
using Xunit;

namespace Physalia.Core.Tests.ConvoInstruct;

public class PromptImageResolverTests
{
    private static IReadOnlyDictionary<string, ImageSource> Map(params (string Alias, ImageSource Source)[] entries)
    {
        var dict = new Dictionary<string, ImageSource>();
        foreach ((string alias, ImageSource source) in entries)
        {
            dict[alias] = source;
        }

        return dict;
    }

    [Fact]
    public void Resolve_InterleavesTextAndImageInReadingOrder()
    {
        var source = new InlineImage(new byte[] { 1, 2, 3 }, "image/png");
        ResolvedPrompt result = PromptImageResolver.Resolve("look at /diagram here", Map(("diagram", source)));

        Assert.Collection(
            result.Blocks,
            b => Assert.Equal("look at", Assert.IsType<TextContent>(b).Text),
            b => Assert.Same(source, Assert.IsType<ImageContent>(b).Source),
            b => Assert.Equal("here", Assert.IsType<TextContent>(b).Text));
        Assert.Equal("look at here", result.Text);
    }

    [Fact]
    public void Resolve_CarriesUrlAndManagedSourcesUnchanged()
    {
        var url = new UrlImage("https://example.com/a.png");
        var managed = new ManagedImage("file-123");
        ResolvedPrompt result = PromptImageResolver.Resolve("/u then /m", Map(("u", url), ("m", managed)));

        Assert.Collection(
            result.Blocks,
            b => Assert.Same(url, Assert.IsType<ImageContent>(b).Source),
            b => Assert.Equal("then", Assert.IsType<TextContent>(b).Text),
            b => Assert.Same(managed, Assert.IsType<ImageContent>(b).Source));
        Assert.Equal("then", result.Text);
    }

    [Fact]
    public void Resolve_UnknownAlias_StaysLiteral()
    {
        ResolvedPrompt result = PromptImageResolver.Resolve("see /unknown ok", Map());

        Assert.Single(result.Blocks);
        Assert.Equal("see /unknown ok", Assert.IsType<TextContent>(result.Blocks[0]).Text);
        Assert.Equal("see /unknown ok", result.Text);
    }

    [Theory]
    [InlineData("and/or maybe")]   // slash not at a word boundary
    [InlineData("http://or.com")]  // slash preceded by ':'
    public void Resolve_SlashNotAtWordBoundary_DoesNotMatch(string prompt)
    {
        var source = new UrlImage("https://example.com/a.png");
        ResolvedPrompt result = PromptImageResolver.Resolve(prompt, Map(("or", source)));

        Assert.Empty(result.Blocks.OfType<ImageContent>());
    }

    [Fact]
    public void Resolve_LongerAliasWinsOverPrefix()
    {
        var photo = new UrlImage("https://example.com/photo.png");
        var photographer = new UrlImage("https://example.com/photographer.png");
        ResolvedPrompt result = PromptImageResolver.Resolve(
            "/photographer", Map(("photo", photo), ("photographer", photographer)));

        ImageContent only = Assert.Single(result.Blocks.OfType<ImageContent>());
        Assert.Same(photographer, only.Source);
        Assert.Equal(string.Empty, result.Text);
    }

    [Fact]
    public void Resolve_AliasThatIsPrefixOfLongerWord_DoesNotMatch()
    {
        var photo = new UrlImage("https://example.com/photo.png");
        ResolvedPrompt result = PromptImageResolver.Resolve("/photographer", Map(("photo", photo)));

        Assert.Empty(result.Blocks.OfType<ImageContent>());
        Assert.Equal("/photographer", result.Text);
    }

    [Fact]
    public void Resolve_MatchesAliasCaseInsensitively()
    {
        var source = new UrlImage("https://example.com/a.png");
        ResolvedPrompt result = PromptImageResolver.Resolve("/Diagram", Map(("diagram", source)));

        Assert.Single(result.Blocks.OfType<ImageContent>());
    }

    [Fact]
    public void Resolve_NullPrompt_ReturnsEmpty()
    {
        ResolvedPrompt result = PromptImageResolver.Resolve(null!, Map());

        Assert.Empty(result.Blocks);
        Assert.Equal(string.Empty, result.Text);
    }
}
