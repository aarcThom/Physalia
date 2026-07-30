// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Physalia.Core.Common;

/// <summary>
/// Maps HTTP failure status codes to <see cref="LlmErrorKind"/> values and renders provider error
/// bodies as something a human can read.
/// Single source of truth for all providers and token estimators.
/// </summary>
public static class HttpErrorMapper
{
    /// <summary>
    /// How much of an unrecognised error body to keep. Long enough to diagnose, short enough that
    /// a Grasshopper balloon and a signal payload stay legible.
    /// </summary>
    private const int MaxRawBodyChars = 600;

    /// <summary>
    /// Maps an HTTP status code to the corresponding error kind.
    /// </summary>
    /// <param name="statusCode">The HTTP status code from a failed response.</param>
    /// <returns>The matching error kind; unrecognised codes map to Network.</returns>
    public static LlmErrorKind MapStatusCode(HttpStatusCode statusCode) => statusCode switch
    {
        HttpStatusCode.Unauthorized => LlmErrorKind.Auth,
        HttpStatusCode.Forbidden => LlmErrorKind.Auth,
        HttpStatusCode.TooManyRequests => LlmErrorKind.RateLimit,
        HttpStatusCode.BadRequest => LlmErrorKind.InvalidRequest,
        HttpStatusCode.UnprocessableEntity => LlmErrorKind.InvalidRequest,
        _ => LlmErrorKind.Network,
    };

    /// <summary>
    /// Maps a provider's error <c>type</c> (or Gemini's <c>status</c>) to an error kind. Needed
    /// separately from <see cref="MapStatusCode"/> because an error can also arrive mid-stream,
    /// inside a response whose status code was 200.
    /// </summary>
    /// <param name="errorType">The provider's error type string; may be null or empty.</param>
    /// <returns>The matching error kind; unrecognised types map to Network.</returns>
    public static LlmErrorKind MapErrorType(string? errorType) => (errorType ?? string.Empty).ToLowerInvariant() switch
    {
        "overloaded_error" => LlmErrorKind.RateLimit,
        "rate_limit_error" => LlmErrorKind.RateLimit,
        "rate_limit_exceeded" => LlmErrorKind.RateLimit,
        "insufficient_quota" => LlmErrorKind.RateLimit,
        "resource_exhausted" => LlmErrorKind.RateLimit,
        "invalid_request_error" => LlmErrorKind.InvalidRequest,
        "invalid_argument" => LlmErrorKind.InvalidRequest,
        "failed_precondition" => LlmErrorKind.InvalidRequest,
        "authentication_error" => LlmErrorKind.Auth,
        "permission_error" => LlmErrorKind.Auth,
        "permission_denied" => LlmErrorKind.Auth,
        "unauthenticated" => LlmErrorKind.Auth,
        "timeout_error" => LlmErrorKind.Timeout,
        "deadline_exceeded" => LlmErrorKind.Timeout,
        _ => LlmErrorKind.Network,
    };

    /// <summary>
    /// Renders a failed response as one readable sentence. Anthropic, OpenAI and Gemini all wrap
    /// the useful part in an <c>error</c> object and surround it with envelope noise; raw JSON in a
    /// canvas balloon is something nobody reads, so pull out the message and name the status.
    /// </summary>
    /// <param name="statusCode">The HTTP status code from the failed response.</param>
    /// <param name="body">The response body, which may or may not be JSON.</param>
    /// <returns>A description of the failure, falling back to the truncated raw body.</returns>
    public static string Describe(HttpStatusCode statusCode, string body)
    {
        string prefix = $"HTTP {(int)statusCode} {statusCode}";
        string? detail = ExtractErrorMessage(body);

        if (detail is null)
        {
            return string.IsNullOrWhiteSpace(body)
                ? prefix + "."
                : $"{prefix}: {Truncate(body.Trim())}";
        }

        return $"{prefix}: {detail}";
    }

    /// <summary>
    /// Pulls the human-readable message out of a provider error body.
    /// </summary>
    /// <param name="body">The response body.</param>
    /// <returns>The message, optionally prefixed by the provider's error type; null when the body is not a recognised error shape.</returns>
    private static string? ExtractErrorMessage(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            // Gemini answers with a top-level array for some failures, hence JsonNode over
            // JsonObject. Anything that is not an object with an "error" member falls through.
            if (JsonNode.Parse(body) is not JsonObject root
                || root["error"] is not JsonObject error)
            {
                return null;
            }

            string? message = error["message"]?.GetValue<string>();
            string? type = error["type"]?.GetValue<string>() ?? error["status"]?.GetValue<string>();

            if (string.IsNullOrWhiteSpace(message))
            {
                return string.IsNullOrWhiteSpace(type) ? null : type;
            }

            return string.IsNullOrWhiteSpace(type)
                ? Truncate(message!)
                : $"{type} — {Truncate(message!)}";
        }
        catch (JsonException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            // A recognised member held an unexpected JSON type; treat the body as opaque.
            return null;
        }
    }

    private static string Truncate(string value) =>
        value.Length <= MaxRawBodyChars
            ? value
            : value.Substring(0, MaxRawBodyChars) + $"… [truncated {value.Length - MaxRawBodyChars} characters]";
}
