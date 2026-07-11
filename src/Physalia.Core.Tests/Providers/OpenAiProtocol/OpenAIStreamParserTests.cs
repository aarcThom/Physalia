// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Text;
using Physalia.Core.Common;
using Physalia.Core.Providers.OpenAiProtocol;
using Xunit;

namespace Physalia.Core.Tests.Providers.OpenAiProtocol;

public class OpenAIStreamParserTests
{
    private sealed class TestableOpenAIProvider : OpenAIProtocolProvider
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
        var provider = new TestableOpenAIProvider();
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
    public async Task Parse_TextDeltas_YieldsContentThenFinal()
    {
        string sse = Sse(
            """{"choices":[{"delta":{"content":"Hello"}}]}""",
            """{"choices":[{"delta":{"content":" world"}}]}""",
            """{"choices":[{"delta":{},"finish_reason":"stop"}]}""",
            "[DONE]");

        List<Result<LlmResponseChunk, LlmError>> chunks = await Parse(sse);

        Assert.Equal(3, chunks.Count);
        Assert.Equal("Hello", Ok(chunks[0]).ContentDelta);
        Assert.Equal(" world", Ok(chunks[1]).ContentDelta);

        LlmResponseChunk final = Ok(chunks[2]);
        Assert.True(final.IsLast);
        Assert.Null(final.ContentDelta);
        Assert.Null(final.ToolCalls);
    }

    [Fact]
    public async Task Parse_ToolCall_JoinsArgumentDeltasByIndex()
    {
        string sse = Sse(
            """{"choices":[{"delta":{"tool_calls":[{"index":0,"id":"call_1","function":{"name":"web_search","arguments":"{\"q\":"}}]}}]}""",
            """{"choices":[{"delta":{"tool_calls":[{"index":0,"function":{"arguments":"\"rhino\"}"}}]}}]}""",
            """{"choices":[{"delta":{},"finish_reason":"tool_calls"}]}""",
            "[DONE]");

        List<Result<LlmResponseChunk, LlmError>> chunks = await Parse(sse);

        LlmResponseChunk final = Ok(Assert.Single(chunks));
        Assert.True(final.IsLast);
        LlmToolCall call = Assert.Single(final.ToolCalls!);
        Assert.Equal("call_1", call.Id);
        Assert.Equal("web_search", call.Name);
        Assert.Equal("""{"q":"rhino"}""", call.InputJson);
    }

    [Fact]
    public async Task Parse_MultipleToolCalls_RemainSeparateByIndex()
    {
        string sse = Sse(
            """{"choices":[{"delta":{"tool_calls":[{"index":0,"id":"id_a","function":{"name":"alpha","arguments":"{}"}}]}}]}""",
            """{"choices":[{"delta":{"tool_calls":[{"index":1,"id":"id_b","function":{"name":"beta","arguments":"{}"}}]}}]}""",
            """{"choices":[{"delta":{},"finish_reason":"tool_calls"}]}""",
            "[DONE]");

        List<Result<LlmResponseChunk, LlmError>> chunks = await Parse(sse);

        LlmResponseChunk final = Ok(Assert.Single(chunks));
        Assert.Equal(2, final.ToolCalls!.Count);
        Assert.Equal(new[] { "id_a", "id_b" }, final.ToolCalls!.Select(c => c.Id));
        Assert.Equal(new[] { "alpha", "beta" }, final.ToolCalls!.Select(c => c.Name));
    }

    [Fact]
    public async Task Parse_MalformedChunk_YieldsDomainErrorNotException()
    {
        string sse = Sse("{not valid json");

        List<Result<LlmResponseChunk, LlmError>> chunks = await Parse(sse);

        Assert.True(Assert.Single(chunks).IsErr(out LlmError? error, out _));
        Assert.Equal(LlmErrorKind.InvalidRequest, error!.Kind);
    }

    [Fact]
    public async Task Parse_ReasoningContentThenContent_WrapsAndCloses()
    {
        string sse = Sse(
            """{"choices":[{"delta":{"reasoning_content":"a"}}]}""",
            """{"choices":[{"delta":{"reasoning_content":"b"}}]}""",
            """{"choices":[{"delta":{"reasoning_content":null,"content":"answer"}}]}""",
            """{"choices":[{"delta":{},"finish_reason":"stop"}]}""",
            "[DONE]");

        List<Result<LlmResponseChunk, LlmError>> chunks = await Parse(sse);

        string text = string.Concat(chunks.Select(c => Ok(c).ContentDelta));
        Assert.Equal("<think>ab</think>\n\nanswer", text);
        Assert.Equal("stop", Ok(chunks[^1]).StopReason);
    }

    [Fact]
    public async Task Parse_ReasoningKey_OpenRouterShape_Wrapped()
    {
        string sse = Sse(
            """{"choices":[{"delta":{"reasoning":"hm"}}]}""",
            """{"choices":[{"delta":{"content":"answer"}}]}""",
            """{"choices":[{"delta":{},"finish_reason":"stop"}]}""",
            "[DONE]");

        List<Result<LlmResponseChunk, LlmError>> chunks = await Parse(sse);

        string text = string.Concat(chunks.Select(c => Ok(c).ContentDelta));
        Assert.Equal("<think>hm</think>\n\nanswer", text);
    }

    [Fact]
    public async Task Parse_EmptyContentDuringReasoning_DoesNotCloseTag()
    {
        string sse = Sse(
            """{"choices":[{"delta":{"reasoning_content":"a","content":""}}]}""",
            """{"choices":[{"delta":{"reasoning_content":"b","content":null}}]}""",
            """{"choices":[{"delta":{"content":"answer"}}]}""",
            """{"choices":[{"delta":{},"finish_reason":"stop"}]}""",
            "[DONE]");

        List<Result<LlmResponseChunk, LlmError>> chunks = await Parse(sse);

        string text = string.Concat(chunks.Select(c => Ok(c).ContentDelta));
        Assert.Equal("<think>ab</think>\n\nanswer", text);
    }

    [Fact]
    public async Task Parse_TruncatedWhileReasoning_ClosesTagAndReportsLength()
    {
        string sse = Sse(
            """{"choices":[{"delta":{"reasoning_content":"endless"}}]}""",
            """{"choices":[{"delta":{},"finish_reason":"length"}]}""",
            "[DONE]");

        List<Result<LlmResponseChunk, LlmError>> chunks = await Parse(sse);

        string text = string.Concat(chunks.Select(c => Ok(c).ContentDelta));
        Assert.Equal("<think>endless</think>", text);

        LlmResponseChunk final = Ok(chunks[^1]);
        Assert.True(final.IsLast);
        Assert.Equal("length", final.StopReason);
    }

    [Fact]
    public async Task Parse_PlainContent_NoTags_StopReasonStop()
    {
        string sse = Sse(
            """{"choices":[{"delta":{"content":"plain"}}]}""",
            """{"choices":[{"delta":{},"finish_reason":"stop"}]}""",
            "[DONE]");

        List<Result<LlmResponseChunk, LlmError>> chunks = await Parse(sse);

        Assert.Equal("plain", Ok(chunks[0]).ContentDelta);
        Assert.DoesNotContain("<think", string.Concat(chunks.Select(c => Ok(c).ContentDelta)));
        Assert.Equal("stop", Ok(chunks[^1]).StopReason);
    }
}
