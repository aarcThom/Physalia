// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Text;
using Physalia.Core.Common;
using Physalia.Core.Providers.Anthropic;
using Xunit;

namespace Physalia.Core.Tests.Providers.Anthropic;

public class AnthropicStreamParserTests
{
    // AnthropicProtocolProvider implements every base abstract; a trivial subclass only needs to
    // surface the protected SSE parser, which accepts a plain Stream (no HTTP) so fixtures drive it.
    private sealed class TestableAnthropicProvider : AnthropicProtocolProvider
    {
        public IAsyncEnumerable<Result<LlmResponseChunk, LlmError>> Parse(Stream stream, CancellationToken ct)
            => ParseSseStreamAsync(stream, ct);
    }

    private static string Sse(params (string Event, string Data)[] events)
    {
        var sb = new StringBuilder();
        foreach ((string evt, string data) in events)
        {
            sb.Append("event: ").Append(evt).Append('\n');
            sb.Append("data: ").Append(data).Append('\n');
            sb.Append('\n');
        }

        return sb.ToString();
    }

    private static async Task<List<Result<LlmResponseChunk, LlmError>>> Parse(string sse)
    {
        var provider = new TestableAnthropicProvider();
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
    public async Task Parse_TextDeltas_YieldsContentThenFinalUsage()
    {
        string sse = Sse(
            ("message_start", """{"message":{"usage":{"input_tokens":10}}}"""),
            ("content_block_delta", """{"delta":{"type":"text_delta","text":"Hello"}}"""),
            ("content_block_delta", """{"delta":{"type":"text_delta","text":" world"}}"""),
            ("message_delta", """{"usage":{"output_tokens":5}}"""));

        List<Result<LlmResponseChunk, LlmError>> chunks = await Parse(sse);

        Assert.Equal(3, chunks.Count);
        Assert.Equal("Hello", Ok(chunks[0]).ContentDelta);
        Assert.False(Ok(chunks[0]).IsLast);
        Assert.Equal(" world", Ok(chunks[1]).ContentDelta);

        LlmResponseChunk final = Ok(chunks[2]);
        Assert.True(final.IsLast);
        Assert.Null(final.ContentDelta);
        Assert.Null(final.ToolCalls);
        Assert.Equal(10, final.Usage!.InputTokens);
        Assert.Equal(5, final.Usage.OutputTokens);
    }

    [Fact]
    public async Task Parse_ToolCall_JoinsPartialArgumentDeltas()
    {
        string sse = Sse(
            ("content_block_start", """{"content_block":{"type":"tool_use","id":"toolu_1","name":"web_search"}}"""),
            ("content_block_delta", """{"delta":{"type":"input_json_delta","partial_json":"{\"query\":"}}"""),
            ("content_block_delta", """{"delta":{"type":"input_json_delta","partial_json":"\"rhino\"}"}}"""),
            ("content_block_stop", "{}"),
            ("message_delta", """{"usage":{"output_tokens":7}}"""));

        List<Result<LlmResponseChunk, LlmError>> chunks = await Parse(sse);

        LlmResponseChunk final = Ok(Assert.Single(chunks));
        Assert.True(final.IsLast);
        LlmToolCall call = Assert.Single(final.ToolCalls!);
        Assert.Equal("toolu_1", call.Id);
        Assert.Equal("web_search", call.Name);
        Assert.Equal("""{"query":"rhino"}""", call.InputJson);
    }

    [Fact]
    public async Task Parse_MultipleToolCalls_RemainSeparate()
    {
        string sse = Sse(
            ("content_block_start", """{"content_block":{"type":"tool_use","id":"id_a","name":"alpha"}}"""),
            ("content_block_delta", """{"delta":{"type":"input_json_delta","partial_json":"{}"}}"""),
            ("content_block_stop", "{}"),
            ("content_block_start", """{"content_block":{"type":"tool_use","id":"id_b","name":"beta"}}"""),
            ("content_block_delta", """{"delta":{"type":"input_json_delta","partial_json":"{}"}}"""),
            ("content_block_stop", "{}"),
            ("message_delta", """{"usage":{"output_tokens":3}}"""));

        List<Result<LlmResponseChunk, LlmError>> chunks = await Parse(sse);

        LlmResponseChunk final = Ok(Assert.Single(chunks));
        Assert.Equal(2, final.ToolCalls!.Count);
        Assert.Equal(new[] { "id_a", "id_b" }, final.ToolCalls!.Select(c => c.Id));
        Assert.Equal(new[] { "alpha", "beta" }, final.ToolCalls!.Select(c => c.Name));
    }

    [Fact]
    public async Task Parse_MalformedEvent_YieldsDomainErrorNotException()
    {
        string sse = Sse(("message_start", "{not valid json"));

        List<Result<LlmResponseChunk, LlmError>> chunks = await Parse(sse);

        Assert.True(Assert.Single(chunks).IsErr(out LlmError? error, out _));
        Assert.Equal(LlmErrorKind.InvalidRequest, error!.Kind);
    }
}
