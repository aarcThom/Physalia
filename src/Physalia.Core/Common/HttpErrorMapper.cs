// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Net;

namespace Physalia.Core.Common;

/// <summary>
/// Maps HTTP failure status codes to <see cref="LlmErrorKind"/> values.
/// Single source of truth for all providers and token estimators.
/// </summary>
public static class HttpErrorMapper
{
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
}
