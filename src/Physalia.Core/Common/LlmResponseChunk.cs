// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Physalia.Core.Common;

/// <summary>
/// Token usage for a completed inference call.
/// Arrives on the final chunk (<see cref="LlmResponseChunk.IsLast"/> is true) when available.
/// </summary>
/// <param name="InputTokens">
/// Number of prompt tokens billed at the full rate — the uncached remainder only. The whole prompt
/// is <c>InputTokens + CacheWriteTokens + CacheReadTokens</c>.
/// </param>
/// <param name="OutputTokens">Number of tokens generated.</param>
public record LlmUsage(int InputTokens, int OutputTokens)
{
    /// <summary>
    /// Gets the prompt tokens written to the provider's cache on this call, billed at a premium
    /// over the base rate. Zero on providers that do not report it.
    /// </summary>
    public int CacheWriteTokens { get; init; }

    /// <summary>
    /// Gets the prompt tokens served from the provider's cache on this call, billed at roughly a
    /// tenth of the base rate. Zero on providers that do not report it.
    ///
    /// <para>This is the one honest signal that prompt caching is working. Physalia rebuilds the
    /// system prompt every turn and relies on the stable/volatile split to keep the prefix
    /// byte-identical; if this stays zero across consecutive turns, something upstream is
    /// perturbing that prefix and the cache is being rewritten instead of read.</para>
    /// </summary>
    public int CacheReadTokens { get; init; }
}

/// <summary>
/// A single streamed chunk from an LLM inference call.
/// </summary>
/// <param name="ContentDelta">
/// The incremental text content for this chunk, or null if this chunk carries no text
/// (e.g. a stop or usage chunk).
/// </param>
/// <param name="IsLast">
/// True when the provider signals the end of the response (finish_reason is set).
/// </param>
/// <param name="Usage">
/// Token usage, populated on the final chunk when the provider includes it.
/// </param>
/// <param name="ToolCalls">
/// Tool calls requested by the model, populated on the final chunk when the model
/// invokes one or more tools. Null when the response contains only text.
/// </param>
/// <param name="StopReason">
/// The raw provider stop/finish reason, populated on the final chunk when available.
/// Examples: "end_turn", "max_tokens" (Anthropic), "stop", "length" (OpenAI protocol),
/// "STOP", "MAX_TOKENS" (Gemini). Null on non-final chunks or when the provider
/// omits it. Use <see cref="StopReasons.IsTruncation"/> to detect token-limit cuts.
/// </param>
public record LlmResponseChunk(
    string? ContentDelta,
    bool IsLast,
    LlmUsage? Usage,
    IReadOnlyList<LlmToolCall>? ToolCalls = null,
    string? StopReason = null);
