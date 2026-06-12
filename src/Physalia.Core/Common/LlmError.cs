// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Physalia.Core.Common;

/// <summary>
/// Classifies the cause of an LLM call failure.
/// </summary>
public enum LlmErrorKind
{
    /// <summary>HTTP or socket-level failure.</summary>
    Network,

    /// <summary>Invalid or missing API key.</summary>
    Auth,

    /// <summary>Provider rate limit exceeded.</summary>
    RateLimit,

    /// <summary>Malformed request or unsupported parameter.</summary>
    InvalidRequest,

    /// <summary>Request timed out before a response was received.</summary>
    Timeout,

    /// <summary>Request was cancelled via <see cref="System.Threading.CancellationToken"/>.</summary>
    Cancelled,
}

/// <summary>
/// Describes a failed LLM call.
/// </summary>
/// <param name="Kind">The category of the failure.</param>
/// <param name="Message">A human-readable description of the failure.</param>
public record LlmError(LlmErrorKind Kind, string Message);
