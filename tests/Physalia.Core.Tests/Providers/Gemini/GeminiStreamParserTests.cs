// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Text;
using Physalia.Core.Common;
using Physalia.Core.Providers.Gemini;
using Xunit;

namespace Physalia.Core.Tests.Providers.Gemini;

public class GeminiStreamParserTests
{
    private sealed class TestableGeminiProvider : GeminiProtocolProvider
    {
        public IAsyncEnumerable<Result<LlmResponseChunk, LlmError>> Parse(Stream stream, CancellationToken ct)
            => ParseSseStreamAsync(stream, ct);
    }

    private static string Sse(params string[] dataLines)
    {
        var sb = new StringBuilder();
        foreach (string data in dataLines)
        {
            sb.Append("data: ").Append(data).Append('\n');
        }

        return sb.ToString();
    }

    private static async Task<List<Result<LlmResponseChunk, LlmError>>> Parse(string sse)
    {
        var provider = new TestableGeminiProvider();
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(sse));
        var results = new List<Result<LlmResponseChunk, LlmError>>();
        await foreach (var chunk in provider.Parse(stream, CancellationToken.None))
        {
            results.Add(chunk);
        }

        return results;
    }

    private static LlmResponseChunk Ok(Result<LlmResponseChunk, LlmError> result)
    {
        Assert.True(result.IsOk(out LlmResponseChunk? chunk, out _));
        return chunk!;
    }

    [Fact]
    public async Task Parse_TextParts_YieldsContentThenFinalUsage()
    {
        string sse = Sse(
            """{"candidates":[{"content":{"parts":[{"text":"Hello"}]}}]}""",
            """{"candidates":[{"content":{"parts":[{"text":" world"}]}}]}""",
            """{"candidates":[{"content":{"parts":[{"text":"!"}]},"finishReason":"STOP"}],"usageMetadata":{"promptTokenCount":12,"candidatesTokenCount":4}}""");

        List<Result<LlmResponseChunk, LlmError>> chunks = await Parse(sse);

        Assert.Equal(3, chunks.Count);
        Assert.Equal("Hello", Ok(chunks[0]).ContentDelta);
        Assert.False(Ok(chunks[0]).IsLast);
        Assert.Equal(" world", Ok(chunks[1]).ContentDelta);

        LlmResponseChunk final = Ok(chunks[2]);
        Assert.True(final.IsLast);
        Assert.Equal("!", final.ContentDelta);
        Assert.Equal(12, final.Usage!.InputTokens);
        Assert.Equal(4, final.Usage.OutputTokens);
    }

    [Fact]
    public async Task Parse_ConcatenatesMultipleTextPartsInOneChunk()
    {
        string sse = Sse(
            """{"candidates":[{"content":{"parts":[{"text":"foo"},{"text":"bar"}]}}]}""");

        List<Result<LlmResponseChunk, LlmError>> chunks = await Parse(sse);

        Assert.Equal("foobar", Ok(Assert.Single(chunks)).ContentDelta);
    }

    [Fact]
    public async Task Parse_MalformedChunk_YieldsDomainErrorNotException()
    {
        string sse = Sse("{not valid json");

        List<Result<LlmResponseChunk, LlmError>> chunks = await Parse(sse);

        Assert.True(Assert.Single(chunks).IsErr(out LlmError? error, out _));
        Assert.Equal(LlmErrorKind.InvalidRequest, error!.Kind);
    }
}
