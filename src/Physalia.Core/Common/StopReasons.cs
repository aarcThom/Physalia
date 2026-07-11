// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Physalia.Core.Common;

/// <summary>
/// Classification helpers for the raw provider stop/finish reason strings carried
/// on the final <see cref="LlmResponseChunk"/> of a stream.
/// </summary>
public static class StopReasons
{
    /// <summary>
    /// Returns true when the provider stop reason indicates the response was cut off
    /// at the max token limit. Covers "max_tokens" (Anthropic), "length" (OpenAI
    /// protocol), and "MAX_TOKENS" (Gemini), compared case-insensitively.
    /// </summary>
    /// <param name="stopReason">The raw provider stop/finish reason, or null.</param>
    /// <returns>true when the response was truncated at the token limit; otherwise false.</returns>
    public static bool IsTruncation(string? stopReason) =>
        stopReason is not null &&
        (stopReason.Equals("max_tokens", StringComparison.OrdinalIgnoreCase) ||
         stopReason.Equals("length", StringComparison.OrdinalIgnoreCase));
}
