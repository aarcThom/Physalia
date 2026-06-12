// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Physalia.Core.Models;

/// <summary>
/// Metadata for a single LLM model, sourced from the LiteLLM model database.
/// </summary>
/// <param name="Provider">The provider name, e.g. "openai", "anthropic", "gemini".</param>
/// <param name="ModelId">The model identifier as used in API calls, e.g. "gpt-4o".</param>
/// <param name="MaxInputTokens">Context window size in tokens.</param>
/// <param name="MaxOutputTokens">Maximum number of tokens the model can generate.</param>
/// <param name="SupportsToolCalls">Whether the model supports function/tool calling.</param>
/// <param name="SupportsVision">Whether the model accepts image inputs.</param>
/// <param name="SupportsReasoning">Whether the model supports extended reasoning/thinking.</param>
public record ModelEntry(
    string Provider,
    string ModelId,
    int MaxInputTokens,
    int MaxOutputTokens,
    bool SupportsToolCalls,
    bool SupportsVision,
    bool SupportsReasoning);
