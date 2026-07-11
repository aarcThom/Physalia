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

    [Fact]
    public async Task Parse_ThinkingThenText_WrapsThinkingInTags()
    {
        string sse = Sse(
            ("content_block_start", """{"content_block":{"type":"thinking"}}"""),
            ("content_block_delta", """{"delta":{"type":"thinking_delta","thinking":"step one"}}"""),
            ("content_block_delta", """{"delta":{"type":"thinking_delta","thinking":", step two"}}"""),
            ("content_block_delta", """{"delta":{"type":"signature_delta","signature":"sig_abc"}}"""),
            ("content_block_stop", "{}"),
            ("content_block_start", """{"content_block":{"type":"text"}}"""),
            ("content_block_delta", """{"delta":{"type":"text_delta","text":"answer"}}"""),
            ("content_block_stop", "{}"),
            ("message_delta", """{"delta":{"stop_reason":"end_turn"},"usage":{"output_tokens":9}}"""));

        List<Result<LlmResponseChunk, LlmError>> chunks = await Parse(sse);

        string text = string.Concat(chunks.Select(c => Ok(c).ContentDelta));
        Assert.Equal("<think>step one, step two</think>\n\nanswer", text);
        Assert.DoesNotContain("sig_abc", text);
        Assert.Equal("end_turn", Ok(chunks[^1]).StopReason);
    }

    [Fact]
    public async Task Parse_ThinkingOnlyTruncated_ClosesTagAndReportsMaxTokens()
    {
        string sse = Sse(
            ("content_block_start", """{"content_block":{"type":"thinking"}}"""),
            ("content_block_delta", """{"delta":{"type":"thinking_delta","thinking":"endless reasoning"}}"""),
            ("message_delta", """{"delta":{"stop_reason":"max_tokens"},"usage":{"output_tokens":4096}}"""));

        List<Result<LlmResponseChunk, LlmError>> chunks = await Parse(sse);

        string text = string.Concat(chunks.Select(c => Ok(c).ContentDelta));
        Assert.Equal("<think>endless reasoning</think>", text);

        LlmResponseChunk final = Ok(chunks[^1]);
        Assert.True(final.IsLast);
        Assert.Equal("max_tokens", final.StopReason);
    }

    [Fact]
    public async Task Parse_EmptyThinkingBlock_EmitsNoTags()
    {
        string sse = Sse(
            ("content_block_start", """{"content_block":{"type":"thinking"}}"""),
            ("content_block_stop", "{}"),
            ("content_block_delta", """{"delta":{"type":"text_delta","text":"answer"}}"""),
            ("message_delta", """{"usage":{"output_tokens":2}}"""));

        List<Result<LlmResponseChunk, LlmError>> chunks = await Parse(sse);

        string text = string.Concat(chunks.Select(c => Ok(c).ContentDelta));
        Assert.Equal("answer", text);
    }

    [Fact]
    public async Task Parse_RedactedThinking_Skipped()
    {
        string sse = Sse(
            ("content_block_start", """{"content_block":{"type":"redacted_thinking"}}"""),
            ("content_block_stop", "{}"),
            ("content_block_delta", """{"delta":{"type":"text_delta","text":"answer"}}"""),
            ("message_delta", """{"usage":{"output_tokens":2}}"""));

        List<Result<LlmResponseChunk, LlmError>> chunks = await Parse(sse);

        string text = string.Concat(chunks.Select(c => Ok(c).ContentDelta));
        Assert.Equal("answer", text);
    }

    [Fact]
    public async Task Parse_MultipleThinkingBlocks_EachWrapped()
    {
        string sse = Sse(
            ("content_block_start", """{"content_block":{"type":"thinking"}}"""),
            ("content_block_delta", """{"delta":{"type":"thinking_delta","thinking":"first"}}"""),
            ("content_block_stop", "{}"),
            ("content_block_delta", """{"delta":{"type":"text_delta","text":"middle"}}"""),
            ("content_block_start", """{"content_block":{"type":"thinking"}}"""),
            ("content_block_delta", """{"delta":{"type":"thinking_delta","thinking":"second"}}"""),
            ("content_block_stop", "{}"),
            ("message_delta", """{"usage":{"output_tokens":6}}"""));

        List<Result<LlmResponseChunk, LlmError>> chunks = await Parse(sse);

        string text = string.Concat(chunks.Select(c => Ok(c).ContentDelta));
        Assert.Equal("<think>first</think>\n\nmiddle<think>second</think>\n\n", text);
    }

    [Fact]
    public async Task Parse_NoStopReason_FinalChunkStopReasonNull()
    {
        string sse = Sse(("message_delta", """{"usage":{"output_tokens":1}}"""));

        List<Result<LlmResponseChunk, LlmError>> chunks = await Parse(sse);

        Assert.Null(Ok(Assert.Single(chunks)).StopReason);
    }
}
