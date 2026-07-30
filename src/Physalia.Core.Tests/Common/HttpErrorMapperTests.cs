// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Net;
using Physalia.Core.Common;
using Xunit;

namespace Physalia.Core.Tests.Common;

public class HttpErrorMapperTests
{
    [Fact]
    public void Describe_AnthropicErrorBody_PullsOutTypeAndMessage()
    {
        // The body that killed the 2026-07-29 staged build, reported verbatim in the canvas
        // balloon and the failure signal because nothing unwrapped it.
        const string body = """
            {"type":"error","error":{"type":"invalid_request_error","message":"messages.3: `tool_use` ids were found without `tool_result` blocks immediately after: toolu_014Be7"},"request_id":"req_011CdXjEXrvyD2SzksN3RqAF"}
            """;

        string described = HttpErrorMapper.Describe(HttpStatusCode.BadRequest, body);

        Assert.Contains("HTTP 400", described);
        Assert.Contains("invalid_request_error", described);
        Assert.Contains("tool_use", described);

        // The envelope noise is gone.
        Assert.DoesNotContain("request_id", described);
        Assert.DoesNotContain("{", described);
    }

    [Fact]
    public void Describe_GeminiErrorBody_UsesStatusAsTheType()
    {
        const string body = """
            {"error":{"code":429,"message":"Quota exceeded.","status":"RESOURCE_EXHAUSTED"}}
            """;

        string described = HttpErrorMapper.Describe(HttpStatusCode.TooManyRequests, body);

        Assert.Contains("RESOURCE_EXHAUSTED", described);
        Assert.Contains("Quota exceeded.", described);
    }

    [Fact]
    public void Describe_NonJsonBody_FallsBackToTheRawText()
    {
        string described = HttpErrorMapper.Describe(HttpStatusCode.BadGateway, "<html>502 Bad Gateway</html>");

        Assert.Contains("HTTP 502", described);
        Assert.Contains("502 Bad Gateway", described);
    }

    [Fact]
    public void Describe_EmptyBody_NamesTheStatusAlone()
    {
        string described = HttpErrorMapper.Describe(HttpStatusCode.ServiceUnavailable, string.Empty);

        Assert.Contains("HTTP 503", described);
    }

    [Fact]
    public void Describe_LongBody_IsTruncated()
    {
        string described = HttpErrorMapper.Describe(HttpStatusCode.BadRequest, new string('x', 5000));

        Assert.Contains("truncated", described);
        Assert.True(described.Length < 1000, $"Expected a truncated description but it was {described.Length} chars.");
    }

    [Theory]
    [InlineData("overloaded_error", LlmErrorKind.RateLimit)]
    [InlineData("rate_limit_error", LlmErrorKind.RateLimit)]
    [InlineData("RESOURCE_EXHAUSTED", LlmErrorKind.RateLimit)]
    [InlineData("invalid_request_error", LlmErrorKind.InvalidRequest)]
    [InlineData("authentication_error", LlmErrorKind.Auth)]
    [InlineData("PERMISSION_DENIED", LlmErrorKind.Auth)]
    [InlineData("deadline_exceeded", LlmErrorKind.Timeout)]
    [InlineData("something_new", LlmErrorKind.Network)]
    [InlineData("", LlmErrorKind.Network)]
    [InlineData(null, LlmErrorKind.Network)]
    public void MapErrorType_MapsProviderErrorTypes(string? errorType, LlmErrorKind expected)
    {
        Assert.Equal(expected, HttpErrorMapper.MapErrorType(errorType));
    }
}
