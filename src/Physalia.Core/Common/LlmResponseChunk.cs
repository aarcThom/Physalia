// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Physalia.Core.Common;

/// <summary>
/// Token usage for a completed inference call.
/// Arrives on the final chunk (<see cref="LlmResponseChunk.IsLast"/> is true) when available.
/// </summary>
/// <param name="InputTokens">Number of tokens in the prompt.</param>
/// <param name="OutputTokens">Number of tokens generated.</param>
public record LlmUsage(int InputTokens, int OutputTokens);

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
public record LlmResponseChunk(
    string? ContentDelta,
    bool IsLast,
    LlmUsage? Usage,
    IReadOnlyList<LlmToolCall>? ToolCalls = null);
